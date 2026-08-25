"""HTTP indirme katmanı.

Resmî kaynaklara karşı nazik davranmak zorunludur: robots.txt'e uyulur, istekler arasında
gecikme bırakılır ve gövde boyutu sınırlanır. Bu, hem etik hem de kaynağın bizi engellememesi
için operasyonel bir gerekliliktir.

Faz 2 düzeltmeleri:

* ``charset`` artık kaybolmuyor. Eskiden ``content-type`` başlığı ``;`` işaretinden
  bölünüp yalnızca medya türü saklanıyordu; karakter kümesi atılınca Türkçe karakterler
  ayrıştırma sırasında bozuluyordu. Artık başlıktan okunur, yoksa gövdeden tespit edilir
  ve indirilen belgeyle birlikte taşınır.
* Yönlendirmeler **elle** izlenir. ``httpx``'in kendi takibi açık bırakılırsa resmî bir
  adres iç ağdaki bir adrese yönlendirebilir ve hiçbir denetim çalışmaz; her adım
  ayrı ayrı doğrulanır.
* TLS doğrulaması hiçbir yerde kapatılmaz.
"""

from __future__ import annotations

import re
import time
from dataclasses import dataclass
from urllib.parse import urljoin, urlparse
from urllib.robotparser import RobotFileParser

import httpx
from tenacity import retry, retry_if_exception_type, stop_after_attempt, wait_exponential

from govai_workers.collector.safety import (
    DomainPolicy,
    UnsafeUrlError,
    ensure_safe_url,
    media_type_allowed,
)
from govai_workers.config import settings
from govai_workers.logging_setup import get_logger

log = get_logger(__name__)

MAX_REDIRECTS = 5

#: HTML gövdesinde bildirilen karakter kümesi (``<meta charset=...>``).
_META_CHARSET = re.compile(
    rb"""<meta[^>]+charset\s*=\s*["']?\s*([a-zA-Z0-9_\-]+)""",
    re.IGNORECASE,
)


@dataclass(slots=True)
class FetchedDocument:
    url: str
    """İsteğin başlatıldığı adres."""

    canonical_url: str
    """Yönlendirmeler sonrası ulaşılan nihai adres."""

    content: bytes
    media_type: str
    status_code: int
    charset: str | None = None
    last_modified: str | None = None

    @property
    def is_pdf(self) -> bool:
        return "pdf" in self.media_type.lower() or self.canonical_url.lower().endswith(".pdf")

    def text(self) -> str:
        """Gövdeyi doğru karakter kümesiyle çözer.

        Sıra: sunucunun bildirdiği charset → gövdedeki ``<meta charset>`` → UTF-8.
        Son çare olarak hatalı baytlar atılır; içeriğin tamamını kaybetmektense
        birkaç karakteri kaybetmek yeğdir.
        """
        for encoding in (self.charset, _sniff_meta_charset(self.content), "utf-8"):
            if not encoding:
                continue
            try:
                return self.content.decode(encoding)
            except (LookupError, UnicodeDecodeError):
                continue

        return self.content.decode("utf-8", errors="replace")


def _sniff_meta_charset(content: bytes) -> str | None:
    match = _META_CHARSET.search(content[:4096])
    return match.group(1).decode("ascii", errors="ignore") if match else None


def _parse_content_type(header: str) -> tuple[str, str | None]:
    """``text/html; charset=utf-8`` → ``("text/html", "utf-8")``."""
    parts = header.split(";")
    media_type = parts[0].strip().lower() or "application/octet-stream"

    for part in parts[1:]:
        key, _, value = part.partition("=")
        if key.strip().lower() == "charset":
            return media_type, value.strip().strip('"\'') or None

    return media_type, None


