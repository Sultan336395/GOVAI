"""Bütçe tutarlarının türüne göre ayrılması (Faz 3).

Gerçek KOSGEB belgesiyle ortaya çıkan hata: KOBİ Dijital Dönüşüm sayfasında

    "geri ödemesiz destek üst limiti 1.500.000 TL"
    "İşletme Başına Kredi Üst Limiti: 20.000.000 TL"

arka arkaya geçiyor. Sistem en büyüğünü alıp 20 milyonu **hibe** gibi gösteriyordu.
Kredi bir borçtur; karışması kullanıcının başvuru kararını yanlış yönlendirir.

Metinler KOSGEB'in gerçek destek detay sayfalarından alınmış alıntılardır.
"""

from __future__ import annotations

from govai_workers.parser.butce_turu import (
    ButceTuru,
    OranTuru,
    butce_kalemleri,
    butce_yuku,
    ilk,
    oran_kalemleri,
)

#: KOBİ Dijital Dönüşüm Destek Programı — hibe ve kredi aynı paragrafta.
KOBI_DIJITAL = (
    "Destek Unsurları Yazılım ve donanım giderleri için geri ödemesiz destek üst "
    "limiti 1.500.000 TL'dir. İşletme Başına Kredi Üst Limiti: 20.000.000 TL "
    "Azami Kredi Vadesi: 36 Ay."
)

#: Girişimci Destek Programı — ortaklık payı ile destek oranı aynı belgede.
GIRISIMCI = (
    "Ancak, girişimcinin destek programı başvurusunda bulunduğu işletmedeki ortaklık "
    "payı en az %50 olmalıdır. Makine-Teçhizat ve Yazılım Giderleri Desteği üst "
    "limiti 1.000.000 TL olup destek oranı %60'tır."
)


class TestKrediHibeAyrimi:
    def test_hibe_ve_kredi_ayrilir(self) -> None:
        kalemler = butce_kalemleri(KOBI_DIJITAL)

        hibe = ilk(kalemler, ButceTuru.HIBE_UST_LIMITI)
        kredi = ilk(kalemler, ButceTuru.KREDI_UST_LIMITI)

        assert hibe is not None and hibe.tutar == 1_500_000.0
        assert kredi is not None and kredi.tutar == 20_000_000.0

    def test_en_buyuk_tutar_hibe_sayilmaz(self) -> None:
        # Asıl hata buydu: 20 milyon kredi, hibe diye gösteriliyordu.
        hibe = ilk(butce_kalemleri(KOBI_DIJITAL), ButceTuru.HIBE_UST_LIMITI)

        assert hibe is not None
        assert hibe.tutar != 20_000_000.0

    def test_eski_alan_krediyle_doldurulmaz(self) -> None:
        yuk = butce_yuku(KOBI_DIJITAL)

        assert yuk is not None
        assert yuk["legacy"] is not None
        assert yuk["legacy"]["maxAmount"] == 1_500_000.0


class TestOranAyrimi:
    def test_ortaklik_payi_destek_orani_degildir(self) -> None:
        oranlar = oran_kalemleri(GIRISIMCI)

        oz_kaynak = [o for o in oranlar if o.tur is OranTuru.OZ_KAYNAK_PAYI]
        destek = [o for o in oranlar if o.tur is OranTuru.DESTEK_ORANI]

        assert any(abs(o.oran - 0.5) < 1e-9 for o in oz_kaynak)
        assert any(abs(o.oran - 0.6) < 1e-9 for o in destek)

    def test_eski_alan_destek_oranini_alir(self) -> None:
        yuk = butce_yuku(GIRISIMCI)

        assert yuk is not None
        assert yuk["legacy"]["supportRate"] == 0.6

    def test_kdv_ve_faiz_orani_alinmaz(self) -> None:
        metin = "Fiyatlara %20 KDV dahildir. Yıllık faiz oranı %45'tir."
        oranlar = oran_kalemleri(metin)

        assert all(o.tur is OranTuru.BELIRSIZ for o in oranlar)


class TestBelirsizlik:
    def test_baglami_kesin_olmayan_tutar_belirsiz_kalir(self) -> None:
        # "Makine-Teçhizat Desteği üst limiti 1.000.000 TL" hibe mi geri ödemeli mi
        # olduğunu söylemiyor; TAHMİN EDİLMEZ.
        metin = "Makine-Teçhizat Desteği üst limiti 1.000.000 TL'dir."
        kalemler = butce_kalemleri(metin)

        assert len(kalemler) == 1
        assert kalemler[0].tur is ButceTuru.BELIRSIZ
        assert kalemler[0].incelenmeli_mi is True

    def test_belirsiz_tutar_eski_alana_yazilmaz(self) -> None:
        # Belirsiz tutar eski alana yazılırsa ekranda kesin hibe gibi görünürdü.
        yuk = butce_yuku("Makine-Teçhizat Desteği üst limiti 1.000.000 TL'dir.")

        assert yuk is not None
        assert yuk["legacy"] is None

    def test_hicbir_tutar_yoksa_none(self) -> None:
        assert butce_yuku("Programın amacı rekabet gücünü artırmaktır.") is None


class TestKanitBaglantisi:
    def test_her_kalem_kendi_alintisina_baglidir(self) -> None:
        for kalem in butce_kalemleri(KOBI_DIJITAL):
            assert kalem.alinti
            assert kalem.baslangic < kalem.bitis
            # Aralık gerçekten o tutarı gösteriyor.
            assert KOBI_DIJITAL[kalem.baslangic:kalem.bitis].strip()

    def test_alinti_belgede_gercekten_var(self) -> None:
        for kalem in butce_kalemleri(KOBI_DIJITAL):
            # Alıntı boşluk normalize edilmiş hâlidir; ilk kelimesi belgede olmalı.
            assert kalem.alinti.split()[0] in KOBI_DIJITAL

    def test_oran_kalemleri_de_kanita_baglidir(self) -> None:
        for oran in oran_kalemleri(GIRISIMCI):
            assert oran.alinti
            assert oran.baslangic < oran.bitis


class TestEtiketler:
    def test_her_turun_turkce_etiketi_var(self) -> None:
        for kalem in butce_kalemleri(KOBI_DIJITAL):
            assert kalem.etiket
            assert not kalem.etiket.startswith("ButceTuru")

    def test_belirsiz_etiketi_acikca_soyler(self) -> None:
        kalem = butce_kalemleri("Makine Desteği üst limiti 1.000.000 TL")[0]

        assert kalem.etiket == "Türü belirlenemedi"


class TestKararlilik:
    def test_ayni_girdi_ayni_sonuc(self) -> None:
        assert butce_yuku(KOBI_DIJITAL) == butce_yuku(KOBI_DIJITAL)

    def test_akla_yatkinlik_korunur(self) -> None:
        # Kanun numarası ve tarih bütçe değildir.
        assert butce_kalemleri("5746 sayılı Kanun kapsamında 2026 yılında") == []
