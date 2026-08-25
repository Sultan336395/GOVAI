"""Faz 2 – toplayıcı güvenlik ve veri kalitesi testleri.

Canlı web sayfalarına bağımlı değildir: küçük, kaynağı belirtilmiş HTML parçaları
kullanılır. CI'ın resmî kurum sitelerine erişebilmesini zorunlu kılmak, hem testleri
kırılgan yapar hem de o siteleri gereksiz yükler.
"""

from __future__ import annotations

import pytest

from govai_workers.collector.crawler import SourceConfig, SourceCrawler
from govai_workers.collector.fetcher import FetchedDocument, _parse_content_type
from govai_workers.collector.safety import (
    DomainPolicy,
    UnsafeUrlError,
    ensure_safe_url,
    media_type_allowed,
)

# Resmî Gazete ilan sayfalarının yapısını taklit eden küçük bir örnek.
LISTING_HTML = """
<html><head><meta charset="windows-1254"></head><body>
  <nav><a class="menu" href="/iletisim">İletişim</a>
       <a class="menu" href="/hakkimizda">Hakkımızda</a></nav>
  <ul>
    <li><a class="ilan" href="/ilan/2026-1">Şükrü Çığır Tebliği</a></li>
    <li><a class="ilan" href="/ilan/2026-2">Öğütücü İşletmeler Genelgesi</a></li>
    <li><a class="ilan" href="/ilan/2026-3">Üçüncü İlan</a></li>
  </ul>
  <a class="ilan" href="https://baska-site.example/ilan/9">Dış bağlantı</a>
</body></html>
"""


def _doc(html: str, charset: str | None = "utf-8", url: str = "https://kurum.gov.tr/ilanlar"):
    encoding = charset or "utf-8"
    return FetchedDocument(
        url=url,
        canonical_url=url,
        content=html.encode(encoding),
        media_type="text/html",
        status_code=200,
        charset=charset,
    )


class TestCharset:
    def test_content_type_charset_ayristirilir(self) -> None:
        assert _parse_content_type("text/html; charset=windows-1254") == ("text/html", "windows-1254")
        assert _parse_content_type("application/pdf") == ("application/pdf", None)

    def test_sunucunun_bildirdigi_charset_uygulanir(self) -> None:
        """Türkçe karakterler windows-1254 ile gelse de doğru çözülmeli."""
        metin = "Şükrü Öğütücü İşletmeler Çığır"
        belge = _doc(f"<html><body><p>{metin}</p></body></html>", charset="windows-1254")

        assert metin in belge.text()

    def test_charset_yoksa_govdedeki_meta_kullanilir(self) -> None:
        metin = "Ğüşiöç ÇİĞDEM"
        html = f'<html><head><meta charset="windows-1254"></head><body>{metin}</body></html>'
        belge = FetchedDocument(
            url="https://kurum.gov.tr/x",
            canonical_url="https://kurum.gov.tr/x",
            content=html.encode("windows-1254"),
            media_type="text/html",
            status_code=200,
            charset=None,
        )

        assert metin in belge.text()

    def test_utf8_turkce_karakterler_korunur(self) -> None:
        metin = "İstanbul Şişli Güngören Çankaya Öğrenci"
        belge = _doc(f"<html><body>{metin}</body></html>", charset="utf-8")

        assert belge.text().count(metin) == 1


class TestSsrfKorumasi:
    @pytest.mark.parametrize(
        "url",
        [
            "http://localhost/admin",
            "http://127.0.0.1:8080/",
            "http://169.254.169.254/latest/meta-data/",
            "http://10.0.0.5/internal",
            "http://192.168.1.1/",
            "file:///etc/passwd",
            "ftp://kurum.gov.tr/dosya",
        ],
    )
    def test_ic_ag_ve_desteklenmeyen_semalar_engellenir(self, url: str) -> None:
        with pytest.raises(UnsafeUrlError):
            ensure_safe_url(url)

    def test_izin_verilen_alan_adi_disina_cikilmaz(self) -> None:
        policy = DomainPolicy.build("https://kosgeb.gov.tr", "kosgeb.gov.tr", None)

        ensure_safe_url("https://kosgeb.gov.tr/destek", policy)
        ensure_safe_url("https://www.kosgeb.gov.tr/destek", policy)

        with pytest.raises(UnsafeUrlError):
            ensure_safe_url("https://baska-site.example/destek", policy)

    def test_benzer_alan_adi_kabul_edilmez(self) -> None:
        """`kotukosgeb.gov.tr`, `kosgeb.gov.tr` ile bitse de alt alan adı değildir."""
        policy = DomainPolicy.build("https://kosgeb.gov.tr", None, None)

        assert policy.allows("https://arge.kosgeb.gov.tr/x")
        assert not policy.allows("https://kotukosgeb.gov.tr/x")

    def test_yonlendirme_hedefi_yeniden_dogrulanir(self) -> None:
        """Resmî adres iç ağa yönlendirirse ikinci denetim yakalar."""
        policy = DomainPolicy.build("https://kurum.gov.tr", None, None)

        ensure_safe_url("https://kurum.gov.tr/duyuru", policy)

        with pytest.raises(UnsafeUrlError):
            ensure_safe_url("http://169.254.169.254/latest/meta-data/", policy)


