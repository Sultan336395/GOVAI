"""Faz 2 – skorlama kuyruğu tüketicisi testleri.

Kuyruğa ve API'ye bağımlı değildir: sahte istemci ve yakalanan yayınlarla çalışır.
"""

from __future__ import annotations

from typing import Any

import httpx
import pytest

from govai_workers.api_client import ApiError
from govai_workers.scoring import runner


class SahteIstemci:
    def __init__(self, hata: Exception | None = None) -> None:
        self.hata = hata
        self.rescore_company_calls: list[str] = []
        self.invalidate_calls: list[str] = []
        self.batch_calls = 0

    def rescore_company(self, company_id: str) -> dict[str, Any]:
        if self.hata:
            raise self.hata
        self.rescore_company_calls.append(company_id)
        return {"evaluatedOpportunityCount": 5}

    def invalidate_opportunity(self, opportunity_id: str) -> dict[str, Any]:
        if self.hata:
            raise self.hata
        self.invalidate_calls.append(opportunity_id)
        return {"supersededAssessmentCount": 2}

    def rescore_batch(self) -> dict[str, Any]:
        if self.hata:
            raise self.hata
        self.batch_calls += 1
        return {"companyCount": 3, "evaluatedOpportunityCount": 30, "failedCompanyCount": 0}


@pytest.fixture
def yayinlar(monkeypatch: pytest.MonkeyPatch) -> list[tuple[str, dict[str, Any]]]:
    """Worker'ın kuyruğa bıraktığı mesajları yakalar."""
    kayit: list[tuple[str, dict[str, Any]]] = []
    monkeypatch.setattr(runner, "publish", lambda key, payload: kayit.append((key, payload)))
    return kayit


class TestIdempotency:
    def test_ayni_mesaj_ikinci_kez_is_yaptirmaz(self, yayinlar) -> None:
        istemci = SahteIstemci()
        handle = runner.make_handler(istemci, runner.IdempotencyCache())

        mesaj = {"reason": "OpportunityCreated", "opportunityId": "firsat-1"}

        handle(dict(mesaj))
        handle(dict(mesaj))

        # İkinci mesaj mükerrer sonuç ÜRETMEZ.
        assert istemci.batch_calls == 1
        assert istemci.invalidate_calls == ["firsat-1"]

    def test_zaman_damgasi_anahtari_degistirmez(self) -> None:
        anahtar = runner.idempotency_key
        a = anahtar({"reason": "X", "opportunityId": "1", "requestedAt": "2026-01-01"})
        b = anahtar({"reason": "X", "opportunityId": "1", "requestedAt": "2026-06-06"})

        assert a == b, "Aynı iş için üretilen iki mesaj aynı anahtarı almalı"

    def test_farkli_is_farkli_anahtar(self) -> None:
        a = runner.idempotency_key({"reason": "X", "opportunityId": "1"})
        b = runner.idempotency_key({"reason": "X", "opportunityId": "2"})

        assert a != b

    def test_acik_anahtar_kullanilir(self) -> None:
        assert runner.idempotency_key({"idempotencyKey": "abc", "opportunityId": "1"}) == "abc"

    def test_pencere_dolunca_en_eski_dusulur(self) -> None:
        cache = runner.IdempotencyCache(capacity=2)

        assert not cache.seen("a")
        assert not cache.seen("b")
        assert cache.seen("a")          # hâlâ pencerede
        assert not cache.seen("c")      # 'b' düşer
        assert not cache.seen("b")


class TestYonlendirme:
    def test_firma_mesaji_yalnizca_o_firmayi_skorlar(self, yayinlar) -> None:
        istemci = SahteIstemci()
        handle = runner.make_handler(istemci, runner.IdempotencyCache())

        handle({"reason": "CompanyProfileUpdated", "companyId": "firma-1", "tenantId": "k-1"})

        assert istemci.rescore_company_calls == ["firma-1"]
        assert istemci.batch_calls == 0

    def test_firsat_mesaji_once_eski_skoru_gecersiz_kilar(self, yayinlar) -> None:
        istemci = SahteIstemci()
        handle = runner.make_handler(istemci, runner.IdempotencyCache())

        handle({"reason": "OpportunityUpdated", "opportunityId": "firsat-9"})

        assert istemci.invalidate_calls == ["firsat-9"]
        assert istemci.batch_calls == 1

    def test_hedefsiz_mesaj_sessizce_atlanir(self, yayinlar) -> None:
        istemci = SahteIstemci()
        handle = runner.make_handler(istemci, runner.IdempotencyCache())

        handle({"reason": "Unknown"})

        assert istemci.batch_calls == 0
        assert yayinlar == []


