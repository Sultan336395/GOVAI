"""Çağrı metninden bütçe çıkarımı (Faz 3).

Fırsat kaydında bütçe alanları (``budget_min``, ``budget_max``, ``support_rate``) Faz 1'den
beri var ama **hiçbir toplanmış kayıtta dolu değil**: bugün yalnızca demo verisiyle
gelenlerde bulunuyor, ayrıştırıcı metinden bütçe okumuyordu. Kullanıcı "azami tutar"
sütununu boş görüyor ve fırsatları tutara göre karşılaştıramıyor.

Bu modül üç şeyi okur:

* **Üst limit** — "üst limiti 300.000 TL", "en fazla 1.500.000 TL", "azami 750 bin TL"
* **Aralık** — "500.000 - 6.000.000 TL", "500.000 TL ile 6.000.000 TL arasında"
* **Destek oranı** — "%50 hibe", "hibe oranı en fazla %75"

Sayı biçimi Türkçedir: binlik ayracı **nokta**, ondalık ayracı **virgül**
(``1.500.000,50``). Bu ters okunursa 1,5 milyon lira 1,5 liraya döner; bu yüzden
ayraçlar açıkça ele alınır, ``float()``'a ham metin verilmez.

İki sınır bilinçlidir:

1. **Bulunamayan değer uydurulmaz.** Yalnızca üst limit yazıyorsa alt limit boş kalır;
   "0'dan başlar" varsayımı yapılmaz. Motor eksik veriyi zaten "bilinmiyor" sayar.
2. **Akla yatkınlık denetimi vardır.** Metindeki her sayı bütçe değildir; kanun
   numarası, telefon, tarih ve sıra numarası da sayıdır. Bu yüzden sayı, para birimi
   ya da bütçe kelimesiyle **aynı cümlede** geçmek zorundadır ve üst/alt akla yatkın
   bir aralıkta olmalıdır.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

#: Bütçe sayılabilecek en küçük tutar. Altındaki sayılar madde numarası, yüzde ya da
#: sıra numarasıdır; "5.000 TL" bir destek üst limiti olarak anlamlı değildir.
ASGARI_TUTAR = 1_000.0

#: Bütçe sayılabilecek en büyük tutar. Üstündeki sayılar çoğu zaman IBAN, vergi
#: numarası ya da yanlış ayrıştırılmış bir dizidir.
AZAMI_TUTAR = 100_000_000_000.0

#: Para birimi işaretleri ve karşılıkları.
PARA_BIRIMLERI: dict[str, str] = {
    "tl": "TRY", "try": "TRY", "₺": "TRY", "lira": "TRY", "turk lirasi": "TRY",
    "eur": "EUR", "euro": "EUR", "avro": "EUR", "€": "EUR",
    "usd": "USD", "dolar": "USD", "$": "USD",
}

#: Çarpan kelimeleri: "750 bin TL", "1,5 milyon avro".
CARPANLAR: dict[str, float] = {"bin": 1_000.0, "milyon": 1_000_000.0, "milyar": 1_000_000_000.0}

_SAYI = r"\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+(?:,\d+)?"

_PARA = r"(?:TL|TRY|₺|EUR|EURO|AVRO|€|USD|USD\$|DOLAR|\$)"

_CARPAN = r"(?:bin|milyon|milyar)"

#: "500.000 TL ile 6.000.000 TL arasında" / "500.000 - 6.000.000 TL"
_ARALIK = re.compile(
    rf"(?P<alt>{_SAYI})\s*(?P<altcarpan>{_CARPAN})?\s*(?:{_PARA})?\s*"
    rf"(?:-|–|ile|ila|arasi|arası)\s*"
    rf"(?P<ust>{_SAYI})\s*(?P<ustcarpan>{_CARPAN})?\s*(?P<birim>{_PARA})",
    re.IGNORECASE,
)

#: "üst limit 300.000 TL", "en fazla 1.500.000 TL", "azami 750 bin TL"
_UST_LIMIT = re.compile(
    rf"(?:ust\s*limit|üst\s*limit|en\s*fazla|azami|en\s*cok|en\s*çok|maksimum|"
    rf"tavan|ustune\s*kadar|üstüne\s*kadar|kadar)\s*"
    rf"(?P<tutar>{_SAYI})\s*(?P<carpan>{_CARPAN})?\s*(?P<birim>{_PARA})",
    re.IGNORECASE,
)

#: "asgari 50.000 TL", "en az 100 bin TL"
_ALT_LIMIT = re.compile(
    rf"(?:asgari|en\s*az|alt\s*limit|minimum|taban)\s*"
    rf"(?P<tutar>{_SAYI})\s*(?P<carpan>{_CARPAN})?\s*(?P<birim>{_PARA})",
    re.IGNORECASE,
)

#: Kelime bağlamı olmadan, doğrudan tutar: "300.000 TL destek", "bütçesi 2 milyon EUR"
_SERBEST_TUTAR = re.compile(
    rf"(?P<tutar>{_SAYI})\s*(?P<carpan>{_CARPAN})?\s*(?P<birim>{_PARA})",
    re.IGNORECASE,
)

#: "%50", "yüzde 75", "oranı %60'a kadar"
_ORAN = re.compile(
    r"(?:%\s*(?P<a>\d{1,3}(?:[.,]\d+)?)|yuzde\s*(?P<b>\d{1,3}(?:[.,]\d+)?)"
    r"|yüzde\s*(?P<c>\d{1,3}(?:[.,]\d+)?))",
    re.IGNORECASE,
)

#: Oranın destek oranı sayılabilmesi için cümlede geçmesi gereken kelimeler. Metindeki
#: her yüzde destek oranı değildir: KDV, faiz ve kadın istihdam oranı da yüzdedir.
ORAN_BAGLAMI: tuple[str, ...] = (
    "destek", "hibe", "katki", "katkı", "karsilanir", "karşılanır",
    "karsilanacak", "karşılanacak", "oranı", "orani", "sübvansiyon", "subvansiyon",
)

#: Oranın destek oranı SAYILMAMASI gereken bağlamlar.
ORAN_DISLAYICI: tuple[str, ...] = (
    "kdv", "faiz", "kadin", "kadın", "genc", "genç", "engelli", "istihdam orani",
    "istihdam oranı", "ar-ge personeli", "stopaj", "vergi orani", "vergi oranı",
    # Ortaklık ve hisse payı bir BAŞVURU KOŞULUDUR, destek oranı değil. Gerçek
    # KOSGEB Girişimci Destek Programı metninde "girişimcinin ortaklık payı en az
    # %50 olmalıdır" cümlesi destek oranı diye okunuyordu.
    "ortaklik payi", "ortaklık payı", "hisse", "pay orani", "pay oranı",
    "sermaye payi", "sermaye payı", "katilim payi", "katılım payı",
)


@dataclass(frozen=True, slots=True)
class Butce:
    """Metinden okunan bütçe. Bulunamayan alan ``None`` kalır — sıfır yazılmaz."""

    alt: float | None
    ust: float | None
    birim: str
    oran: float | None

    @property
    def bos_mu(self) -> bool:
        return self.alt is None and self.ust is None and self.oran is None


def sayiya_cevir(ham: str, carpan: str | None = None) -> float | None:
    """Türkçe biçimli sayıyı çevirir: binlik ayracı nokta, ondalık ayracı virgül.

    ``1.500.000,50`` → ``1500000.5``. Ters okunursa 1,5 milyon lira 1,5 liraya döner.
    """
    temiz = (ham or "").strip()
    if not temiz:
        return None

    temiz = temiz.replace(".", "").replace(",", ".")

    try:
        deger = float(temiz)
    except ValueError:
        return None

    if carpan:
        deger *= CARPANLAR.get(carpan.lower(), 1.0)

    return deger


def birime_cevir(ham: str | None) -> str:
    """Para birimi işaretini ISO koduna çevirir; tanınmazsa TRY varsayılır."""
    anahtar = (ham or "").strip().lower().rstrip("$")

    return PARA_BIRIMLERI.get(anahtar, "TRY")


def _makul_mu(tutar: float | None) -> bool:
    return tutar is not None and ASGARI_TUTAR <= tutar <= AZAMI_TUTAR


def _cumleler(metin: str) -> list[str]:
    return [c for c in re.split(r"(?<=[.!?;\n])\s+", metin or "") if c.strip()]


def oran_bul(metin: str) -> float | None:
    """Destek oranını 0..1 aralığında döner; bulunamazsa ``None``.

    Yalnızca destek bağlamındaki yüzdeler sayılır. KDV, faiz ya da kadın istihdam
    oranı destek oranı değildir ve alınırsa çağrı yanlış anlatılır.
    """
    for cumle in _cumleler(metin):
        katlanmis = cumle.lower()

        if any(d in katlanmis for d in ORAN_DISLAYICI):
            continue

        if not any(b in katlanmis for b in ORAN_BAGLAMI):
            continue

        for eslesme in _ORAN.finditer(cumle):
            ham = eslesme.group("a") or eslesme.group("b") or eslesme.group("c")
            deger = sayiya_cevir(ham.replace(".", ","))

            if deger is not None and 0 < deger <= 100:
                return round(deger / 100, 4)

    return None


def butce_bul(metin: str) -> Butce:
    """Metinden bütçe aralığını ve destek oranını çıkarır.

    Sıra kasıtlıdır: önce açık aralık, sonra adlandırılmış üst/alt limit, en sonda
    bağlamsız tutar. Adlandırılmış ifade her zaman daha güvenilirdir.
    """
    govde = metin or ""

    alt: float | None = None
    ust: float | None = None
    birim = "TRY"

    if (eslesme := _ARALIK.search(govde)) is not None:
        a = sayiya_cevir(eslesme.group("alt"), eslesme.group("altcarpan"))
        u = sayiya_cevir(eslesme.group("ust"), eslesme.group("ustcarpan"))

        if _makul_mu(a) and _makul_mu(u) and a <= u:  # type: ignore[operator]
            alt, ust, birim = a, u, birime_cevir(eslesme.group("birim"))

    if ust is None and (eslesme := _UST_LIMIT.search(govde)) is not None:
        u = sayiya_cevir(eslesme.group("tutar"), eslesme.group("carpan"))

        if _makul_mu(u):
            ust, birim = u, birime_cevir(eslesme.group("birim"))

    if alt is None and (eslesme := _ALT_LIMIT.search(govde)) is not None:
        a = sayiya_cevir(eslesme.group("tutar"), eslesme.group("carpan"))

        # Alt limit üst limiti aşamaz; aşıyorsa yanlış cümleden okunmuştur.
        if _makul_mu(a) and (ust is None or a <= ust):  # type: ignore[operator]
            alt, birim = a, birime_cevir(eslesme.group("birim"))

    if alt is None and ust is None and (eslesme := _SERBEST_TUTAR.search(govde)) is not None:
        u = sayiya_cevir(eslesme.group("tutar"), eslesme.group("carpan"))

        if _makul_mu(u):
            ust, birim = u, birime_cevir(eslesme.group("birim"))

    return Butce(alt=alt, ust=ust, birim=birim, oran=oran_bul(govde))


def butce_yuku(metin: str) -> dict[str, object] | None:
    """API'nin beklediği ``budget`` gövdesi; bütçe okunamazsa ``None``.

    ``None`` dönmesi önemlidir: boş bir bütçe nesnesi göndermek, kaydı "bütçesi
    girilmiş ama sıfır" hâline getirir ve veri kalitesi ölçümünü yanıltır.
    """
    butce = butce_bul(metin)

    if butce.bos_mu:
        return None

    return {
        "minAmount": butce.alt,
        "maxAmount": butce.ust,
        "currency": butce.birim,
        "supportRate": butce.oran,
    }
