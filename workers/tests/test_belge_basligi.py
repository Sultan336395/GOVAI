"""Belge başlığının metinden çıkarılması.

Resmî Gazete günlük nüshalarında sayfa ``<title>``'ı yalnızca tarihtir
("26 Ağustos 2026 ÇARŞAMBA"); belgenin adı değildir. Bu sayfalardan üretilen
mevzuat kayıtlarının hepsi aynı, anlamsız başlığı taşıyordu.
"""

from __future__ import annotations

import pytest

from govai_workers.parser.runner import belge_basligi

RESMI_GAZETE_METNI = """26 Ağustos 2026 ÇARŞAMBA
Resmî Gazete
Sayı : 33352
TEBLİĞ
Adalet Bakanlığından:
KONKORDATO GİDER AVANSI TARİFESİ
Amaç ve kapsam
MADDE 1- (1) Bu Tarife, 9/6/1932 tarihli ve 2004 sayılı İcra ve İflas Kanunu gereğince
konkordato talep edilirken mahkeme veznesine yatırılacak olan avansın miktarını belirler.
"""


class TestSayfaBasligiYeterliyse:
    def test_anlamli_baslik_aynen_kullanilir(self) -> None:
        assert belge_basligi("Ar-Ge Merkezi Destek Programı Tebliği", "gövde") == (
            "Ar-Ge Merkezi Destek Programı Tebliği"
        )

    def test_bosluklar_kirpilir(self) -> None:
        assert belge_basligi("  Sanayi Sicil Yönetmeliği  ", "gövde") == "Sanayi Sicil Yönetmeliği"


class TestTarihBasligi:
    @pytest.mark.parametrize(
        "sayfa_basligi",
        ["26 Ağustos 2026 ÇARŞAMBA", "1 Ocak 2026", "26.08.2026", "26/08/2026 tarihli"],
    )
    def test_tarih_basligi_reddedilir(self, sayfa_basligi: str) -> None:
        sonuc = belge_basligi(sayfa_basligi, RESMI_GAZETE_METNI)

        assert sonuc is not None
        assert sonuc != sayfa_basligi
        assert "KONKORDATO GİDER AVANSI TARİFESİ" in sonuc

    def test_belge_turu_ile_ad_birlestirilir(self) -> None:
        # "TEBLİĞ" tek başına ad değildir; asıl adla birlikte anlam kazanır.
        sonuc = belge_basligi("26 Ağustos 2026 ÇARŞAMBA", RESMI_GAZETE_METNI)

        assert sonuc == "TEBLİĞ — KONKORDATO GİDER AVANSI TARİFESİ"

    def test_cok_kisa_baslik_da_reddedilir(self) -> None:
        assert belge_basligi("TEBLİĞ", RESMI_GAZETE_METNI) == (
            "TEBLİĞ — KONKORDATO GİDER AVANSI TARİFESİ"
        )


class TestUydurmaYok:
    def test_metinde_baslik_yoksa_none_doner(self) -> None:
        # Uygun satır yoksa uydurulmaz; alan NotProvided kalır.
        assert belge_basligi(None, "kısa gövde metni burada.") is None

    def test_kucuk_harfli_paragraf_baslik_sayilmaz(self) -> None:
        metin = ("Bu bir normal paragraftır ve başlık olarak kullanılmamalıdır, "
                 "çünkü küçük harflidir.")

        assert belge_basligi(None, metin) is None

    def test_noktayla_biten_cumle_baslik_sayilmaz(self) -> None:
        metin = "BU BİR CÜMLEDİR VE NOKTAYLA BİTTİĞİ İÇİN BAŞLIK DEĞİLDİR."

        assert belge_basligi(None, metin) is None

    def test_sayfa_basligi_yoksa_ve_metin_uygunsa_metinden_alinir(self) -> None:
        sonuc = belge_basligi(None, RESMI_GAZETE_METNI)

        assert sonuc == "TEBLİĞ — KONKORDATO GİDER AVANSI TARİFESİ"
