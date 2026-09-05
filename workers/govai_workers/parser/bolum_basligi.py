"""Toplu bölüm başlığı tespiti (Faz 2).

Resmî Gazete'nin ilan bölümü tek sayfada onlarca ayrı ilan yayımlar ve sayfanın
başlığı bunların ortak başlığıdır: "ARTIRMA, EKSİLTME VE İHALE İLÂNLARI". Bu bir
ihale kaydı değildir — ne açan kurumu, ne konusu, ne tarihi, ne bedeli vardır.

Kural sunucuda da vardır (``SectionHeading``) ve **kaynak doğru odur**. Buradaki
kopya yalnızca gereksiz bir tur atmayı önler: worker'a güvenilmez, sunucu yine
reddeder.

Karşılaştırma tam eşleşmedir. Gerçek bir ilanın başlığında bu ifade geçebilir
(ör. "... ihale ilânları kapsamında düzeltme"); yalnızca başlığın **kendisi**
toplu başlıksa kayıt açılmaz.
"""

from __future__ import annotations

#: Türkçe harfleri karşılaştırma için katlar. ``str.lower()`` noktasız ``ı`` ile
#: noktalı ``I``'yı eşleştirmez; ``casefold()`` da Türkçe için doğru çalışmaz.
_KATLAMA = str.maketrans(
    {
        "ı": "i", "İ": "i", "I": "i",
        "ş": "s", "Ş": "s",
        "ğ": "g", "Ğ": "g",
        "ü": "u", "Ü": "u",
        "ö": "o", "Ö": "o",
        "ç": "c", "Ç": "c",
        "â": "a", "Â": "a",
        "î": "i", "Î": "i",
        "û": "u", "Û": "u",
    }
)

#: Resmî yayınların toplu ilan bölümü başlıkları. C# tarafındaki liste ile aynı.
_TOPLU_BASLIKLAR = (
    "ARTIRMA, EKSİLTME VE İHALE İLÂNLARI",
    "ARTIRMA EKSİLTME VE İHALE İLANLARI",
    "ÇEŞİTLİ İLÂNLAR",
    "İLÂN BÖLÜMÜ",
    "İLAN BÖLÜMÜ",
    "İHALE İLÂNLARI",
    "İHALE İLANLARI",
    "İLÂNLAR",
    "İLANLAR",
    "DUYURULAR",
    "RESMÎ İLÂNLAR",
)


def _katla(deger: str) -> str:
    """Başlığı karşılaştırmaya hazırlar: katlar, harf/rakam dışını tek boşluğa indirir."""
    katlanmis = deger.translate(_KATLAMA).lower()
    parcalar = ["".join(ch for ch in kelime if ch.isalnum()) for kelime in katlanmis.split()]

    return " ".join(parca for parca in parcalar if parca)


_KATLANMIS = frozenset(_katla(baslik) for baslik in _TOPLU_BASLIKLAR)


def toplu_bolum_basligi(baslik: str | None) -> bool:
    """Başlık, tek bir ilanın değil bir **bölümün** başlığı mı?"""
    if not baslik or not baslik.strip():
        return False

    return _katla(baslik) in _KATLANMIS
