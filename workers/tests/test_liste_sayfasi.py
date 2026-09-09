"""Liste sayfası başlığının tanınması.

Sahada görülen sorun: KOSGEB'in iki ayrı liste sayfası kataloğa çağrı olarak girdi
ve **ikisi de aynı adı taşıdı** — kurum her sayfaya aynı ``<title>``'ı koyuyor
("Destekler Listesi - KOSGEB T.C. Küçük ve Orta Ölçekli İşletmeleri…"). Katalogda
iki özdeş satır göründü; kullanıcı bunu mükerrer kayıt sandı. Oysa kayıtlar
farklıydı ve **hiçbiri kayıt olmamalıydı**: ikisi de liste sayfasıydı.

Süzgecin iki yönü de sınanır. Yanlış eleme, yanlış geçirmekten pahalıdır: geçen çöp
kayıt görülür ve silinir, elenen gerçek çağrı hiç görülmez.
"""

from __future__ import annotations

import pytest

from govai_workers.collector.alaka import liste_sayfasi_basligi_mi

# ── Liste sayfası: kayıt AÇILMAMALI ─────────────────────────────────────────

LISTE_SAYFALARI = [
    (
        "Destekler Listesi - KOSGEB T.C. Küçük ve Orta Ölçekli İşletmeleri "
        "Geliştirme ve Destekleme İdaresi Başkanlığı",
        "KOSGEB",
    ),
    ("Yürürlükten Kaldırılan Destekler - KOSGEB", "KOSGEB"),
    ("Duyurular", "Sosyal Güvenlik Kurumu"),
    ("Duyurular | Sosyal Güvenlik Kurumu", "Sosyal Güvenlik Kurumu"),
    ("İlanlar", "Resmî Gazete"),
    ("Tüm Duyurular", "Ticaret Bakanlığı"),
    ("Programlar", "KOSGEB"),
]


@pytest.mark.parametrize(("baslik", "kurum"), LISTE_SAYFALARI)
def test_liste_sayfasi_taninir(baslik: str, kurum: str) -> None:
    assert liste_sayfasi_basligi_mi(baslik, kurum) is True


# ── Gerçek kayıt: kesinlikle ELENMEMELİ ─────────────────────────────────────

GERCEK_KAYITLAR = [
    # Listeden SÖZ EDEN duyuru; listenin kendisi değil. Bu ayrım olmadan SGK'nın
    # gerçek duyuruları elenirdi.
    (
        "Bedeli Ödenecek İlaçlar Listesinde Yapılan Düzenlemeler Hakkında Duyuru",
        "Sosyal Güvenlik Kurumu",
    ),
    (
        "29/08/2026 Tarihli ve 33355 Sayılı Resmî Gazete'de Yayımlanan Tebliğ",
        "Sosyal Güvenlik Kurumu",
    ),
    ("KOBİ Dijital Dönüşüm Destek Programı", "KOSGEB"),
    ("Girişimci Destek Programı", "KOSGEB"),
    ("2026 Yılı Ar-Ge ve Dijitalleşme Mali Destek Programı", "Çukurova Kalkınma Ajansı"),
    ("Pazara Giriş Belgeleri Desteği", "Ticaret Bakanlığı"),
    ("ORMAN EMVALİ SATILACAKTIR", "Resmî Gazete"),
    ("TAŞINMAZ SATILACAKTIR", "Resmî Gazete"),
    ("Kadın ve Genç İstihdamı Prim Desteği", "Resmî Gazete"),
    # "Destekler" kelimesi geçiyor ama sayfa bir liste değil.
    ("Destekler Kapsamında Yapılacak Ödemeler Hakkında Tebliğ", "KOSGEB"),
]


@pytest.mark.parametrize(("baslik", "kurum"), GERCEK_KAYITLAR)
def test_gercek_kayit_elenmez(baslik: str, kurum: str) -> None:
    assert liste_sayfasi_basligi_mi(baslik, kurum) is False


# ── Sınır durumlar ──────────────────────────────────────────────────────────


def test_bos_baslik_elenmez() -> None:
    # Başlığı olmayan belge için karar VERİLMEZ; ayrıştırıcı ve karantina karar verir.
    assert liste_sayfasi_basligi_mi("", "KOSGEB") is False
    assert liste_sayfasi_basligi_mi("   ", "KOSGEB") is False


def test_kurum_adi_verilmese_de_calisir() -> None:
    # Kurum adı bilinmiyorsa ek sökülemez ama düz liste adları yine tanınır.
    assert liste_sayfasi_basligi_mi("Duyurular") is True
    assert liste_sayfasi_basligi_mi("KOBİ Dijital Dönüşüm Destek Programı") is False


def test_kurum_eki_yanlis_sokulmez() -> None:
    """Ayraçtan sonrası kurum adı DEĞİLSE başlık bölünmez.

    "Destekler Listesi - 2026 Yılı" başlığında ayraçtan sonrası kurum adı değil,
    kaydın kendi ayrıntısıdır. Körü körüne bölmek, kaydı liste sanmaya yol açardı —
    ama burada kalan ad zaten liste adı olduğu için sonuç aynı çıkar. Asıl korunan
    durum aşağıdaki gibisidir: ek sökülmediği için tam ad karşılaştırılır.
    """
    assert liste_sayfasi_basligi_mi("Ar-Ge Desteği - Başvuru Rehberi", "KOSGEB") is False


def test_liste_adi_cumlenin_icinde_gecerse_elenmez() -> None:
    # Tam eşleşme aranır. "Duyurular" bir liste adıdır ama "Duyurular Hakkında
    # Değişiklik" bir kayıttır.
    assert liste_sayfasi_basligi_mi("Duyurular Hakkında Değişiklik", "SGK") is False
