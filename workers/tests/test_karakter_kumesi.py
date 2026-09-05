"""Toplayıcının karakter kümesi tespiti (Faz 2).

Karantina ekranındaki ``Ä°``, ``ÅŸ``, ``ÄŸ`` bozulmalarının kaynağı burasıydı.

Eski kural "bildirilen kümeyle çözmeyi dene, **hata verirse** sonrakine geç"
biçimindeydi. Ama ``iso-8859-1`` ve ``windows-1254`` tek baytlıdır: neredeyse her
baytı bir karaktere eşlerler ve **asla hata vermezler**. Sunucu yanlışlıkla
``iso-8859-1`` bildirdiğinde UTF-8 gövde sessizce bozuluyor, hiçbir uyarı
üretilmiyordu.

Yeni kural önce baytların kendisine bakar: gövde geçerli UTF-8 ise ve çok baytlı
dizi içeriyorsa UTF-8'dir. Gerçek bir tek baytlı belgede geçerli çok baytlı UTF-8
dizilerinin rastlantıyla oluşması pratikte imkânsızdır.
"""

from __future__ import annotations

from govai_workers.collector.fetcher import FetchedDocument

TURKCE = "İş güvenliği ve şüpheli ödeme: ğüçöşı ÇĞİÖŞÜ"
BASLIK = "ARTIRMA, EKSİLTME VE İHALE İLÂNLARI"


def _belge(icerik: bytes, charset: str | None = None) -> FetchedDocument:
    adres = "https://www.resmigazete.gov.tr/eskiler/2026/08/20260827.htm"

    return FetchedDocument(
        url=adres,
        canonical_url=adres,
        content=icerik,
        media_type="text/html",
        status_code=200,
        charset=charset,
    )


class TestYanlisBildirilenKume:
    def test_sunucu_iso88591_dese_de_utf8_govde_bozulmaz(self) -> None:
        # Gerçek arıza: sunucu iso-8859-1 bildiriyor, gövde UTF-8.
        belge = _belge(TURKCE.encode("utf-8"), charset="iso-8859-1")

        assert belge.text() == TURKCE

    def test_sunucu_windows1254_dese_de_utf8_govde_bozulmaz(self) -> None:
        belge = _belge(TURKCE.encode("utf-8"), charset="windows-1254")

        assert belge.text() == TURKCE

    def test_bozuk_karakterler_hic_olusmaz(self) -> None:
        belge = _belge(BASLIK.encode("utf-8"), charset="iso-8859-1")
        metin = belge.text()

        # Kullanıcının bildirdiği bozulma imzaları.
        for bozuk in ("Ä°", "ÅŸ", "ÄŸ", "Ã¼", "Ã§", "Ã¶"):
            assert bozuk not in metin

        assert metin == BASLIK

    def test_meta_etiketi_yanlissa_da_utf8_kazanir(self) -> None:
        govde = f'<html><head><meta charset="iso-8859-9"></head><body>{TURKCE}</body></html>'
        belge = _belge(govde.encode("utf-8"), charset=None)

        assert TURKCE in belge.text()


class TestDogruBildirilenKume:
    def test_gercek_windows1254_govde_dogru_cozulur(self) -> None:
        # Sunucu doğru söylüyor ve gövde gerçekten windows-1254.
        belge = _belge(TURKCE.encode("windows-1254"), charset="windows-1254")

        assert belge.text() == TURKCE

    def test_gercek_iso88599_govde_dogru_cozulur(self) -> None:
        metin = "İhale ilânı: şartname ücreti"
        belge = _belge(metin.encode("iso-8859-9"), charset="iso-8859-9")

        assert belge.text() == metin

    def test_utf8_bildirilen_utf8_govde(self) -> None:
        belge = _belge(TURKCE.encode("utf-8"), charset="utf-8")

        assert belge.text() == TURKCE

    def test_bildirim_yoksa_meta_okunur(self) -> None:
        govde = (
            '<html><head><meta charset="windows-1254"></head>'
            f"<body>{TURKCE}</body></html>"
        )
        belge = _belge(govde.encode("windows-1254"), charset=None)

        assert TURKCE in belge.text()

    def test_hicbir_bildirim_yoksa_turkce_kumeler_denenir(self) -> None:
        belge = _belge(TURKCE.encode("windows-1254"), charset=None)

        assert belge.text() == TURKCE


class TestSinirDurumlar:
    def test_sadece_ascii_govde_her_kumede_ayni(self) -> None:
        ascii_metin = "TENDER NOTICE 2026/1 - budget 1000 TRY"

        for kume in (None, "utf-8", "iso-8859-1", "windows-1254"):
            assert _belge(ascii_metin.encode("ascii"), charset=kume).text() == ascii_metin

    def test_bos_govde_cokertmez(self) -> None:
        assert _belge(b"", charset="utf-8").text() == ""

    def test_taninmayan_kume_adi_cokertmez(self) -> None:
        belge = _belge(TURKCE.encode("utf-8"), charset="boyle-bir-kume-yok")

        assert belge.text() == TURKCE

    def test_hicbir_kumeyle_cozulemeyen_govde_kaybolmaz(self) -> None:
        # Rastgele ikili içerik: metin değil, ama içerik tamamen kaybedilmemeli.
        ikili = bytes([0xFF, 0xFE, 0x00, 0x41, 0x00, 0x42])
        metin = _belge(ikili, charset="utf-8").text()

        assert isinstance(metin, str)


class TestPdfEtkilenmez:
    def test_pdf_baytlari_bozulmadan_kalir(self) -> None:
        # PDF metni çıkarıcıya ham bayt olarak gider; text() burada kullanılmaz.
        pdf = b"%PDF-1.4\nbinary\xc3\xa7\xc5\x9f"
        adres = "https://www.resmigazete.gov.tr/eskiler/2026/08/karar.pdf"
        belge = FetchedDocument(
            url=adres,
            canonical_url=adres,
            content=pdf,
            media_type="application/pdf",
            status_code=200,
        )

        assert belge.is_pdf is True
        assert belge.content == pdf
