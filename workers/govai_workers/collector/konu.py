"""Sosyal güvenlik duyurularının konu ayrımı (Faz 3).

SGK tek bir duyuru akışında çok farklı işler yayımlar. Bir haftalık gerçek liste:

* Bedeli Ödenecek İlaçlar Listesinde Yapılan Düzenlemeler (×4)
* SUT Değişiklik Tebliği
* Gayrimenkul Satış İlanı
* Planlı Altyapı Çalışması (bilgi işlem bakımı)
* Sözleşmeli Bilişim Personeli Giriş Sınavı
* Danıştay Kararı Hakkında (genel sağlık sigortası)

Bunların hiçbiri **işveren mevzuatı** değildir. GOVAI'nin kullanıcısı bir işverendir;
ilaç listesi değişikliğini "sizi ilgilendiren mevzuat değişikliği" diye göstermek,
gerçek bir prim teşviki duyurusunun arasında kaybolmasına yol açar.

Karar üç değerlidir ve sırası önemlidir:

1. **Dışlayıcı işaret varsa hayır.** Sağlık, ilaç, satış ilanı, personel alımı ve
   sistem bakımı duyuruları işveren mevzuatı değildir — başlıkta "işveren" geçse bile.
2. **İşveren işareti varsa evet.**
3. **Hiçbiri yoksa BELİRSİZ.** Tahmin edilmez: belirsiz kayıt sessizce işveren
   mevzuatı sayılmaz, insan incelemesine bırakılır.

Üçüncü kural bilinçlidir. Bu modül bağlantıyı elerken de kaydı sınıflandırırken de
kullanılır; "bilmiyorum" ile "hayır"ı ayırmayan bir süzgeç ya gerçek duyuruları eler
ya da çöp geçirir.
"""

from __future__ import annotations

import re
from enum import StrEnum

from govai_workers.collector.alaka import katla


class KonuKarari(StrEnum):
    """Duyurunun işveren mevzuatıyla ilgisi."""

    ISVEREN = "Isveren"
    ILGISIZ = "Ilgisiz"
    BELIRSIZ = "Belirsiz"


#: İşveren, prim ve istihdam mevzuatını gösteren ifadeler. Katlanmış yazılır.
ISVEREN_ISARETLERI: tuple[str, ...] = (
    "isveren", "isyeri", "isyeri tescil", "sigorta primi", "prim borcu",
    "prim tesviki", "tesvik", "asgari ucret", "asgari isciluk",
    "aylik prim ve hizmet belgesi", "aphb", "muhtasar", "prim hizmet beyanname",
    "e bildirge", "istihdam", "is kazasi bildirimi", "meslek hastaligi bildirimi",
    "yapilandirma", "borclarin", "suresinin uzatilmasi", "surenin uzatilmasi",
    "4 a sigortalilari", "4 b sigortalilari", "hizmet borclanmasi",
    "calisma yukumlulugu", "eksik gun", "sgk prim", "prim odeme",
    "isten ayrilis bildirgesi", "ise giris bildirgesi",
)

#: Bu işaretler varsa duyuru işveren mevzuatı DEĞİLDİR — başlıkta işveren geçse bile.
DISLAYICI_ISARETLER: tuple[str, ...] = (
    # Sağlık ve ilaç
    "sut", "saglik uygulama tebligi", "bedeli odenecek ilaclar", "ilac",
    "medula", "saglik hizmeti", "saglik uygulama", "genel saglik sigortasi",
    "optik", "tibbi malzeme", "recete", "hekim",
    # Satış ve ihale
    "gayrimenkul satis", "satis ilani", "tasinmaz satis", "kiraya verilecek",
    # Kurum içi işler
    "giris sinavi", "personel alimi", "sozlesmeli bilisim personeli",
    "atama", "gorevde yukselme", "unvan degisikligi",
    "planli altyapi calismasi", "sistem bakimi", "sistem altyapi",
    "altyapi calismasi", "altyapi iyilestirme", "kesinti",
    "kpss", "yerlestirme sonuc", "danisma gunleri", "sosyal guvenlik haftasi",
    # "Basın duyurusu" bir BİÇİMDİR, konu değil: prim borcu erteleme duyuruları da
    # bu başlıkla çıkar. Dışlayıcı listeye alınırsa gerçek işveren duyurusu elenir.
    "ziyaret", "toplanti", "protokol imza",
)


def _gecer_mi(katlanmis: str, isaret: str) -> bool:
    """İşaret metinde kelime sınırıyla geçiyor mu?

    Düz alt dize araması "sut" ile "usturuplu"yu, "ilac" ile "ilacak"ı karıştırır.
    Türkçe ekler yüzünden sona esneklik bırakılır, başa bırakılmaz.
    """
    return re.search(rf"(?<![a-z0-9]){re.escape(isaret)}", katlanmis) is not None


def konu_karari(baslik: str, metin: str = "") -> KonuKarari:
    """Duyuru işveren mevzuatı mı?

    Karar öncelikle **başlığa** göre verilir: SGK duyuru başlıkları konuyu açıkça
    söyler. Gövde yalnızca başlık kararsız kaldığında ve destekleyici olarak okunur;
    uzun bir gövdede "işveren" kelimesinin geçmesi duyuruyu işveren mevzuatı yapmaz.
    """
    katlanmis_baslik = katla(baslik or "")

    if not katlanmis_baslik:
        return KonuKarari.BELIRSIZ

    if any(_gecer_mi(katlanmis_baslik, i) for i in DISLAYICI_ISARETLER):
        return KonuKarari.ILGISIZ

    if any(_gecer_mi(katlanmis_baslik, i) for i in ISVEREN_ISARETLERI):
        return KonuKarari.ISVEREN

    # Başlık karar vermedi: gövdenin başına bakılır. Tamamı taranmaz — uzun metinde
    # her kelime geçer ve karar anlamsızlaşır.
    katlanmis_govde = katla((metin or "")[:1500])

    if any(_gecer_mi(katlanmis_govde, i) for i in DISLAYICI_ISARETLER):
        return KonuKarari.ILGISIZ

    if any(_gecer_mi(katlanmis_govde, i) for i in ISVEREN_ISARETLERI):
        return KonuKarari.ISVEREN

    return KonuKarari.BELIRSIZ


def isveren_mevzuati_mi(baslik: str, metin: str = "") -> bool:
    """Kesin ``True`` yalnızca işveren kararında. Belirsiz kayıt işveren sayılmaz."""
    return konu_karari(baslik, metin) is KonuKarari.ISVEREN


def gerekce(baslik: str, metin: str = "") -> str:
    """Kararın operatöre gösterilecek gerekçesi."""
    karar = konu_karari(baslik, metin)
    katlanmis = katla(baslik or "")

    if karar is KonuKarari.ILGISIZ:
        isaret = next(
            (i for i in DISLAYICI_ISARETLER if _gecer_mi(katlanmis, i)), "dışlayıcı konu"
        )
        return (
            f"Başlıkta '{isaret}' geçiyor; sağlık, satış veya kurum içi duyuru sayıldı. "
            "İşveren mevzuatı olarak kaydedilmez."
        )

    if karar is KonuKarari.ISVEREN:
        isaret = next(
            (i for i in ISVEREN_ISARETLERI if _gecer_mi(katlanmis, i)), "işveren konusu"
        )
        return f"Başlıkta '{isaret}' geçiyor; işveren mevzuatı sayıldı."

    return (
        "Başlık ne işveren ne de dışlayıcı işaret taşıyor; konu belirsiz. "
        "İşveren mevzuatı olarak kaydedilmez, insan incelemesine bırakılır."
    )
