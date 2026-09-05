"""Resmî Gazete ihale ilanlarının alan çıkarımı (Faz 2).

Metinler 06.09.2026 tarihli Resmî Gazete ilan bölümünden alınmış gerçek ilanların
baş kısımlarıdır.

Ayrıştırıcının genel yolu başlığı belgenin İLK SATIRINDAN alıyordu; iki satıra
bölünmüş başlıkların yarısı kesiliyor ve kurum olarak KAYNAĞIN adı ("Resmî Gazete
İhale İlanları") yazılıyordu. Oysa ihaleyi açan idare belgenin içinde yazılıdır ve
bir danışman için asıl bilgi odur.
"""

from __future__ import annotations

from govai_workers.parser.ihale import ihale_alanlari

# Gerçek ilan: 20260906-3-2.pdf
TIBBI_CIHAZ = """13 KALEM TIBBİ CİHAZ/DEMİRBAŞ MALZEME
ALIMI İHALE EDİLECEKTİR
Ankara İl Sağlık Müdürlüğü Ankara Bilkent Şehir Hastanesi Koordinatör Başhekimliğinden:
2026 Yılı 13 Kalem Tıbbi Cihaz/Demirbaş Malzeme İhalesi 21/02/2013 tarihli ve 6428
sayılı Sağlık Bakanlığınca Kamu Özel İş Birliği Modeli ile Tesis Yaptırılması
hükümlerine göre yapılacaktır.
İhale tarihi 25/09/2026 günü saat 10.00'dur.
"""

# Gerçek ilan: 20260906-3-6.pdf
CAY_NAKLIYE = """DİDİ SOĞUK ÇAY NAKLİYE HİZMETİ SATIN ALINACAKTIR
Çay İşletmeleri Genel Müdürlüğünden:
1- Teşekkülümüz ihtiyacı olan Didi soğuk çay nakliye hizmeti, % 20 artar - azalır
toleranslı olarak Satınalma ve İhale Yönetmeliğimiz kapsamında alınacaktır.
"""


class TestBaslik:
    def test_iki_satira_bolunmus_baslik_birlestirilir(self) -> None:
        a = ihale_alanlari(TIBBI_CIHAZ)

        assert a.baslik == "13 KALEM TIBBİ CİHAZ/DEMİRBAŞ MALZEME ALIMI İHALE EDİLECEKTİR"

    def test_tek_satirlik_baslik_bozulmaz(self) -> None:
        a = ihale_alanlari(CAY_NAKLIYE)

        assert a.baslik == "DİDİ SOĞUK ÇAY NAKLİYE HİZMETİ SATIN ALINACAKTIR"


class TestKurum:
    def test_ihaleyi_acan_idare_cikarilir(self) -> None:
        a = ihale_alanlari(TIBBI_CIHAZ)

        assert a.kurum == (
            "Ankara İl Sağlık Müdürlüğü Ankara Bilkent Şehir Hastanesi "
            "Koordinatör Başhekimliğinden"
        )

    def test_kisa_kurum_adi(self) -> None:
        a = ihale_alanlari(CAY_NAKLIYE)

        assert a.kurum == "Çay İşletmeleri Genel Müdürlüğünden"

    def test_kaynak_adi_kuruma_karismaz(self) -> None:
        # Eski davranış kurum olarak kaynağın adını yazıyordu.
        a = ihale_alanlari(TIBBI_CIHAZ)

        assert a.kurum is not None
        assert "Resmî Gazete" not in a.kurum


class TestTarih:
    def test_ihale_tarihi_baglamdan_alinir(self) -> None:
        a = ihale_alanlari(TIBBI_CIHAZ)

        assert a.ihale_tarihi == "2026-09-25"

    def test_mevzuat_atfindaki_tarih_ihale_tarihi_sayilmaz(self) -> None:
        # Metinde "21/02/2013 tarihli ve 6428 sayılı Kanun" geçiyor; bu bir mevzuat
        # atfıdır, ihale tarihi değildir.
        a = ihale_alanlari(TIBBI_CIHAZ)

        assert a.ihale_tarihi != "2013-02-21"

    def test_tarih_yoksa_uydurulmaz(self) -> None:
        a = ihale_alanlari(CAY_NAKLIYE)

        assert a.ihale_tarihi is None

    def test_gecersiz_tarih_atlanir(self) -> None:
        metin = "BİR İŞ İHALE EDİLECEKTİR\nX Müdürlüğünden:\nİhale tarihi 45/13/2026 günüdür.\n"

        assert ihale_alanlari(metin).ihale_tarihi is None


class TestBeklenmeyenBicim:
    def test_kurum_satiri_yoksa_hicbir_alan_uydurulmaz(self) -> None:
        # Beklenen ilan biçiminde değil: başlık da güvenilir değildir.
        metin = "Rastgele bir belge metni.\nİkinci satır.\nÜçüncü satır.\n"
        a = ihale_alanlari(metin)

        assert a.baslik is None
        assert a.kurum is None
        assert a.ihale_tarihi is None

    def test_govdedeki_uzak_atif_kurum_sayilmaz(self) -> None:
        # 6 satırdan sonra gelen "…den:" ilanın künyesi değil, gövdedeki bir atıftır.
        metin = "BAŞLIK\n" + "gövde satırı\n" * 8 + "Başka Bir Müdürlükten:\n"

        assert ihale_alanlari(metin).kurum is None

    def test_bos_metin_cokertmez(self) -> None:
        a = ihale_alanlari("")

        assert a.baslik is None and a.kurum is None


class TestBolumBasligiKarismaz:
    def test_toplu_bolum_basligi_ihale_alani_uretmez(self) -> None:
        # Bölüm sayfasında "…den:" satırı yoktur; alan üretilmez.
        metin = (
            "ARTIRMA, EKSİLTME VE İHALE İLÂNLARI\n"
            "6 Eylül 2026 PAZAR | Resmî Gazete | Sayı : 33362\n"
        )
        a = ihale_alanlari(metin)

        assert a.baslik is None
        assert a.kurum is None
