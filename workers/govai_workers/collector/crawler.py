"""Kaynak tarayıcı.

Her kaynağın yapısı farklıdır; bu nedenle seçiciler kaynak kaydındaki `configurationJson`
alanından okunur. Yeni bir kurum eklemek kod değişikliği değil, konfigürasyon değişikliğidir:

    {
      "listUrl": "/duyurular",
      "linkSelector": "a.duyuru-link",
      "titleSelector": "h1.baslik",
      "contentSelector": "div.icerik",
      "maxPages": 3,
      "urlPattern": "duyuru|ilan|cagri"
    }
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from urllib.parse import urljoin

from bs4 import BeautifulSoup

from govai_workers.collector.alaka import alakasiz_mi
from govai_workers.collector.fetcher import FetchedDocument, PoliteFetcher
from govai_workers.collector.safety import DomainPolicy
from govai_workers.config import settings
from govai_workers.logging_setup import get_logger
from govai_workers.parser.extractors import extract_text

log = get_logger(__name__)


@dataclass(slots=True)
class SourceConfig:
    """Kaynağın tarama planı.

    Faz 2'de plan alanları kaynak kaydında kolonlara taşındı; eski
    ``configurationJson`` gövdesi geriye uyumluluk için hâlâ okunur.

    ``link_selector`` artık varsayılan olarak ``"a"`` DEĞİLDİR. Eskiden öyleydi ve
    seçicisi girilmemiş bir kaynak sitedeki bütün bağlantıları (menü, iletişim,
    hakkımızda) ilan sanıp topluyordu.
    """

    list_url: str = ""
    link_selector: str = ""
    title_selector: str = "h1"
    content_selector: str = ""
    url_pattern: str = ""
    max_pages: int = 1
    allowed_domains: str = ""

    #: Yayın takvimi olan kaynaklarda arşiv adresi şablonu.
    #: Örn. "/fihrist?tarih={date}". Boşsa "bugün yayın yok" ayrımı yapılamaz
    #: ve boş liste eskisi gibi seçici arızası sayılır.
    archive_url_template: str = ""

    @property
    def is_crawlable(self) -> bool:
        """Liste seçicisi veya URL kalıbından en az biri olmadan tarama yapılmaz."""
        return bool(self.link_selector.strip() or self.url_pattern.strip())

    @classmethod
    def from_source(cls, source: dict) -> SourceConfig:
        """Önce kaynak kolonlarından, eksik kalanı eski JSON gövdesinden okur."""
        legacy = cls.parse(source.get("configurationJson"))

        def pick(column: str, fallback: str) -> str:
            value = source.get(column)
            return str(value).strip() if value not in (None, "") else fallback

        raw_max = source.get("maxPages")
        try:
            resolved_max = int(raw_max) if raw_max not in (None, "") else legacy.max_pages
        except (TypeError, ValueError):
            resolved_max = legacy.max_pages

        return cls(
            archive_url_template=pick("archiveUrlTemplate", legacy.archive_url_template),
            list_url=pick("startUrl", legacy.list_url),
            link_selector=pick("listSelector", legacy.link_selector),
            title_selector=pick("titleSelector", legacy.title_selector) or "h1",
            content_selector=pick("contentSelector", legacy.content_selector),
            url_pattern=pick("urlPattern", legacy.url_pattern),
            max_pages=max(1, resolved_max),
            allowed_domains=pick("allowedDomains", legacy.allowed_domains),
        )

    @classmethod
    def parse(cls, raw: str | None) -> SourceConfig:
        if not raw:
            return cls()

        try:
            data = json.loads(raw)
        except json.JSONDecodeError:
            log.warning("source_config_invalid_json")
            return cls()

        try:
            max_pages = int(data.get("maxPages", 1))
        except (TypeError, ValueError):
            max_pages = 1

        return cls(
            list_url=data.get("listUrl", ""),
            link_selector=data.get("linkSelector", ""),
            title_selector=data.get("titleSelector", "h1"),
            content_selector=data.get("contentSelector", ""),
            url_pattern=data.get("urlPattern", ""),
            max_pages=max(1, max_pages),
            allowed_domains=data.get("allowedDomains", ""),
            archive_url_template=data.get("archiveUrlTemplate", ""),
        )


@dataclass(slots=True)
class CrawlResult:
    collected: int = 0
    skipped: int = 0
    failed: int = 0
    errors: list[str] = field(default_factory=list)

    def summary(self) -> str:
        return f"{self.collected} doküman alındı, {self.skipped} atlandı, {self.failed} başarısız"


class SourceCrawler:
    """Tek bir kaynağı tarar ve bulduğu ilan sayfalarını API'ye bırakır."""

    def __init__(self, fetcher: PoliteFetcher, ingest) -> None:  # noqa: ANN001 - callable protokolü
        self._fetcher = fetcher
        self._ingest = ingest

    def crawl(self, source: dict, list_url_override: str | None = None) -> CrawlResult:
        """Kaynağı tarar.

        ``list_url_override`` verilirse yapılandırmadaki başlangıç adresi yerine o
        kullanılır. İhale ilanları günlük adreslerde yayımlanır ve adres tarihten
        üretilir; kaynağa sabit bir adres yazılamaz (bkz. ``ihale_takvimi``).
        """
        config = SourceConfig.from_source(source)
        base_url = source["baseUrl"]

        result = CrawlResult()

        # Yapılandırması olmayan kaynak TARANMAZ. Bu bir performans tercihi değil,
        # veri kalitesi kuralıdır: seçicisiz tarama siteyi olduğu gibi toplar.
        if not config.is_crawlable:
            log.warning("source_not_configured", source=source.get("name"))
            result.errors.append(
                "Liste seçicisi veya URL kalıbı tanımlı değil; kaynak taranmadı."
            )
            result.skipped += 1
            return result

        # Tarayıcı bu kaynağın izin verdiği alan adlarının dışına çıkamaz; yönlendirme
        # sonrası da aynı politika yeniden uygulanır.
        policy = DomainPolicy.build(base_url, source.get("officialDomain"), config.allowed_domains)
        self._fetcher.with_policy(policy)

        list_url = (
            list_url_override
            or (urljoin(base_url, config.list_url) if config.list_url else base_url)
        )

        listing = self._safe_fetch(list_url, result)
        if listing is None:
            return result

        links = self._discover_links(listing, policy, config)
        log.info("links_discovered", source=source["name"], count=len(links))

        # Kaynağın kendi sınırı ile genel üst sınırın küçüğü uygulanır. Eskiden yalnızca
        # genel ayar okunuyordu ve kaynağın maxPages değeri hiçbir şey ifade etmiyordu.
        page_limit = min(config.max_pages, settings.crawl_max_pages)

        for link in links[:page_limit]:
            document = self._safe_fetch(link, result)
            if document is None:
                result.skipped += 1
                continue

            try:
                title, content = self._extract(document, config)
                if not content.strip():
                    result.skipped += 1
                    continue

                response = self._ingest(
                    source_id=source["id"],
                    url=document.url,
                    title=title,
                    raw_content=content,
                    media_type=document.media_type,
                    canonical_url=document.canonical_url,
                    charset=document.charset,
                    http_status_code=document.status_code,
                )

                if response and response.get("contentChanged", True):
                    result.collected += 1
                else:
                    result.skipped += 1

            except Exception as exc:  # noqa: BLE001 - tek doküman hatası taramayı durdurmamalı
                result.failed += 1
                result.errors.append(f"{link}: {exc}")
                log.exception("document_ingest_failed", url=link)

        return result

    def _safe_fetch(self, url: str, result: CrawlResult) -> FetchedDocument | None:
        try:
            return self._fetcher.fetch(url)
        except Exception as exc:  # noqa: BLE001
            result.failed += 1
            result.errors.append(f"{url}: {exc}")
            log.warning("fetch_failed", url=url, error=str(exc))
            return None

    @staticmethod
    def _discover_links(
        listing: FetchedDocument, policy: DomainPolicy, config: SourceConfig
    ) -> list[str]:
        if listing.is_pdf:
            return [listing.canonical_url]

        # Doğru karakter kümesiyle çözülmüş metin verilir; aksi hâlde Türkçe karakterler
        # bozulur ve başlıklar hatalı çıkar.
        soup = BeautifulSoup(listing.text(), "lxml")
        pattern = re.compile(config.url_pattern, re.IGNORECASE) if config.url_pattern else None

        links: list[str] = []
        seen: set[str] = set()

        for anchor in soup.select(config.link_selector):
            href = anchor.get("href")
            if not href or href.startswith(("#", "mailto:", "javascript:")):
                continue

            absolute = urljoin(listing.canonical_url, href)

            # Kaynak dışı alan adlarına çıkma; tarayıcı izin verilen alan adlarında kalmalı.
            if not policy.allows(absolute):
                continue

            if pattern is not None and not pattern.search(absolute):
                continue

            # Konuya alaka: yukarıdaki üç süzgeç de ADRESE bakar. Kurum siteleri çağrı
            # listesiyle aynı bölümde "Çerez Politikası", "KVKK" ve "İletişim"e de
            # bağlantı verir; bunlar desene takılmadan geçer, indirilir ve çöp kayıt
            # üretir. Kararsız kalınan bağlantı GEÇİRİLİR — elenen bir çağrı hiç
            # görülmez, geçen bir çöp görülür ve silinir.
            if alakasiz_mi(absolute, anchor.get_text(" ", strip=True)):
                continue

            if absolute in seen:
                continue

            seen.add(absolute)
            links.append(absolute)

        return links

    @staticmethod
    def _extract(document: FetchedDocument, config: SourceConfig) -> tuple[str, str]:
        if document.is_pdf:
            text = extract_text(document.content, "application/pdf")
            title = text.strip().splitlines()[0][:300] if text.strip() else document.url
            return title, text

        soup = BeautifulSoup(document.text(), "lxml")

        title_node = soup.select_one(config.title_selector) if config.title_selector else None
        title = (title_node.get_text(strip=True) if title_node else None) or (
            soup.title.get_text(strip=True) if soup.title else document.url
        )

        if config.content_selector:
            content_node = soup.select_one(config.content_selector)
            html = str(content_node) if content_node else str(soup)
        else:
            html = str(soup)

        return title[:300], html
