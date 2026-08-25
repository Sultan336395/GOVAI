"""Parser worker giriş noktası.

`govai.document.parse.requested` kuyruğunu dinler:
    ham doküman → normalize metin → kural çıkarımı → /api/opportunities upsert
"""

from __future__ import annotations

import argparse
import sys
from typing import Any

from govai_workers.api_client import GovAiClient
from govai_workers.collector.fetcher import PoliteFetcher
from govai_workers.logging_setup import configure_logging, get_logger
from govai_workers.messaging import RoutingKeys, consume
from govai_workers.parser.chunker import build_chunks
from govai_workers.parser.extractors import extract_document
from govai_workers.parser.rule_extractor import extract_rules, rules_to_payload

log = get_logger(__name__)

# Metinde geçen anahtar kelimelerden destek türü tahmini; LLM devre dışıyken de kategori dolar.
_CATEGORY_KEYWORDS: list[tuple[str, tuple[str, ...]]] = [
    ("Tender", ("ihale", "teklif verme", "ekap", "yaklaşık maliyet")),
    ("RndSupport", ("ar-ge", "araştırma geliştirme", "tübitak", "prototip")),
    ("DigitalTransformation", ("dijital dönüşüm", "dijitalleşme", "yazılım altyapısı")),
    ("GreenTransformation", ("yeşil dönüşüm", "karbon", "enerji verimliliği", "sürdürülebilir")),
    ("ExportSupport", ("ihracat", "pazara giriş", "yurt dışı fuar")),
    ("EmploymentIncentive", ("istihdam", "sigorta primi", "sgk teşvik")),
    ("InvestmentIncentive", ("yatırım teşvik", "kapasite artırım", "makine teçhizat")),
    ("Loan", ("faiz desteği", "kredi", "kefalet")),
    ("Grant", ("hibe", "mali destek programı")),
]


def guess_category(title: str, text: str) -> str:
    haystack = f"{title}\n{text[:8000]}".lower()

    for category, keywords in _CATEGORY_KEYWORDS:
        if any(keyword in haystack for keyword in keywords):
            return category

    return "Other"


def process_document(client: GovAiClient, document: dict[str, Any]) -> None:
    """Bir dokümanı ayrıştırır ve fırsat kaydına çevirir."""
    url = document["url"]
    source_id = document["sourceId"]
    document_id = document["documentId"]

    log.info("parse_started", document_id=document_id, url=url)

    raw = document.get("rawContent")
    media_type = document.get("mediaType", "text/html")

    # Ham içerik mesajda taşınmıyorsa kaynaktan yeniden indirilir.
    if raw is None:
        with PoliteFetcher() as fetcher:
            fetched = fetcher.fetch(url)
            if fetched is None:
                log.warning("parse_skipped_unreachable", url=url)
                client.record_parse_result(
                    document_id, status="Failed", error="Kaynak adresine erişilemedi."
                )
                return
            extracted = extract_document(fetched.content, fetched.media_type)
            media_type = fetched.media_type
    else:
        extracted = extract_document(raw.encode("utf-8"), media_type)

    # Taranmış PDF: uydurma metin ÜRETİLMEZ, belge insana bırakılır.
    if extracted.needs_ocr:
        log.warning("parse_needs_ocr", document_id=document_id, url=url)
        client.record_parse_result(
            document_id,
            status="NeedsOcr",
            page_count=extracted.page_count,
            error="Taranmış PDF; metin katmanı yok.",
        )
        return

    text = extracted.text

    if not text.strip():
        # Belge SİLİNMEZ; karantinaya alınır ve yeniden ayrıştırılabilir.
        log.warning("parse_produced_empty_text", document_id=document_id)
        client.record_parse_result(
            document_id,
            status="Failed",
            error=extracted.error or "Ayrıştırma boş metin üretti.",
        )
        return

    # Kanıt parçaları GERÇEK ayrıştırma hattı tarafından üretilir; testte elle yazılmaz.
    chunks = build_chunks(text)

    parse_result = client.record_parse_result(
        document_id,
        status="Parsed",
        normalized_text=text,
        title=document.get("title"),
        language=document.get("language") or "tr",
        page_count=extracted.page_count,
        chunks=[chunk.to_payload() for chunk in chunks],
    )

    log.info(
        "evidence_recorded",
        document_id=document_id,
        version_id=parse_result.get("documentVersionId"),
        chunk_count=parse_result.get("chunkCount"),
    )

    title = document.get("title") or text.splitlines()[0][:300]
    extraction = extract_rules(title, text)

    if not extraction.rules:
        # Kuralsız fırsat yine de kaydedilir: danışman elle kural ekleyebilsin diye.
        log.info("no_rules_extracted", document_id=document_id, url=url)

    payload = {
        "sourceId": source_id,
        "sourceDocumentId": document_id,
        "sourceType": document.get("sourceType", "Other"),
        "supportCategory": extraction.detected_category or guess_category(title, text),
        "title": title,
        "publisher": document.get("publisher") or document.get("sourceName") or "Bilinmiyor",
        "publishedAt": document.get("collectedAt"),
        "summary": extraction.summary or text[:1500],
        "sourceUrl": url,
        "deadline": extraction.deadline,
        "ruleExtractionConfidence": extraction.confidence,
        "rules": rules_to_payload(extraction.rules),
        "documentChecklist": [
            {
                "code": doc["code"],
                "name": doc.get("name", doc["code"]),
                "isMandatory": bool(doc.get("isMandatory", True)),
                "issuingAuthority": doc.get("issuingAuthority"),
                "notes": None,
            }
            for doc in extraction.documents
        ],
    }

    result = client.upsert_opportunity(payload)

    log.info(
        "parse_finished",
        document_id=document_id,
        opportunity_id=result.get("id"),
        rule_count=len(extraction.rules),
        confidence=extraction.confidence,
    )


def main() -> int:
    configure_logging()

    parser = argparse.ArgumentParser(description="GOVAI doküman ayrıştırma worker'ı")
    parser.add_argument("--url", help="Tek bir URL'yi ayrıştır ve sonucu ekrana yaz (kayıt yapmaz)")
    args = parser.parse_args()

    if args.url:
        with PoliteFetcher() as fetcher:
            fetched = fetcher.fetch(args.url)
            if fetched is None:
                log.error("url_unreachable", url=args.url)
                return 1

            extracted = extract_document(fetched.content, fetched.media_type)
            text = extracted.text
            extraction = extract_rules(args.url, text)
            chunks = build_chunks(text)

            print(f"Metin uzunluğu: {len(text)} karakter")
            print(f"Kanıt parçası: {len(chunks)}")
            print(f"OCR gerekli mi: {extracted.needs_ocr}")
            print(f"Kural sayısı: {len(extraction.rules)} (güven: {extraction.confidence})")
            print(f"Son başvuru tahmini: {extraction.deadline}")
            for rule in extraction.rules:
                print(
                    f"  - [{rule.severity}/{rule.dimension}] "
                    f"{rule.field} {rule.operator} {rule.value}"
                )

        return 0

    with GovAiClient() as client:
        def handle(payload: dict[str, Any]) -> None:
            process_document(client, payload)

        consume("govai.parser", [RoutingKeys.DOCUMENT_PARSE_REQUESTED], handle)

    return 0


if __name__ == "__main__":
    sys.exit(main())
