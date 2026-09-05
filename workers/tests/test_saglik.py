"""Worker sağlık kontrolü (Faz 2).

Bir worker'ın "ayakta" olması süreç tablosunda görünmesi demek değildir. Python
süreci yaşarken RabbitMQ bağlantısı kopmuş, tüketici ölmüş ve worker hiçbir mesaj
işlemiyor olabilir. Docker bunu "çalışıyor" gösterir, kuyruk sessizce birikir.

Bu testler sağlık kontrolünün o durumu **yakaladığını** sabitler.
"""

from __future__ import annotations

from pathlib import Path

import pytest

from govai_workers import health


@pytest.fixture
def nabiz(tmp_path: Path) -> Path:
    return tmp_path / "govai-health"


class TestNabizYok:
    def test_dosya_yoksa_saglıksiz(self, nabiz: Path) -> None:
        saglikli, aciklama = health.durum(dosya=nabiz)

        assert saglikli is False
        assert "başlamadı" in aciklama or "çöktü" in aciklama

    def test_bozuk_dosya_saglıksiz(self, nabiz: Path) -> None:
        nabiz.write_text("bu json değil", encoding="utf-8")

        saglikli, aciklama = health.durum(dosya=nabiz)

        assert saglikli is False
        assert "okunamadı" in aciklama


class TestTazeNabiz:
    def test_taze_ve_bagli_saglikli(self, nabiz: Path) -> None:
        health.isaretle("govai.parse", dosya=nabiz, simdi=1000.0)

        saglikli, aciklama = health.durum(dosya=nabiz, simdi=1005.0)

        assert saglikli is True
        assert "govai.parse" in aciklama
        assert "kuyruğa bağlı" in aciklama

    def test_sinirin_hemen_altinda_hala_saglikli(self, nabiz: Path) -> None:
        health.isaretle("govai.parse", dosya=nabiz, simdi=1000.0)

        saglikli, _ = health.durum(dosya=nabiz, azami_yas=60.0, simdi=1059.0)

        assert saglikli is True


class TestBayatNabiz:
    def test_surec_yasarken_baglanti_olmusse_saglıksiz(self, nabiz: Path) -> None:
        # Asıl senaryo: nabız bir kez atmış, sonra AMQP bağlantısı kopmuş.
        # Süreç hâlâ yaşıyor ama olay döngüsü durduğu için nabız da durdu.
        health.isaretle("govai.parse", dosya=nabiz, simdi=1000.0)

        saglikli, aciklama = health.durum(dosya=nabiz, azami_yas=60.0, simdi=1200.0)

        assert saglikli is False
        assert "200 saniyedir atmıyor" in aciklama
        assert "kuyruk tüketicisi çalışmıyor" in aciklama

    def test_sinirin_hemen_ustu_saglıksiz(self, nabiz: Path) -> None:
        health.isaretle("govai.parse", dosya=nabiz, simdi=1000.0)

        saglikli, _ = health.durum(dosya=nabiz, azami_yas=60.0, simdi=1061.0)

        assert saglikli is False


class TestBaglantiYok:
    def test_ayakta_ama_bagli_degilse_saglıksiz(self, nabiz: Path) -> None:
        # Süreç başladı, nabız taze — ama kuyruğa henüz bağlanmadı.
        # "Hazır" sinyali verilmez.
        health.isaretle("govai.parse", baglanti_var=False, dosya=nabiz, simdi=1000.0)

        saglikli, aciklama = health.durum(dosya=nabiz, simdi=1001.0)

        assert saglikli is False
        assert "kuyruğa bağlı değil" in aciklama


class TestYazim:
    def test_nabiz_atomik_yazilir(self, nabiz: Path) -> None:
        # Yarım yazılmış dosya okunmamalı: yazım geçici dosya üzerinden yapılır.
        health.isaretle("govai.crawl", dosya=nabiz, simdi=1000.0)
        health.isaretle("govai.crawl", dosya=nabiz, simdi=1015.0)

        assert nabiz.exists()
        assert not nabiz.with_suffix(".gecici").exists()

        saglikli, _ = health.durum(dosya=nabiz, simdi=1020.0)
        assert saglikli is True

    def test_bilesen_adi_korunur(self, nabiz: Path) -> None:
        health.isaretle("govai.scoring", dosya=nabiz, simdi=1000.0)

        _, aciklama = health.durum(dosya=nabiz, simdi=1001.0)

        assert "govai.scoring" in aciklama


class TestSabitler:
    def test_nabiz_araligi_azami_yastan_kucuk(self) -> None:
        # Aksi hâlde sağlıklı bir worker bile sürekli sağlıksız görünürdü.
        assert health.NABIZ_ARALIGI < health.AZAMI_YAS

    def test_uc_atim_kaybina_tolerans(self) -> None:
        # Tek bir gecikmiş atım container'ı yeniden başlatmamalı.
        assert health.AZAMI_YAS >= health.NABIZ_ARALIGI * 3
