"""Resmî Gazete ihale ilanlarının tarih takvimi (Faz 2).

İlan adresi tarihe bağlıdır; kaynağa sabit bir adres yazılamaz. Bu testler adresin
doğru üretildiğini ve hangi günlerin taranacağını sabitler.

Hiçbir testte gerçek saat kullanılmaz: "bugün" her zaman dışarıdan verilir. Aksi
hâlde testler çalıştıkları güne göre sonuç değiştirir ve bir gün sessizce kırılırdı.
"""

from __future__ import annotations

from datetime import date, datetime, timedelta
from zoneinfo import ZoneInfo

from govai_workers.collector.ihale_takvimi import (
    AZAMI_GERI_GUN,
    VARSAYILAN_SABLON,
    bugun_istanbul,
    ihale_adresi,
    taranacak_gunler,
    tekil_ilan_mi,
)

TABAN = "https://www.resmigazete.gov.tr"
UTC = ZoneInfo("UTC")


class TestGunTespiti:
    def test_turkiye_saatine_gore_belirlenir(self) -> None:
        # UTC 21:30 -> Türkiye'de ertesi gün 00:30. UTC'ye bakan bir sistem o günün
        # ilanlarını hiç toplamazdı.
        an = datetime(2026, 9, 6, 21, 30, tzinfo=UTC)

        assert bugun_istanbul(an) == date(2026, 9, 7)

    def test_gun_ortasi_ayni_gun_kalir(self) -> None:
        an = datetime(2026, 9, 6, 9, 0, tzinfo=UTC)

        assert bugun_istanbul(an) == date(2026, 9, 6)

    def test_zaman_dilimsiz_deger_utc_sayilir(self) -> None:
        # Sunucular UTC çalışır; zaman dilimi bilgisi olmayan değer UTC varsayılır.
        an = datetime(2026, 9, 6, 22, 15)

        assert bugun_istanbul(an) == date(2026, 9, 7)


class TestAdresUretimi:
    def test_normal_gun(self) -> None:
        adres = ihale_adresi(VARSAYILAN_SABLON, TABAN, date(2026, 9, 6))

        assert adres == f"{TABAN}/ilanlar/eskiilanlar/2026/09/20260906-3.htm"

    def test_ay_sifir_dolgulu(self) -> None:
        # "2026/9/..." yanlış adrestir; ay iki hane olmalı.
        adres = ihale_adresi(VARSAYILAN_SABLON, TABAN, date(2026, 1, 5))

        assert "/2026/01/20260105-3.htm" in adres

    def test_ay_sonu(self) -> None:
        adres = ihale_adresi(VARSAYILAN_SABLON, TABAN, date(2026, 9, 30))

        assert "/2026/09/20260930-3.htm" in adres

    def test_yil_sonu(self) -> None:
        adres = ihale_adresi(VARSAYILAN_SABLON, TABAN, date(2026, 12, 31))

        assert "/2026/12/20261231-3.htm" in adres

    def test_yil_basi(self) -> None:
        adres = ihale_adresi(VARSAYILAN_SABLON, TABAN, date(2027, 1, 1))

        assert "/2027/01/20270101-3.htm" in adres

    def test_artik_yil_29_subat(self) -> None:
        adres = ihale_adresi(VARSAYILAN_SABLON, TABAN, date(2028, 2, 29))

        assert "/2028/02/20280229-3.htm" in adres

    def test_diger_yer_tutucular(self) -> None:
        adres = ihale_adresi("/a/{yyyy}/{MM}/{dd}/{date}.htm", TABAN, date(2026, 9, 6))

        assert adres == f"{TABAN}/a/2026/09/06/2026-09-06.htm"


