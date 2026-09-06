"""İlan metninden sektör çıkarımı (Faz 2).

Sahada görülen hata: ihale ilanlarından hiçbir sektör koşulu çıkarılmıyordu, bu yüzden
"Didi soğuk çay nakliye" ihalesi bir makine imalatçısına 88 puanla öneriliyordu. Bu
testler çıkarımı sabitler ve iki sınırı korur: tanınmayan konuya sektör UYDURULMAZ,
üretilen kural engelleyici DEĞİLDİR.

Başlıklar Resmî Gazete'den gerçekten toplanmış ilanlardan alınmıştır.
"""

from __future__ import annotations

from govai_workers.parser.rule_extractor import ALLOWED_FIELDS, extract_deterministic
from govai_workers.parser.sektor import SEKTORLER, katla, sektor_bul


def _nace(baslik: str, metin: str = "") -> tuple[str, ...] | None:
    sektor = sektor_bul(baslik, metin)
    return sektor.nace if sektor else None


class TestKatlama:
    def test_turkce_harfler_katlanir(self) -> None:
        # "İ" ve "ı" ayrı ayrı yanlış katlanırsa "İNŞAAT" ile "inşaat" eşleşmez.
        assert katla("İNŞAAT İŞLERİ") == "insaat isleri"
        assert katla("Sıvı Çözücü") == "sivi cozucu"

    def test_noktalama_bosluga_iner(self) -> None:
        assert katla("13 Kalem Tıbbi Cihaz (Alım)") == "13 kalem tibbi cihaz alim"

    def test_bos_deger(self) -> None:
        assert katla("") == ""


class TestGercekIlanlar:
    def test_cay_nakliye_tasimacilik(self) -> None:
        assert _nace("Didi Soğuk Çay Nakliye İşi İhale Edilecektir") == ("49.41", "49.39", "52.29")

    def test_tibbi_cihaz(self) -> None:
        assert _nace("13 Kalem Tıbbi Cihaz Satın Alınacaktır") == ("32.50", "46.46")

    def test_safkan_at_tarim(self) -> None:
        assert _nace("26 Baş Safkan Arap Koşu Tayı Satılacaktır") == ("01.11", "01.40", "01.50")

    def test_dikili_agac_ormancilik(self) -> None:
        assert _nace("İbreli ve Yapraklı Dikili Ağaç Satış İlanı") == ("02.10", "02.20", "16.10")

    def test_yht_gari_demiryolu(self) -> None:
        assert _nace("Sivas YHT Garı Buz Çözme ve Önleme Hizmeti Alınacaktır") == (
            "42.12", "49.10", "33.17",
        )

    def test_konusu_belirsiz_ilan_sektor_uretmez(self) -> None:
        # "Yatırımcılara Duyuru" hiçbir sektörü göstermez. Uydurulmaz.
        assert sektor_bul("Yatırımcılara Duyuru", "") is None


class TestSinirlar:
    def test_tanimayan_metin_none_doner(self) -> None:
        assert sektor_bul("Düzeltme İlânı", "İlgili ilan iptal edilmiştir.") is None

    def test_bos_girdi_none_doner(self) -> None:
        assert sektor_bul("", "") is None

    def test_baslik_govdeden_agir_basar(self) -> None:
        # Gövdede idarenin adresi geçse bile ilanın konusu başlıktadır.
        sektor = sektor_bul(
            "Malzemeli Yemek Hizmeti Alınacaktır",
            "Teslim yeri: Organize Sanayi Bölgesi inşaat sahası yanı. İnşaat halindeki blok.",
        )

        assert sektor is not None
        assert sektor.ad.startswith("Gıda")

    def test_govde_tek_basina_yeterlidir(self) -> None:
        sektor = sektor_bul("İhale İlanı", "Söz konusu iş bir demiryolu ray yenileme işidir.")

        assert sektor is not None
        assert "Demiryolu" in sektor.ad

    def test_kelime_ortasinda_eslesmez(self) -> None:
        # "insaat" kelimesi bir başka kelimenin ORTASINDA geçerse tetiklenmemeli.
        assert sektor_bul("Reinsaat Danışma", "") is None

    def test_turkce_ekler_serbesttir(self) -> None:
        for baslik in ("İnşaatı Yapılacaktır", "İnşaat İşi", "Nakliyat İhalesi", "Nakliye İşi"):
            assert sektor_bul(baslik, "") is not None, baslik


