"""Alakasız bağlantıların elenmesi (Faz 3).

Tarayıcı bugün üç süzgeçten geçiriyor: izin verilen alan adı, bağlantı seçicisi ve
URL deseni. Üçü de **adrese** bakıyor. Kurum siteleri çağrı listesiyle aynı bölümde
"Çerez Politikası", "KVKK Aydınlatma Metni", "İletişim", "Site Haritası" gibi
sayfalara da bağlantı veriyor; bunlar desene takılmadan geçiyor, indiriliyor,
ayrıştırılıyor ve karşılığında çöp fırsat kaydı üretiyor.

Bu modül dördüncü süzgeci ekler: **konuya alaka**. İki noktada çalışır — bağlantı
keşfinde adrese ve son metnine, ayrıştırma öncesinde başlığa bakarak.

Üç sınır bilinçlidir:

1. **Yalnızca kesin işaretlerde eler.** Kararsız kalınan bağlantı geçirilir; asıl
   ayıklama karantina ve danışman onayında yapılır. Bir çağrıyı yanlışlıkla elemek,
   bir çöp kaydı geçirmekten pahalıdır — çöp görülür ve silinir, elenen çağrı hiç
   görülmez.

2. **Karar adrese ve bağlantı metnine göre verilir, belge içeriğine göre değil.**
   Amaç indirmeden önce elemek; indirilmiş bir belge için karar zaten
   ``publication.Screen`` ve karantinada veriliyor.

3. **Liste kurum sitelerinin ortak kalıplarıdır**, tek bir kuruma özel değildir.
   Kuruma özel eleme, kaynağın kendi ``urlPattern`` yapılandırmasında yapılır.
"""

from __future__ import annotations

import re
from urllib.parse import urlparse

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

#: Adres yolunda geçtiğinde bağlantının kurumsal sayfa olduğunu gösteren ifadeler.
#: **Katlanmış ve boşlukla ayrılmış** yazılır: adres "site-haritasi" da olabilir
#: "site_haritasi" da, ikisi de katlandığında "site haritasi" olur. Eşleşme kelime
#: sınırıyla aranır, böylece "arama" eleyip "aramalar" elemez.
YOL_ISARETLERI: tuple[str, ...] = (
    "cerez", "kvkk", "gizlilik", "kullanim kosullari",
    "iletisim", "contact", "hakkimizda", "about", "site haritasi", "sitemap",
    "giris", "login", "signin", "uye ol", "register", "sifremi unuttum",
    "arama", "search", "rss", "abone", "newsletter",
    "galeri", "gallery", "foto", "video", "webtv",
    "organizasyon semasi", "personel", "yonetim semasi",
    "engelsiz", "erisilebilirlik", "accessibility",
    "bilgi edinme", "bimer", "cimer", "ihbar",
    "ziyaretci", "istatistik", "anket",
)

#: Bağlantı metninde ya da başlıkta geçtiğinde alakasız sayılan ifadeler.
METIN_ISARETLERI: tuple[str, ...] = (
    "cerez politikasi", "kvkk", "kisisel verilerin korunmasi", "aydinlatma metni",
    "gizlilik politikasi", "kullanim kosullari", "site haritasi",
    "bize ulasin", "iletisim bilgileri", "hakkimizda", "kurumsal kimlik",
    "organizasyon semasi", "yonetim semasi", "telefon rehberi",
    "bilgi edinme", "basvuru formu indir", "logo indir",
    "sikca sorulan sorular", "sss", "yardim", "kullanici kilavuzu",
    "fotograf galerisi", "video galeri", "web tv", "basin bulteni arsivi",
    "onceki sayfa", "sonraki sayfa", "ana sayfa", "yukari cik",
)

#: Belge olmayan uzantılar. Kurum siteleri listelerde logo, afiş ve sunum da bağlar.
ELENEN_UZANTILAR: tuple[str, ...] = (
    ".jpg", ".jpeg", ".png", ".gif", ".svg", ".webp", ".ico", ".bmp",
    ".mp3", ".mp4", ".avi", ".mov", ".wmv", ".zip", ".rar", ".7z",
    ".css", ".js", ".xml", ".json",
)

