"""Gerçek KOSGEB belgeleriyle bütçe ve dayanak çıkarımı (Faz 3).

Metinler 07.09.2026'da KOSGEB'in gerçek destek detay sayfalarından alınmış kısa
alıntılardır. Belgelerin tamamı depoya konmaz; testin işi çıkarımı doğrulamaktır,
kaynağın kopyasını saklamak değil.

Bu testler iki gerçek hatayı sabitler — ikisi de gerçek metinle karşılaşınca çıktı:

1. "girişimcinin ortaklık payı en az %50 olmalıdır" cümlesi **destek oranı** sanılıyordu.
   Ortaklık payı bir başvuru koşuludur; destek oranı olarak kaydedilirse çağrı yanlış
   anlatılır ve danışman hibe oranını yanlış hesaplar.
2. Kurum sayfası "Mevzuat" başlığının hemen ardından yönerge adını yazıyor; künye
   "Mevzuat KOBİ Dijital Dönüşüm … Yönergesi" oluyordu.
"""

from __future__ import annotations

from govai_workers.parser.butce import oran_bul
from govai_workers.parser.butce_turu import ButceTuru, butce_kalemleri, ilk
from govai_workers.parser.dayanak import dayanak_metni

#: KOBİ Dijital Dönüşüm Destek Programı — gerçek metinden alıntı.
KOBI_DIJITAL = (
    "KOBİ Dijital Dönüşüm Destek Programı Programın Amacı Ülkenin ulusal ve "
    "uluslararası hedefleri doğrultusunda, küçük ve orta ölçekli işletmelerin iş "
    "süreçlerinin geliştirilmesi ve verimli hale getirilmesi amacıyla dijital dönüşüm "
    "süreçlerinin desteklenmesidir. Başvuru Şartları Başvuru yapacak işletmenin; NACE "
    "koduna göre C-İmalat sektöründe faaliyet gösteriyor olması, KOSGEB Veri Tabanında "
    "kayıtlı olması gerekmektedir. Destek Unsurları Yazılım ve donanım giderleri için "
    "geri ödemesiz destek üst limiti 1.500.000 TL'dir. İşletme Başına Kredi Üst Limiti: "
    "20.000.000 TL Azami Kredi Vadesi: 36 Ay. Mevzuat KOBİ Dijital Dönüşüm Destek "
    "Programı Yönergesi kapsamında yürütülür."
)

#: Girişimci Destek Programı — gerçek metinden alıntı.
GIRISIMCI = (
    "Girişimci Destek Programı Programın Amacı yeni kurulan işletmeleri desteklemektir. "
    "Başvuru Şartları İş Kurma Desteği için KOSGEB tarafından desteklenen sektörlerde "
    "faaliyet gösteren 0-1 yaş aralığındaki işletme olması gerekmektedir. Ancak, "
    "girişimcinin destek programı başvurusunda bulunduğu işletmedeki ortaklık payı en az "
    "%50 olmalıdır. Destek programı süresinde girişimcinin ortaklık payı %50'nin altına "
    "düşemez. Destek Unsurları Makine-Teçhizat ve Yazılım Giderleri Desteği üst limiti "
    "1.000.000 TL olup destek oranı %60'tır. Mevzuat Girişimci Destek Programı Yönergesi "
    "ve Uygulama Esasları çerçevesinde uygulanır."
)


class TestKobiDijital:
    def test_hibe_ust_limiti_krediden_ayrilir(self) -> None:
        kalemler = butce_kalemleri(KOBI_DIJITAL)

        hibe = ilk(kalemler, ButceTuru.HIBE_UST_LIMITI)
        kredi = ilk(kalemler, ButceTuru.KREDI_UST_LIMITI)

        assert hibe is not None and hibe.tutar == 1_500_000.0
        assert kredi is not None and kredi.tutar == 20_000_000.0

    def test_dayanak_bolum_basligi_olmadan_okunur(self) -> None:
        # "Mevzuat" bir bölüm başlığıdır, mevzuatın adı değil.
        dayanak = dayanak_metni(KOBI_DIJITAL)

        assert dayanak is not None
        assert "Yönergesi" in dayanak
        assert not dayanak.startswith("Mevzuat ")


class TestGirisimci:
    def test_ortaklik_payi_destek_orani_sayilmaz(self) -> None:
        # Gerçek metinle ortaya çıkan hata: "%50 ortaklık payı" destek oranı sanılıyordu.
        # Doğru destek oranı aynı metinde %60 olarak yazılı.
        assert oran_bul(GIRISIMCI) == 0.6

    def test_ust_limit_okunur(self) -> None:
        kalemler = butce_kalemleri(GIRISIMCI)

        assert any(k.tutar == 1_000_000.0 for k in kalemler)

    def test_dayanak_okunur(self) -> None:
        dayanak = dayanak_metni(GIRISIMCI)

        assert dayanak is not None
        assert "Girişimci Destek Programı Yönergesi" in dayanak


class TestUydurmama:
    def test_bulunmayan_alan_none_kalir(self) -> None:
        # Bütçesi yazılmayan bir metinde tutar UYDURULMAZ; alan NotProvided kalır.
        metin = (
            "Programın Amacı işletmelerin rekabet gücünü artırmaktır. Başvuru Şartları "
            "KOSGEB Veri Tabanında kayıtlı olmaktır."
        )

        assert butce_kalemleri(metin) == []
        assert dayanak_metni(metin) is None

    def test_alt_limit_uydurulmaz(self) -> None:
        from govai_workers.parser.butce_turu import butce_yuku

        yuk = butce_yuku(KOBI_DIJITAL)

        assert yuk is not None
        assert yuk["legacy"]["minAmount"] is None

    def test_ayni_girdi_ayni_sonuc(self) -> None:
        assert butce_kalemleri(GIRISIMCI) == butce_kalemleri(GIRISIMCI)
        assert dayanak_metni(GIRISIMCI) == dayanak_metni(GIRISIMCI)
