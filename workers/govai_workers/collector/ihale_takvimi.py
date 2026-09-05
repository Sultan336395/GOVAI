"""Resmî Gazete ihale ilanlarının tarih takvimi (Faz 2).

İhale ilanları günlük yayımlanır ve her günün ilan bölümü kendi adresindedir::

    /ilanlar/eskiilanlar/{yyyy}/{MM}/{yyyyMMdd}-3.htm

Adres tarihe bağlı olduğu için kaynağa **sabit** bir başlangıç adresi yazılamaz:
bugün doğru olan adres yarın eski sayıyı gösterir. Bu modül adresi günün tarihinden
üretir ve hangi günlerin taranacağına karar verir.

Üç kural işi güvenli kılar:

1. **Gün Türkiye saatine göre belirlenir.** Sunucu UTC çalışır; UTC'ye göre "bugün"
   Türkiye'de gece yarısından sonraki üç saat boyunca YANLIŞ günü gösterir ve o
   günün ilanları hiç toplanmaz.

2. **Gelecek taranmaz.** Yayımlanmamış bir sayının adresi 404 döner; bunu arıza
   saymak kaynağı boşuna bozuk işaretler.

3. **Kaçırılan günler tamamlanır ama sınırlıdır.** Sistem birkaç gün çalışmadıysa
   aradaki günler sonradan taranır; üst sınır olmadan uzun bir kesinti resmî
   sunucuya yüzlerce istek gönderirdi.
"""

from __future__ import annotations

import re
from datetime import date, datetime, timedelta
from urllib.parse import urljoin
from zoneinfo import ZoneInfo

#: Resmî Gazete Türkiye saatine göre yayımlanır.
ISTANBUL = ZoneInfo("Europe/Istanbul")

#: Tek çalıştırmada en fazla kaç gün geriye gidilir. Uzun bir kesinti sonrası resmî
#: sunucuya yüzlerce istek göndermemek için üst sınır.
AZAMI_GERI_GUN = 14

#: İhale bölümünün sayı eki. Resmî Gazete'nin ilan bölümleri 3, 4, 5 diye numaralanır;
#: ihale ilanları 3'tedir.
IHALE_BOLUMU = 3

#: Kaynağa yazılan şablon. Kod içine tarih GÖMÜLMEZ; yalnızca yer tutucular durur.
VARSAYILAN_SABLON = "/ilanlar/eskiilanlar/{yyyy}/{MM}/{yyyyMMdd}-3.htm"


def bugun_istanbul(simdi: datetime | None = None) -> date:
    """Türkiye saatine göre bugünün tarihi.

    ``simdi`` testten verilir; üretimde geçerli an kullanılır. Zaman dilimi bilgisi
    olmayan bir ``datetime`` UTC sayılır — sunucular UTC çalışır.
    """
    an = simdi or datetime.now(tz=ISTANBUL)

    if an.tzinfo is None:
        an = an.replace(tzinfo=ZoneInfo("UTC"))

    return an.astimezone(ISTANBUL).date()


def ihale_adresi(sablon: str, base_url: str, gun: date) -> str:
    """Şablondan günün ilan adresini üretir.

    Yer tutucular: ``{yyyy}``, ``{MM}``, ``{dd}``, ``{yyyyMMdd}``, ``{date}``.
    Ay ve gün **sıfır dolgulu**dur; Eylül ``09`` olarak yazılır, ``9`` değil.
    """
    yol = (
        sablon.replace("{yyyy}", f"{gun.year:04d}")
        .replace("{MM}", f"{gun.month:02d}")
        .replace("{dd}", f"{gun.day:02d}")
        .replace("{yyyyMMdd}", gun.strftime("%Y%m%d"))
        .replace("{yyyymmdd}", gun.strftime("%Y%m%d"))
        .replace("{date}", gun.isoformat())
    )

    return urljoin(base_url, yol)


def taranacak_gunler(
    son_basarili: date | None,
    bugun: date,
    azami_geri_gun: int = AZAMI_GERI_GUN,
) -> list[date]:
    """Bu çalıştırmada hangi günler taranacak?

    * Son başarılı tarama yoksa yalnızca bugün taranır — ilk çalıştırma tüm arşivi
      indirmeye kalkmamalıdır.
    * Son başarılı taramadan sonraki günler sırayla tamamlanır (kaçırılan günler).
    * Gelecek tarih hiçbir koşulda listeye girmez.
    * Liste ``azami_geri_gun`` ile sınırlıdır ve **en eski günden başlar**.

    Sıra kritiktir. En yeni günler önce alınsaydı, üst sınırı aşan bir kesintide
    aradaki günler ATLANIR ve bir daha hiç toplanmazdı: son başarılı tarih bugüne
    sıçrar, geride kalan günler kalıcı olarak kaybolurdu. Kronolojik sırada ise her
    çalıştırma son başarılı günün hemen ardından devam eder ve birkaç turda açık
    kapanır — güncel ilanlar birkaç tur gecikir ama hiçbir gün kaybolmaz.
    """
    if son_basarili is None:
        return [bugun]

    if son_basarili >= bugun:
        # Bugün zaten tarandı; yeniden taramak mükerrer kayıt üretmez ama gereksiz
        # istek gönderir. Yine de bugünü döndürürüz: gün içinde yeni ilan eklenebilir.
        return [bugun]

    gun_sayisi = (bugun - son_basarili).days
    adet = min(gun_sayisi, azami_geri_gun)

    gunler = [son_basarili + timedelta(days=k) for k in range(1, adet + 1)]

    # Gelecek asla taranmaz.
    return [g for g in gunler if g <= bugun]


def tekil_ilan_mi(url: str) -> bool:
    """Adres tekil bir ihale ilanı PDF'i mi?

    Bölüm sayfası (``20260906-3.htm``) tekil ilan DEĞİLDİR: içinde onlarca ayrı ilan
    barındırır ve başlığı hepsinin ortak başlığıdır. Tekil ilanların adresinde bölüm
    numarasından sonra bir de sıra numarası vardır: ``20260906-3-2.pdf``.
    """
    return bool(re.search(r"/\d{8}-\d+-\d+\.pdf$", url, re.IGNORECASE))
