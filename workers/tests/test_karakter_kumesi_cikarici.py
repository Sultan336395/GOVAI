"""Karakter kümesi kararının **tek elden** verilmesi (Faz 3).

`test_karakter_kumesi.py` indiricinin kararını sınar. Bu dosya bir adım sonrasını
sınar: ayrıştırıcının aynı kararı kullandığını.

Sahada görülen sorun: iki KOSGEB belgesinin metni ``KÃ¼Ã§Ã¼k ve Orta ÃlÃ§ekli``
diye kaydedildi. Ham içerik **temizdi**; bozulan yalnızca çıkarılmış metindi.

Sebep yapısaldı. İndirici kümeyi özenle belirliyor, makullük denetiminden geçiriyor
ve saklıyordu — ama ayrıştırıcı ham baytları doğrudan BeautifulSoup'a veriyor, o da
kümeyi **kendi başına yeniden tahmin ediyordu**. Aynı belge iki ayrı yerde iki farklı
şekilde çözülüyordu; toplayıcı doğru saklıyor, ayrıştırıcı bozuk metin üretiyordu.

Bu testler o ayrışmanın geri gelmesini engeller.
"""

from __future__ import annotations

import pytest

from govai_workers.collector.fetcher import FetchedDocument, metni_coz
from govai_workers.parser.extractors import extract_document

TURKCE = "Küçük ve Orta Ölçekli İşletmeleri Geliştirme ve Destekleme İdaresi Başkanlığı"

#: UTF-8 gövdenin tek baytlı çözülmüş hâlinde ortaya çıkan iz.
BOZUK_IZ = "Ã"


def html(govde: str, meta: str = "") -> str:
    return f"<!DOCTYPE html><html><head>{meta}</head><body><p>{govde}</p></body></html>"


class TestCikariciKendiTahminiYapmaz:
    """Asıl delik buydu: çıkarıcı kümeyi kendi başına belirliyordu."""

    def test_yanlis_kume_bildirimi_utf8_govdeyi_bozamaz(self) -> None:
        baytlar = html(TURKCE).encode("utf-8")

        sonuc = extract_document(baytlar, "text/html", charset="iso-8859-1")

        assert BOZUK_IZ not in sonuc.text
        assert "Ölçekli" in sonuc.text

    @pytest.mark.parametrize("kume", ["utf-8", "iso-8859-1", "windows-1252", None])
    def test_hicbir_bildirim_utf8_govdeyi_bozamaz(self, kume: str | None) -> None:
        baytlar = html(TURKCE).encode("utf-8")

        assert BOZUK_IZ not in extract_document(baytlar, "text/html", charset=kume).text

    def test_dogru_kume_bildirimi_kullanilir(self) -> None:
        baytlar = html(TURKCE).encode("windows-1254")

        sonuc = extract_document(baytlar, "text/html", charset="windows-1254")

        assert "Küçük" in sonuc.text
        assert BOZUK_IZ not in sonuc.text

    def test_bildirim_yoksa_meta_etiketi_okunur(self) -> None:
        baytlar = html(TURKCE, '<meta charset="windows-1254">').encode("windows-1254")

        assert "İşletmeleri" in extract_document(baytlar, "text/html").text


class TestIkiBilesenAyrismaz:
    """İndirici ve çıkarıcı aynı belgeyi aynı şekilde çözmelidir."""

    @pytest.mark.parametrize(
        ("bildirilen", "gercek_kodlama"),
        [
            ("utf-8", "utf-8"),
            ("windows-1254", "windows-1254"),
            # Sunucu yanlış söylüyor; iki bileşen de aldanmamalı.
            ("iso-8859-1", "utf-8"),
            (None, "utf-8"),
        ],
    )
    def test_ayni_sonucu_verirler(self, bildirilen: str | None, gercek_kodlama: str) -> None:
        baytlar = html(TURKCE).encode(gercek_kodlama)

        belge = FetchedDocument(
            url="https://www.kosgeb.gov.tr/site/tr/genel/destekler",
            canonical_url="https://www.kosgeb.gov.tr/site/tr/genel/destekler",
            content=baytlar,
            media_type="text/html",
            status_code=200,
            charset=bildirilen,
        )

        # Çıkarıcı etiketleri atar; karşılaştırma Türkçe metnin kendisi üzerinden.
        assert TURKCE in belge.text()
        assert TURKCE in extract_document(baytlar, "text/html", charset=bildirilen).text

    def test_cozme_karari_tek_islevde(self) -> None:
        """Her iki yol da ``metni_coz`` üzerinden geçer."""
        baytlar = html(TURKCE).encode("utf-8")

        belge = FetchedDocument(
            url="https://ornek.gov.tr/d",
            canonical_url="https://ornek.gov.tr/d",
            content=baytlar,
            media_type="text/html",
            status_code=200,
            charset="iso-8859-1",
        )

        assert belge.text() == metni_coz(baytlar, "iso-8859-1")


class TestSahadakiArizaninKendisi:
    def test_kosgeb_bozulmasi_tekrar_olusmaz(self) -> None:
        """Kaydedilen bozuk metnin birebir imzası.

        Veritabanında ``KÃ1⁄4Ã§Ã1⁄4k`` duruyordu: UTF-8 gövde tek baytlı çözülmüş,
        ardından NFKC normalizasyonu ``¼`` karakterini ``1⁄4``e açmıştı.
        """
        baytlar = html(TURKCE).encode("utf-8")

        metin = extract_document(baytlar, "text/html", charset="iso-8859-1").text

        assert "KÃ" not in metin
        assert "⁄" not in metin  # kesir çizgisi ⁄
        assert "Küçük" in metin
