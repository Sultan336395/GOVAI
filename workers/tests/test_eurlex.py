"""EUR-Lex resmî makine erişimi.

Ağa çıkmazlar: adres eşlemesi ve künye ayrıştırma test edilir.

Bu modülün varlık sebebi bir sınırdır: ``eur-lex.europa.eu`` tarayıcı arayüzü
otomatik isteklere boş gövdeli HTTP 202 döndürüyor ve bu koruma **aşılmıyor**.
Aşağıdaki testler, sistemin gerçekten resmî makine erişim yolunu kullandığını
doğrular.
"""

from __future__ import annotations

import pytest

from govai_workers.collector import eurlex


class TestCelexBulma:
    @pytest.mark.parametrize(
        ("adres", "beklenen"),
        [
            ("https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32026R1961", "32026R1961"),
            ("https://eur-lex.europa.eu/legal-content/TR/TXT/?uri=celex:32026L0044", "32026L0044"),
            (
                "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32026R1782R(01)&x=1",
                "32026R1782R(01)",
            ),
        ],
    )
    def test_resmi_adresten_celex_cikarilir(self, adres: str, beklenen: str) -> None:
        assert eurlex.celex_bul(adres) == beklenen

    @pytest.mark.parametrize(
        "adres",
        [
            "https://www.resmigazete.gov.tr/eskiler/2026/08/20260826-4.htm",
            "https://eur-lex.europa.eu/homepage.html",
            # Benzeyen ama BAŞKA bir alan adı, resmî sayılmamalı.
            "https://eur-lex.europa.eu.saldirgan.example/?uri=CELEX:32026R1961",
        ],
    )
    def test_ilgisiz_adres_none_doner(self, adres: str) -> None:
        assert eurlex.celex_bul(adres) is None


class TestAdresler:
    def test_kullaniciya_gosterilen_adres_resmi_eurlex(self) -> None:
        kayit = eurlex.AbMevzuati("32026R1961", "Başlık", "2026-08-24", None, "REG_IMPL")

        assert kayit.resmi_adres.startswith("https://eur-lex.europa.eu/legal-content/")
        assert "32026R1961" in kayit.resmi_adres

    def test_icerik_adresi_yayin_ofisinin_ucu(self) -> None:
        kayit = eurlex.AbMevzuati("32026R1961", "Başlık", "2026-08-24", None, "REG_IMPL")

        assert kayit.cellar_adresi == (
            "https://publications.europa.eu/resource/celex/32026R1961"
        )

    def test_sparql_ucu_resmi_alan_adinda(self) -> None:
        assert eurlex.SPARQL_UCU.startswith("https://publications.europa.eu/")
        assert eurlex.CELLAR_UCU.startswith("https://publications.europa.eu/")


class TestKunye:
    def test_yururluk_tarihi_yoksa_uydurulmaz(self) -> None:
        kayit = eurlex.AbMevzuati("32026R1961", "Başlık", "2026-08-24", None, None)

        assert kayit.yururluk_tarihi is None
        assert kayit.belge_turu is None

    def test_sorgu_her_zaman_limitli(self) -> None:
        sorgu = eurlex._SORGU.format(onek="32026R", limit=5)

        # Uçta 60 saniyelik zaman aşımı var; limitsiz sorgu atılmaz.
        assert "LIMIT 5" in sorgu
        assert "32026R" in sorgu