#: Bu kelimeler geçiyorsa bağlantı **elenmez**: kurumsal görünen bir adreste bile
#: çağrı ya da mevzuat olabilir ("hakkimizda/destek-programlari" gibi).
KORUYUCU_ISARETLER: tuple[str, ...] = (
    "ilan", "duyuru", "cagri", "destek", "tesvik", "hibe", "fon", "ihale",
    "basvuru", "program", "mevzuat", "kanun", "yonetmelik", "teblig",
    "genelge", "karar", "sirkuler", "resmi gazete", "call", "grant",
    "funding", "tender", "regulation",
)


def katla(deger: str) -> str:
    """Metni karşılaştırmaya hazırlar: Türkçe harfleri katlar, harf/rakam dışını boşluğa indirir."""
    katlanmis = (deger or "").translate(_KATLAMA).lower()
    temiz = "".join(ch if ch.isalnum() else " " for ch in katlanmis)

    return " ".join(temiz.split())


def yol_metni(url: str) -> str:
    """Adresin yol ve sorgu kısmını karşılaştırılabilir tek metne indirir.

    ``/site-haritasi``, ``/site_haritasi`` ve ``/site/haritasi`` aynı metne iner:
    ``site haritasi``. Ayraçların hangisinin kullanıldığı kuruma göre değişir ve
    süzgecin buna takılmaması gerekir.
    """
    ayrisik = urlparse(url)

    return katla(f"{ayrisik.path} {ayrisik.query}")


def belge_uzantisi_elenir_mi(url: str) -> bool:
    """Adres belge olmayan bir dosyayı mı gösteriyor?"""
    yol = urlparse(url).path.lower()

    return yol.endswith(ELENEN_UZANTILAR)


def _gecer_mi(katlanmis: str, isaret: str) -> bool:
    """İşaret metinde kelime sınırıyla geçiyor mu?

    Düz alt dize araması "arama" ile "aramalar"ı, "personel" ile "personeli"yi
    karıştırır. Türkçe ekler yüzünden sona esneklik bırakılır, başa bırakılmaz.
    """
    return re.search(rf"(?<![a-z0-9]){re.escape(isaret)}", katlanmis) is not None


def _korunuyor_mu(katlanmis: str) -> bool:
    return any(_gecer_mi(katlanmis, isaret) for isaret in KORUYUCU_ISARETLER)


def alakasiz_mi(url: str, baglanti_metni: str = "") -> bool:
    """Bağlantı, çağrı/mevzuat aramasıyla alakasız mı?

    ``True`` dönerse bağlantı indirilmez. Kararsız kalınan her durumda ``False``
    döner: elenen bir çağrı hiç görülmez, geçen bir çöp kayıt görülür ve silinir.
    """
    if not url:
        return True

    if belge_uzantisi_elenir_mi(url):
        return True

    katlanmis_metin = katla(baglanti_metni)
    katlanmis_yol = yol_metni(url)

    # Koruyucu kelime varsa eleme: kurumsal görünen adreste de çağrı olabilir
    # ("hakkimizda/destek-programlarimiz").
    if _korunuyor_mu(katlanmis_metin) or _korunuyor_mu(katlanmis_yol):
        return False

    if any(_gecer_mi(katlanmis_yol, isaret) for isaret in YOL_ISARETLERI):
        return True

    return any(_gecer_mi(katlanmis_metin, isaret) for isaret in METIN_ISARETLERI)


def alakasiz_baslik_mi(baslik: str) -> bool:
    """Ayrıştırılmış belgenin başlığı kurumsal sayfa başlığı mı?

    Bağlantı süzgecinden geçmiş ama içeriği kurumsal sayfa çıkmış belgeler için
    ikinci savunma hattıdır.
    """
    katlanmis = katla(baslik)

    if not katlanmis:
        return False

    if _korunuyor_mu(katlanmis):
        return False

    return any(_gecer_mi(katlanmis, isaret) for isaret in METIN_ISARETLERI)
