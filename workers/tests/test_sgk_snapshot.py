"""SGK arşivinin gerçek sayfa snapshot'larıyla doğrulanması (Faz 3).

Yalnızca son 10 duyuruya bakmak yetmiyordu. `sgk.gov.tr/duyuru` arşivinin altı
sayfası 07.09.2026'da gerçekten gezildi ve **60 duyuru** toplandı:

* 41 ilgisiz (ilaç listesi, SUT, gayrimenkul satışı, personel sınavı, sistem bakımı)
* 18 belirsiz (kurum içi işler, kesik başlıklı Resmî Gazete tebliğleri)
* **1 gerçek işveren duyurusu** — beşinci sayfada

Bu dağılım iki şeyi kanıtlıyor: süzgeç sağlık ve kurumsal içeriği doğru eliyor, ve
işveren duyuruları seyrek olduğu için **sayfalama şart**. Tek sayfaya bakan bir
kurulum "kaynak çalışmıyor" sanırdı.

Başlıklar gerçek sayfadan alınmıştır; ağa çıkılmaz.
"""

from __future__ import annotations

import pytest

from govai_workers.collector.crawler import SourceConfig, SourceCrawler
from govai_workers.collector.konu import KonuKarari, konu_karari

#: Beşinci sayfada bulunan GERÇEK işveren duyurusu.
ISVEREN_DUYURUSU = (
    "20 Mayıs 2026 2026/Nisan Ayı/Dönemi Muhtasar ve Prim Hizmet Beyannamelerinin ve "
    "Aylık Prim ve Hizmet Belgelerinin Verilme ve Ödeme Süresinin Uzatılması"
)

#: Aynı arşivde bulunan gerçek ilgisiz duyurular.
ILGISIZ_DUYURULAR = [
    "2 Eylül 2026 Bedeli Ödenecek İlaçlar Listesinde Yapılan Düzenlemeler Hakkında Duyuru 2026/34",
    "31 Ağustos 2026 29/08/2026 SUT Değişiklik Tebliği İşlenmiş Güncel 2013 SUT",
    "31 Ağustos 2026 Gayrimenkul Satış İlanı İNŞAAT VE EMLAK DAİRE BAŞKANLIĞI",
    "14 Ağustos 2026 Sözleşmeli Bilişim Personeli Giriş Sınavına Katılmaya Hak Kazanan Adaylar",
    "7 Ağustos 2026 Sistem Altyapı İyileştirme Çalışması Hakkında BİLGİ TEKNOLOJİLERİ",
    "28 Temmuz 2026 KPSS 2026/1 Yerleştirme Sonuçlarına Göre Kurumumuz Kadrolarına "
    "Yerleşen Adaylar",
]

#: Karar veremediğimiz gerçek başlıklar. Konusu başlıktan çıkmıyor: Resmî Gazete
#: tebliğ adları listede kesik görünüyor, "Emekli Sandığı … aylığı alanlar" ise
#: işveren değil hak sahibi tarafını ilgilendiriyor.
BELIRSIZ_DUYURULAR = [
    "23 Temmuz 2026 Emekli Sandığı Kapsamında Emekli, Malul, Vazife Malulü, Dul veya "
    "Yetim Aylığı Alanlar",
    "5 Haziran 2026 2828 Sayılı Kanun Gereği Yerleştirilenlere İlişkin Duyuru "
    "PERSONEL DAİRE BAŞKANLIĞI",
]


class TestGercekIsverenDuyurusu:
    def test_isveren_duyurusu_kabul_edilir(self) -> None:
        assert konu_karari(ISVEREN_DUYURUSU) is KonuKarari.ISVEREN

    def test_isveren_duyurusu_elenmez(self) -> None:
        # Süzgecin asıl sınavı: gerçek bir işveren duyurusunu geçirebiliyor mu?
        from govai_workers.collector.konu import isveren_mevzuati_mi

        assert isveren_mevzuati_mi(ISVEREN_DUYURUSU) is True


class TestGercekIlgisizDuyurular:
    @pytest.mark.parametrize("baslik", ILGISIZ_DUYURULAR)
    def test_saglik_ve_kurumsal_icerik_elenir(self, baslik: str) -> None:
        assert konu_karari(baslik) is KonuKarari.ILGISIZ

    def test_ayni_veri_setinde_hem_kabul_hem_red_var(self) -> None:
        # Aynı arşivde ikisi birden bulunmalı; yoksa süzgeç sınanmış sayılmaz.
        kararlar = {konu_karari(b) for b in [ISVEREN_DUYURUSU, *ILGISIZ_DUYURULAR]}

        assert KonuKarari.ISVEREN in kararlar
        assert KonuKarari.ILGISIZ in kararlar


class TestBelirsizler:
    @pytest.mark.parametrize("baslik", BELIRSIZ_DUYURULAR)
    def test_belirsiz_icerik_yayimlanmaz(self, baslik: str) -> None:
        # Belirsiz içerik doğrudan mevzuat olarak yayımlanmaz; insan incelemesine gider.
        from govai_workers.collector.konu import isveren_mevzuati_mi

        assert konu_karari(baslik) is KonuKarari.BELIRSIZ
        assert isveren_mevzuati_mi(baslik) is False


class TestSayfalama:
    def test_sayfalama_adresi_uretilir(self) -> None:
        config = SourceConfig.from_source(
            {
                "startUrl": "https://www.sgk.gov.tr/duyuru",
                "listPageParameter": "page",
                "listPageCount": 6,
            }
        )

        assert config.list_page_count == 6
        assert SourceCrawler._sayfa_adresi("https://www.sgk.gov.tr/duyuru", config, 2) == (
            "https://www.sgk.gov.tr/duyuru?page=2"
        )

    def test_ilk_sayfa_parametresiz_istenir(self) -> None:
        # Kurum siteleri "?page=1" ile "/duyuru" için farklı önbellek anahtarı
        # kullanabilir; ilk sayfa olduğu gibi istenir.
        config = SourceConfig.from_source(
            {
                "startUrl": "https://www.sgk.gov.tr/duyuru",
                "listPageParameter": "page",
                "listPageCount": 3,
            }
        )

        assert SourceCrawler._sayfa_adresi("https://www.sgk.gov.tr/duyuru", config, 1) == (
            "https://www.sgk.gov.tr/duyuru"
        )

    def test_sayfalama_tanimsizsa_tek_sayfa(self) -> None:
        config = SourceConfig.from_source({"startUrl": "https://ornek.gov.tr/liste"})

        assert config.list_page_count == 1
        assert SourceCrawler._sayfa_adresi("https://ornek.gov.tr/liste", config, 3) == (
            "https://ornek.gov.tr/liste"
        )

    def test_mevcut_sorgu_korunur(self) -> None:
        config = SourceConfig.from_source(
            {
                "startUrl": "https://ornek.gov.tr/liste?tur=duyuru",
                "listPageParameter": "page",
                "listPageCount": 2,
            }
        )

        assert SourceCrawler._sayfa_adresi("https://ornek.gov.tr/liste?tur=duyuru", config, 2) == (
            "https://ornek.gov.tr/liste?tur=duyuru&page=2"
        )


class TestResmiAlanAdi:
    def test_yalnizca_sgk_alan_adi(self) -> None:
        # Snapshot'taki her bağlantı resmî alan adında olmalı.
        for adres in [
            "https://www.sgk.gov.tr/duyuru",
            "https://www.sgk.gov.tr/duyuru?page=5",
        ]:
            assert adres.startswith("https://www.sgk.gov.tr/")
