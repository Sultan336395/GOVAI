"""Yayın takvimi: "bugün yayın yok" arıza değildir.

Resmî Gazete hafta sonu ve resmî tatillerde yayımlanmaz. 30.08.2026'da doğrulama
"seçici hiç bağlantı çıkarmadı" deyip kaynağı arızalı işaretledi — oysa sayfa
erişilebilirdi ve seçici sağlamdı, sadece o gün sayı yoktu.

Testler üç durumu ayırır ve **hiçbir tarihi koda yazmaz**: "bugün" her testte
dışarıdan verilir.
"""

from __future__ import annotations

from datetime import date

from govai_workers.collector.publication import (
    PublicationOutcome,
    archive_url,
    baglantidan_tarih,
    son_yayini_bul,
)

TEMPLATE = "/fihrist?tarih={date}"
BASE = "https://www.resmigazete.gov.tr"


class SahteArsiv:
    """Belirli günlerde sayı yayımlamış bir arşiv."""

    def __init__(self, yayin_gunleri: set[date], erisilemeyen: set[date] | None = None) -> None:
        self._yayin = yayin_gunleri
        self._erisilemeyen = erisilemeyen or set()
        self.istenen: list[str] = []

    def fetch(self, url: str):
        self.istenen.append(url)
        gun = date.fromisoformat(url.rsplit("=", 1)[1])

        if gun in self._erisilemeyen:
            return None

        return {"gun": gun}

    def link_cikar(self, belge) -> list[str]:
        gun = belge["gun"]

        if gun not in self._yayin:
            return []

        ymd = gun.strftime("%Y%m%d")
        return [
            f"{BASE}/eskiler/{gun.year}/{gun.month:02d}/{ymd}-1.htm",
            f"{BASE}/ilanlar/eskiilanlar/{gun.year}/{gun.month:02d}/{ymd}-3.htm",
        ]

    def ara(self, bugun: date, geriye_gun: int = 9):
        return son_yayini_bul(
            fetch=self.fetch, link_cikar=self.link_cikar,
            template=TEMPLATE, base_url=BASE, bugun=bugun, geriye_gun=geriye_gun,
        )


class TestBugunYayinVar:
    def test_bugunun_sayisi_varsa_dogrulanir(self) -> None:
        bugun = date(2026, 8, 27)
        sonuc = SahteArsiv({bugun}).ara(bugun)

        assert sonuc.outcome is PublicationOutcome.VERIFIED
        assert sonuc.outcome.is_healthy
        assert sonuc.published_on == bugun
        assert len(sonuc.links) == 2
        assert sonuc.searched_days == 1


class TestTatilVeHaftaSonu:
    def test_tatilde_yayin_yok_ama_kaynak_saglikli(self) -> None:
        # Cuma yayımlanmış; cumartesi/pazar/tatil yayın yok.
        cuma = date(2026, 8, 28)
        pazar = date(2026, 8, 30)

        sonuc = SahteArsiv({cuma}).ara(pazar)

        assert sonuc.outcome is PublicationOutcome.NO_NEW_CONTENT
        assert sonuc.outcome.is_healthy, "Yayın yok bir ARIZA DEĞİLDİR"
        assert sonuc.published_on == cuma
        assert len(sonuc.links) == 2
        assert "yayın yok" in sonuc.note

    def test_uzun_bayram_tatili_de_kapsanir(self) -> None:
        # Yedi günlük ara: arşiv penceresi bunu görmeli.
        son_yayin = date(2026, 8, 21)
        bugun = date(2026, 8, 28)

        sonuc = SahteArsiv({son_yayin}).ara(bugun)

        assert sonuc.outcome is PublicationOutcome.NO_NEW_CONTENT
        assert sonuc.published_on == son_yayin
        assert sonuc.searched_days == 8

    def test_en_son_yayimlanan_sayi_bulunur(self) -> None:
        # Birden çok yayın günü varsa EN YENİSİ seçilmeli.
        bugun = date(2026, 8, 30)
        arsiv = SahteArsiv({date(2026, 8, 24), date(2026, 8, 26), date(2026, 8, 28)})

        sonuc = arsiv.ara(bugun)

        assert sonuc.published_on == date(2026, 8, 28)


