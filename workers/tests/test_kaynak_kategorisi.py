"""Kaynak kategorisi mesajdan kör biçimde alınmaz.

Faz 2'de mevzuat belgesi fırsat kataloğuna yazılmaya çalışıldı: elle tetiklenen
bir yeniden ayrıştırmada mesajda ``sourceCategory`` yoktu ve ayrıştırıcı fırsat
yoluna düştü. Sunucu isteği reddetti, ama ayrıştırma da hataya düştü.

Doğru kaynak veritabanındaki Source kaydıdır. Bu testler onu korur.
"""

from __future__ import annotations

from typing import Any

from govai_workers.parser.runner import _kaynak_kategorisi

KAYNAK_ID = "01a0536e-87bb-7b8d-a7bc-ae3719938169"


class SahteIstemci:
    def __init__(self, kaynak: dict[str, Any] | None, hata: Exception | None = None) -> None:
        self._kaynak = kaynak
        self._hata = hata
        self.cagri_sayisi = 0

    def get_source(self, source_id: str) -> dict[str, Any] | None:
        self.cagri_sayisi += 1
        if self._hata:
            raise self._hata
        return self._kaynak


class TestVeritabaniOncelikli:
    def test_kategori_veritabanindan_okunur(self) -> None:
        istemci = SahteIstemci({"id": KAYNAK_ID, "category": "Regulation"})

        # Mesaj YANLIŞ söylüyor; veritabanı doğruyu söylüyor.
        sonuc = _kaynak_kategorisi(istemci, KAYNAK_ID, {"sourceCategory": "Grant"})

        assert sonuc == "Regulation"
        assert istemci.cagri_sayisi == 1

    def test_mesajda_kategori_yoksa_da_veritabanindan_gelir(self) -> None:
        istemci = SahteIstemci({"id": KAYNAK_ID, "category": "Tax"})

        # Faz 2'deki hatanın tam senaryosu: mesajda alan YOK.
        assert _kaynak_kategorisi(istemci, KAYNAK_ID, {}) == "Tax"

    def test_uncategorized_kaynak_mevzuat_sayilmaz(self) -> None:
        istemci = SahteIstemci({"id": KAYNAK_ID, "category": "Uncategorized"})

        assert _kaynak_kategorisi(istemci, KAYNAK_ID, {}) == "Uncategorized"


class TestKaynakOkunamazsa:
    def test_kaynak_bulunamazsa_mesaja_dusulur(self) -> None:
        istemci = SahteIstemci(None)

        assert _kaynak_kategorisi(istemci, KAYNAK_ID, {"sourceCategory": "Grant"}) == "Grant"

    def test_kaynak_da_mesaj_da_yoksa_none(self) -> None:
        istemci = SahteIstemci(None)

        # Uydurma yapılmaz: çağıran fırsat kaydı AÇMAZ.
        assert _kaynak_kategorisi(istemci, KAYNAK_ID, {}) is None

    def test_api_hatasi_ayristirmayi_durdurmaz(self) -> None:
        istemci = SahteIstemci(None, hata=RuntimeError("api erişilemedi"))

        assert _kaynak_kategorisi(istemci, KAYNAK_ID, {"sourceCategory": "Fund"}) == "Fund"

    def test_api_hatasi_ve_mesaj_yoksa_none(self) -> None:
        istemci = SahteIstemci(None, hata=RuntimeError("api erişilemedi"))

        assert _kaynak_kategorisi(istemci, KAYNAK_ID, {}) is None
