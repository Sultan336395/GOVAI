"""Yayın takvimi olan kaynaklarda "bugün yayın yok" durumu.

**Sorun:** Resmî Gazete hafta sonu ve resmî tatillerde yayımlanmaz. O günlerde ana
sayfa erişilebilir ve sağlamdır, ama günün sayısına ait hiçbir bağlantı içermez.
Doğrulama bunu "seçici hiç bağlantı çıkarmadı" sayıp kaynağı arızalı işaretliyordu.

Bu, üç ayrı durumu birbirine karıştırmaktı:

======================  ==================================================
Durum                   Ne anlama gelir
======================  ==================================================
``NO_NEW_CONTENT``      Kaynak sağlıklı, seçici çalışıyor, bugün yayın yok.
``SELECTOR_BROKEN``     Sayfa duruyor ama yapısı değişmiş; seçici tutmuyor.
``UNREACHABLE``         Ağ, TLS ya da robots engeli.
======================  ==================================================

Ayrımı **takvime bakarak değil kanıtla** yapıyoruz: kaynağın arşivi geriye doğru
taranır. Son günlerde yayımlanmış bir sayı bulunabiliyorsa seçici çalışıyor
demektir ve bugünkü boşluk gerçekten "yayın yok"tur. Arşivde de hiçbir şey
bulunamıyorsa sorun seçicidedir.

Hiçbir tarih koda yazılmaz; "bugün" dışarıdan verilir ve arşiv adresi kaynağın
yapılandırmasından gelir.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from datetime import date, timedelta
from enum import StrEnum
from urllib.parse import urljoin

from govai_workers.logging_setup import get_logger

log = get_logger(__name__)

#: Arşivde en fazla kaç gün geriye bakılır. Resmî Gazete'de en uzun yayın arası
#: bayram tatilleridir; dokuz gün onu da kapsar.
VARSAYILAN_GERIYE_GUN = 9


class PublicationOutcome(StrEnum):
    """Doğrulamanın sonucu."""

    VERIFIED = "Verified"
    NO_NEW_CONTENT = "NoNewContent"
    SELECTOR_BROKEN = "SelectorBroken"
    UNREACHABLE = "Unreachable"

    @property
    def is_healthy(self) -> bool:
        """Kaynak sağlıklı sayılır mı? "Yayın yok" bir arıza DEĞİLDİR."""
        return self in (PublicationOutcome.VERIFIED, PublicationOutcome.NO_NEW_CONTENT)


@dataclass(slots=True)
class ArchiveLookup:
    """Arşiv taramasının sonucu."""

    outcome: PublicationOutcome
    #: Bulunan en son yayının tarihi.
    published_on: date | None = None
    #: O yayında eşleşen bağlantılar.
    links: list[str] = field(default_factory=list)
    #: Kaç gün geriye bakıldı.
    searched_days: int = 0
    note: str = ""


def archive_url(template: str, base_url: str, gun: date) -> str:
    """Arşiv adresini üretir.

    Şablonda ``{date}`` ISO biçimiyle (2026-08-27), ``{yyyymmdd}`` ise sıkışık
    biçimle (20260827) değiştirilir.
    """
    yol = template.replace("{date}", gun.isoformat()).replace("{yyyymmdd}", gun.strftime("%Y%m%d"))
    return urljoin(base_url, yol)


def son_yayini_bul(
    fetch,
    link_cikar,
    template: str,
    base_url: str,
    bugun: date,
    geriye_gun: int = VARSAYILAN_GERIYE_GUN,
) -> ArchiveLookup:
    """Arşivde geriye doğru en son yayımlanmış sayıyı arar.

    ``fetch(url)`` belgeyi döner ya da ``None``; ``link_cikar(document)`` seçiciyi
    uygulayıp eşleşen bağlantıları döner. İkisi de dışarıdan verilir; bu fonksiyon
    ağ ya da HTML ayrıntısı bilmez ve test edilebilir kalır.
    """
    erisilen_gun = 0

    for gecmis in range(geriye_gun + 1):
        gun = bugun - timedelta(days=gecmis)
        adres = archive_url(template, base_url, gun)

        belge = fetch(adres)

        if belge is None:
            # Tek bir günün arşivi açılmadı: bu tek başına arıza değildir.
            continue

        erisilen_gun += 1
        links = link_cikar(belge)

        if links:
            return ArchiveLookup(
                outcome=(
                    PublicationOutcome.VERIFIED if gecmis == 0
                    else PublicationOutcome.NO_NEW_CONTENT
                ),
                published_on=gun,
                links=list(links),
                searched_days=gecmis + 1,
                note=(
                    "Bugünün sayısı yayımlanmış."
                    if gecmis == 0
                    else f"Bugün yayın yok; en son {gun.isoformat()} sayısı bulundu."
                ),
            )

    if erisilen_gun == 0:
        # Hiçbir arşiv sayfası açılamadı: bu bir erişim sorunudur.
        return ArchiveLookup(
            outcome=PublicationOutcome.UNREACHABLE,
            searched_days=geriye_gun + 1,
            note="Arşiv sayfalarının hiçbirine erişilemedi.",
        )

    # Arşiv sayfaları açıldı ama hiçbirinde eşleşen bağlantı yok: seçici tutmuyor.
    return ArchiveLookup(
        outcome=PublicationOutcome.SELECTOR_BROKEN,
        searched_days=geriye_gun + 1,
        note=(
            f"{erisilen_gun} arşiv sayfası açıldı, hiçbirinde eşleşen bağlantı yok; "
            "seçici ya da URL kalıbı büyük olasılıkla bozuldu."
        ),
    )


#: Yayımlanmış bir sayının bağlantısında geçen tarih (…/2026/08/20260827-3.htm).
_TARIH_KALIBI = re.compile(r"/(\d{4})/(\d{2})/(\d{4})(\d{2})(\d{2})")


def baglantidan_tarih(url: str) -> date | None:
    """Bağlantıdaki yayın tarihini çıkarır; yoksa ``None``.

    Aynı sayının iki kez işlenip işlenmediğini anlamak için kullanılır.
    """
    m = _TARIH_KALIBI.search(url)

    if not m:
        return None

    try:
        return date(int(m.group(3)), int(m.group(4)), int(m.group(5)))
    except ValueError:
        return None
