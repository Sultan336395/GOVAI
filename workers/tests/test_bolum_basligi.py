"""Toplu bölüm başlığından fırsat açılmaması (Faz 2).

Kural sunucuda da vardır ve **kaynak doğru odur**; buradaki kopya yalnızca gereksiz
bir tur atmayı önler. Bu yüzden iki listenin aynı kalması gerekir — testler her iki
tarafın da aynı başlıkları tanıdığını sabitler.
"""

from __future__ import annotations

from govai_workers.parser.bolum_basligi import toplu_bolum_basligi


class TestTopluBasliklar:
    def test_resmi_gazete_ilan_bolumu_taninir(self) -> None:
        assert toplu_bolum_basligi("ARTIRMA, EKSİLTME VE İHALE İLÂNLARI") is True

    def test_yazim_farklari_etkilemez(self) -> None:
        for yazim in (
            "ARTIRMA, EKSILTME VE IHALE ILANLARI",
            "artırma, eksiltme ve ihale ilânları",
            "ARTIRMA  EKSİLTME  VE  İHALE  İLANLARI",
            "  ARTIRMA, EKSİLTME VE İHALE İLÂNLARI  ",
        ):
            assert toplu_bolum_basligi(yazim) is True, yazim

    def test_diger_toplu_basliklar(self) -> None:
        for baslik in ("ÇEŞİTLİ İLÂNLAR", "İLAN BÖLÜMÜ", "İHALE İLANLARI", "DUYURULAR"):
            assert toplu_bolum_basligi(baslik) is True, baslik


class TestGercekIlanlar:
    def test_tekil_ihale_ilanlari_elenmez(self) -> None:
        gercekler = (
            "Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğünden: TAŞINMAZ SATILACAKTIR",
            "TCDD 3. Bölge Müdürlüğünden: HİZMET ALINACAKTIR",
            "Sağlık Bakanlığından: TIBBİ CİHAZ SATIN ALINACAKTIR",
            "2026/1 sayılı ihale ilânları kapsamında düzeltme",
            "KOSGEB İşletme Geliştirme Destek Programı Çağrısı",
        )

        for baslik in gercekler:
            assert toplu_bolum_basligi(baslik) is False, baslik

    def test_bos_baslik_toplu_sayilmaz(self) -> None:
        for baslik in (None, "", "   ", "\n"):
            assert toplu_bolum_basligi(baslik) is False


class TestTurkceKatlama:
    def test_noktasiz_i_ile_noktali_i_esitlenir(self) -> None:
        # str.lower() bunu yapmaz: "İLANLAR".lower() -> "i̇lanlar" (birleşik nokta).
        assert toplu_bolum_basligi("İLANLAR") is True
        assert toplu_bolum_basligi("ilanlar") is True
        assert toplu_bolum_basligi("ILANLAR") is True
