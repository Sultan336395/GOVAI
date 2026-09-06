"""İlan metninden sektör çıkarımı (Faz 2).

Neden var: Faz 2'de bir inşaat firmasına "Didi soğuk çay nakliye" ihalesi, bir makine
imalatçısına "Sivas YHT Garı buz çözme" işi 88 puanla önerildi. Sebep basitti — ihale
ilanlarından **hiçbir sektör koşulu çıkarılmıyordu**. Koşul olmayınca motor sektör
boyutunu "kısıt yok" sayıp tam puan veriyor, ilan listenin başına çıkıyordu.

Bu modül boşluğun ilan tarafını kapatır: ilanın konusunu Türkçe anahtar kelimelerle
tanır ve NACE karşılığını verir; kuralı bundan ``rule_extractor`` üretir. Motor
tarafındaki karşılığı
``EligibilityEngine.UnverifiedSectorScore``'dur: kural üretilemezse tam puan değil
"doğrulanamadı" puanı verilir.

Üç sınır bilinçlidir:

1. **Kural ``Blocking`` değil ``Major``dır.** Sektör burada ilanın *konusundan*
   çıkarılır, metinde yazan bir yeterlilik şartından değil. Engelleyici yapmak firmayı
   hukuken hak sahibi olduğu bir ihaleden eler; ürün eksik veriyle firma elemez
   (CLAUDE.md §2.2). Uyumsuzluk skoru düşürür ve kaydı listenin sonuna atar, silmez.

2. **Eşleşme yoksa kural üretilmez.** Tanınmayan konuya sektör uydurulmaz; ilan
   "sektör uyumu doğrulanamadı" etiketiyle listede kalır.

3. **Tek sektör seçilir.** Metinde birkaç alan geçebilir (ihale ilanında "inşaat"
   kelimesi bir adres tarifinde de geçer). Başlıktaki eşleşme gövdedekinden ağır
   basar; başlık ilanın konusudur.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

#: Türkçe harfleri karşılaştırma için katlar. ``str.lower()`` noktasız ``ı`` ile noktalı
#: ``I``'yı eşleştirmez; ``casefold()`` da Türkçe için doğru çalışmaz.
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

#: Başlıktaki eşleşmenin gövdedekine göre ağırlığı. Başlık ilanın konusudur; gövdede
#: geçen bir kelime çoğu zaman adres, teslim yeri veya idarenin adıdır.
BASLIK_AGIRLIGI = 3

#: Bir sektörün seçilebilmesi için gereken en düşük ağırlıklı eşleşme sayısı.
ASGARI_SKOR = 1


@dataclass(frozen=True, slots=True)
class Sektor:
    """Bir faaliyet alanı: insan tarafından okunur adı, NACE karşılığı, tetikleyicileri."""

    ad: str
    nace: tuple[str, ...]
    anahtarlar: tuple[str, ...]


#: Anahtar kelimeler **katlanmış** yazılır (ı→i, ş→s, ...) ve kelime başından eşleşir;
#: Türkçe ekler serbesttir ("insaat" → "inşaatı", "nakliy" → "nakliye"/"nakliyat").
#:
#: Sıra önemlidir: eşit skorda önce tanımlanan kazanır. Dar ve ayırt edici alanlar
#: (tıbbi cihaz, ilaç) genel alanlardan (inşaat, taşımacılık) ÖNCE gelir; aksi hâlde
#: "hastane inşaatı için tıbbi cihaz" gibi bir metin yanlış tarafa düşer.
SEKTORLER: tuple[Sektor, ...] = (
    Sektor(
        ad="Tıbbi cihaz ve medikal malzeme",
        nace=("32.50", "46.46"),
        anahtarlar=("tibbi cihaz", "tibbi malzeme", "medikal", "tibbi sarf", "ameliyat"),
    ),
    Sektor(
        ad="İlaç ve eczacılık ürünleri",
        nace=("21.20", "46.46"),
        anahtarlar=("ilac alim", "eczacilik", "farmasotik", "serum alim"),
    ),
    Sektor(
        ad="Laboratuvar ve teknik analiz hizmetleri",
        nace=("71.20",),
        anahtarlar=("laboratuvar hizmet", "analiz hizmet", "kalibrasyon"),
    ),
    Sektor(
        ad="Bilişim, yazılım ve donanım",
        nace=("62.01", "62.02", "26.20", "46.51"),
        anahtarlar=(
            "yazilim", "bilisim", "bilgisayar", "sunucu alim", "veri merkezi",
            "lisans alim", "donanim alim",
        ),
    ),
    Sektor(
        ad="Ormancılık ve ağaç ürünleri",
        nace=("02.10", "02.20", "16.10"),
        anahtarlar=("dikili agac", "orman emval", "ormancilik", "tomruk", "agaclandirma"),
    ),
    Sektor(
        ad="Tarım ve hayvancılık",
        nace=("01.11", "01.40", "01.50"),
        anahtarlar=(
            "safkan", "kosu tayi", "damizlik", "hayvancilik", "besi", "yem alim",
            "gubre alim", "tohumluk", "fidan",
        ),
    ),
    Sektor(
        ad="Gıda üretimi ve tedariki",
        nace=("10.00", "56.29", "46.30"),
        anahtarlar=("yemek hizmet", "gida alim", "malzemeli yemek", "kuru gida", "ekmek alim"),
    ),
    Sektor(
        ad="Tekstil ve hazır giyim",
        nace=("13.00", "14.00"),
        anahtarlar=("tekstil", "hazir giyim", "kumas", "uniforma", "is elbise"),
    ),
    Sektor(
        ad="Mobilya ve büro donatımı",
        nace=("31.00", "46.65"),
        anahtarlar=("mobilya", "buro mobilya", "okul sirasi"),
    ),
    Sektor(
        ad="Matbaa ve basım hizmetleri",
        nace=("18.10",),
        anahtarlar=("matbaa", "basim hizmet", "kitap basim", "afis basim"),
    ),
    Sektor(
        ad="Makine ve ekipman imalatı",
        nace=("28.00", "25.62", "33.12"),
        anahtarlar=(
            "makine imalat", "makine alim", "tezgah alim", "cnc", "talasli imalat",
            "ekipman imalat",
        ),
    ),
    Sektor(
        ad="Elektrik üretimi, dağıtımı ve tesisatı",
        nace=("35.10", "43.21"),
        anahtarlar=(
            "elektrik tesisat", "enerji nakil", "trafo", "aydinlatma", "elektrik uretim",
            "gunes enerji",
        ),
    ),
    Sektor(
        ad="Akaryakıt ve madeni yağ tedariki",
        nace=("46.71", "47.30"),
        anahtarlar=("akaryakit", "motorin", "madeni yag", "lpg alim"),
    ),
    Sektor(
        ad="Demiryolu yapımı, bakımı ve işletmesi",
        nace=("42.12", "49.10", "33.17"),
        anahtarlar=("demiryolu", "yht", "tren seti", "vagon", "ray yenileme", "gar bakim"),
    ),
    Sektor(
        ad="İnşaat ve altyapı yapım işleri",
        nace=("41.20", "42.00", "43.00"),
        anahtarlar=(
            "yapim isi", "insaat", "onarim isi", "tadilat", "altyapi", "asfalt",
            "kaba yapi", "ince yapi", "cati yenileme",
        ),
    ),
    Sektor(
        ad="Taşımacılık ve lojistik",
        nace=("49.41", "49.39", "52.29"),
        anahtarlar=(
            "nakliy", "tasima hizmet", "personel tasima", "lojistik", "arac kiralama",
            "yuk tasimacilik",
        ),
    ),
    Sektor(
        ad="Temizlik ve destek hizmetleri",
        nace=("81.21", "81.29"),
        anahtarlar=("temizlik hizmet", "malzemeli temizlik", "hasere", "ilaclama hizmet"),
    ),
    Sektor(
        ad="Özel güvenlik hizmetleri",
        nace=("80.10",),
        anahtarlar=("ozel guvenlik", "guvenlik hizmet alim"),
    ),
    Sektor(
        ad="Turizm ve konaklama",
        nace=("55.10", "79.11"),
        anahtarlar=("konaklama hizmet", "otel hizmet", "seyahat acente"),
    ),
    Sektor(
        ad="Eğitim ve danışmanlık hizmetleri",
        nace=("85.59", "70.22"),
        anahtarlar=("egitim hizmet alim", "danismanlik hizmet alim", "kurs hizmet"),
    ),
)


def katla(deger: str) -> str:
    """Metni karşılaştırmaya hazırlar: Türkçe harfleri katlar, harf/rakam dışını boşluğa indirir."""
    katlanmis = deger.translate(_KATLAMA).lower()
    temiz = "".join(ch if ch.isalnum() else " " for ch in katlanmis)

    return " ".join(temiz.split())


def _sayim(katlanmis: str, anahtar: str) -> int:
    """Anahtar kelime kaç kez geçiyor? Kelime başından eşleşir, Türkçe ekler serbesttir."""
    kalip = r"(?<![0-9a-z])" + re.escape(anahtar)

    return len(re.findall(kalip, katlanmis))


def sektor_bul(baslik: str, metin: str) -> Sektor | None:
    """İlanın konusunu tanır. Tanıyamazsa ``None`` döner — sektör UYDURULMAZ."""
    katlanmis_baslik = katla(baslik or "")
    katlanmis_metin = katla(metin or "")

    en_iyi: Sektor | None = None
    en_iyi_skor = 0

    for sektor in SEKTORLER:
        skor = sum(
            BASLIK_AGIRLIGI * _sayim(katlanmis_baslik, anahtar) + _sayim(katlanmis_metin, anahtar)
            for anahtar in sektor.anahtarlar
        )

        # Kesin ">" : eşitlikte önce tanımlanan (daha dar) sektör kazanır.
        if skor > en_iyi_skor:
            en_iyi, en_iyi_skor = sektor, skor

    return en_iyi if en_iyi_skor >= ASGARI_SKOR else None
