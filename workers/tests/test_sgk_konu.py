"""SGK duyurularının konu ayrımı (Faz 3).

İlk taramada SGK'dan gelen beş kaydın üçü ilaç ve sağlık uygulama tebliğiydi; biri
gayrimenkul satış ilanıydı. Hiçbiri işveren mevzuatı değil. Bunları "sizi ilgilendiren
mevzuat değişikliği" diye göstermek, gerçek bir prim teşviki duyurusunun çöpün arasında
kaybolmasına yol açar.

Başlıkların tamamı SGK'nın gerçek duyuru listesinden alınmıştır.
"""

from __future__ import annotations

import pytest

from govai_workers.collector.konu import (
    KonuKarari,
    gerekce,
    isveren_mevzuati_mi,
    konu_karari,
)

#: SGK /duyuru sayfasından 07.09.2026'da alınmış gerçek başlıklar.
ILGISIZ_BASLIKLAR = [
    "Bedeli Ödenecek İlaçlar Listesinde Yapılan Düzenlemeler Hakkında Duyuru 2026/34",
    "29/08/2026 SUT Değişiklik Tebliği İşlenmiş Güncel 2013 SUT",
    "29/08/2026 Tarihli ve 33355 Sayılı Resmî Gazete’de Yayımlanan Sağlık Uygulama Tebliği",
    "Gayrimenkul Satış İlanı",
    "Planlı Altyapı Çalışması",
    "Sözleşmeli Bilişim Personeli Giriş Sınavına Katılmaya Hak Kazanan Adaylar",
]

ISVEREN_BASLIKLARI = [
    "2026 Nisan Ayı Muhtasar ve Prim Hizmet Beyannamelerinin Verilme Süresinin Uzatılması",
    "Kuruma Olan Borçların Son Ödeme Tarihinin Uzatılmasına Dair Basın Duyurusu",
    "İşyeri Tescil Programlarında 4857 Sayılı Kanun Kapsamında İşveren Vekili Tanımlanması",
    "Asgari Ücret Desteği Hakkında Duyuru",
    "İşverenlere Yönelik Prim Teşviki Uygulaması",
]


class TestIlgisizIcerik:
    @pytest.mark.parametrize("baslik", ILGISIZ_BASLIKLAR)
    def test_saglik_ve_kurumsal_icerik_isveren_mevzuati_degildir(self, baslik: str) -> None:
        assert konu_karari(baslik) is KonuKarari.ILGISIZ
        assert isveren_mevzuati_mi(baslik) is False

    def test_isveren_kelimesi_gecse_bile_saglik_iceriği_elenir(self) -> None:
        # Dışlayıcı işaret önceliklidir: "işveren" geçen bir SUT duyurusu yine de
        # sağlık içeriğidir.
        baslik = "SUT Değişikliği: İşverenlerin Bilgisine"

        assert konu_karari(baslik) is KonuKarari.ILGISIZ


class TestIsverenIcerigi:
    @pytest.mark.parametrize("baslik", ISVEREN_BASLIKLARI)
    def test_isveren_mevzuati_taninir(self, baslik: str) -> None:
        assert konu_karari(baslik) is KonuKarari.ISVEREN
        assert isveren_mevzuati_mi(baslik) is True


class TestBelirsizlik:
    @pytest.mark.parametrize("baslik", ["Danıştay Kararı Hakkında", "Duyuru", "Bilgilendirme"])
    def test_belirsiz_baslik_isveren_sayilmaz(self, baslik: str) -> None:
        # "Bilmiyorum" ile "hayır" ayrıdır; belirsiz kayıt tahminle işveren sayılmaz.
        assert konu_karari(baslik) is KonuKarari.BELIRSIZ
        assert isveren_mevzuati_mi(baslik) is False

    def test_bos_baslik_belirsizdir(self) -> None:
        assert konu_karari("") is KonuKarari.BELIRSIZ

    def test_govde_baslik_kararsizsa_okunur(self) -> None:
        karar = konu_karari(
            "Duyuru",
            "İşverenlerin aylık prim ve hizmet belgesi verme süresi uzatılmıştır.",
        )

        assert karar is KonuKarari.ISVEREN

    def test_govde_dislayici_ise_elenir(self) -> None:
        karar = konu_karari("Duyuru", "Bedeli ödenecek ilaçlar listesinde değişiklik yapılmıştır.")

        assert karar is KonuKarari.ILGISIZ


class TestGerekce:
    def test_gerekce_operatore_gosterilebilir(self) -> None:
        for baslik in ILGISIZ_BASLIKLAR + ISVEREN_BASLIKLARI + ["Danıştay Kararı Hakkında"]:
            metin = gerekce(baslik)

            assert metin.endswith(".")
            assert len(metin) > 20

    def test_ilgisiz_gerekce_sebebi_soyler(self) -> None:
        assert "kaydedilmez" in gerekce("Gayrimenkul Satış İlanı")


class TestKararlilik:
    def test_ayni_girdi_ayni_sonuc(self) -> None:
        baslik = "Asgari Ücret Desteği Hakkında Duyuru"

        assert konu_karari(baslik) is konu_karari(baslik)

    def test_kelime_ortasinda_eslesmez(self) -> None:
        # "sut" dışlayıcıdır ama "usturuplu" içinde geçmesi eleme sebebi olamaz.
        assert konu_karari("Usturuplu İşveren Duyurusu") is KonuKarari.ISVEREN
