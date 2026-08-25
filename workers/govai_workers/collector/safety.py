"""Tarama güvenliği: SSRF koruması, alan adı sınırı ve medya türü denetimi.

Tarayıcı, kaynak kaydındaki adreslere gider. O adresler platform yöneticisi tarafından
girilir ve yönlendirmelerle değişebilir; yani tarayıcı **dışarıdan yönlendirilebilir bir
istemcidir**. Bu yüzden her istekten önce ve her yönlendirmeden sonra hedefin gerçekten
izin verilen bir resmî adres olduğu yeniden doğrulanır.

Engellenenler:

* ``localhost`` ve döngü adresleri
* özel ağ blokları (RFC 1918), link-local, benzersiz yerel adresler
* bulut metadata uçları (169.254.169.254 ve benzerleri)
* http/https dışındaki şemalar
* kaynağın izin verilen alan adları dışındaki adresler
"""

from __future__ import annotations

import ipaddress
import socket
from dataclasses import dataclass
from urllib.parse import urlparse

from govai_workers.logging_setup import get_logger

log = get_logger(__name__)

#: Bulut sağlayıcıların örnek kimlik bilgisi sunan metadata uçları.
BLOCKED_HOSTS = frozenset(
    {
        "localhost",
        "metadata.google.internal",
        "metadata.goog",
        "instance-data",
    }
)

ALLOWED_SCHEMES = frozenset({"http", "https"})

#: Resmî kaynaklardan kabul edilen belge türleri. Bunun dışındakiler indirilmez.
DEFAULT_ALLOWED_MEDIA_TYPES = (
    "text/html",
    "application/xhtml+xml",
    "application/pdf",
    "text/plain",
    "application/rss+xml",
    "application/atom+xml",
    "application/xml",
    "text/xml",
)


class UnsafeUrlError(Exception):
    """Adres güvenlik denetiminden geçemedi."""


@dataclass(frozen=True, slots=True)
class DomainPolicy:
    """Bir kaynağın gidebileceği alan adları."""

    allowed_hosts: frozenset[str]

    @classmethod
    def build(cls, base_url: str, official_domain: str | None, extra: str | None) -> DomainPolicy:
        hosts: set[str] = set()

        for candidate in (urlparse(base_url).netloc, official_domain):
            if candidate:
                hosts.add(candidate.strip().lower().split(":")[0])

        for item in (extra or "").split(","):
            host = item.strip().lower().split(":")[0]
            if host:
                hosts.add(host)

        return cls(frozenset(h for h in hosts if h))

    def allows(self, url: str) -> bool:
        """Adres izin verilen alan adlarından birine mi ait?

        Alt alan adları kabul edilir (``x.kosgeb.gov.tr`` → ``kosgeb.gov.tr``), fakat
        yalnızca gerçek bir nokta sınırında: ``kotukosgeb.gov.tr`` eşleşmez.
        """
        host = urlparse(url).netloc.lower().split(":")[0]
        if not host:
            return False

        return any(
            host == allowed or host.endswith("." + allowed)
            for allowed in self.allowed_hosts
        )


def _is_private_address(host: str) -> bool:
    """Ad çözümlemesi yaparak hedefin özel/iç bir adrese düşüp düşmediğine bakar."""
    try:
        infos = socket.getaddrinfo(host, None)
    except OSError:
        # Çözümlenemeyen ad zaten indirilemez; burada engellemeye gerek yok.
        return False

    for info in infos:
        raw = info[4][0]
        try:
            address = ipaddress.ip_address(raw)
        except ValueError:
            continue

        if (
            address.is_private
            or address.is_loopback
            or address.is_link_local
            or address.is_reserved
            or address.is_multicast
            or address.is_unspecified
        ):
            return True

    return False


def ensure_safe_url(url: str, policy: DomainPolicy | None = None) -> None:
    """Adres güvenli değilse :class:`UnsafeUrlError` yükseltir.

    Bu işlev hem ilk istekten önce hem de **her yönlendirmeden sonra** çağrılır:
    resmî bir adres, iç ağdaki bir adrese yönlendirebilir.
    """
    parsed = urlparse(url)

    if parsed.scheme not in ALLOWED_SCHEMES:
        raise UnsafeUrlError(f"Desteklenmeyen şema: {parsed.scheme or '(yok)'}")

    host = parsed.netloc.lower().split(":")[0]
    if not host:
        raise UnsafeUrlError("Adreste alan adı yok.")

    if host in BLOCKED_HOSTS:
        raise UnsafeUrlError(f"Engellenen ana makine: {host}")

    if _is_private_address(host):
        raise UnsafeUrlError(f"İç ağ adresine istek gönderilemez: {host}")

    if policy is not None and not policy.allows(url):
        raise UnsafeUrlError(f"Kaynağın izin verilen alan adları dışında: {host}")


def media_type_allowed(media_type: str, allowed: tuple[str, ...] | None = None) -> bool:
    """Medya türü kabul listesinde mi?"""
    if not media_type:
        return False

    normalized = media_type.split(";")[0].strip().lower()
    return normalized in (allowed or DEFAULT_ALLOWED_MEDIA_TYPES)
