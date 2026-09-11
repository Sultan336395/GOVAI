"""GOVAI REST API istemcisi.

Worker'lar veritabanına doğrudan yazmaz. Bunun iki sebebi var:
    1. Uygunluk, tekilleştirme ve bildirim kuralları tek bir yerde (.NET Application katmanı) kalır.
    2. Şema değişikliği worker'ları kırmaz; sözleşme REST API'dir.
"""

from __future__ import annotations

from typing import Any

import httpx
from tenacity import retry, retry_if_exception_type, stop_after_attempt, wait_exponential

from govai_workers.config import settings
from govai_workers.logging_setup import get_logger

log = get_logger(__name__)


class ApiError(RuntimeError):
    """API'den 4xx/5xx döndüğünde fırlatılır."""

    def __init__(self, status_code: int, detail: str) -> None:
        super().__init__(f"GOVAI API hatası ({status_code}): {detail}")
        self.status_code = status_code
        self.detail = detail


class GovAiClient:
    """Basit, senkron API istemcisi. Jeton süresi dolduğunda kendini yeniler."""

    def __init__(self, base_url: str | None = None) -> None:
        self._base_url = (base_url or settings.api_url).rstrip("/")
        self._client = httpx.Client(
            base_url=self._base_url,
            timeout=settings.api_timeout_seconds,
            headers={"User-Agent": "GovAI-Worker/0.1"},
        )
        self._token: str | None = None

    # ---- oturum ----

    def _authenticate(self) -> None:
        response = self._client.post(
            "/api/auth/login",
            json={"email": settings.api_email, "password": settings.api_password},
        )
        if response.status_code != httpx.codes.OK:
            raise ApiError(response.status_code, response.text)

        self._token = response.json()["accessToken"]
        log.info("api_authenticated", email=settings.api_email)

    def _headers(self) -> dict[str, str]:
        if self._token is None:
            self._authenticate()
        return {"Authorization": f"Bearer {self._token}"}

    @retry(
        retry=retry_if_exception_type(httpx.TransportError),
        stop=stop_after_attempt(3),
        wait=wait_exponential(multiplier=1, min=1, max=10),
        reraise=True,
    )
    def _request(self, method: str, path: str, **kwargs: Any) -> Any:
        response = self._client.request(method, path, headers=self._headers(), **kwargs)

        # Jeton süresi dolmuşsa bir kez yenile ve tekrar dene.
        if response.status_code == httpx.codes.UNAUTHORIZED:
            self._token = None
            response = self._client.request(method, path, headers=self._headers(), **kwargs)

        if response.status_code >= httpx.codes.BAD_REQUEST:
            raise ApiError(response.status_code, response.text[:500])

        if response.status_code == httpx.codes.NO_CONTENT or not response.content:
            return None

        return response.json()

    # ---- kaynaklar ----

    def get_source(self, source_id: str) -> dict[str, Any] | None:
        """Tek bir kaynağın kaydını döner.

        Kaynağın kategorisi mesajdan DEĞİL buradan okunur: mesaj eksik ya da
        yanlış gelirse mevzuat belgesi fırsat kataloğuna yazılabilirdi.
        """
        for source in self.list_sources(only_enabled=False):
            if source.get("id") == source_id:
                return source

        return None

    def list_sources(self, only_enabled: bool = True) -> list[dict[str, Any]]:
        return self._request("GET", "/api/sources", params={"onlyEnabled": only_enabled}) or []

    def ingest_document(
        self,
        source_id: str,
        url: str,
        title: str,
        raw_content: str,
        media_type: str,
        canonical_url: str | None = None,
        charset: str | None = None,
        http_status_code: int = 200,
    ) -> dict[str, Any]:
        """Belgeyi API'ye bırakır.

        Kanıt alanları (nihai adres, karakter kümesi, HTTP durumu) sunucuda belge
        sürümüne yazılır; ileride DeepTech motoru kaynak gösterirken bunlara dayanır.
        """
        return self._request(
            "POST",
            "/api/sources/documents",
            json={
                "sourceId": source_id,
                "url": url,
                "title": title,
                "rawContent": raw_content,
                "mediaType": media_type,
                "canonicalUrl": canonical_url or url,
                "charset": charset,
                "httpStatusCode": http_status_code,
            },
        )

    def record_parse_result(
        self,
        document_id: str,
        status: str,
        normalized_text: str | None = None,
        title: str | None = None,
        language: str | None = None,
        page_count: int | None = None,
        error: str | None = None,
        chunks: list[dict[str, Any]] | None = None,
    ) -> dict[str, Any]:
        """Ayrıştırma sonucunu ve kanıt parçalarını belge sürümüne yazar.

        Kanıt parçaları yalnızca bu yoldan kaydedilir; yapay zekâ üretimi metin
        bu uca gönderilmez.
        """
        return self._request(
            "POST",
            f"/api/sources/documents/{document_id}/parse-result",
            json={
                "status": status,
                "normalizedText": normalized_text,
                "title": title,
                "language": language,
                "pageCount": page_count,
                "error": error,
                "chunks": chunks or [],
            },
        )

    def record_verification(self, source_id: str, payload: dict[str, Any]) -> dict[str, Any]:
        """Canlı doğrulama sonucunu bildirir; kaynağı aktif eden tek yol budur."""
        return self._request("POST", f"/api/sources/{source_id}/verification", json=payload)

    def record_run(
        self,
        source_id: str,
        status: str,
        message: str | None,
        document_count: int,
    ) -> None:
        self._request(
            "POST",
            f"/api/sources/{source_id}/runs",
            json={"status": status, "message": message, "documentCount": document_count},
        )

    def trigger_crawl(self, source_id: str) -> None:
        self._request("POST", f"/api/sources/{source_id}/crawl")

    # ---- fırsatlar ----

    def upsert_opportunity(self, payload: dict[str, Any]) -> dict[str, Any]:
        return self._request("POST", "/api/opportunities", json=payload)

    # ---- skorlama ve bildirim ----

    def rescore_batch(self) -> dict[str, Any]:
        """Kiracıdaki tüm firmaları yeniden skorlar.

        Yanıt yalnızca sayı içerir; worker müşteri verisi görmez.
        """
        return self._request("POST", "/api/eligibility/rescore-batch")

    def pull_erp_profiles(self) -> dict[str, Any]:
        """Firmaların ERP'lerinden profil verisini çeker.

        Ciro, personel kırılımı ve belgeler firmanın kendi ERP'sinden okunur; profil
        böylece elle güncellenmeyi beklemeden güncel kalır. ERP'de BULUNAMAYAN alan
        eksik sayılır, sıfır yazılmaz.
        """
        return self._request("POST", "/api/erp/pull-batch")

    def collect_ai_second_opinions(self) -> dict[str, Any]:
        """Kural motorunun kararlarına yapay zekâdan bağımsız ikinci görüş toplar.

        Bu görüş HİÇBİR SKORU DEĞİŞTİRMEZ; ayrışan vakaları insana işaret eder.
        Yapay zekâ anahtarı yoksa yanıt ``aiEnabled: false`` döner ve hiçbir görüş
        kaydedilmez — sahte görüş üretilmez.
        """
        return self._request("POST", "/api/calibration/ai-review-batch")

    def generate_weekly_reports(self) -> dict[str, Any]:
        """Kiracıdaki tüm firmalar için haftalık raporu üretir.

        Raporu sunucu kurar; worker yalnızca tetikler. Yanıt sayı içerir, firma verisi
        içermez — worker müşteri verisi görmez.
        """
        return self._request("POST", "/api/reports/weekly-batch")

    def invalidate_opportunity(self, opportunity_id: str) -> dict[str, Any]:
        """Bir çağrının eski skorlarını geçersiz işaretler. Kayıt silinmez."""
        return self._request("POST", f"/api/eligibility/opportunities/{opportunity_id}/invalidate")

    def rescore_company(self, company_id: str) -> dict[str, Any]:
        return self._request("POST", f"/api/eligibility/companies/{company_id}/rescore")

    def list_companies(self) -> list[dict[str, Any]]:
        return self._request("GET", "/api/company-profile") or []

    def dispatch_notifications(self, batch_size: int = 100) -> dict[str, Any]:
        return self._request(
            "POST", "/api/notifications/dispatch", params={"batchSize": batch_size}
        )

    def close(self) -> None:
        self._client.close()

    def __enter__(self) -> GovAiClient:
        return self

    def __exit__(self, *_: object) -> None:
        self.close()
