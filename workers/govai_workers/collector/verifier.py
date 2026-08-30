"""Kaynak doğrulama.

Bir kaynağın taranabilir sayılması için seçicisinin **gerçekten çalıştığının** canlı
olarak kanıtlanması gerekir. Doğrulama tek bir sayfa indirir, seçiciyi uygular ve kaç
bağlantı çıktığını sayar; hiçbir belge kaydetmez.

Erişilemeyen ya da seçicisi tutmayan kaynak başarılı gösterilmez: nedeniyle birlikte
pasif kalır. TLS doğrulaması kapatılmaz, anti-bot engeli aşılmaya çalışılmaz.
"""

from __future__ import annotations

import time
from dataclasses import dataclass
from datetime import date
from typing import Any
from urllib.parse import urljoin

from govai_workers.collector.crawler import SourceConfig, SourceCrawler
from govai_workers.collector.fetcher import PoliteFetcher
from govai_workers.collector.publication import (
    PublicationOutcome,
    son_yayini_bul,
)
from govai_workers.collector.safety import DomainPolicy, UnsafeUrlError
from govai_workers.logging_setup import get_logger

log = get_logger(__name__)


@dataclass(slots=True)
class VerificationResult:
    source_id: str
    source_name: str
    requested_url: str
    reachable: bool = False
    discovered_link_count: int = 0
    http_status_code: int | None = None
    final_url: str | None = None
    charset: str | None = None
    failure_reason: str | None = None
    duration_seconds: float = 0.0

    #: Erişim başarılı ama yeni sayı yok mu, yoksa seçici mi bozuldu?
    outcome: PublicationOutcome = PublicationOutcome.UNREACHABLE

    #: Arşivden bulunan en son yayın tarihi (varsa).
    last_published_on: str | None = None

    def to_payload(self) -> dict[str, Any]:
        return {
            "reachable": self.reachable,
            "discoveredLinkCount": self.discovered_link_count,
            "httpStatusCode": self.http_status_code,
            "finalUrl": self.final_url,
            "charset": self.charset,
            "failureReason": self.failure_reason,
            "outcome": self.outcome.value,
            "lastPublishedOn": self.last_published_on,
        }

    @property
    def is_healthy(self) -> bool:
        """"Bugün yayın yok" bir arıza DEĞİLDİR."""
        return self.outcome.is_healthy

    def summary(self) -> str:
        durum = {
            PublicationOutcome.VERIFIED: "DOĞRULANDI",
            PublicationOutcome.NO_NEW_CONTENT: "DOĞRULANDI (yeni yayın yok)",
            PublicationOutcome.SELECTOR_BROKEN: "SEÇİCİ BOZUK",
            PublicationOutcome.UNREACHABLE: "ERİŞİLEMEDİ",
        }[self.outcome]
        return (
            f"{durum} | {self.source_name} | istenen={self.requested_url} "
            f"| son={self.final_url or '-'} | http={self.http_status_code or '-'} "
            f"| charset={self.charset or '-'} | bağlantı={self.discovered_link_count} "
            f"| süre={self.duration_seconds:.1f}s"
            + (f" | neden={self.failure_reason}" if self.failure_reason else "")
        )


def verify_source(source: dict[str, Any], today: date | None = None) -> VerificationResult:
    """Tek bir kaynağı canlı doğrular. Hiçbir belge kaydetmez.

    ``today`` yalnızca testte verilir; üretimde sistem tarihi kullanılır.
    Takvim koda YAZILMAZ.
    """
    config = SourceConfig.from_source(source)
    base_url = source["baseUrl"]
    requested = urljoin(base_url, config.list_url) if config.list_url else base_url

    result = VerificationResult(
        source_id=source["id"],
        source_name=source["name"],
        requested_url=requested,
    )

    if not config.is_crawlable:
        result.outcome = PublicationOutcome.SELECTOR_BROKEN
        result.failure_reason = "Liste seçicisi veya URL kalıbı tanımlı değil."
        return result

    policy = DomainPolicy.build(base_url, source.get("officialDomain"), config.allowed_domains)
    started = time.monotonic()

    try:
        with PoliteFetcher(policy) as fetcher:
            document = fetcher.fetch(requested)

            if document is None:
                result.outcome = PublicationOutcome.UNREACHABLE
                result.failure_reason = (
                    "Adres indirilemedi (robots.txt engeli, desteklenmeyen içerik türü "
                    "ya da başarısız yanıt)."
                )
                return result

            result.reachable = True
            result.http_status_code = document.status_code
            result.final_url = document.canonical_url
            result.charset = document.charset

            links = SourceCrawler._discover_links(document, policy, config)
            result.discovered_link_count = len(links)

            if links:
                result.outcome = PublicationOutcome.VERIFIED
                return result

            # Bağlantı yok. Bu üç ayrı şey olabilir ve ayırmadan karar veremeyiz:
            # bugün yayın yok, seçici bozuldu, ya da sayfa yapısı değişti.
            if not config.archive_url_template:
                result.outcome = PublicationOutcome.SELECTOR_BROKEN
                result.failure_reason = "Seçici hiç bağlantı çıkarmadı."
                return result

            arsiv = son_yayini_bul(
                fetch=fetcher.fetch,
                link_cikar=lambda d: SourceCrawler._discover_links(d, policy, config),
                template=config.archive_url_template,
                base_url=base_url,
                bugun=today or date.today(),
            )

            result.outcome = arsiv.outcome
            result.discovered_link_count = len(arsiv.links)
            result.last_published_on = (
                arsiv.published_on.isoformat() if arsiv.published_on else None
            )

            if arsiv.outcome is PublicationOutcome.NO_NEW_CONTENT:
                # Kaynak sağlıklı: seçici arşivde çalışıyor, bugün yeni sayı yok.
                log.info(
                    "publication_no_new_content",
                    source=source["name"],
                    last_published_on=result.last_published_on,
                )
            elif arsiv.outcome is PublicationOutcome.VERIFIED:
                # Ana sayfa boştu ama arşivde bugünün sayısı var: seçici çalışıyor.
                log.info(
                    "publication_found_via_archive",
                    source=source["name"],
                    published_on=result.last_published_on,
                )
            else:
                result.failure_reason = arsiv.note

    except UnsafeUrlError as exc:
        result.outcome = PublicationOutcome.UNREACHABLE
        result.failure_reason = f"Güvenlik denetimi reddetti: {exc}"
    except Exception as exc:  # noqa: BLE001 - bir kaynağın hatası diğerlerini durdurmamalı
        result.outcome = PublicationOutcome.UNREACHABLE
        result.failure_reason = f"{type(exc).__name__}: {exc}"[:400]
    finally:
        result.duration_seconds = time.monotonic() - started

    return result
