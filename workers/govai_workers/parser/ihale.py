"""Resmî Gazete ihale ilanlarının alan çıkarımı (Faz 2).

Resmî Gazete'nin ilan bölümündeki her tekil ihale aynı biçimi izler::

    13 KALEM TIBBİ CİHAZ/DEMİRBAŞ MALZEME
    ALIMI İHALE EDİLECEKTİR
    Ankara İl Sağlık Müdürlüğü ... Koordinatör Başhekimliğinden:
    2026 Yılı 13 Kalem Tıbbi Cihaz/Demirbaş Malzeme İhalesi ...

Başlık ilk satırlarda ve **birden fazla satıra bölünmüş** olabilir; ihaleyi açan
idare, sonu iki nokta üst üste ile biten "…den:" / "…dan:" satırıdır.

Ayrıştırıcının genel yolu başlığı belgenin ilk satırından alıyordu; bu, iki satıra
bölünmüş başlıkların yarısını kesiyor ve kurum olarak **kaynağın adını** ("Resmî
Gazete İhale İlanları") yazıyordu. Oysa ihaleyi açan idare belgenin içinde yazılıdır
ve bir danışman için asıl bilgi odur.

Hiçbir değer **uydurulmaz**: kalıp tutmuyorsa alan ``None`` döner ve arayüz
"Resmî kaynakta belirtilmemiş" gösterir.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

#: İhaleyi açan idarenin satırı: "… Müdürlüğünden:", "… Başkanlığından:", "… A.Ş.den:"
#: Türkçe ayrılma hâli ekleri: -dan/-den/-tan/-ten, önünde ünlü uyumuna göre n kaynaştırması.
_KURUM = re.compile(r"^(?P<kurum>.{6,200}?[dt][ae]n)\s*:\s*$", re.IGNORECASE)

#: "… 15/09/2026 … tarihinde" biçiminde ihale tarihi.
_TARIH = re.compile(r"\b(?P<g>\d{1,2})[./](?P<a>\d{1,2})[./](?P<y>20\d{2})\b")

#: İhale tarihini anlatan bağlam sözcükleri. Belgede geçen HER tarih ihale tarihi
#: değildir (mevzuat atıfları, yayım tarihleri); yalnızca bu bağlamdakiler alınır.
_TARIH_BAGLAMI = re.compile(
    r"(ihale[si]?\s+(tarihi|günü)|tarihinde\s+.{0,40}(yapılacak|ihale)"
    r"|son\s+(başvuru|teklif)|teklifler(in)?\s+.{0,30}(son|kadar))",
    re.IGNORECASE,
)


@dataclass(frozen=True, slots=True)
class IhaleAlanlari:
    """Tekil ihale ilanından çıkarılan alanlar. Bulunamayan alan ``None``dır."""

    baslik: str | None = None
    kurum: str | None = None
    ihale_tarihi: str | None = None


def _satirlar(metin: str) -> list[str]:
    return [s.strip() for s in metin.replace("\f", "\n").splitlines()]


def ihale_alanlari(metin: str, *, azami_baslik_satiri: int = 6) -> IhaleAlanlari:
    """İlan metninden başlık, idare ve ihale tarihini çıkarır.

    ``azami_baslik_satiri`` kurum satırının makul uzaklıkta aranmasını sağlar:
    daha ötede bulunan bir "…den:" satırı gövdenin içindeki bir atıftır, ilanın
    künyesi değildir.
    """
    satirlar = _satirlar(metin)
    baslik_parcalari: list[str] = []
    kurum: str | None = None
    kurum_indeksi: int | None = None

    for i, satir in enumerate(satirlar[: azami_baslik_satiri + 1]):
        if not satir:
            continue

        eslesme = _KURUM.match(satir)
        if eslesme:
            kurum = eslesme.group("kurum").strip()
            kurum_indeksi = i
            break

        baslik_parcalari.append(satir)

    # Kurum satırı bulunamadıysa başlık da güvenilir değildir: bu belge beklenen
    # ilan biçiminde değil. Tahmin edilmez.
    if kurum is None:
        return IhaleAlanlari()

    baslik = " ".join(baslik_parcalari).strip() or None

    return IhaleAlanlari(
        baslik=baslik,
        kurum=kurum,
        ihale_tarihi=_ihale_tarihi(satirlar[kurum_indeksi:]),
    )


def _ihale_tarihi(satirlar: list[str]) -> str | None:
    """Bağlamı ihale/son başvuru olan ilk tarihi ISO biçiminde döner.

    Belgede geçen her tarih ihale tarihi değildir; mevzuat atıflarındaki tarihler
    (ör. "21/02/2013 tarihli ve 6428 sayılı Kanun") alınmamalıdır. Bu yüzden yalnızca
    ihale bağlamı taşıyan satırlardaki tarihler değerlendirilir.
    """
    for satir in satirlar:
        if not _TARIH_BAGLAMI.search(satir):
            continue

        eslesme = _TARIH.search(satir)
        if not eslesme:
            continue

        gun, ay, yil = (int(eslesme.group(x)) for x in ("g", "a", "y"))

        if not (1 <= gun <= 31 and 1 <= ay <= 12):
            continue

        return f"{yil:04d}-{ay:02d}-{gun:02d}"

    return None
