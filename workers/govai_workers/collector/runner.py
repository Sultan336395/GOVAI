"""Collector worker giriş noktası.

`govai.source.crawl.requested` kuyruğunu dinler. Mesaj gelmese de belirli aralıklarla
tüm etkin kaynakları taramak için `--once` modu zamanlayıcıdan da çağrılabilir.
"""

from __future__ import annotations

import argparse
import sys
from typing import Any

from govai_workers.api_client import GovAiClient
from govai_workers.collector.crawler import SourceCrawler
from govai_workers.collector.fetcher import PoliteFetcher
from govai_workers.collector.verifier import verify_source
from govai_workers.logging_setup import configure_logging, get_logger
from govai_workers.messaging import RoutingKeys, consume

log = get_logger(__name__)


def crawl_source(client: GovAiClient, source: dict[str, Any]) -> None:
    log.info("crawl_started", source=source["name"], url=source["baseUrl"])

    with PoliteFetcher() as fetcher:
        crawler = SourceCrawler(fetcher, client.ingest_document)
        result = crawler.crawl(source)

    # Kısmi başarı da "başarılı" sayılır; hiçbir doküman alınamadıysa kaynak hatalı işaretlenir.
    status = "Failed" if result.collected == 0 and result.failed > 0 else "Succeeded"
    message = result.summary()

    if result.errors:
        message += " | ilk hata: " + result.errors[0][:400]

    client.record_run(source["id"], status, message, result.collected)

    log.info(
        "crawl_finished",
        source=source["name"],
        collected=result.collected,
        skipped=result.skipped,
        failed=result.failed,
    )


def crawl_all(client: GovAiClient) -> int:
    sources = client.list_sources(only_enabled=True)
    log.info("crawl_all_started", source_count=len(sources))

    for source in sources:
        try:
            crawl_source(client, source)
        except Exception as exc:  # noqa: BLE001 - bir kaynağın hatası diğerlerini durdurmamalı
            log.exception("crawl_source_failed", source=source.get("name"))
            try:
                client.record_run(source["id"], "Failed", str(exc)[:400], 0)
            except Exception:  # noqa: BLE001
                log.exception("record_run_failed", source=source.get("name"))

    return len(sources)


def verify_all(client: GovAiClient, source_id: str | None = None) -> int:
    """Kaynakları canlı doğrular ve sonuçları API'ye bildirir.

    Doğrulanamayan kaynak pasif kalır; başarılı gösterilmez.
    """
    sources = client.list_sources(only_enabled=False)

    if source_id:
        sources = [s for s in sources if s["id"] == source_id]
        if not sources:
            log.error("source_not_found", source_id=source_id)
            return 1

    dogrulanan = 0

    for source in sources:
        result = verify_source(source)

        try:
            client.record_verification(source["id"], result.to_payload())
        except Exception:  # noqa: BLE001 - bildirim hatası doğrulamayı durdurmamalı
            log.exception("verification_report_failed", source=source.get("name"))

        if result.reachable and result.discovered_link_count > 0:
            dogrulanan += 1

        print(result.summary())

    pasif = len(sources) - dogrulanan
    print(f"\nToplam {len(sources)} kaynak; {dogrulanan} doğrulandı, {pasif} pasif.")
    return 0


def main() -> int:
    configure_logging()

    parser = argparse.ArgumentParser(description="GOVAI kaynak tarama worker'ı")
    parser.add_argument("--once", action="store_true", help="Tüm kaynakları bir kez tara ve çık")
    parser.add_argument("--source-id", help="Yalnızca belirtilen kaynağı tara")
    parser.add_argument(
        "--verify",
        action="store_true",
        help="Kaynakları canlı doğrula (belge kaydetmez); doğrulanan kaynak taranabilir olur",
    )
    args = parser.parse_args()

    with GovAiClient() as client:
        if args.verify:
            return verify_all(client, args.source_id)

        if args.source_id:
            all_sources = client.list_sources(only_enabled=False)
            sources = [s for s in all_sources if s["id"] == args.source_id]
            if not sources:
                log.error("source_not_found", source_id=args.source_id)
                return 1
            crawl_source(client, sources[0])
            return 0

        if args.once:
            crawl_all(client)
            return 0

        def handle(payload: dict[str, Any]) -> None:
            source_id = payload.get("sourceId")
            if not source_id:
                log.warning("crawl_message_missing_source_id", payload=payload)
                return

            sources = [s for s in client.list_sources(only_enabled=False) if s["id"] == source_id]
            if not sources:
                log.warning("crawl_message_unknown_source", source_id=source_id)
                return

            crawl_source(client, sources[0])

        consume("govai.collector", [RoutingKeys.SOURCE_CRAWL_REQUESTED], handle)

    return 0


if __name__ == "__main__":
    sys.exit(main())