class TestSeciciBozulmasi:
    def test_arsiv_aciliyor_ama_hicbir_gunde_baglanti_yoksa_secici_bozuk(self) -> None:
        # Sayfalar açılıyor (site ayakta) ama seçici hiçbir şey çıkarmıyor.
        sonuc = SahteArsiv(set()).ara(date(2026, 8, 30))

        assert sonuc.outcome is PublicationOutcome.SELECTOR_BROKEN
        assert not sonuc.outcome.is_healthy
        assert "seçici" in sonuc.note.lower()

    def test_secici_bozuksa_yayin_yok_denmez(self) -> None:
        sonuc = SahteArsiv(set()).ara(date(2026, 8, 30))

        assert sonuc.outcome is not PublicationOutcome.NO_NEW_CONTENT
        assert sonuc.published_on is None


class TestErisimHatasi:
    def test_hicbir_arsiv_sayfasi_acilmazsa_erisilemedi(self) -> None:
        bugun = date(2026, 8, 30)
        hepsi = {bugun.replace(day=g) for g in range(21, 31)}
        arsiv = SahteArsiv(set(), erisilemeyen=hepsi)

        sonuc = arsiv.ara(bugun)

        assert sonuc.outcome is PublicationOutcome.UNREACHABLE
        assert not sonuc.outcome.is_healthy
        assert "erişilemedi" in sonuc.note

    def test_bir_gunun_acilmamasi_arizaya_yol_acmaz(self) -> None:
        # Tek gün açılmadı ama başka bir günde yayın bulundu.
        bugun = date(2026, 8, 30)
        sonuc = SahteArsiv({date(2026, 8, 28)}, erisilemeyen={date(2026, 8, 29)}).ara(bugun)

        assert sonuc.outcome is PublicationOutcome.NO_NEW_CONTENT
        assert sonuc.published_on == date(2026, 8, 28)


class TestAdresUretimi:
    def test_iso_tarih_sablonu(self) -> None:
        assert archive_url("/fihrist?tarih={date}", BASE, date(2026, 8, 27)) == (
            "https://www.resmigazete.gov.tr/fihrist?tarih=2026-08-27"
        )

    def test_sikisik_tarih_sablonu(self) -> None:
        assert archive_url("/arsiv/{yyyymmdd}.htm", BASE, date(2026, 8, 27)) == (
            "https://www.resmigazete.gov.tr/arsiv/20260827.htm"
        )


class TestMukerrerIsleme:
    """Aynı sayı iki kez işlendiğinde mükerrer kayıt oluşmamalı.

    Belge düzeyinde tekilleştirme içerik hash'iyle sunucuda yapılır; burada
    bağlantıdan yayın tarihinin güvenilir biçimde çıkarıldığı doğrulanır — aynı
    tarihe ait bağlantılar aynı sayıyı gösterir.
    """

    def test_baglantidan_yayin_tarihi_cikarilir(self) -> None:
        assert baglantidan_tarih(
            f"{BASE}/eskiler/2026/08/20260827-3.htm"
        ) == date(2026, 8, 27)

        assert baglantidan_tarih(
            f"{BASE}/ilanlar/eskiilanlar/2026/08/20260827-3.htm"
        ) == date(2026, 8, 27)

    def test_ayni_sayinin_iki_baglantisi_ayni_tarihi_verir(self) -> None:
        a = baglantidan_tarih(f"{BASE}/eskiler/2026/08/20260827-1.htm")
        b = baglantidan_tarih(f"{BASE}/eskiler/2026/08/20260827-9.htm")

        assert a == b is not None

    def test_iki_kez_aranan_ayni_gun_ayni_sonucu_verir(self) -> None:
        bugun = date(2026, 8, 30)
        arsiv = SahteArsiv({date(2026, 8, 28)})

        ilk = arsiv.ara(bugun)
        ikinci = arsiv.ara(bugun)

        assert ilk.published_on == ikinci.published_on
        assert ilk.links == ikinci.links

    def test_tarihsiz_adres_none_doner(self) -> None:
        assert baglantidan_tarih(f"{BASE}/hakkimizda.html") is None
