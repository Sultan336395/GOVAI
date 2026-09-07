"""Çağrı metninden mevzuat dayanağı çıkarımı (Faz 3).

Bir destek çağrısı boşlukta durmaz; bir kanuna, yönetmeliğe ya da karara dayanır.
"Bu çağrı 5746 sayılı Kanun'un 3 üncü maddesine dayanır" bilgisi olmadan danışman
başvurunun hukuki zeminini kaynağa kadar takip edemez — ürünün açıklanabilirlik
iddiasının mevzuat ayağı budur.

Modül üç kalıbı tanır:

* **Numaralı mevzuat** — "5746 sayılı Ar-Ge Kanunu", "6102 sayılı Türk Ticaret Kanunu"
* **Adlandırılmış mevzuat** — "… Yönetmeliği", "… Tebliği", "… Genelgesi"
* **Madde göndermesi** — "3 üncü maddesi", "madde 5", "5/A maddesi"

İki sınır bilinçlidir:

1. **Uydurulmaz.** Metinde geçmeyen bir kanun numarası üretilmez; hiçbir kalıp
   tutmazsa boş döner. Yanlış bir dayanak, dayanağın hiç olmamasından kötüdür:
   danışman onu doğru sanıp kontrol etmez.

2. **Metinde yazan biçim korunur.** "5746 sayılı Kanun" olduğu gibi saklanır,
   resmî tam adına genişletilmez. Genişletme bir eşleme tablosu gerektirir ve o
   tablo eskidiğinde sessizce yanlış ada dönüşür.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

#: Bir metinden alınacak en fazla dayanak sayısı. Uzun mevzuat metinleri onlarca
#: gönderme içerir; ilk birkaçı çağrının kendi dayanağıdır, gerisi atıf zinciridir.
AZAMI_DAYANAK = 4

#: Saklanan metnin üst sınırı. Alan künye içindir, metnin kendisi değil.
AZAMI_UZUNLUK = 400

#: "5746 sayılı Kanun", "6102 sayılı Türk Ticaret Kanunu", "193 sayılı Gelir Vergisi Kanunu"
_NUMARALI = re.compile(
    r"(?P<numara>\d{3,5})\s*say[ıi]l[ıi]\s+"
    r"(?P<ad>(?:[A-ZÇĞİÖŞÜ][\wçğıöşüÇĞİÖŞÜ]*\s+){0,6}?"
    r"(?:Kanun|Kanunu|Kanun\s+H[üu]km[üu]nde\s+Kararname|KHK|Karar|Karar[ıi]))",
    re.IGNORECASE,
)

#: "Ar-Ge Merkezleri Yönetmeliği", "… Tebliği", "… Genelgesi", "… Kararı"
_ADLANDIRILMIS = re.compile(
    r"(?P<ad>(?:[A-ZÇĞİÖŞÜ][\wçğıöşüÇĞİÖŞÜ\-]*\s+){1,8}"
    r"(?:Y[öo]netmeli[ğg]i|Tebli[ğg]i|Genelgesi|Y[öo]nergesi|Esaslar[ıi]|"
    r"Uygulama\s+Esaslar[ıi]|Karar[ıi]))"
)

#: "3 üncü maddesi", "madde 5", "5/A maddesi", "12 nci maddesinde"
_MADDE = re.compile(
    r"(?:(?P<no1>\d{1,3}(?:/[A-ZÇĞİÖŞÜa-zçğıöşü])?)\s*"
    r"(?:[ıiuü]nc[ıiuü]|[ıiuü]nc[uü]|nci|ncı|inci|uncu|üncü)?\s*madde"
    r"|madde\s*(?P<no2>\d{1,3}(?:/[A-ZÇĞİÖŞÜa-zçğıöşü])?))",
    re.IGNORECASE,
)

#: Dayanak cümlesini işaret eden ifadeler. Bir mevzuat göndermesi bu kelimelerin
#: yakınındaysa çağrının kendi dayanağı olma ihtimali yüksektir.
DAYANAK_ISARETLERI: tuple[str, ...] = (
    "dayan", "uyarınca", "uyarinca", "gereğince", "geregince", "hükümlerine",
    "hukumlerine", "kapsamında", "kapsaminda", "istinaden", "göre", "gore",
    "çerçevesinde", "cercevesinde", "mevzuat",
)


@dataclass(frozen=True, slots=True)
class Dayanak:
    """Metinde geçtiği biçimiyle bir mevzuat göndermesi."""

    metin: str
    numara: str | None
    madde: str | None


def _temizle(deger: str) -> str:
    return " ".join((deger or "").split()).strip(" ,;:-")


def _madde_bul(cumle: str) -> str | None:
    eslesme = _MADDE.search(cumle)
    if eslesme is None:
        return None

    return eslesme.group("no1") or eslesme.group("no2")


def _cumleler(metin: str) -> list[str]:
    return [c for c in re.split(r"(?<=[.!?;\n])\s+", metin or "") if c.strip()]


def dayanaklari_bul(metin: str) -> list[Dayanak]:
    """Metindeki mevzuat göndermelerini bulur.

    Dayanak işareti taşıyan cümleler önce gelir: uzun bir metinde geçen her mevzuat
    adı çağrının dayanağı değildir, çoğu atıf zinciridir.
    """
    bulunanlar: list[Dayanak] = []
    gorulen: set[str] = set()

    cumleler = _cumleler(metin)
    isaretli = [c for c in cumleler if any(i in c.lower() for i in DAYANAK_ISARETLERI)]
    sirali = isaretli + [c for c in cumleler if c not in isaretli]

    for cumle in sirali:
        madde = _madde_bul(cumle)

        for eslesme in _NUMARALI.finditer(cumle):
            ad = _temizle(eslesme.group("ad"))
            gosterim = _temizle(f"{eslesme.group('numara')} sayılı {ad}")

            if gosterim.lower() in gorulen:
                continue

            gorulen.add(gosterim.lower())
            bulunanlar.append(Dayanak(gosterim, eslesme.group("numara"), madde))

            if len(bulunanlar) >= AZAMI_DAYANAK:
                return bulunanlar

        for eslesme in _ADLANDIRILMIS.finditer(cumle):
            gosterim = _temizle(eslesme.group("ad"))

            # Numaralı kalıpta zaten yakalanmış bir ad ikinci kez eklenmez.
            if gosterim.lower() in gorulen or any(gosterim.lower() in g for g in gorulen):
                continue

            gorulen.add(gosterim.lower())
            bulunanlar.append(Dayanak(gosterim, None, madde))

            if len(bulunanlar) >= AZAMI_DAYANAK:
                return bulunanlar

    return bulunanlar


def dayanak_metni(metin: str) -> str | None:
    """Kayda yazılacak tek satırlık dayanak künyesi; bulunamazsa ``None``.

    Madde bilgisi varsa ilk dayanağa eklenir: "5746 sayılı Kanun md. 3".
    """
    dayanaklar = dayanaklari_bul(metin)

    if not dayanaklar:
        return None

    parcalar: list[str] = []

    for dayanak in dayanaklar:
        parca = dayanak.metin
        if dayanak.madde:
            parca = f"{parca} md. {dayanak.madde}"
        parcalar.append(parca)

    birlesik = "; ".join(parcalar)

    return birlesik[:AZAMI_UZUNLUK].rstrip(" ;")
