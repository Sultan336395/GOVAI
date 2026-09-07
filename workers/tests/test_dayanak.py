"""Çağrı metninden mevzuat dayanağı çıkarımı (Faz 3).

Bir destek çağrısı bir kanuna, yönetmeliğe ya da karara dayanır. Danışman başvurunun
hukuki zeminini bu bilgi olmadan kaynağa kadar takip edemez.

En kritik test uydurmama testidir: yanlış bir dayanak, dayanağın hiç olmamasından
kötüdür — danışman onu doğru sanıp kontrol etmez.
"""

from __future__ import annotations

from govai_workers.parser.dayanak import AZAMI_UZUNLUK, dayanak_metni, dayanaklari_bul


class TestNumaraliMevzuat:
    def test_sayili_kanun_okunur(self) -> None:
        metin = "Bu program 5746 sayılı Araştırma ve Geliştirme Kanunu kapsamında yürütülür."

        assert "5746 sayılı" in (dayanak_metni(metin) or "")

    def test_madde_eklenir(self) -> None:
        metin = "Destek, 5746 sayılı Kanunun 3 üncü maddesine dayanılarak verilir."
        sonuc = dayanak_metni(metin) or ""

        assert "5746 sayılı" in sonuc
        assert "md. 3" in sonuc

    def test_madde_kisa_yazim(self) -> None:
        metin = "6102 sayılı Türk Ticaret Kanunu madde 64 uyarınca."
        sonuc = dayanak_metni(metin) or ""

        assert "6102 sayılı" in sonuc
        assert "md. 64" in sonuc

    def test_numara_alani_dolar(self) -> None:
        dayanaklar = dayanaklari_bul("193 sayılı Gelir Vergisi Kanunu gereğince.")

        assert dayanaklar
        assert dayanaklar[0].numara == "193"


class TestAdlandirilmisMevzuat:
    def test_yonetmelik_okunur(self) -> None:
        metin = "Başvurular Ar-Ge Merkezleri Yönetmeliği hükümlerine göre değerlendirilir."

        assert "Yönetmeliği" in (dayanak_metni(metin) or "")

    def test_teblig_okunur(self) -> None:
        metin = "Vergi Usul Kanunu Genel Tebliği uyarınca düzenlenir."

        assert "Tebliği" in (dayanak_metni(metin) or "")


class TestUydurmama:
    def test_mevzuat_gecmeyen_metin_bos_doner(self) -> None:
        metin = "13 kalem tıbbi cihaz satın alınacaktır. Teklifler elden verilecektir."

        assert dayanak_metni(metin) is None

    def test_bos_metin(self) -> None:
        assert dayanak_metni("") is None
        assert dayanaklari_bul("") == []

    def test_rastgele_sayi_kanun_sayilmaz(self) -> None:
        # "2026/15" ihale kayıt numarasıdır, kanun numarası değil.
        assert dayanak_metni("İhale kayıt numarası 2026/15'tir.") is None


class TestSiralamaVeSinir:
    def test_dayanak_isaretli_cumle_once_gelir(self) -> None:
        metin = (
            "Program tanıtımında 4691 sayılı Kanundan söz edilmektedir. "
            "İşbu çağrı 5746 sayılı Kanuna dayanılarak yayımlanmıştır."
        )
        sonuc = dayanak_metni(metin) or ""

        # "dayanılarak" geçen cümle önce gelmeli.
        assert sonuc.index("5746") < sonuc.index("4691")

    def test_ayni_mevzuat_iki_kez_yazilmaz(self) -> None:
        metin = (
            "5746 sayılı Kanun uyarınca. Yine 5746 sayılı Kanun kapsamında. "
            "Tekrar 5746 sayılı Kanuna göre."
        )

        assert (dayanak_metni(metin) or "").count("5746") == 1

    def test_uzunluk_sinirlanir(self) -> None:
        metin = " ".join(f"{1000 + k} sayılı Kanun uyarınca." for k in range(40))
        sonuc = dayanak_metni(metin) or ""

        assert len(sonuc) <= AZAMI_UZUNLUK

    def test_ayni_girdi_ayni_sonuc(self) -> None:
        metin = "5746 sayılı Kanunun 3 üncü maddesine dayanılarak."

        assert dayanak_metni(metin) == dayanak_metni(metin)


class TestGercekMetin:
    def test_tesvik_cagrisi(self) -> None:
        metin = (
            "KADIN VE GENÇ İSTİHDAMI PRİM DESTEĞİ\n"
            "4447 sayılı İşsizlik Sigortası Kanununun geçici 10 uncu maddesi "
            "uyarınca, özel sektör işverenlerince istihdam edilen kadın ve genç "
            "çalışanlar için sigorta primi desteği sağlanır."
        )
        sonuc = dayanak_metni(metin) or ""

        assert "4447 sayılı" in sonuc
        assert "md. 10" in sonuc
