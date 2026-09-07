"""Menü/kategori başlığının duyuru adı olarak kullanılmaması (Faz 3).

Sahada görülen hata: SGK'nın "29/08/2026 SUT Değişiklik Tebliği …" başlıklı iki
duyurusu, mevzuat kaydına **"ÇALIŞAN VE İŞVEREN"** adıyla girdi. Sebep iki katmanlıydı:

1. Tarihle BAŞLAYAN başlık, "tarih başlığı" sanılıp reddediliyordu. Oysa resmî
   duyuruların tarihle başlaması olağandır.
2. Reddedilince metne düşülüyordu ve gövdedeki menü başlığı büyük harfli olduğu için
   "resmî başlık" denetiminden geçiyordu.

Bu testler iki tarafı da sabitler: geçerli başlık elenmez, menü başlığı geçmez.
"""

from __future__ import annotations

import pytest

from govai_workers.parser.runner import belge_basligi, gezinme_basligi_mi, tarih_basligi_mi

#: SGK duyuru sayfasının gövdesinden alınmış menü satırları.
SGK_GOVDE = "ÇALIŞAN VE İŞVEREN\nMENÜ\nKURUMSAL\nDuyurunun gövde metni burada yer alır."


class TestTarihBasligi:
    @pytest.mark.parametrize(
        "baslik",
        [
            "29/08/2026 SUT Değişiklik Tebliği İşlenmiş Güncel 2013 SUT",
            "29/08/2026 Tarihli ve 33355 Sayılı Resmî Gazete’de Yayımlanan Tebliğ",
            "01.01.2026 Yılı Asgari Ücret Tespit Komisyonu Kararı",
        ],
    )
    def test_tarihle_baslayan_gercek_baslik_elenmez(self, baslik: str) -> None:
        # Sahadaki hatanın birinci ayağı buydu.
        assert tarih_basligi_mi(baslik) is False
        assert belge_basligi(baslik, SGK_GOVDE) == baslik

    @pytest.mark.parametrize(
        "baslik",
        [
            "29/08/2026",
            "26.08.2026",
            "26/08/2026 tarihli",
            "1 Ocak 2026",
            "26 Ağustos 2026 ÇARŞAMBA",
        ],
    )
    def test_yalnizca_tarihten_ibaret_baslik_reddedilir(self, baslik: str) -> None:
        assert tarih_basligi_mi(baslik) is True


class TestGezinmeBasligi:
    @pytest.mark.parametrize(
        "satir",
        [
            "ÇALIŞAN VE İŞVEREN",
            "Kurumsal",
            "MENÜ",
            "Duyurular",
            "Mevzuat",
            "İletişim",
            "Hakkımızda",
        ],
    )
    def test_menu_basligi_taninir(self, satir: str) -> None:
        assert gezinme_basligi_mi(satir) is True

    @pytest.mark.parametrize(
        "satir",
        [
            "Çalışan ve işveren primlerine ilişkin duyuru",
            "Gayrimenkul Satış İlanı",
            "Bedeli Ödenecek İlaçlar Listesinde Yapılan Düzenlemeler Hakkında Duyuru 2026/33",
        ],
    )
    def test_gercek_baslik_menu_sayilmaz(self, satir: str) -> None:
        # Karşılaştırma TAM eşleşmedir: gerçek bir duyurunun adında bu kelimeler geçebilir.
        assert gezinme_basligi_mi(satir) is False


class TestBelgeBasligi:
    def test_menu_basligi_duyuru_adi_olmaz(self) -> None:
        # Sahadaki hatanın ikinci ayağı: gövdedeki menü başlığı ada dönüşüyordu.
        assert belge_basligi("ÇALIŞAN VE İŞVEREN", SGK_GOVDE) is None

    def test_sayfa_basligi_menu_ise_metne_dusulur_ve_menu_alinmaz(self) -> None:
        assert belge_basligi("Kurumsal", SGK_GOVDE) is None

    def test_guvenilir_baslik_yoksa_none_doner(self) -> None:
        # None dönmesi karantina demektir; uydurma başlık üretilmez.
        assert belge_basligi("29/08/2026", SGK_GOVDE) is None
        assert belge_basligi(None, SGK_GOVDE) is None

    @pytest.mark.parametrize(
        "baslik",
        [
            "Gayrimenkul Satış İlanı",
            "Bedeli Ödenecek İlaçlar Listesinde Yapılan Düzenlemeler Hakkında Duyuru 2026/33",
            "Bedeli Ödenecek İlaçlar Listesinde Yapılan Düzenlemeler Hakkında Duyuru 2026/34",
        ],
    )
    def test_gercek_sgk_basliklari_korunur(self, baslik: str) -> None:
        # İlk taramada doğru gelen üç kayıt; düzeltme onları bozmamalı.
        assert belge_basligi(baslik, SGK_GOVDE) == baslik

    def test_ayni_girdi_ayni_sonuc(self) -> None:
        baslik = "29/08/2026 SUT Değişiklik Tebliği İşlenmiş Güncel 2013 SUT"

        assert belge_basligi(baslik, SGK_GOVDE) == belge_basligi(baslik, SGK_GOVDE)