class PoliteFetcher:
    """robots.txt'e uyan, hız sınırlı ve SSRF'e karşı korunmuş HTTP istemcisi."""

    def __init__(self, policy: DomainPolicy | None = None) -> None:
        self._client = httpx.Client(
            # Yönlendirmeler elle izlenir; her adım güvenlik denetiminden geçmelidir.
            follow_redirects=False,
            timeout=settings.api_timeout_seconds,
            headers={"User-Agent": settings.crawl_user_agent},
        )
        self._robots: dict[str, RobotFileParser | None] = {}
        self._last_request_at: float = 0.0
        self._policy = policy

    def with_policy(self, policy: DomainPolicy) -> PoliteFetcher:
        """Kaynağa özgü alan adı politikasını bağlar."""
        self._policy = policy
        return self

    def can_fetch(self, url: str) -> bool:
        if not settings.respect_robots_txt:
            return True

        parsed = urlparse(url)
        origin = f"{parsed.scheme}://{parsed.netloc}"

        if origin not in self._robots:
            self._robots[origin] = self._load_robots(origin)

        parser = self._robots[origin]
        if parser is None:
            # robots.txt okunamadıysa engellenmediğimizi varsayarız (RFC 9309 davranışı).
            return True

        return parser.can_fetch(settings.crawl_user_agent, url)

    def _load_robots(self, origin: str) -> RobotFileParser | None:
        try:
            response = self._client.get(urljoin(origin, "/robots.txt"))
            if response.status_code != httpx.codes.OK:
                return None

            parser = RobotFileParser()
            parser.parse(response.text.splitlines())
            return parser
        except httpx.HTTPError:
            return None

    def _throttle(self) -> None:
        elapsed = time.monotonic() - self._last_request_at
        if elapsed < settings.crawl_delay_seconds:
            time.sleep(settings.crawl_delay_seconds - elapsed)
        self._last_request_at = time.monotonic()

    @retry(
        retry=retry_if_exception_type(httpx.TransportError),
        stop=stop_after_attempt(3),
        wait=wait_exponential(multiplier=2, min=2, max=30),
        reraise=True,
    )
    def fetch(self, url: str) -> FetchedDocument | None:
        """Adresi indirir. Güvenli değilse :class:`UnsafeUrlError` yükseltir."""
        ensure_safe_url(url, self._policy)

        if not self.can_fetch(url):
            log.info("robots_disallow", url=url)
            return None

        current = url

        for _ in range(MAX_REDIRECTS + 1):
            self._throttle()

            with self._client.stream("GET", current) as response:
                if response.is_redirect:
                    location = response.headers.get("location")
                    if not location:
                        log.warning("redirect_without_location", url=current)
                        return None

                    target = urljoin(current, location)

                    # Yönlendirme sonrası hedef YENİDEN doğrulanır: resmî bir adres
                    # iç ağa ya da başka bir alan adına yönlendirebilir.
                    ensure_safe_url(target, self._policy)

                    log.info("redirect_followed", frm=current, to=target)
                    current = target
                    continue

                if response.status_code != httpx.codes.OK:
                    log.warning("fetch_non_ok", url=current, status=response.status_code)
                    return None

                media_type, charset = _parse_content_type(
                    response.headers.get("content-type", "application/octet-stream")
                )

                if not media_type_allowed(media_type):
                    log.info("media_type_rejected", url=current, media_type=media_type)
                    return None

                chunks: list[bytes] = []
                total = 0
                for chunk in response.iter_bytes():
                    total += len(chunk)
                    if total > settings.crawl_max_document_bytes:
                        log.warning("document_too_large", url=current, bytes=total)
                        return None
                    chunks.append(chunk)

                return FetchedDocument(
                    url=url,
                    canonical_url=str(response.url),
                    content=b"".join(chunks),
                    media_type=media_type,
                    status_code=response.status_code,
                    charset=charset,
                    last_modified=response.headers.get("last-modified"),
                )

        log.warning("too_many_redirects", url=url)
        return None

    def close(self) -> None:
        self._client.close()

    def __enter__(self) -> PoliteFetcher:
        return self

    def __exit__(self, *_: object) -> None:
        self.close()


__all__ = ["FetchedDocument", "PoliteFetcher", "UnsafeUrlError"]
