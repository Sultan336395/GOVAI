"""PDF metin katmanının yeterliliği (Faz 2).

Eski kural yalnızca **tamamen boş** metni taranmış sayıyordu. Cumhurbaşkanı
kararlarının PDF'lerinden ise künye çıkıyor (başlık, karar numarası, imza;
142-214 karakter) ama kararın asıl gövdesi çıkmıyordu. Sistem bunu
"tam ayrıştırıldı, OCR gerekmedi" diye raporluyordu.

Bu bir yalandır ve kanıt zincirini bozar: üretilen kanıt parçaları künyeden
gelir, kararın içeriği hiç okunmamıştır. Bir danışman o parçalara bakıp
"karar şu koşulu getiriyor" diyemez.

Testler gerçek PDF üretir ve gerçek ayrıştırma hattından geçirir; sahte
çıkarım nesnesi kullanılmaz.
"""

from __future__ import annotations

from govai_workers.parser.extractors import MIN_CHARS_PER_PAGE, extract_document

# Gerçek bir Cumhurbaşkanı kararı künyesi — gövdesi olmadan.
KUNYE = (
    "CUMHURBASKANI KARARI Karar Sayisi: 11646 26 Agustos 2026 "
    "Recep Tayyip ERDOGAN CUMHURBASKANI Resmi Gazete Sayi: 33353"
)


def _pdf(sayfa_metinleri: list[str]) -> bytes:
    """Verilen metinleri taşıyan, metin katmanı GERÇEK olan bir PDF üretir."""
    nesneler: list[bytes] = []
    sayfa_ref_ilk = 3
    sayfa_sayisi = len(sayfa_metinleri)

    kids = " ".join(f"{sayfa_ref_ilk + i * 2} 0 R" for i in range(sayfa_sayisi))
    font_ref = sayfa_ref_ilk + sayfa_sayisi * 2

    nesneler.append(b"<</Type/Catalog/Pages 2 0 R>>")
    nesneler.append(
        f"<</Type/Pages/Kids[{kids}]/Count {sayfa_sayisi}>>".encode("latin-1")
    )

    for i, metin in enumerate(sayfa_metinleri):
        icerik_ref = sayfa_ref_ilk + i * 2 + 1
        nesneler.append(
            f"<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
            f"/Resources<</Font<</F1 {font_ref} 0 R>>>>"
            f"/Contents {icerik_ref} 0 R>>".encode("latin-1")
        )
        akis = f"BT /F1 12 Tf 40 750 Td ({metin}) Tj ET".encode("latin-1")
        nesneler.append(
            b"<</Length " + str(len(akis)).encode() + b">>stream\n" + akis + b"\nendstream"
        )

    nesneler.append(b"<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>")

    cikti = bytearray(b"%PDF-1.4\n")
    konumlar: list[int] = []

    for i, nesne in enumerate(nesneler, start=1):
        konumlar.append(len(cikti))
        cikti += f"{i} 0 obj".encode("latin-1") + nesne + b"endobj\n"

    xref = len(cikti)
    cikti += f"xref\n0 {len(nesneler) + 1}\n".encode("latin-1")
    cikti += b"0000000000 65535 f \n"

    for konum in konumlar:
        cikti += f"{konum:010d} 00000 n \n".encode("latin-1")

    cikti += (
        f"trailer<</Size {len(nesneler) + 1}/Root 1 0 R>>\n"
        f"startxref\n{xref}\n%%EOF"
    ).encode("latin-1")

    return bytes(cikti)


class TestEksikMetinKatmani:
    def test_yalnizca_kunye_cikan_pdf_ocr_ister(self) -> None:
        sonuc = extract_document(_pdf([KUNYE]), "application/pdf")

        # Metin VAR ama gövde yok. "Tam ayrıştırıldı" DENMEZ.
        assert sonuc.text.strip()
        assert sonuc.needs_ocr is True
        assert sonuc.succeeded is False

    def test_gerekce_sayilarla_raporlanir(self) -> None:
        sonuc = extract_document(_pdf([KUNYE]), "application/pdf")

        # Gerekçe denetlenebilir olmalı: kaç sayfa, kaç karakter, eşik neydi.
        assert sonuc.ocr_reason is not None
        assert "1 sayfa" in sonuc.ocr_reason
        assert str(MIN_CHARS_PER_PAGE) in sonuc.ocr_reason
        assert "OCR" in sonuc.ocr_reason

    def test_kunye_metni_atilmaz(self) -> None:
        sonuc = extract_document(_pdf([KUNYE]), "application/pdf")

        # Çıkan az metin de kanıttır; silinmez, insana onunla birlikte gider.
        assert "Karar Sayisi: 11646" in sonuc.text

    def test_cok_sayfali_kararda_toplam_yogunluga_bakilir(self) -> None:
        # Dört sayfa, her birinde yalnızca künye: hiçbiri gövde değil.
        sonuc = extract_document(_pdf([KUNYE] * 4), "application/pdf")

        assert sonuc.page_count == 4
        assert sonuc.needs_ocr is True
        assert "4 sayfa" in (sonuc.ocr_reason or "")


class TestYeterliMetinKatmani:
    def test_gercek_govdesi_olan_pdf_ocr_istemez(self) -> None:
        govde = "MADDE 1 - " + ("Bu karar kapsaminda destek saglanir. " * 40)
        sonuc = extract_document(_pdf([govde]), "application/pdf")

        assert sonuc.needs_ocr is False
        assert sonuc.succeeded is True
        assert sonuc.ocr_reason is None

    def test_esigin_hemen_ustu_gecer(self) -> None:
        sonuc = extract_document(_pdf(["A" * (MIN_CHARS_PER_PAGE + 20)]), "application/pdf")

        assert sonuc.needs_ocr is False

    def test_esigin_hemen_altinda_isaretlenir(self) -> None:
        sonuc = extract_document(_pdf(["A" * (MIN_CHARS_PER_PAGE - 40)]), "application/pdf")

        assert sonuc.needs_ocr is True

    def test_cok_sayfali_gercek_belge_gecer(self) -> None:
        sayfa = "Madde metni burada devam eder ve yeterince uzundur. " * 20
        sonuc = extract_document(_pdf([sayfa] * 3), "application/pdf")

        assert sonuc.page_count == 3
        assert sonuc.needs_ocr is False


class TestMetinKatmaniHicYok:
    def test_bos_pdf_ayri_gerekce_verir(self) -> None:
        sonuc = extract_document(_pdf([" "]), "application/pdf")

        assert sonuc.needs_ocr is True
        # Hiç katman olmaması ile eksik katman AYRI iki durumdur; gerekçeleri de ayrıdır.
        assert sonuc.ocr_reason is not None
        assert "taranmis" in sonuc.ocr_reason.lower() or "taranmış" in sonuc.ocr_reason.lower()


class TestHtmlEtkilenmez:
    def test_kisa_html_ocr_istemez(self) -> None:
        # Eşik YALNIZCA PDF içindir. Kısa HTML mevzuat metni elenmez —
        # "kısa olmak eleme sebebi değildir" kuralı burada geçerlidir.
        sonuc = extract_document(
            b"<html><body>YONETMELIK Karar Sayisi: 11646</body></html>", "text/html"
        )

        assert sonuc.needs_ocr is False
        assert sonuc.succeeded is True