class TestKatalogTutarliligi:
    def test_anahtarlar_katlanmis_yazilmis(self) -> None:
        # Anahtar katlanmamış yazılırsa ("inşaat") hiçbir metinle eşleşmez ve
        # sessizce ölü kalır; test bunu yakalar.
        for sektor in SEKTORLER:
            for anahtar in sektor.anahtarlar:
                assert katla(anahtar) == anahtar, f"{sektor.ad}: {anahtar!r} katlanmamış"

    def test_her_sektorun_nace_kodu_var(self) -> None:
        for sektor in SEKTORLER:
            assert sektor.nace, sektor.ad
            for kod in sektor.nace:
                assert kod.replace(".", "").isdigit(), f"{sektor.ad}: {kod!r}"

    def test_anahtarlar_benzersiz(self) -> None:
        # Aynı anahtar iki sektörde olursa hangisinin kazandığı sıraya bağlı kalır.
        gorulen: dict[str, str] = {}
        for sektor in SEKTORLER:
            for anahtar in sektor.anahtarlar:
                assert anahtar not in gorulen, f"{anahtar!r}: {gorulen.get(anahtar)} / {sektor.ad}"
                gorulen[anahtar] = sektor.ad


class TestKuralUretimi:
    def _sektor_kurallari(self, baslik: str, metin: str = "") -> list:
        return [k for k in extract_deterministic(metin, baslik) if k.dimension == "Sector"]

    def test_kural_uretilir(self) -> None:
        kurallar = self._sektor_kurallari("Didi Soğuk Çay Nakliye İşi İhale Edilecektir")

        assert len(kurallar) == 1
        assert kurallar[0].field == "Company.NaceCodes"
        assert kurallar[0].operator == "NaceMatch"
        assert kurallar[0].value == "49.41,49.39,52.29"

    def test_kural_engelleyici_degildir(self) -> None:
        # Sektör ilanın KONUSUNDAN çıkarılır, metinde yazan bir yeterlilik şartından
        # değil. Engelleyici yapmak firmayı hukuken hak sahibi olduğu ihaleden elerdi.
        kural = self._sektor_kurallari("13 Kalem Tıbbi Cihaz Satın Alınacaktır")[0]

        assert kural.severity == "Major"
        assert kural.confidence < 1.0

    def test_alan_beyaz_listede(self) -> None:
        # CLAUDE.md §3: beyaz liste dışı alan motor tarafında sessizce "Unknown" olur.
        kural = self._sektor_kurallari("Malzemeli Yemek Hizmeti Alınacaktır")[0]

        assert kural.field in ALLOWED_FIELDS

    def test_aciklama_turkce_ve_okunabilir(self) -> None:
        kural = self._sektor_kurallari("İbreli ve Yapraklı Dikili Ağaç Satış İlanı")[0]

        assert kural.humanReadable.endswith(".")
        assert "NACE" in kural.humanReadable

    def test_kaynak_alintisi_baslik(self) -> None:
        baslik = "Sivas YHT Garı Buz Çözme ve Önleme Hizmeti Alınacaktır"

        assert self._sektor_kurallari(baslik)[0].sourceExcerpt == baslik

    def test_tanimayan_ilanda_kural_yok(self) -> None:
        assert self._sektor_kurallari("Yatırımcılara Duyuru") == []

    def test_diger_kurallar_bozulmaz(self) -> None:
        # Sektör kuralı eklenirken mevcut deterministik kalıplar çalışmaya devam etmeli.
        kurallar = extract_deterministic(
            "Başvuru için asgari 10 sigortalı çalışan şartı aranır. TR62 bölgesi.",
            "Makine Alımı İhalesi",
        )
        alanlar = {k.field for k in kurallar}

        assert "Workforce.EmployeeCount" in alanlar
        assert "Company.Nuts2Codes" in alanlar

    def test_ayni_girdi_ayni_sonuc(self) -> None:
        baslik = "Didi Soğuk Çay Nakliye İşi İhale Edilecektir"

        assert self._sektor_kurallari(baslik)[0] == self._sektor_kurallari(baslik)[0]