class TestBaglantiHatasi:
    """API erişilemezse retry/ölü mektup mantığı ÇALIŞMALI.

    İlk yazımda yalnızca ``ApiError`` yakalanıyordu. API kapalıyken istemci
    ``httpx.ConnectError`` fırlatıyor ve bu, işleyicinin dışına kaçarak retry
    sayacını ve ölü mektup kaydını tamamen atlıyordu. Canlı önizlemede görüldü.
    """

    def test_baglanti_hatasi_yeniden_denenir(self, yayinlar) -> None:
        istemci = SahteIstemci(hata=httpx.ConnectError("api erisilemedi"))
        handle = runner.make_handler(istemci, runner.IdempotencyCache())

        handle({"reason": "OpportunityCreated", "opportunityId": "firsat-b1", "attempt": 1})

        assert len(yayinlar) == 1
        key, payload = yayinlar[0]
        assert key == runner.RoutingKeys.SCORING_REQUESTED
        assert payload["attempt"] == 2

    def test_baglanti_hatasi_son_denemede_olu_mektuba_gider(self, yayinlar) -> None:
        istemci = SahteIstemci(hata=httpx.ConnectError("api erisilemedi"))
        handle = runner.make_handler(istemci, runner.IdempotencyCache())

        handle({
            "reason": "OpportunityCreated",
            "opportunityId": "firsat-b2",
            "attempt": runner.MAX_ATTEMPTS,
        })

        assert len(yayinlar) == 1
        key, payload = yayinlar[0]
        assert key == runner.DEAD_LETTER_KEY

        # Ölü mektup kaydı hatanın NEDENİNİ taşımalı; kuyruğun kendi DLX'i bunu yazmaz.
        assert "error" in payload
        assert "erisilemedi" in payload["error"]

    def test_zaman_asimi_da_yeniden_denenir(self, yayinlar) -> None:
        istemci = SahteIstemci(hata=httpx.ReadTimeout("zaman asimi"))
        handle = runner.make_handler(istemci, runner.IdempotencyCache())

        handle({"reason": "OpportunityCreated", "opportunityId": "firsat-b3", "attempt": 1})

        assert len(yayinlar) == 1
        assert yayinlar[0][0] == runner.RoutingKeys.SCORING_REQUESTED


class TestRetryVeDeadLetter:
    def test_gecici_hatada_yeniden_denenir(self, yayinlar) -> None:
        istemci = SahteIstemci(hata=ApiError(503, "gecici hata"))
        handle = runner.make_handler(istemci, runner.IdempotencyCache())

        handle({"reason": "OpportunityCreated", "opportunityId": "firsat-1", "attempt": 1})

        assert len(yayinlar) == 1
        key, payload = yayinlar[0]
        assert key == runner.RoutingKeys.SCORING_REQUESTED
        assert payload["attempt"] == 2

    def test_israrli_hata_olu_mektuba_gider(self, yayinlar) -> None:
        istemci = SahteIstemci(hata=ApiError(500, "kalici hata"))
        handle = runner.make_handler(istemci, runner.IdempotencyCache())

        handle({
            "reason": "OpportunityCreated",
            "opportunityId": "firsat-1",
            "attempt": runner.MAX_ATTEMPTS,
        })

        assert len(yayinlar) == 1
        key, payload = yayinlar[0]

        # Tüketici tıkanmaz; mesaj ölü mektup kuyruğuna bırakılır.
        assert key == runner.DEAD_LETTER_KEY
        assert "error" in payload
