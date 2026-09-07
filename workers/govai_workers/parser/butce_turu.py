"""Bütçe tutarlarının **türüne göre** ayrılması (Faz 3).

Önceki sürüm metindeki en büyük tutarı bütçe sayıyordu. Gerçek KOSGEB belgesiyle
denendiğinde ne olduğu görüldü: KOBİ Dijital Dönüşüm programında aynı sayfada

    "geri ödemesiz destek üst limiti 1.500.000 TL"
    "İşletme Başına Kredi Üst Limiti: 20.000.000 TL"

yazıyor. Sistem 20.000.000'u alıyor ve kullanıcıya 20 milyon liralık **hibe** varmış
gibi gösteriyordu. Kredi bir borçtur; hibe değildir. Bu, kullanıcının başvuru kararını
doğrudan yanlış yönlendirir.

Bu modül tutarı **bağlamıyla birlikte** okur ve türünü belirler. Bağlam kesin değilse
tür `BELIRSIZ` kalır: tahmin edilmez. Belirsiz tutar ekranda hibe gibi gösterilemez.

Her kalem kendi **kanıt alıntısına ve karakter aralığına** bağlıdır; kullanıcı tutarın
belgede nerede yazdığını görebilir.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from enum import StrEnum

from govai_workers.parser.butce import (
    ASGARI_TUTAR,
    AZAMI_TUTAR,
    CARPANLAR,
    PARA_BIRIMLERI,
    birime_cevir,
    sayiya_cevir,
)


class ButceTuru(StrEnum):
    """Bir tutarın ne olduğu. Tür bilinmeden tutar kullanıcıya sunulamaz."""

    TOPLAM_PROGRAM_BUTCESI = "ToplamProgramButcesi"
    HIBE_UST_LIMITI = "HibeUstLimiti"
    KREDI_UST_LIMITI = "KrediUstLimiti"
    GERI_ODEMELI_DESTEK = "GeriOdemeliDestek"
    UYGUN_HARCAMA = "UygunHarcama"
    BELIRSIZ = "Belirsiz"


class OranTuru(StrEnum):
    """Bir yüzdenin ne olduğu. Her yüzde destek oranı değildir."""

    DESTEK_ORANI = "DestekOrani"
    OZ_KAYNAK_PAYI = "OzKaynakPayi"
    BELIRSIZ = "Belirsiz"


#: Türkçe gösterim. Ekranda tutarın yanında bu yazar; "1.500.000 TL" tek başına
#: hangi tür olduğunu söylemez.
TUR_ETIKETLERI: dict[str, str] = {
    ButceTuru.TOPLAM_PROGRAM_BUTCESI: "Toplam program bütçesi",
    ButceTuru.HIBE_UST_LIMITI: "Hibe üst limiti",
    ButceTuru.KREDI_UST_LIMITI: "Kredi üst limiti",
    ButceTuru.GERI_ODEMELI_DESTEK: "Geri ödemeli destek",
    ButceTuru.UYGUN_HARCAMA: "Uygun harcama tutarı",
    ButceTuru.BELIRSIZ: "Türü belirlenemedi",
}

#: Tür işaretleri. Sıra ÖNEMLİDİR: daha dar ve ayırt edici olan önce gelir.
#: "geri ödemesiz" ile "geri ödemeli" tek harf farkıyla zıt anlamlıdır ve ilki
#: önce denenmelidir, aksi hâlde "geri ödemeli" kalıbı ikisini birden yakalar.
TUR_ISARETLERI: tuple[tuple[str, tuple[str, ...]], ...] = (
    (ButceTuru.KREDI_UST_LIMITI, ("kredi", "finansman", "faiz destegi", "vade", "banka")),
    (ButceTuru.HIBE_UST_LIMITI, ("geri odemesiz", "hibe", "karsiliksiz")),
    (ButceTuru.GERI_ODEMELI_DESTEK, ("geri odemeli", "geri odeme kosullu")),
    (
        ButceTuru.TOPLAM_PROGRAM_BUTCESI,
        ("program butcesi", "toplam butce", "cagri butcesi", "tahsis edilen"),
    ),
    (
        ButceTuru.UYGUN_HARCAMA,
        ("uygun harcama", "uygun maliyet", "proje butcesi", "harcama tutari"),
    ),
)

#: Oranın öz kaynak/ortaklık payı olduğunu gösteren ifadeler.
OZ_KAYNAK_ISARETLERI: tuple[str, ...] = (
    "ortaklik payi", "hisse", "pay orani", "sermaye payi", "katilim payi",
    "oz kaynak", "ozkaynak", "esfinansman", "es finansman", "teminat",
)

#: Oranın destek oranı olduğunu gösteren ifadeler.
DESTEK_ORANI_ISARETLERI: tuple[str, ...] = (
    "destek orani", "hibe orani", "destek olarak", "karsilanir", "karsilanacak",
    "destek oraninda", "geri odemesiz destek",
)

#: Hiçbir koşulda destek oranı sayılmayan bağlamlar.
ORAN_DISLAYICI: tuple[str, ...] = (
    "kdv", "faiz", "teminat", "stopaj", "vergi orani", "gecikme zammi",
    "kadin", "genc", "engelli calisan", "istihdam orani",
)

#: Tutarın çevresinden alınacak bağlam penceresi (karakter). Dar tutulur: KOSGEB
#: belgesinde "geri ödemesiz destek üst limiti 1.500.000 TL" ile "Kredi Üst Limiti:
#: 20.000.000 TL" arka arkaya geçiyor. Geniş pencerede iki tutarın bağlamı birbirine
#: karışır ve hibe, kredi sayılır.
BAGLAM_PENCERESI = 70

#: Etiket tutardan ÖNCE yazılır ("Kredi Üst Limiti: 20.000.000 TL"). Bu yüzden sol
#: taraf daha geniş taranır; sağ taraf yalnızca "… TL hibe olarak verilir" gibi
#: sonradan gelen nitelemeler için açık kalır.
SAG_PENCERE = 40

_KATLAMA = str.maketrans(
    {
        "ı": "i", "İ": "i", "I": "i", "ş": "s", "Ş": "s", "ğ": "g", "Ğ": "g",
        "ü": "u", "Ü": "u", "ö": "o", "Ö": "o", "ç": "c", "Ç": "c",
        "â": "a", "Â": "a", "î": "i", "Î": "i", "û": "u", "Û": "u",
    }
)

_SAYI = r"\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+(?:,\d+)?"
_PARA = r"(?:TL|TRY|₺|EUR|EURO|AVRO|€|USD|DOLAR|\$)"
_CARPAN = r"(?:bin|milyon|milyar)"

_TUTAR = re.compile(
    rf"(?P<tutar>{_SAYI})\s*(?P<carpan>{_CARPAN})?\s*(?P<birim>{_PARA})",
    re.IGNORECASE,
)

_ORAN = re.compile(
    r"%\s*(?P<deger>\d{1,3}(?:[.,]\d+)?)|y[üu]zde\s*(?P<deger2>\d{1,3}(?:[.,]\d+)?)",
    re.IGNORECASE,
)


def katla(deger: str) -> str:
    """Türkçe harfleri katlar ve harf/rakam dışını tek boşluğa indirir."""
    katlanmis = (deger or "").translate(_KATLAMA).lower()
    temiz = "".join(ch if ch.isalnum() else " " for ch in katlanmis)

    return " ".join(temiz.split())


@dataclass(frozen=True, slots=True)
class ButceKalemi:
    """Türü belirlenmiş tek bir tutar ve belgedeki yeri."""

    tur: ButceTuru
    tutar: float
    birim: str
    alinti: str
    baslangic: int
    bitis: int

    @property
    def etiket(self) -> str:
        return TUR_ETIKETLERI[self.tur]

    @property
    def incelenmeli_mi(self) -> bool:
        """Türü belirlenememiş tutar kullanıcıya hibe gibi GÖSTERİLEMEZ."""
        return self.tur is ButceTuru.BELIRSIZ


@dataclass(frozen=True, slots=True)
class OranKalemi:
    """Türü belirlenmiş tek bir yüzde ve belgedeki yeri."""

    tur: OranTuru
    oran: float
    alinti: str
    baslangic: int
    bitis: int

    @property
    def incelenmeli_mi(self) -> bool:
        return self.tur is OranTuru.BELIRSIZ


def _baglam(metin: str, baslangic: int, bitis: int) -> tuple[str, int]:
    """Tutarın çevresi ve tutarın bu pencere içindeki konumu."""
    sol = max(0, baslangic - BAGLAM_PENCERESI)
    sag = min(len(metin), bitis + SAG_PENCERE)

    return metin[sol:sag], baslangic - sol


def _en_yakin_isaret(baglam: str, tutar_konumu: int, isaretler: tuple[str, ...]) -> int | None:
    """İşaretlerden en yakınının tutara uzaklığı; hiçbiri yoksa ``None``.

    Katlama karakter sayısını korur (harf/rakam dışı tek boşluğa iner ama silinmez),
    bu yüzden konumlar ham metinle hizalı kalır.
    """
    katlanmis = _konum_koruyan_katla(baglam)
    en_yakin: int | None = None

    for isaret in isaretler:
        for eslesme in re.finditer(re.escape(isaret), katlanmis):
            uzaklik = abs(eslesme.start() - tutar_konumu)
            if en_yakin is None or uzaklik < en_yakin:
                en_yakin = uzaklik

    return en_yakin


def _konum_koruyan_katla(deger: str) -> str:
    """Katlar ama uzunluğu DEĞİŞTİRMEZ; konum hesabı buna dayanır."""
    katlanmis = deger.translate(_KATLAMA).lower()

    return "".join(ch if ch.isalnum() else " " for ch in katlanmis)


def _turu_belirle(baglam: str, tutar_konumu: int) -> ButceTuru:
    """Tutarın türünü, EN YAKIN işarete bakarak belirler; kesin değilse BELİRSİZ.

    Sabit öncelik sırası yanlış sonuç verir: "geri ödemesiz destek üst limiti
    1.500.000 TL … Kredi Üst Limiti: 20.000.000 TL" cümlesinde her iki işaret de
    penceredeydi ve kredi önce denendiği için hibe de kredi sayılıyordu. Karar artık
    hangi etiketin tutara daha yakın durduğuna göre verilir.
    """
    en_iyi: tuple[int, ButceTuru] | None = None

    for tur, isaretler in TUR_ISARETLERI:
        uzaklik = _en_yakin_isaret(baglam, tutar_konumu, isaretler)

        if uzaklik is None:
            continue

        if en_iyi is None or uzaklik < en_iyi[0]:
            en_iyi = (uzaklik, ButceTuru(tur))

    return en_iyi[1] if en_iyi else ButceTuru.BELIRSIZ


def butce_kalemleri(metin: str) -> list[ButceKalemi]:
    """Metindeki tüm tutarları türleriyle birlikte döner.

    Akla yatkınlık sınırları korunur: metindeki her sayı bütçe değildir; kanun
    numarası, tarih ve sıra numarası da sayıdır.
    """
    govde = metin or ""
    kalemler: list[ButceKalemi] = []
    gorulen: set[tuple[str, float]] = set()

    for eslesme in _TUTAR.finditer(govde):
        tutar = sayiya_cevir(eslesme.group("tutar"), eslesme.group("carpan"))

        if tutar is None or not (ASGARI_TUTAR <= tutar <= AZAMI_TUTAR):
            continue

        baglam, konum = _baglam(govde, eslesme.start(), eslesme.end())
        tur = _turu_belirle(baglam, konum)

        # Aynı türden aynı tutar iki kez listelenmez; belgeler tutarı tabloda ve
        # metinde tekrarlar.
        anahtar = (str(tur), tutar)
        if anahtar in gorulen:
            continue

        gorulen.add(anahtar)
        kalemler.append(
            ButceKalemi(
                tur=tur,
                tutar=tutar,
                birim=birime_cevir(eslesme.group("birim")),
                alinti=" ".join(baglam.split()),
                baslangic=eslesme.start(),
                bitis=eslesme.end(),
            )
        )

    return kalemler


def _oran_turu(baglam: str, oran_konumu: int) -> OranTuru:
    """Yüzdenin türü, en yakın işarete göre. Dışlayıcı bağlam her zaman kazanır."""
    katlanmis = _konum_koruyan_katla(baglam)

    if any(i in katlanmis for i in ORAN_DISLAYICI):
        return OranTuru.BELIRSIZ

    oz_kaynak = _en_yakin_isaret(baglam, oran_konumu, OZ_KAYNAK_ISARETLERI)
    destek = _en_yakin_isaret(baglam, oran_konumu, DESTEK_ORANI_ISARETLERI)

    if oz_kaynak is None and destek is None:
        return OranTuru.BELIRSIZ

    if destek is None:
        return OranTuru.OZ_KAYNAK_PAYI

    if oz_kaynak is None or destek < oz_kaynak:
        return OranTuru.DESTEK_ORANI

    return OranTuru.OZ_KAYNAK_PAYI


def oran_kalemleri(metin: str) -> list[OranKalemi]:
    """Metindeki yüzdeleri türleriyle döner. Her yüzde destek oranı değildir."""
    govde = metin or ""
    kalemler: list[OranKalemi] = []
    gorulen: set[tuple[str, float]] = set()

    for eslesme in _ORAN.finditer(govde):
        ham = eslesme.group("deger") or eslesme.group("deger2")
        deger = sayiya_cevir((ham or "").replace(".", ","))

        if deger is None or not 0 < deger <= 100:
            continue

        baglam, konum = _baglam(govde, eslesme.start(), eslesme.end())
        tur = _oran_turu(baglam, konum)
        anahtar = (str(tur), deger)

        if anahtar in gorulen:
            continue

        gorulen.add(anahtar)
        kalemler.append(
            OranKalemi(
                tur=tur,
                oran=round(deger / 100, 4),
                alinti=" ".join(baglam.split()),
                baslangic=eslesme.start(),
                bitis=eslesme.end(),
            )
        )

    return kalemler


def ilk(kalemler: list[ButceKalemi], tur: ButceTuru) -> ButceKalemi | None:
    """Belirli türdeki ilk kalem; yoksa ``None`` (NotProvided)."""
    return next((k for k in kalemler if k.tur is tur), None)


def butce_yuku(metin: str) -> dict[str, object] | None:
    """API'nin beklediği türlü bütçe gövdesi; hiçbir kalem yoksa ``None``.

    Geriye dönük uyum: eski tek alanlı ``budget`` gövdesi de doldurulur ama
    **yalnızca türü kesin olan** bir hibe/program tutarı varsa. Türü belirsiz bir
    tutar eski alana yazılırsa ekranda kesin hibe gibi görünürdü.
    """
    kalemler = butce_kalemleri(metin)
    oranlar = oran_kalemleri(metin)

    if not kalemler and not oranlar:
        return None

    destek_orani = next((o for o in oranlar if o.tur is OranTuru.DESTEK_ORANI), None)
    hibe = ilk(kalemler, ButceTuru.HIBE_UST_LIMITI)
    program = ilk(kalemler, ButceTuru.TOPLAM_PROGRAM_BUTCESI)
    uygun = ilk(kalemler, ButceTuru.UYGUN_HARCAMA)

    # Eski alan yalnızca türü KESİN bir tutarla doldurulur.
    eski_ust = hibe or program or uygun
    birim = eski_ust.birim if eski_ust else (kalemler[0].birim if kalemler else "TRY")

    return {
        # Geriye dönük uyum: eski tek alanlı gövde. YALNIZCA türü kesin bir tutarla
        # doldurulur; belirsiz tutar buraya yazılırsa ekranda kesin hibe gibi görünür.
        "legacy": None if eski_ust is None and destek_orani is None else {
            "minAmount": None,
            "maxAmount": eski_ust.tutar if eski_ust else None,
            "currency": birim,
            "supportRate": destek_orani.oran if destek_orani else None,
        },
        "items": [
            {
                "type": str(k.tur),
                "label": k.etiket,
                "amount": k.tutar,
                "currency": k.birim,
                "excerpt": k.alinti,
                "startOffset": k.baslangic,
                "endOffset": k.bitis,
                "needsReview": k.incelenmeli_mi,
            }
            for k in kalemler
        ],
        "rates": [
            {
                "type": str(o.tur),
                "rate": o.oran,
                "excerpt": o.alinti,
                "startOffset": o.baslangic,
                "endOffset": o.bitis,
                "needsReview": o.incelenmeli_mi,
            }
            for o in oranlar
        ],
    }


__all__ = [
    "BAGLAM_PENCERESI",
    "CARPANLAR",
    "PARA_BIRIMLERI",
    "TUR_ETIKETLERI",
    "ButceKalemi",
    "ButceTuru",
    "OranKalemi",
    "OranTuru",
    "butce_kalemleri",
    "butce_yuku",
    "ilk",
    "katla",
    "oran_kalemleri",
]
