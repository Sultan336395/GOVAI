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
    """Bekleyen bildirimleri kanallarına aktarır.

    Log "işlendi" değil **ne olduğunu** yazar: kaçı gerçekten gönderildi, kaçı
    başarısız oldu, kaçı kanalı yapılandırılmadığı için beklemede kaldı. Tek bir
    "işlendi" sayısı, hiç ulaşmayan bildirimleri başarı gibi gösteriyordu.
    """
    try:
        result = client.dispatch_notifications(batch_size=200) or {}
        processed = result.get("processedCount", 0)
        failed = result.get("failedCount", 0)
        skipped = result.get("skippedCount", 0)

        if processed:
            log.info(
                "notifications_dispatched",
                processed=processed,
                sent=result.get("sentCount", 0),
                failed=failed,
                skipped=skipped,
            )

        if failed:
            log.warning("notification_delivery_failed", count=failed)
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


def _erp_pull(client: GovAiClient) -> None:
    """Firmaların ERP'lerinden profil verisini çeker.

    Skorlama turundan ÖNCE çalışır: profil önce tazelenir, skorlar sonra o güncel
    veriyle hesaplanır. Ters sırada çalışsa skorlar bir gün eski profille üretilir.

    ERP'de bulunamayan alan eksik sayılır, sıfır yazılmaz; ERP bordro modülü
    kullanmayan bir firmada elle girilmiş doğru personel verisi silinmez.
    """
    log.info("erp_pull_started")

    try:
        result = client.pull_erp_profiles() or {}
        log.info(
            "erp_pull_finished",
            connections=result.get("connectionCount"),
            succeeded=result.get("succeededCount"),
            no_change=result.get("noChangeCount"),
            failed=result.get("failedCount"),
        )
    except ApiError:
        log.exception("erp_pull_failed")


def _ai_second_opinions(client: GovAiClient) -> None:
    """Kural motorunun kararlarına bağımsız ikinci görüş toplar.

    Gece skorlama turundan SONRA çalışır: görüş, o anki karara karşı verilir ve skor
    turdan sonra değişirse karşılaştırma eski karara bakmış olurdu.

    Görüş HİÇBİR SKORU DEĞİŞTİRMEZ. İşi taramadır: iki taraf ayrı yollardan aynı
    sonuca varıyorsa kayıt büyük olasılıkla doğrudur, ayrılıyorsa insan bakmalıdır.
    """
    log.info("ai_second_opinions_started")

    try:
        result = client.collect_ai_second_opinions() or {}

        if not result.get("aiEnabled", True):
            # Anahtar yok. Bu bir arıza DEĞİLDİR: sistem kural tabanlı çalışmaya
            # devam eder, yalnızca ikinci görüş toplanmaz.
            log.info("ai_second_opinions_disabled")
            return

        log.info(
            "ai_second_opinions_finished",
            examined=result.get("examinedCount"),
            recorded=result.get("recordedCount"),
            agreed=result.get("agreedCount"),
            disagreed=result.get("disagreedCount"),
            skipped=result.get("skippedCount"),
        )
    except ApiError:
        log.exception("ai_second_opinions_failed")


def _weekly_reports(client: GovAiClient) -> None:
    """Firmaların haftalık raporunu üretir.

    Pazartesi sabahı çalışır ve GEÇEN haftayı raporlar. İçinde bulunulan haftayı
    raporlamak yarım veri sunmak olurdu; hafta sınırı sunucu tarafında Türkiye
    saatiyle çizilir.

    Raporu worker KURMAZ, yalnızca tetikler: içerik ve yetki kararları sunucuda verilir
    ve worker müşteri verisi görmez (yanıt yalnızca sayı taşır).
    """
    log.info("weekly_reports_started")

    try:
        result = client.generate_weekly_reports() or {}
        log.info(
            "weekly_reports_finished",
            companies=result.get("companyCount"),
            generated=result.get("generatedCount"),
            failed=result.get("failedCount"),
            period_start=result.get("periodStart"),
            period_end=result.get("periodEnd"),
        )
    except ApiError:
        log.exception("weekly_reports_failed")


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

    # ERP çekme: her gece 02:45, skorlama turundan (03:30) ÖNCE.
    #
    # Sıra önemli: profil önce tazelenir, skorlar sonra o güncel veriyle hesaplanır.
    # Ters sırada skorlar bir gün eski profille üretilir ve firma dün düzelttiği
    # eksiğin sonucunu bir gün sonra görür.
    scheduler.add_job(
        _erp_pull,
        CronTrigger(hour=2, minute=45),
        args=[client],
        id="erp-pull",
        max_instances=1,
        coalesce=True,
    )

    # İkinci görüş: her gece 04:15, skorlama turundan (03:30) SONRA.
    #
    # Sıra önemli: görüş o anki karara karşı verilir. Skorlamadan önce çalışsa görüş
    # bir gün eski karara bakmış olur ve ayrışma gerçek değil takvim kaynaklı çıkardı.
    scheduler.add_job(
        _ai_second_opinions,
        CronTrigger(hour=4, minute=15),
        args=[client],
        id="ai-second-opinions",
        max_instances=1,
        coalesce=True,
    )

    # Haftalık rapor: Pazartesi 07:30, gece skorlama turundan SONRA.
    #
    # Sıra önemli: rapor o anki skorların anlık görüntüsünü alır. Skorlama turundan
    # önce çalışsa rapor bir gün eski skorlarla üretilir ve hafta boyunca öyle kalırdı
    # (geçmiş rapor bilerek yeniden hesaplanmaz).
    scheduler.add_job(
        _weekly_reports,
        CronTrigger(day_of_week="mon", hour=7, minute=30),
        args=[client],
        id="weekly-reports",
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
