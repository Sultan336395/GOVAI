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
from typing import Any
from urllib.parse import urljoin

from govai_workers.collector.crawler import SourceConfig, SourceCrawler
from govai_workers.collector.fetcher import PoliteFetcher
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

    def to_payload(self) -> dict[str, Any]:
        return {
            "reachable": self.reachable,
            "discoveredLinkCount": self.discovered_link_count,
            "httpStatusCode": self.http_status_code,
            "finalUrl": self.final_url,
            "charset": self.charset,
            "failureReason": self.failure_reason,
        }

    def summary(self) -> str:
        basarili = self.reachable and self.discovered_link_count > 0
        durum = "DOĞRULANDI" if basarili else "DOĞRULANAMADI"
        return (
            f"{durum} | {self.source_name} | istenen={self.requested_url} "
            f"| son={self.final_url or '-'} | http={self.http_status_code or '-'} "
            f"| charset={self.charset or '-'} | bağlantı={self.discovered_link_count} "
            f"| süre={self.duration_seconds:.1f}s"
            + (f" | neden={self.failure_reason}" if self.failure_reason else "")
        )


def verify_source(source: dict[str, Any]) -> VerificationResult:
    """Tek bir kaynağı canlı doğrular. Hiçbir belge kaydetmez."""
    config = SourceConfig.from_source(source)
    base_url = source["baseUrl"]
    requested = urljoin(base_url, config.list_url) if config.list_url else base_url

    result = VerificationResult(
        source_id=source["id"],
        source_name=source["name"],
        requested_url=requested,
    )

    if not config.is_crawlable:
        result.failure_reason = "Liste seçicisi veya URL kalıbı tanımlı değil."
        return result

    policy = DomainPolicy.build(base_url, source.get("officialDomain"), config.allowed_domains)
    started = time.monotonic()

    try:
        with PoliteFetcher(policy) as fetcher:
            document = fetcher.fetch(requested)

            if document is None:
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

            if not links:
                result.failure_reason = "Seçici hiç bağlantı çıkarmadı."

    except UnsafeUrlError as exc:
        result.failure_reason = f"Güvenlik denetimi reddetti: {exc}"
    except Exception as exc:  # noqa: BLE001 - bir kaynağın hatası diğerlerini durdurmamalı
        result.failure_reason = f"{type(exc).__name__}: {exc}"[:400]
    finally:
        result.duration_seconds = time.monotonic() - started

    return result
