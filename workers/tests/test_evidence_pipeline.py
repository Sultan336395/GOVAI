"""Faz 2 – kanıt üretim hattı testleri.

Kanıt parçaları elle yazılmaz; gerçek ayrıştırma hattı (``extract_document`` →
``build_chunks`` → ``record_parse_result``) tarafından üretilir. Bu testler o hattı
uçtan uca, sahte bir API istemcisiyle koşturur.
"""

from __future__ import annotations

from typing import Any

from govai_workers.parser import runner
from govai_workers.parser.chunker import PAGE_BREAK, build_chunks
from govai_workers.parser.extractors import extract_document

RESMI_METIN = (
    "YÜRÜRLÜK\n\n"
    "Bu tebliğ, 5746 sayılı Kanun kapsamında yapılan Ar-Ge harcamalarının "
    "belgelendirilmesine ilişkin usul ve esasları düzenler.\n\n"
    "Şirketlerin ilgili dönemde yaptıkları harcamalar, yeminli mali müşavir raporu ile "
    "belgelendirilir ve indirim olarak dikkate alınır."
)


class SahteIstemci:
    """Parser'ın çağırdığı uçları kaydeden sahte API istemcisi."""

    def __init__(self) -> None:
        self.parse_calls: list[dict[str, Any]] = []
        self.opportunities: list[dict[str, Any]] = []

    def record_parse_result(self, document_id: str, **kwargs: Any) -> dict[str, Any]:
        self.parse_calls.append({"documentId": document_id, **kwargs})
        return {
            "documentVersionId": "sürüm-1",
            "chunkCount": len(kwargs.get("chunks") or []),
            "status": kwargs.get("status"),
        }

    def upsert_opportunity(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.opportunities.append(payload)
        return {"id": "firsat-1"}


def _document(raw: str, media_type: str = "text/html") -> dict[str, Any]:
    return {
        "documentId": "belge-1",
        "sourceId": "kaynak-1",
        "url": "https://kurum.gov.tr/ilan/1",
        "title": "Ar-Ge Harcamaları Tebliği",
        "rawContent": raw,
        "mediaType": media_type,
        "sourceName": "Gelir İdaresi Başkanlığı",
    }


class TestKanitUretimi:
    def test_parser_kanit_parcasi_uretir(self) -> None:
        istemci = SahteIstemci()
        html = f"<html><body><div>{RESMI_METIN}</div></body></html>"

        runner.process_document(istemci, _document(html))

        assert len(istemci.parse_calls) == 1
        cagri = istemci.parse_calls[0]

        assert cagri["status"] == "Parsed"
        assert cagri["chunks"], "Gerçek hat en az bir kanıt parçası üretmeli"

        # Parça metni normalize metinden gelmeli, uydurulmamalı.
        for chunk in cagri["chunks"]:
            assert chunk["text"].strip() in cagri["normalized_text"]
            assert chunk["endOffset"] >= chunk["startOffset"]

    def test_kanit_parcasi_konumu_metne_geri_gosterir(self) -> None:
        extracted = extract_document(RESMI_METIN.encode("utf-8"), "text/plain")
        chunks = build_chunks(extracted.text)

        assert chunks
        for chunk in chunks:
            dilim = extracted.text[chunk.start_offset : chunk.end_offset]
            assert dilim.strip() == chunk.text.strip()

    def test_bos_ve_anlamsiz_parcalar_kaydedilmez(self) -> None:
        gurultu = "12345\n\n...\n\n-\n\nA\n\n"
        metin = gurultu + "Bu paragraf kanıt sayılacak kadar uzun ve anlamlıdır."
        chunks = build_chunks(metin)

        assert len(chunks) == 1
        assert chunks[0].text.startswith("Bu paragraf")

    def test_sayfa_numarasi_korunur(self) -> None:
        birinci = "Birinci sayfadaki paragraf yeterince uzundur."
        ikinci = "İkinci sayfadaki paragraf da yeterince uzundur."
        metin = f"{birinci}{PAGE_BREAK}{ikinci}"
        chunks = build_chunks(metin)

        assert [c.page_number for c in chunks] == [1, 2]

    def test_bolum_basligi_parcaya_tasinir(self) -> None:
        extracted = extract_document(RESMI_METIN.encode("utf-8"), "text/plain")
        chunks = build_chunks(extracted.text)

        assert chunks[0].section_title == "YÜRÜRLÜK"
        # Başlığın kendisi kanıt parçası olmaz.
        assert all(c.text != "YÜRÜRLÜK" for c in chunks)

    def test_turkce_karakterler_kanitta_bozulmaz(self) -> None:
        istemci = SahteIstemci()
        html = f"<html><body>{RESMI_METIN}</body></html>"

        runner.process_document(istemci, _document(html))

        metin = istemci.parse_calls[0]["normalized_text"]
        assert "Şirketlerin" in metin
        assert "yeminli mali müşavir" in metin


class TestBasarisizAyristirma:
    def test_bos_metin_belgeyi_karantinaya_yollar(self) -> None:
        istemci = SahteIstemci()

        runner.process_document(istemci, _document("<html><body></body></html>"))

        assert len(istemci.parse_calls) == 1
        assert istemci.parse_calls[0]["status"] == "Failed"

        # Belge fırsat kataloğuna GİRMEZ.
        assert istemci.opportunities == []

    def test_taranmis_pdf_needs_ocr_isaretlenir(self) -> None:
        istemci = SahteIstemci()

        # Metin katmanı olmayan geçerli bir PDF: tek boş sayfa.
        bos_pdf = (
            b"%PDF-1.4\n"
            b"1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n"
            b"2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n"
            b"3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>endobj\n"
            b"trailer<</Root 1 0 R>>\n"
        )

        belge = _document(bos_pdf.decode("latin-1"), media_type="application/pdf")
        runner.process_document(istemci, belge)

        assert len(istemci.parse_calls) == 1
        durum = istemci.parse_calls[0]["status"]

        # Ya OCR gerekli ya da ayrıştırılamadı; her iki hâlde de UYDURMA METİN ÜRETİLMEZ.
        assert durum in {"NeedsOcr", "Failed"}
        assert not istemci.parse_calls[0].get("normalized_text")
        assert istemci.opportunities == []
