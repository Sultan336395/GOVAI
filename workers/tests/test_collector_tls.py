"""TLS uyumluluk politikasının sözleşmesi.

Bu testlerin tek işi şunu garanti etmek: uyumluluk kaydı olsun ya da olmasın,
**sertifika doğrulaması ve ana bilgisayar adı denetimi hiçbir zaman kapanmaz**.
Ağa çıkmazlar.
"""

from __future__ import annotations

import ssl

import pytest

from govai_workers.collector import tls


class TestUyumEslesmesi:
    @pytest.mark.parametrize(
        "host",
        ["resmigazete.gov.tr", "www.resmigazete.gov.tr", "WWW.RESMIGAZETE.GOV.TR",
         "ekap.kik.gov.tr", "ekapv2.kik.gov.tr", "kik.gov.tr"],
    )
    def test_tanimli_alanlar_eslesiyor(self, host: str) -> None:
        assert tls.uyum_bul(host) is not None

    @pytest.mark.parametrize(
        "host",
        ["kosgeb.gov.tr", "eur-lex.europa.eu", "example.com", "sgk.gov.tr",
         # Benzer görünen ama BAŞKA alan adları politikaya girmemeli.
         "resmigazete.gov.tr.saldirgan.example", "sahte-kik.gov.tr.example"],
    )
    def test_tanimsiz_alanlar_eslesmiyor(self, host: str) -> None:
        assert tls.uyum_bul(host) is None

    def test_alt_alan_adi_eslesir_ama_ek_metin_eslesmez(self) -> None:
        assert tls.uyum_bul("bir.iki.kik.gov.tr") is not None
        # "xkik.gov.tr" farklı bir alan adıdır.
        assert tls.uyum_bul("xkik.gov.tr") is None


class TestBaglam:
    def test_tanimsiz_host_varsayilan_baglami_alir(self) -> None:
        baglam = tls.baglam_olustur("example.com")

        assert baglam.verify_mode == ssl.CERT_REQUIRED
        assert baglam.check_hostname is True

    def test_sifre_gevsetmesi_dogrulamayi_kapatmaz(self, monkeypatch: pytest.MonkeyPatch) -> None:
        # Ara sertifika indirme ağa çıkmasın.
        monkeypatch.setattr(tls, "_ara_sertifikayi_indir", lambda uyum: None)
        monkeypatch.setattr(tls, "_onbellek", tls._Onbellek())

        baglam = tls.baglam_olustur("ekap.kik.gov.tr")

        assert baglam.verify_mode == ssl.CERT_REQUIRED
        assert baglam.check_hostname is True

        # Sunucunun kabul ettiği eski takım artık listede.
        adlar = {s["name"] for s in baglam.get_ciphers()}
        assert "AES128-GCM-SHA256" in adlar

    def test_ara_sertifika_parmak_izi_uymazsa_kullanilmaz(
        self, monkeypatch: pytest.MonkeyPatch
    ) -> None:
        class SahteYanit:
            def read(self) -> bytes:
                return b"beklenmeyen icerik"

            def __enter__(self) -> SahteYanit:
                return self

            def __exit__(self, *_: object) -> None:
                return None

        monkeypatch.setattr(tls.urllib.request, "urlopen", lambda *a, **k: SahteYanit())

        uyum = tls.UYUMLULUK["resmigazete.gov.tr"]
        # Önbellekteki dosya karışmasın diye parmak izi bilinçli olarak değiştirilir.
        sahte = tls.TlsUyumu(
            gerekce="test",
            ara_sertifika_url=uyum.ara_sertifika_url,
            ara_sertifika_sha256="0" * 64,
        )

        assert tls._ara_sertifikayi_indir(sahte) is None

    def test_her_kayit_gerekce_tasir(self) -> None:
        # Gevşetmenin nedeni yazılmadan politikaya kayıt eklenemez.
        for alan, uyum in tls.UYUMLULUK.items():
            assert uyum.gerekce.strip(), f"{alan} için gerekçe yok"
            assert uyum.ara_sertifika_url or uyum.sifre_listesi, f"{alan} için çözüm yok"

            if uyum.ara_sertifika_url:
                # Parmak izi olmadan ara sertifika kabul edilmez.
                assert uyum.ara_sertifika_sha256, f"{alan} için parmak izi yok"
                assert len(uyum.ara_sertifika_sha256) == 64

    def test_politika_yalnizca_resmi_alan_adlari_icerir(self) -> None:
        for alan in tls.UYUMLULUK:
            assert alan.endswith((".gov.tr", ".europa.eu")), f"{alan} resmî alan adı değil"
