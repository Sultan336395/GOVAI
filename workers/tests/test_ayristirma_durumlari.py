"""Ayrıştırma durum adlarının iki yığında aynı olması.

Sahada görülen arıza: worker liste sayfasını eleyip ``status="Skipped"`` bildiriyor,
API ise bu değeri tanımıyordu. Cevap 400 "The JSON value could not be converted to
DocumentParseStatus" oluyor, mesaj ölü kuyruğa düşüyor ve eleme hiç kaydedilmiyordu.

Derleyici bunu yakalayamaz: değer iki ayrı yığında, biri serbest metin. CLAUDE.md §3'teki
çapraz sözleşmelerin aynı cinsidir ve C# tarafı kaynak doğrudur — uyuşmazlıkta Python ona
uydurulur, tersi değil.

``check_contract_parity.py`` alan beyaz listesini ve kuyruk adlarını denetler; bu dosya
aynı işi ayrıştırma durumları için yapar.
"""

from __future__ import annotations

import re
from pathlib import Path

KOK = Path(__file__).resolve().parents[2]

ENUM_DOSYASI = KOK / "src" / "GovAI.Domain" / "Common" / "Enums.cs"
RUNNER = KOK / "workers" / "govai_workers" / "parser" / "runner.py"


def csharp_durumlari() -> set[str]:
    """``DocumentParseStatus`` üyelerini C# kaynağından okur."""
    kaynak = ENUM_DOSYASI.read_text(encoding="utf-8")

    govde = re.search(
        r"enum DocumentParseStatus\s*\{(.*?)\n\}", kaynak, re.DOTALL
    )
    assert govde is not None, "DocumentParseStatus bulunamadı; enum taşınmış olabilir."

    # Yorum satırları atılır; yalnızca "Ad = sayı" biçimindeki üyeler alınır.
    return set(re.findall(r"^\s*([A-Z][A-Za-z]*)\s*=\s*\d+", govde.group(1), re.MULTILINE))


def worker_durumlari() -> set[str]:
    """Worker'ın API'ye gönderdiği ``status=`` değerleri."""
    kaynak = RUNNER.read_text(encoding="utf-8")

    return set(re.findall(r'status="([A-Za-z]+)"', kaynak))


class TestDurumAdlariUyusur:
    def test_workerin_bildirdigi_her_durum_csharpta_vardir(self) -> None:
        csharp = csharp_durumlari()
        worker = worker_durumlari()

        tanimsiz = worker - csharp

        assert not tanimsiz, (
            f"Worker tanınmayan durum bildiriyor: {sorted(tanimsiz)}. "
            f"API 400 döner ve mesaj ölü kuyruğa düşer. "
            f"C# tarafındaki DocumentParseStatus üyeleri: {sorted(csharp)}"
        )

    def test_sahadaki_arizanin_kendisi(self) -> None:
        """``Skipped`` her iki tarafta da bulunmalı."""
        assert "Skipped" in csharp_durumlari()
        assert "Skipped" in worker_durumlari()

    def test_okuma_gercekten_calisiyor(self) -> None:
        """Süzgeçler boş küme döndürüp testi sessizce geçersiz kılmasın."""
        csharp = csharp_durumlari()

        assert {"Pending", "Parsed", "Failed", "NeedsOcr"} <= csharp
        assert len(worker_durumlari()) >= 4
