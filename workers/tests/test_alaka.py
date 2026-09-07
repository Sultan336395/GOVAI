"""Alakasız bağlantıların elenmesi (Faz 3).

Tarayıcının üç süzgeci de adrese bakıyordu. Kurum siteleri çağrı listesiyle aynı
bölümde "Çerez Politikası" ve "İletişim"e de bağlantı verir; bunlar geçip çöp fırsat
kaydına dönüşüyordu.

Bu testlerin asıl işi süzgecin **fazla eleme yapmamasını** korumaktır: elenen bir çağrı
hiç görülmez, geçen bir çöp görülür ve silinir.
"""

from __future__ import annotations

import pytest

from govai_workers.collector.alaka import alakasiz_baslik_mi, alakasiz_mi, katla

TABAN = "https://ornek.gov.tr"


class TestKatlama:
    def test_turkce_harfler_katlanir(self) -> None:
        assert katla("ÇEREZ POLİTİKASI") == "cerez politikasi"
        assert katla("İletişim") == "iletisim"

    def test_bos_deger(self) -> None:
        assert katla("") == ""


class TestElenenler:
    @pytest.mark.parametrize(
        "yol",
        [
            "/cerez-politikasi",
            "/kvkk-aydinlatma-metni",
            "/iletisim",
            "/hakkimizda",
            "/site-haritasi",
            "/kullanici-girisi/login",
            "/arama?q=test",
            "/foto-galeri",
            "/bilgi-edinme",
        ],
    )
    def test_kurumsal_sayfalar_elenir(self, yol: str) -> None:
        assert alakasiz_mi(f"{TABAN}{yol}") is True

    @pytest.mark.parametrize(
        "dosya",
        ["/logo.png", "/afis.jpg", "/sunum.zip", "/stil.css", "/tanitim.mp4"],
    )
    def test_belge_olmayan_dosyalar_elenir(self, dosya: str) -> None:
        assert alakasiz_mi(f"{TABAN}{dosya}") is True

    def test_baglanti_metnine_gore_elenir(self) -> None:
        # Adres nötr ama bağlantı metni kurumsal sayfayı ele veriyor.
        assert alakasiz_mi(f"{TABAN}/sayfa/12", "Kişisel Verilerin Korunması") is True

    def test_bos_adres_elenir(self) -> None:
        assert alakasiz_mi("") is True


class TestGecenler:
    @pytest.mark.parametrize(
        "yol",
        [
            "/duyurular/2026-yili-destek-cagrisi",
            "/ilanlar/eskiilanlar/2026/09/20260906-3-1.pdf",
            "/mevzuat/yonetmelik/5746",
            "/destek-programlari/arge",
            "/tesvikler/istihdam",
            "/ihale/2026-15",
            "/en/funding/calls/2026",
        ],
    )
    def test_cagri_ve_mevzuat_gecer(self, yol: str) -> None:
        assert alakasiz_mi(f"{TABAN}{yol}") is False

    def test_koruyucu_kelime_elemeyi_iptal_eder(self):
        # "hakkimizda" elenir; ama altında destek programı varsa geçmelidir.
        assert alakasiz_mi(f"{TABAN}/hakkimizda/destek-programlarimiz") is False

    def test_personel_destegi_elenmez(self) -> None:
        # "personel" tek başına kurumsal sayfadır; "personel desteği" çağrıdır.
        assert alakasiz_mi(f"{TABAN}/personel") is True
        assert alakasiz_mi(f"{TABAN}/personel-destegi-cagrisi") is False

    def test_kararsizlikta_gecirilir(self) -> None:
        # Tanınmayan bir adres elenmez: elenen çağrı hiç görülmez.
        assert alakasiz_mi(f"{TABAN}/sayfa/99231") is False

    def test_pdf_gecer(self) -> None:
        assert alakasiz_mi(f"{TABAN}/belge/2026-cagri.pdf") is False


class TestBaslikSuzgeci:
    @pytest.mark.parametrize(
        "baslik",
        [
            "Çerez Politikası",
            "Kişisel Verilerin Korunması Hakkında Aydınlatma Metni",
            "Sıkça Sorulan Sorular",
            "Bize Ulaşın",
        ],
    )
    def test_kurumsal_baslik_elenir(self, baslik: str) -> None:
        assert alakasiz_baslik_mi(baslik) is True

    @pytest.mark.parametrize(
        "baslik",
        [
            "13 KALEM TIBBİ CİHAZ SATIN ALINACAKTIR",
            "2026 Yılı Ar-Ge ve Dijitalleşme Mali Destek Programı",
            "Kadın ve Genç İstihdamı Prim Desteği",
            "Vergi Usul Kanunu Genel Tebliği",
        ],
    )
    def test_gercek_baslik_gecer(self, baslik: str) -> None:
        assert alakasiz_baslik_mi(baslik) is False

    def test_bos_baslik_elenmez(self) -> None:
        # Başlıksız belge burada değil, karantinada değerlendirilir.
        assert alakasiz_baslik_mi("") is False