class TestTaranacakGunler:
    def test_ilk_calistirmada_yalnizca_bugun(self) -> None:
        # İlk çalıştırma tüm arşivi indirmeye kalkmamalı.
        assert taranacak_gunler(None, date(2026, 9, 6)) == [date(2026, 9, 6)]

    def test_normal_gun_bir_gun_sonra(self) -> None:
        gunler = taranacak_gunler(date(2026, 9, 5), date(2026, 9, 6))

        assert gunler == [date(2026, 9, 6)]

    def test_kacirilan_gunler_tamamlanir(self) -> None:
        # Sistem 3 gün çalışmadı: aradaki günler sırayla tamamlanır.
        gunler = taranacak_gunler(date(2026, 9, 2), date(2026, 9, 6))

        assert gunler == [
            date(2026, 9, 3),
            date(2026, 9, 4),
            date(2026, 9, 5),
            date(2026, 9, 6),
        ]

    def test_hafta_sonu_atlanmaz_listeye_girer(self) -> None:
        # Hafta sonu günleri listeden ÇIKARILMAZ: ilan olup olmadığına sayfa
        # açılınca karar verilir, takvimden varsayılmaz.
        gunler = taranacak_gunler(date(2026, 9, 3), date(2026, 9, 7))
        cumartesi, pazar = date(2026, 9, 5), date(2026, 9, 6)

        assert cumartesi in gunler
        assert pazar in gunler

    def test_ay_gecisi(self) -> None:
        gunler = taranacak_gunler(date(2026, 8, 30), date(2026, 9, 2))

        assert gunler == [date(2026, 8, 31), date(2026, 9, 1), date(2026, 9, 2)]

    def test_yil_gecisi(self) -> None:
        gunler = taranacak_gunler(date(2026, 12, 30), date(2027, 1, 2))

        assert gunler == [
            date(2026, 12, 31),
            date(2027, 1, 1),
            date(2027, 1, 2),
        ]

    def test_uzun_kesinti_ust_sinirla_kisitlanir(self) -> None:
        # 100 günlük kesinti resmî sunucuya 100 istek göndermemeli.
        gunler = taranacak_gunler(date(2026, 1, 1), date(2026, 4, 11))

        assert len(gunler) == AZAMI_GERI_GUN

        # En ESKİ günden başlanır: aksi hâlde aradaki günler atlanır ve bir daha
        # hiç toplanmaz.
        assert gunler[0] == date(2026, 1, 2)
        assert gunler[-1] == date(2026, 1, 15)

    def test_gelecek_taranmaz(self) -> None:
        # Son başarılı tarama ileri bir tarihte görünse bile gelecek taranmaz.
        gunler = taranacak_gunler(date(2026, 9, 20), date(2026, 9, 6))

        assert gunler == [date(2026, 9, 6)]
        assert all(g <= date(2026, 9, 6) for g in gunler)

    def test_ayni_gun_tekrar_taranabilir(self) -> None:
        # Gün içinde yeni ilan eklenebilir; aynı gün yeniden taranır. Mükerrer kayıt
        # oluşmaması içerik hash'iyle sunucu tarafında sağlanır.
        assert taranacak_gunler(date(2026, 9, 6), date(2026, 9, 6)) == [date(2026, 9, 6)]

    def test_hicbir_gun_tekrarlanmaz(self) -> None:
        gunler = taranacak_gunler(date(2026, 9, 1), date(2026, 9, 6))

        assert len(gunler) == len(set(gunler))

    def test_gunler_sirali(self) -> None:
        gunler = taranacak_gunler(date(2026, 9, 1), date(2026, 9, 6))

        assert gunler == sorted(gunler)


class TestTekilIlanAyrimi:
    def test_tekil_ilan_pdf_kabul_edilir(self) -> None:
        for u in (
            "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/09/20260906-3-1.pdf",
            "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/09/20260906-3-12.pdf",
            "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/09/20260906-4-2.PDF",
        ):
            assert tekil_ilan_mi(u) is True, u

    def test_bolum_sayfasi_tekil_ilan_degil(self) -> None:
        # Bölüm sayfası onlarca ilan barındırır; başlığı hepsinin ortak başlığıdır.
        for u in (
            "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/09/20260906-3.htm",
            "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/09/20260906-3.pdf",
            "https://www.resmigazete.gov.tr/",
            "https://www.resmigazete.gov.tr/eskiler/2026/09/20260906.htm",
        ):
            assert tekil_ilan_mi(u) is False, u


class TestKesintisizlik:
    def test_ardisik_calistirmalar_gun_atlamaz(self) -> None:
        """Her çalıştırma bir öncekinin bıraktığı yerden devam etmeli.

        Gün atlanırsa o günün ilanları hiç toplanmaz ve kimse fark etmez.
        """
        bugun = date(2026, 9, 20)
        son_basarili = date(2026, 9, 1)
        gorulen: set[date] = set()

        # Üst sınır yüzünden tek turda tamamlanamaz; ardışık turlarla ilerler.
        for _ in range(5):
            gunler = taranacak_gunler(son_basarili, bugun)
            gorulen.update(gunler)
            son_basarili = max(gunler)

        beklenen = {date(2026, 9, 1) + timedelta(days=k) for k in range(1, 20)}

        assert beklenen.issubset(gorulen)
