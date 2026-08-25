"""Skorlama worker'ı.

``govai.scoring.requested`` kuyruğunun tüketicisi. Denetimde bu kuyruğun **hiç
tüketicisi olmadığı** bulunmuştu: mesajlar üretiliyor ama kimse işlemiyordu.

Mesaj türleri ve yapılan iş:

* ``CompanyProfileUpdated`` → o firmanın skorları yenilenir.
* ``OpportunityCreated`` / ``OpportunityUpdated`` → çağrıya ait eski skorlar
  **geçersiz** işaretlenir (silinmez) ve kiracıdaki firmalar yeniden skorlanır.

Kurallar:

* **Idempotency**: aynı mesaj tekrar gelirse iş ikinci kez yapılmaz. Anahtar mesajdan
  türetilir; işlenen anahtarlar sınırlı bir pencerede tutulur.
* **Retry + dead-letter**: geçici hatada mesaj yeniden denenir; ısrarla başarısız olan
  mesaj ölü mektup kuyruğuna bırakılır ve tüketici tıkanmaz.
* Loglarda kiracı, firma, fırsat ve korelasyon kimliği bulunur; **secret loglanmaz**.
* Worker müşteri verisi görmez: toplu skorlama ucu yalnızca sayı döner.
"""

from __future__ import annotations

import hashlib
import json
import sys
from collections import OrderedDict
from typing import Any

from govai_workers.api_client import ApiError, GovAiClient
from govai_workers.logging_setup import configure_logging, get_logger
from govai_workers.messaging import RoutingKeys, consume, publish

log = get_logger(__name__)

#: Kaç mesaj anahtarının hatırlanacağı. Kuyruk yeniden teslim ettiğinde
#: (ör. tüketici yeniden başladığında) aynı iş ikinci kez yapılmasın diye.
IDEMPOTENCY_WINDOW = 5000

#: Bir mesajın kaç kez denendikten sonra ölü mektuba gideceği.
MAX_ATTEMPTS = 3

#: Ölü mektup yönlendirme anahtarı.
DEAD_LETTER_KEY = "govai.scoring.dead-letter"


class IdempotencyCache:
    """İşlenen mesaj anahtarlarını sınırlı bir pencerede tutar."""

    def __init__(self, capacity: int = IDEMPOTENCY_WINDOW) -> None:
        self._seen: OrderedDict[str, None] = OrderedDict()
        self._capacity = capacity

    def seen(self, key: str) -> bool:
        if key in self._seen:
            # En son görülen sona taşınır; sık gelen anahtar pencereden düşmesin.
            self._seen.move_to_end(key)
            return True

        self._seen[key] = None
        if len(self._seen) > self._capacity:
            self._seen.popitem(last=False)

        return False


def idempotency_key(payload: dict[str, Any]) -> str:
    """Mesajın kimliği.

    Üretici açıkça bir anahtar verdiyse o kullanılır; vermediyse mesajın anlamlı
    alanlarından türetilir. Zaman damgası **dışarıda bırakılır**: aynı iş için iki kez
    üretilen mesaj aynı anahtarı almalıdır.
    """
    explicit = payload.get("idempotencyKey")
    if explicit:
        return str(explicit)

    parts = {
        "reason": payload.get("reason"),
        "opportunityId": payload.get("opportunityId"),
        "companyId": payload.get("companyId"),
        "tenantId": payload.get("tenantId"),
    }

    raw = json.dumps(parts, sort_keys=True, ensure_ascii=False)
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


def _handle(client: GovAiClient, payload: dict[str, Any]) -> None:
    """Tek bir skorlama isteğini işler."""
    reason = payload.get("reason") or "Unknown"
    opportunity_id = payload.get("opportunityId")
    company_id = payload.get("companyId")
    correlation_id = payload.get("correlationId")

    # Firma profili değiştiyse yalnızca o firma yenilenir.
    if company_id:
        result = client.rescore_company(company_id)
        log.info(
            "company_rescored",
            reason=reason,
            company_id=company_id,
            tenant_id=payload.get("tenantId"),
            correlation_id=correlation_id,
            evaluated=(result or {}).get("evaluatedOpportunityCount"),
        )
        return

    if not opportunity_id:
        log.warning("scoring_message_without_target", reason=reason, correlation_id=correlation_id)
        return

    # Çağrı değişti: eski skorlar artık o çağrıyı anlatmıyor.
    invalidated = client.invalidate_opportunity(opportunity_id)

    # Karantinadaki çağrı zaten değerlendirmeye girmez; toplu tur onu atlar.
    batch = client.rescore_batch()

    log.info(
        "opportunity_rescored",
        reason=reason,
        opportunity_id=opportunity_id,
        correlation_id=correlation_id,
        superseded=(invalidated or {}).get("supersededAssessmentCount"),
        companies=(batch or {}).get("companyCount"),
        evaluated=(batch or {}).get("evaluatedOpportunityCount"),
        failed=(batch or {}).get("failedCompanyCount"),
    )


def make_handler(client: GovAiClient, cache: IdempotencyCache):  # noqa: ANN201
    """Kuyruk tüketicisine verilecek işleyiciyi üretir."""

    def handle(payload: dict[str, Any]) -> None:
        key = idempotency_key(payload)

        if cache.seen(key):
            # Aynı iş ikinci kez YAPILMAZ; mükerrer sonuç oluşmaz.
            log.info(
                "scoring_message_skipped_duplicate",
                key=key[:16],
                reason=payload.get("reason"),
                correlation_id=payload.get("correlationId"),
            )
            return

        attempt = int(payload.get("attempt", 1))

        try:
            _handle(client, payload)
        except ApiError as exc:
            if attempt >= MAX_ATTEMPTS:
                # Israrla başarısız mesaj tüketiciyi tıkamaz; ölü mektuba gider.
                log.error(
                    "scoring_message_dead_lettered",
                    attempt=attempt,
                    status=getattr(exc, "status_code", None),
                    reason=payload.get("reason"),
                    tenant_id=payload.get("tenantId"),
                    company_id=payload.get("companyId"),
                    opportunity_id=payload.get("opportunityId"),
                    correlation_id=payload.get("correlationId"),
                )
                publish(DEAD_LETTER_KEY, {**payload, "attempt": attempt, "error": str(exc)[:400]})
                return

            log.warning(
                "scoring_message_retry",
                attempt=attempt,
                reason=payload.get("reason"),
                correlation_id=payload.get("correlationId"),
            )

            # Yeniden dene: sayaç artırılarak kuyruğa geri bırakılır.
            publish(RoutingKeys.SCORING_REQUESTED, {**payload, "attempt": attempt + 1})

    return handle


def main() -> int:
    configure_logging()

    cache = IdempotencyCache()

    with GovAiClient() as client:
        consume("govai.scoring", [RoutingKeys.SCORING_REQUESTED], make_handler(client, cache))

    return 0


if __name__ == "__main__":
    sys.exit(main())
