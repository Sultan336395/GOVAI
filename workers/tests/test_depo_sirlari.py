"""Depoda gerçek bir sır (API anahtarı, parola) bulunmadığını doğrular.

Neden test: kural insanın hatırlamasına bırakılamaz. "Anahtarı compose dosyasına
yazma" kuralı, bir kez unutulduğunda geri alınamaz — depo geçmişine giren anahtar,
sonradan silinse bile itilmiş commit'lerde kalır.

Denetlenen dosyalar bilerek dardır: yalnızca dağıtım yapılandırması. Kaynak kodda
sahte anahtarlar test verisi olarak geçer ve orada olması doğrudur.
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]

#: Yalnızca dağıtım dosyaları. Sır buralara sızarsa sunucuya da gider.
DENETLENEN = [
    REPO_ROOT / "deploy" / ".env.example",
    REPO_ROOT / "deploy" / "docker-compose.yml",
    REPO_ROOT / "deploy" / "docker-compose.release.yml",
    REPO_ROOT / "deploy" / "docker-compose.rollback.yml",
]

#: Değeri boş ya da yalnızca bir ortam değişkeni başvurusu olması gereken anahtarlar.
SIR_ANAHTARLARI = (
    "GOVAI_AI_API_KEY",
    "OPENAI_API_KEY",
    "POSTGRES_PASSWORD",
    "RABBITMQ_PASSWORD",
    "JWT_SIGNING_KEY",
    "SEED_ADMIN_PASSWORD",
    "WORKER_PASSWORD",
    "GOVAI_PLATFORM_BOOTSTRAP_SECRET",
)

#: `ANAHTAR=deger` ya da `ANAHTAR: deger` — iki biçim de yakalanır.
# `\s` KULLANILMAZ: satır sonunu da yutar ve boş bir değer, bir sonraki satırın
# tamamını değer sanardı. Ayırıcının etrafında yalnızca boşluk ve sekme kabul edilir.
_ATAMA = re.compile(
    r"^[ \t]*(?P<ad>[A-Z0-9_]+)[ \t]*[:=][ \t]*(?P<deger>[^\n]*?)[ \t]*$",
    re.MULTILINE,
)

#: Bilinen sağlayıcı anahtarı biçimleri. Değişken adından bağımsız olarak aranır.
_ANAHTAR_IZI = re.compile(r"\b(sk-[A-Za-z0-9_-]{16,}|ghp_[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16})\b")


def _degeri_guvenli_mi(deger: str) -> bool:
    """Boş, yorumlanmış ya da tamamen ortam değişkeni başvurusu olan değer güvenlidir."""
    if not deger or deger.startswith("#"):
        return True

    # ${DEGISKEN}, ${DEGISKEN:-}, ${DEGISKEN:?mesaj} — hepsi geçiş, değer değil.
    return bool(re.fullmatch(r'"?\$\{[A-Z0-9_]+(:[-?][^}]*)?\}"?', deger))


@pytest.mark.parametrize("dosya", DENETLENEN, ids=lambda p: p.name)
def test_dagitim_dosyalarinda_sir_yok(dosya: Path) -> None:
    if not dosya.exists():
        pytest.skip(f"{dosya.name} bu depoda yok")

    metin = dosya.read_text(encoding="utf-8")

    for eslesme in _ATAMA.finditer(metin):
        ad = eslesme.group("ad")

        if ad not in SIR_ANAHTARLARI:
            continue

        deger = eslesme.group("deger")

        assert _degeri_guvenli_mi(deger), (
            f"{dosya.name}: {ad} gerçek bir değer taşıyor. "
            "Sırlar yalnızca sunucudaki .env dosyasında durur."
        )


@pytest.mark.parametrize("dosya", DENETLENEN, ids=lambda p: p.name)
def test_saglayici_anahtar_izi_yok(dosya: Path) -> None:
    """Değişken adı değişse bile anahtarın kendi biçimi yakalanır."""
    if not dosya.exists():
        pytest.skip(f"{dosya.name} bu depoda yok")

    bulunan = _ANAHTAR_IZI.search(dosya.read_text(encoding="utf-8"))

    assert bulunan is None, (
        f"{dosya.name}: sağlayıcı anahtarı biçiminde bir değer var "
        f"({bulunan.group(0)[:6]}…). Sır depoya yazılmaz."
    )


def test_ai_yapilandirmasi_ornekte_kapali() -> None:
    """Örnek yapılandırma varsayılan olarak modeli KAPALI bırakır.

    Açık gelseydi, anahtarı olmayan bir kurulum her analizde başarısız istek
    denerdi; kullanıcı da "hibrit" beklerken kural tabanlı sonuç görürdü.
    """
    ornek = REPO_ROOT / "deploy" / ".env.example"
    metin = ornek.read_text(encoding="utf-8")

    assert "GOVAI_AI_PROVIDER=none" in metin
    assert "GOVAI_AI_API_KEY=\n" in metin or metin.rstrip().endswith("GOVAI_AI_API_KEY=")
    assert "GOVAI_AI_MODEL=\n" in metin or metin.rstrip().endswith("GOVAI_AI_MODEL=")
