"""Bakım betiğinin güvenlik ve kullanılabilirlik garantileri.

Betik ``scripts/bakim-uygula.sh`` katalog düzeltme ve dayanak eşleştirmeyi tek
oturumda uygular. Parolayı sorar, dolayısıyla iki şeyi birden tutmak zorundadır:
parola hiçbir yere sızmamalı ve onay kapısı atlanamamalı.

Sahada görülen arıza: betik ``ssh sunucu 'bash betik.sh'`` biçiminde çalıştırıldı,
uzak tarafta terminal açılmadı, ``read`` anında dosya sonu gördü ve betik parola
sorulmadan "parola boş" deyip çıktı. Kullanıcı iki kez denedi, ikisinde de hiçbir
şey olmadı ve sebebi ekranda görünmüyordu.
"""

from __future__ import annotations

from pathlib import Path

BETIK = Path(__file__).resolve().parents[2] / "scripts" / "bakim-uygula.sh"


def kaynak() -> str:
    return BETIK.read_text(encoding="utf-8")


class TestParolaSizmaz:
    def test_parola_dosyaya_yazilmaz(self) -> None:
        metin = kaynak()

        # Parola değişkeninin yönlendirmeyle bir dosyaya gitmesi yasak.
        for yasak in ('$PAROLA >', '${PAROLA} >', 'echo "$PAROLA"', "echo $PAROLA"):
            assert yasak not in metin

    def test_parola_kullanildiktan_sonra_silinir(self) -> None:
        metin = kaynak()

        assert "unset PAROLA" in metin
        assert "unset GOVDE" in metin
        assert "unset JETON" in metin

    def test_parola_ekranda_gorunmez(self) -> None:
        assert "stty" in kaynak() and "-echo" in kaynak()


class TestKlavyedenOkur:
    def test_parola_terminalden_okunur(self) -> None:
        """Arızanın kendisi: standart girdiden okumak dosya sonuyla karşılaşır."""
        metin = kaynak()

        assert "/dev/tty" in metin
        assert 'read -r cevap < "$TERMINAL"' in metin

    def test_terminal_yoksa_ne_yapilacagi_soylenir(self) -> None:
        metin = kaynak()

        # Sessizce çıkmak yerine çözümü yazar.
        assert "Klavye okunamıyor" in metin
        assert "ssh -t" in metin


class TestOnayKapisiAtlanamaz:
    def test_her_uygulamadan_once_plan_alinir(self) -> None:
        metin = kaynak()

        assert "catalog-repair/plan" in metin
        assert "rule-evidence/backfill/plan" in metin

    def test_uygulama_plan_parmak_iziyle_gonderilir(self) -> None:
        """Sunucu da aynı kuralı uygular; betik onayı tek başına atlayamaz."""
        metin = kaynak()

        assert metin.count("planHash") >= 4

    def test_katalog_duzeltme_iki_gecis_calistirir(self) -> None:
        # Birinci geçiş başlığı düzeltir; kaydın gerçek konusu ancak o zaman görünür.
        metin = kaynak()

        assert "katalog_gecisi 1" in metin
        assert "katalog_gecisi 2" in metin