class TestMedyaTuru:
    def test_desteklenmeyen_dosya_turu_reddedilir(self) -> None:
        assert media_type_allowed("text/html")
        assert media_type_allowed("application/pdf")
        assert not media_type_allowed("application/zip")
        assert not media_type_allowed("application/x-msdownload")
        assert not media_type_allowed("")


class TestYapilandirma:
    def test_secicisiz_kaynak_taranabilir_sayilmaz(self) -> None:
        assert not SourceConfig().is_crawlable
        assert SourceConfig(link_selector="a.ilan").is_crawlable
        assert SourceConfig(url_pattern="ilan|duyuru").is_crawlable

    def test_kolonlar_eski_json_govdesini_ezer(self) -> None:
        config = SourceConfig.from_source(
            {
                "configurationJson": '{"linkSelector": "a.eski", "maxPages": 9}',
                "listSelector": "a.yeni",
                "maxPages": 3,
            }
        )

        assert config.link_selector == "a.yeni"
        assert config.max_pages == 3

    def test_bozuk_json_taramayi_dusurmez(self) -> None:
        config = SourceConfig.from_source({"configurationJson": "{bozuk"})
        assert not config.is_crawlable


class TestBaglantiKesfi:
    @staticmethod
    def _links(config: SourceConfig) -> list[str]:
        policy = DomainPolicy.build("https://kurum.gov.tr", None, config.allowed_domains)
        return SourceCrawler._discover_links(_doc(LISTING_HTML, "windows-1254"), policy, config)

    def test_yalnizca_secici_ile_eslesenler_toplanir(self) -> None:
        links = self._links(SourceConfig(link_selector="a.ilan"))

        assert all("/ilan/" in link for link in links)
        assert not any("iletisim" in link or "hakkimizda" in link for link in links)

    def test_url_kalibi_uygulanir(self) -> None:
        links = self._links(SourceConfig(link_selector="a", url_pattern=r"/ilan/\d{4}-2$"))

        assert links == ["https://kurum.gov.tr/ilan/2026-2"]

    def test_kaynak_disi_alan_adi_atlanir(self) -> None:
        links = self._links(SourceConfig(link_selector="a.ilan"))

        assert not any("baska-site.example" in link for link in links)


class TestSayfaSiniri:
    def test_max_pages_gercekten_uygulanir(self) -> None:
        """Kaynağın kendi sınırı eskiden yok sayılıyordu; artık uygulanıyor."""
        alinanlar: list[str] = []

        class SahteFetcher:
            def with_policy(self, policy):  # noqa: ANN001, ANN202
                return self

            def fetch(self, url: str):  # noqa: ANN202
                if url.endswith("/ilanlar"):
                    return _doc(LISTING_HTML, "windows-1254")

                alinanlar.append(url)
                return _doc(
                    "<html><body><h1>Başlık</h1><div class='govde'>İçerik</div></body></html>",
                    "utf-8",
                    url=url,
                )

        def sahte_ingest(**kwargs: object) -> dict[str, object]:
            return {"contentChanged": True}

        crawler = SourceCrawler(SahteFetcher(), sahte_ingest)
        sonuc = crawler.crawl(
            {
                "id": "kaynak-1",
                "name": "Test Kurumu",
                "baseUrl": "https://kurum.gov.tr",
                "startUrl": "/ilanlar",
                "listSelector": "a.ilan",
                "maxPages": 2,
            }
        )

        assert len(alinanlar) == 2, f"maxPages=2 iken {len(alinanlar)} sayfa indirildi"
        assert sonuc.collected == 2

    def test_yapilandirmasiz_kaynak_hic_indirilmez(self) -> None:
        cagrildi = False

        class SahteFetcher:
            def with_policy(self, policy):  # noqa: ANN001, ANN202
                return self

            def fetch(self, url: str):  # noqa: ANN202
                nonlocal cagrildi
                cagrildi = True
                return _doc(LISTING_HTML)

        crawler = SourceCrawler(SahteFetcher(), lambda **_: {"contentChanged": True})
        sonuc = crawler.crawl(
            {"id": "k", "name": "Yapılandırmasız", "baseUrl": "https://kurum.gov.tr"}
        )

        assert not cagrildi, "Yapılandırmasız kaynak için HTTP isteği yapılmamalı"
        assert sonuc.collected == 0
        assert sonuc.errors
