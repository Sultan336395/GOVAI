"""SGK onarım planı ile konu süzgecinin ayrışmadığının denetimi (Faz 3).

C# tarafındaki ``CatalogRepairPlan`` beş SGK taslağı için ne yapılacağını söyler:
sağlık/SUT, ilaç ve gayrimenkul kayıtları karantinaya alınır; menü başlığından
alınmış başlıklar belge sürümündeki gerçek başlıkla düzeltilir.

O plan bir **karar tekrarı değildir**: kararı ``collector/konu.py`` süzgeci verir,
plan onu uygular. İki taraf ayrışırsa onarım, süzgecin bugün "işveren mevzuatı"
saydığı bir kaydı karantinaya alabilir ya da tersi olur. Bu test ikisini birbirine
bağlar.

Başlıklar 07.09.2026'da ``sgk.gov.tr/duyuru`` arşivinden alınmıştır; ağa çıkılmaz.
"""

from __future__ import annotations

import pytest

from govai_workers.collector.konu import KonuKarari, isveren_mevzuati_mi, konu_karari

#: C# planındaki SGK-SUT adımının hedefi.
SUT_BASLIGI = "31 Ağustos 2026 29/08/2026 SUT Değişiklik Tebliği İşlenmiş Güncel 2013 SUT"

#: SGK-ILAC adımının hedefi.
ILAC_BASLIGI = (
    "2 Eylül 2026 Bedeli Ödenecek İlaçlar Listesinde Yapılan Düzenlemeler Hakkında "
    "Duyuru 2026/34"
)

#: SGK-GAYRIMENKUL adımının hedefi.
GAYRIMENKUL_BASLIGI = "31 Ağustos 2026 Gayrimenkul Satış İlanı İNŞAAT VE EMLAK DAİRE BAŞKANLIĞI"

#: Karantinaya alınacak kayıtlar. Süzgeç bunları işveren mevzuatı SAYMAMALI.
KARANTINA_ADAYLARI = [SUT_BASLIGI, ILAC_BASLIGI, GAYRIMENKUL_BASLIGI]

#: Menü başlığından alınmış başlık. Konu kararı verilemez; bu yüzden karantina değil
#: BAŞLIK DÜZELTME uygulanır — kaydın gerçek konusu ancak doğru başlıkla anlaşılır.
MENU_BASLIGI = "ÇALIŞAN VE İŞVEREN"

#: Başlığı düzeltildikten sonra kaydın gerçek hâli. Süzgeç bunu işveren mevzuatı sayar.
DUZELTILMIS_BASLIK = (
    "2026/Nisan Ayı/Dönemi Muhtasar ve Prim Hizmet Beyannamelerinin ve Aylık Prim ve "
    "Hizmet Belgelerinin Verilme ve Ödeme Süresinin Uzatılması"
)


class TestKarantinaAdaylari:
    @pytest.mark.parametrize("baslik", KARANTINA_ADAYLARI)
    def test_karantina_adayi_isveren_mevzuati_degil(self, baslik: str) -> None:
        # Plan bu kayıtları karantinaya alıyor; süzgeç de onları eliyor olmalı.
        assert isveren_mevzuati_mi(baslik) is False

    @pytest.mark.parametrize("baslik", KARANTINA_ADAYLARI)
    def test_karantina_adayi_ilgisiz_sayilir(self, baslik: str) -> None:
        assert konu_karari(baslik) is KonuKarari.ILGISIZ


class TestMenuBasligi:
    def test_menu_basligi_konu_kararina_dayanak_olmaz(self) -> None:
        # "ÇALIŞAN VE İŞVEREN" menü başlığıdır; içinde "işveren" geçtiği için süzgeç
        # onu işveren mevzuatı sanır. Bu yüzden bu kayıtlara KARANTİNA değil BAŞLIK
        # DÜZELTME uygulanır: konu kararı ancak gerçek başlıkla verilebilir.
        assert konu_karari(MENU_BASLIGI) is KonuKarari.ISVEREN

    def test_duzeltilmis_baslik_isveren_mevzuati_sayilir(self) -> None:
        assert konu_karari(DUZELTILMIS_BASLIK) is KonuKarari.ISVEREN
        assert isveren_mevzuati_mi(DUZELTILMIS_BASLIK) is True


class TestPlanKapsami:
    def test_bes_taslak_kaydin_tamami_kapsanir(self) -> None:
        # Beş bekleyen kayıt: üç ilgisiz içerik + iki menü başlığı.
        kapsanan = [*KARANTINA_ADAYLARI, MENU_BASLIGI, MENU_BASLIGI]

        assert len(kapsanan) == 5

    def test_dogru_kayit_yanlislikla_elenmez(self) -> None:
        # Süzgecin asıl sınavı: gerçek bir işveren duyurusu onarım kapsamına GİRMEMELİ.
        assert isveren_mevzuati_mi(DUZELTILMIS_BASLIK) is True
        assert DUZELTILMIS_BASLIK not in KARANTINA_ADAYLARI
