"""Zamanlayıcı worker'ı.

İki işi var:
    * Her kaynağın kendi cron takvimine göre taramaya alınmasını tetiklemek
    * Bekleyen bildirimleri düzenli aralıklarla gönderime almak

Skorların yeniden hesaplanması API tarafında olay tabanlı tetiklenir; burada yalnızca
gece toplu bir doğrulama turu çalıştırılır (kaçan olay varsa telafi eder).
"""

from __future__ import annotations

import signal
import sys
from types import FrameType

from apscheduler.schedulers.blocking import BlockingScheduler
from apscheduler.triggers.cron import CronTrigger
from apscheduler.triggers.interval import IntervalTrigger

from govai_workers import health
from govai_workers.api_client import ApiError, GovAiClient
from govai_workers.logging_setup import configure_logging, get_logger

log = get_logger(__name__)


def _trigger_due_crawls(client: GovAiClient) -> None:
    """Cron takvimi gelen kaynakları tarama kuyruğuna bırakır."""
    sources = client.list_sources(only_enabled=True)
    triggered = 0

    for source in sources:
        try:
            client.trigger_crawl(source["id"])
            triggered += 1
        except ApiError:
            log.exception("trigger_crawl_failed", source=source.get("name"))

    log.info("crawls_triggered", count=triggered, total_sources=len(sources))


def _dispatch_notifications(client: GovAiClient) -> None:
    try:
        result = client.dispatch_notifications(batch_size=200)
        count = (result or {}).get("processedCount", 0)
        if count:
            log.info("notifications_dispatched", count=count)
    except ApiError:
        log.exception("notification_dispatch_failed")


def _nightly_rescore(client: GovAiClient) -> None:
    """Gece toplu doğrulama turu; kaçan skorlama olaylarını telafi eder.

    Eskiden firma listesini ``/api/company-profile`` üzerinden okuyordu. O uç Faz 1'den
    beri üyelikten filtreleniyor ve worker'ın hiçbir şirkette üyeliği yok; liste **boş**
    dönüyordu, yani gece turu fiilen hiçbir şey yapmıyordu. Artık firmaları sunucu
    tarafında dolaşan toplu uç kullanılıyor — worker müşteri verisi görmez.
    """
    log.info("nightly_rescore_started")

    try:
        result = client.rescore_batch()
        log.info(
            "nightly_rescore_finished",
            companies=(result or {}).get("companyCount"),
            evaluated=(result or {}).get("evaluatedOpportunityCount"),
            eligible=(result or {}).get("eligibleCount"),
            failed=(result or {}).get("failedCompanyCount"),
        )
    except ApiError:
        log.exception("nightly_rescore_failed")


def main() -> int:
    configure_logging()

    client = GovAiClient()
    scheduler = BlockingScheduler(timezone="Europe/Istanbul")

    # Kaynak taramaları: her gün 07:00 ve 19:00 (kaynak bazlı cron API tarafında saklanır;
    # burada iki tur tetikleyip kaynak kendi takvimine göre atlama kararını verir).
    scheduler.add_job(
        _trigger_due_crawls,
        CronTrigger(hour="7,19", minute=0),
        args=[client],
        id="trigger-crawls",
        max_instances=1,
        coalesce=True,
    )

    scheduler.add_job(
        _dispatch_notifications,
        IntervalTrigger(minutes=5),
        args=[client],
        id="dispatch-notifications",
        max_instances=1,
        coalesce=True,
    )

    scheduler.add_job(
        _nightly_rescore,
        CronTrigger(hour=3, minute=30),
        args=[client],
        id="nightly-rescore",
        max_instances=1,
        coalesce=True,
    )

    # Scheduler kuyruk tüketmez; sağlığı ZAMANLAYICININ yaşadığına bağlıdır.
    # İş çalışmıyorsa nabız eskir ve container sağlıksız işaretlenir.
    scheduler.add_job(
        lambda: health.isaretle("scheduler", baglanti_var=True),
        IntervalTrigger(seconds=int(health.NABIZ_ARALIGI)),
        id="heartbeat",
        max_instances=1,
        coalesce=True,
    )

    def _shutdown(_signum: int, _frame: FrameType | None) -> None:
        log.info("scheduler_stopping")
        scheduler.shutdown(wait=False)
        client.close()

    signal.signal(signal.SIGINT, _shutdown)
    signal.signal(signal.SIGTERM, _shutdown)

    # İlk nabız hemen atılır ki container "başlatılıyor" aşamasında sağlıksız görünsün
    # değil, hazır olduğu an sağlıklı olsun.
    health.isaretle("scheduler", baglanti_var=True)

    log.info("scheduler_started", jobs=[job.id for job in scheduler.get_jobs()])
    scheduler.start()

    return 0


if __name__ == "__main__":
    sys.exit(main())
