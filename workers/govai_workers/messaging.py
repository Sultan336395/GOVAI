"""RabbitMQ tüketici/üretici sarmalayıcısı.

.NET tarafındaki `QueueNames` sabitleriyle birebir aynı routing key'ler kullanılır.
"""

from __future__ import annotations

import json
from collections.abc import Callable
from typing import Any

import pika
from pika.adapters.blocking_connection import BlockingChannel

from govai_workers import health
from govai_workers.config import settings
from govai_workers.logging_setup import get_logger

log = get_logger(__name__)


class RoutingKeys:
    """.NET `GovAI.Application.Abstractions.Services.QueueNames` ile eşleşmelidir."""

    SOURCE_CRAWL_REQUESTED = "govai.source.crawl.requested"
    DOCUMENT_PARSE_REQUESTED = "govai.document.parse.requested"
    RULE_EXTRACTION_REQUESTED = "govai.rules.extraction.requested"
    SCORING_REQUESTED = "govai.scoring.requested"
    NOTIFICATION_DISPATCH_REQUESTED = "govai.notification.dispatch.requested"


def _connection_parameters() -> pika.ConnectionParameters:
    return pika.ConnectionParameters(
        host=settings.rabbitmq_host,
        port=settings.rabbitmq_port,
        virtual_host=settings.rabbitmq_vhost,
        credentials=pika.PlainCredentials(settings.rabbitmq_user, settings.rabbitmq_password),
        heartbeat=60,
        blocked_connection_timeout=300,
    )


def publish(routing_key: str, payload: dict[str, Any]) -> None:
    connection = pika.BlockingConnection(_connection_parameters())
    try:
        channel = connection.channel()
        channel.exchange_declare(
            exchange=settings.rabbitmq_exchange, exchange_type="topic", durable=True
        )
        channel.basic_publish(
            exchange=settings.rabbitmq_exchange,
            routing_key=routing_key,
            body=json.dumps(payload).encode("utf-8"),
            properties=pika.BasicProperties(content_type="application/json", delivery_mode=2),
        )
        log.debug("event_published", routing_key=routing_key)
    finally:
        connection.close()


def consume(
    queue_name: str,
    routing_keys: list[str],
    handler: Callable[[dict[str, Any]], None],
) -> None:
    """Kuyruğu dinler ve her mesaj için `handler` çağırır.

    Hata durumunda mesaj yeniden kuyruğa alınmaz (`requeue=False`); sonsuz döngüyü önlemek için
    dead-letter kuyruğuna düşer. Kalıcı hatalar loglardan takip edilir.
    """
    connection = pika.BlockingConnection(_connection_parameters())
    channel: BlockingChannel = connection.channel()

    channel.exchange_declare(
        exchange=settings.rabbitmq_exchange, exchange_type="topic", durable=True
    )

    dead_letter_exchange = f"{settings.rabbitmq_exchange}.dlx"
    channel.exchange_declare(exchange=dead_letter_exchange, exchange_type="topic", durable=True)
    channel.queue_declare(queue=f"{queue_name}.dead", durable=True)
    channel.queue_bind(queue=f"{queue_name}.dead", exchange=dead_letter_exchange, routing_key="#")

    # İşleyicinin KENDİ ölü mektup yayını da aynı kuyruğa düşmeli.
    #
    # İki ayrı yol var ve ikisi de gerekli:
    #   * Kuyruğun DLX'i — işleyici çökerse ya da mesajı reddederse devreye girer,
    #     ama hatanın nedenini yazamaz.
    #   * İşleyicinin publish(DEAD_LETTER_KEY, ...) çağrısı — deneme sayacı dolduğunda
    #     hatanın nedeniyle birlikte bırakır.
    #
    # İkincisi ANA exchange'e yayımlanıyor; bu bağlama olmadan hiçbir kuyruğa
    # ulaşmıyor ve sessizce kayboluyordu.
    channel.queue_bind(
        queue=f"{queue_name}.dead",
        exchange=settings.rabbitmq_exchange,
        routing_key=f"{queue_name}.dead-letter",
    )

    channel.queue_declare(
        queue=queue_name,
        durable=True,
        arguments={"x-dead-letter-exchange": dead_letter_exchange},
    )

    for routing_key in routing_keys:
        channel.queue_bind(
            queue=queue_name, exchange=settings.rabbitmq_exchange, routing_key=routing_key
        )

    channel.basic_qos(prefetch_count=settings.prefetch_count)

    def _on_message(ch: BlockingChannel, method: Any, _properties: Any, body: bytes) -> None:
        try:
            payload = json.loads(body.decode("utf-8"))
        except json.JSONDecodeError:
            log.error("message_not_json", routing_key=method.routing_key)
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)
            return

        try:
            handler(payload)
            ch.basic_ack(delivery_tag=method.delivery_tag)
        except Exception:
            log.exception("message_handler_failed", routing_key=method.routing_key)
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

    channel.basic_consume(queue=queue_name, on_message_callback=_on_message)

    # Nabız, bağlantının KENDİ olay döngüsünden atar. Bağlantı koparsa döngü durur ve
    # nabız eskir; süreç yaşamaya devam etse bile container sağlıksız işaretlenir.
    # Süreç varlığına bakan bir kontrol bu durumu göremezdi.
    def _nabiz() -> None:
        health.isaretle(queue_name, baglanti_var=True)
        connection.call_later(health.NABIZ_ARALIGI, _nabiz)

    _nabiz()

    log.info("consumer_started", queue=queue_name, routing_keys=routing_keys)
    try:
        channel.start_consuming()
    except KeyboardInterrupt:
        log.info("consumer_stopping", queue=queue_name)
        channel.stop_consuming()
    finally:
        connection.close()
