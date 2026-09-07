"""Çağrı metninden bütçe çıkarımı (Faz 3).

Fırsat kaydında bütçe alanları vardı ama hiçbir toplanmış kayıtta dolu değildi;
kullanıcı "azami tutar" sütununu boş görüyordu.

En kritik test sayı biçimidir: Türkçede binlik ayracı **nokta**, ondalık ayracı
**virgül**. Ters okunursa 1.500.000 TL, 1,5 TL'ye döner ve çağrı tamamen yanlış
anlatılır.
"""

from __future__ import annotations

import pytest

from govai_workers.parser.butce import butce_bul, butce_yuku, oran_bul, sayiya_cevir


class TestSayiBicimi:
    @pytest.mark.parametrize(
        ("ham", "beklenen"),
        [
            ("1.500.000", 1_500_000.0),
            ("500.000", 500_000.0),
            ("1.500.000,50", 1_500_000.5),
            ("250", 250.0),
            ("2,5", 2.5),
        ],
    )
    def test_turkce_bicim_dogru_okunur(self, ham: str, beklenen: float) -> None:
        assert sayiya_cevir(ham) == beklenen

    def test_carpan_uygulanir(self) -> None:
        assert sayiya_cevir("750", "bin") == 750_000.0
        assert sayiya_cevir("1,5", "milyon") == 1_500_000.0

    def test_bozuk_deger_none_doner(self) -> None:
        assert sayiya_cevir("abc") is None
        assert sayiya_cevir("") is None


class TestUstLimit:
    @pytest.mark.parametrize(
        "metin",
        [
            "Destek üst limiti 300.000 TL'dir.",
            "Program kapsamında en fazla 300.000 TL destek verilir.",
            "Azami 300.000 TL tutarında hibe sağlanır.",
        ],
    )
    def test_ust_limit_okunur(self, metin: str) -> None:
        butce = butce_bul(metin)

        assert butce.ust == 300_000.0
        assert butce.birim == "TRY"

    def test_carpanli_ust_limit(self) -> None:
        assert butce_bul("Üst limit 750 bin TL olarak belirlenmiştir.").ust == 750_000.0

    def test_alt_limit_uydurulmaz(self) -> None:
        # Yalnızca üst limit yazıyorsa alt limit BOŞ kalır; "0'dan başlar" varsayılmaz.
        assert butce_bul("Üst limit 300.000 TL'dir.").alt is None


class TestAralik:
    def test_tireli_aralik(self) -> None:
        butce = butce_bul("Proje bütçesi 500.000 - 6.000.000 TL aralığında olmalıdır.")

        assert butce.alt == 500_000.0
        assert butce.ust == 6_000_000.0

    def test_ile_baglaci(self) -> None:
        butce = butce_bul("Destek tutarı 100.000 TL ile 2.000.000 TL arasındadır.")

        assert butce.alt == 100_000.0
        assert butce.ust == 2_000_000.0

    def test_ters_aralik_kabul_edilmez(self) -> None:
        # Üst alttan küçükse cümle yanlış okunmuştur; uydurma aralık üretilmez.
        butce = butce_bul("6.000.000 - 500.000 TL")

        assert butce.alt is None


class TestParaBirimi:
    @pytest.mark.parametrize(
        ("metin", "beklenen"),
        [
            ("Üst limit 300.000 TL", "TRY"),
            ("Üst limit 300.000 EUR", "EUR"),
            ("Üst limit 300.000 USD", "USD"),
            ("Üst limit 2 milyon avro", "EUR"),
        ],
    )
    def test_birim_okunur(self, metin: str, beklenen: str) -> None:
        assert butce_bul(metin).birim == beklenen


class TestDestekOrani:
    @pytest.mark.parametrize(
        "metin",
        [
            "Uygun maliyetlerin %50'si hibe olarak karşılanır.",
            "Destek oranı yüzde 50'dir.",
            "Hibe oranı %50 olarak uygulanır.",
        ],
    )
    def test_destek_orani_okunur(self, metin: str) -> None:
        assert oran_bul(metin) == 0.5

    @pytest.mark.parametrize(
        "metin",
        [
            "Fiyatlara %20 KDV dahildir.",
            "Kadın çalışan oranı en az %30 olmalıdır.",
            "Yıllık faiz oranı %45'tir.",
        ],
    )
    def test_destek_disi_yuzdeler_alinmaz(self, metin: str) -> None:
        # Metindeki her yüzde destek oranı değildir; alınırsa çağrı yanlış anlatılır.
        assert oran_bul(metin) is None


class TestAklaYatkinlik:
    @pytest.mark.parametrize(
        "metin",
        [
            "5746 sayılı Kanun kapsamında değerlendirilir.",
            "İhale kayıt numarası 2026/15 TL",
            "Madde 3 uyarınca",
        ],
    )
    def test_butce_olmayan_sayilar_alinmaz(self, metin: str) -> None:
        assert butce_bul(metin).ust is None

    def test_cok_kucuk_tutar_alinmaz(self) -> None:
        # 500 TL bir destek üst limiti olarak anlamlı değildir.
        assert butce_bul("Başvuru ücreti 500 TL'dir.").ust is None


class TestYuk:
    def test_bulunamazsa_none_doner(self) -> None:
        # Boş bütçe nesnesi kaydı "girilmiş ama sıfır" hâline getirir; None gitmeli.
        assert butce_yuku("Herhangi bir tutar geçmeyen metin.") is None

    def test_yuk_api_alanlariyla_ayni(self) -> None:
        yuk = butce_yuku("Üst limit 300.000 TL, destek oranı %50'dir.")

        assert yuk == {
            "minAmount": None,
            "maxAmount": 300_000.0,
            "currency": "TRY",
            "supportRate": 0.5,
        }

    def test_gercek_cagri_metni(self) -> None:
        metin = (
            "2026 Yılı Ar-Ge ve Dijitalleşme Mali Destek Programı kapsamında "
            "proje bütçesi 500.000 TL ile 6.000.000 TL arasında olacaktır. "
            "Uygun maliyetlerin en fazla %60'ı destek olarak karşılanır."
        )
        yuk = butce_yuku(metin)

        assert yuk is not None
        assert yuk["minAmount"] == 500_000.0
        assert yuk["maxAmount"] == 6_000_000.0
        assert yuk["supportRate"] == 0.6

    def test_ayni_girdi_ayni_sonuc(self) -> None:
        metin = "Üst limit 1.500.000 TL'dir."

        assert butce_yuku(metin) == butce_yuku(metin)
