"""Parser worker giriş noktası.

`govai.document.parse.requested` kuyruğunu dinler:
    ham doküman → normalize metin → kural çıkarımı → /api/opportunities upsert
"""

from __future__ import annotations

import argparse
import re
import sys
from typing import Any

from govai_workers.api_client import GovAiClient
from govai_workers.collector import eurlex
from govai_workers.collector.alaka import (
    alakasiz_baslik_mi,
    katla,
    liste_sayfasi_basligi_mi,
)
from govai_workers.collector.fetcher import PoliteFetcher
from govai_workers.logging_setup import configure_logging, get_logger
from govai_workers.messaging import RoutingKeys, consume
from govai_workers.parser.bolum_basligi import toplu_bolum_basligi
from govai_workers.parser.butce_turu import butce_yuku as turlu_butce
from govai_workers.parser.chunker import build_chunks
from govai_workers.parser.dayanak import dayanak_metni
from govai_workers.parser.extractors import extract_document
from govai_workers.parser.ihale import IhaleAlanlari, ihale_alanlari
from govai_workers.parser.rule_extractor import extract_rules, rules_to_payload

log = get_logger(__name__)

#: Bu kategorilerdeki kaynaklardan gelen belge mevzuattır, fırsat değildir.
REGULATION_CATEGORIES = frozenset(
    {"Regulation", "Tax", "SocialSecurity", "LabourLaw", "CommercialLaw"}
)

#: İhale kaynakları. Bu kategorilerde ilan biçimi düzenlidir ve başlık/idare
#: belgeden çıkarılabilir (bkz. parser/ihale.py).
TENDER_CATEGORIES = frozenset({"Tender"})

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


#: Sayfa başlığı olarak işe yaramayan kalıplar. Resmî Gazete günlük nüshalarında
#: <title> yalnızca tarihtir ("26 Ağustos 2026 ÇARŞAMBA"), belgenin adı değildir.
#: Başlığın başındaki tarih. Sökülüp geriye ne kaldığına bakılır.
_ONDEKI_TARIH = re.compile(
    r"^\s*(?:\d{1,2}[./]\d{1,2}[./]\d{2,4}|\d{1,2}\s+\w+\s+\d{4})\s*",
    re.UNICODE,
)

#: Tarihten sonra kalan ama tek başına ad sayılmayan bağlaç/sıfatlar.
_TARIH_EKLERI = frozenset({
    "tarihli", "tarihinde", "tarihli ve", "carsamba", "persembe", "cuma",
    "cumartesi", "pazar", "pazartesi", "sali", "gunlu",
})


def tarih_basligi_mi(aday: str) -> bool:
    """Başlık gerçekte yalnızca bir tarih mi?

    Tarihle BAŞLAYAN başlık geçerlidir ve elenmemelidir: resmî duyuruların tarihle
    başlaması olağandır ("29/08/2026 SUT Değişiklik Tebliği …"). İlk sürüm tarihle
    başlayan her başlığı eliyordu; sonucu sahada görüldü — SGK'nın iki duyurusunun
    doğru adı reddedildi, metne düşüldü ve gövdedeki "ÇALIŞAN VE İŞVEREN" menü
    başlığı duyuru adı olarak kaydedildi.

    Karar, tarih sökülünce geriye anlamlı bir ad kalıp kalmadığına göre verilir.
    """
    kalan = _ONDEKI_TARIH.sub("", aday or "", count=1).strip(" -–—,:")

    if not kalan:
        return True

    return katla(kalan) in _TARIH_EKLERI or len(kalan) <= 12


def belge_basligi(sayfa_basligi: str | None, metin: str) -> str | None:
    """Belgenin gerçek adı.

    Sayfa başlığı belgeyi tanıtıyorsa aynen kullanılır. Yalnızca tarih ya da
    anlamsız kadar kısaysa, metnin ilk **gerçek** başlık satırına düşülür.
    Uydurma yapılmaz: metinde de uygun bir satır yoksa ``None`` döner ve alan
    ``NotProvided`` olarak işaretlenir.
    """
    aday = (sayfa_basligi or "").strip()

    # Menü/kategori başlığı sayfa başlığı olarak da gelebilir; uzun ve tarihsiz olması
    # onu belgenin adı yapmaz.
    if aday and len(aday) > 12 and not tarih_basligi_mi(aday) and not gezinme_basligi_mi(aday):
        return aday

    # Belge türü satırı (TEBLİĞ, YÖNETMELİK…) tek başına ad değildir; asıl ad
    # genellikle onu izleyen büyük harfli satırdır.
    tur_satiri = None

    for ham in metin.splitlines():
        satir = " ".join(ham.split())

        # Tür satırı kısa olabilir ("TEBLİĞ"); önce o denenir.
        if satir.upper() in _BELGE_TURLERI:
            tur_satiri = satir.upper()
            continue

        if not (12 < len(satir) <= 200):
            continue
        if tarih_basligi_mi(satir):
            continue
        if satir.endswith((".", ":", ";")):
            continue

        harfler = [k for k in satir if k.isalpha()]
        if not harfler:
            continue

        # Resmî belge başlıkları büyük harfle yazılır.
        if sum(1 for k in harfler if k.isupper()) / len(harfler) < 0.8:
            continue

        # Menü, kategori ve bölüm başlıkları da büyük harflidir ve bu denetimden
        # geçerler. Kurum sayfasının gövdesinde "ÇALIŞAN VE İŞVEREN", "KURUMSAL",
        # "MENÜ" gibi satırlar bulunur; bunlar belgenin adı DEĞİLDİR.
        if gezinme_basligi_mi(satir):
            continue

        return f"{tur_satiri} — {satir}" if tur_satiri else satir

    if tur_satiri:
        return tur_satiri

    # Son çare sayfa başlığıdır — ama yalnızca güvenilirse. Tarihten ibaret ya da
    # menü başlığı olan bir değer başlık sayılmaz; None dönerse kayıt zorunlu alan
    # eksikliğinden karantinaya girer ve insan inceler. Uydurma yapılmaz.
    if aday and not tarih_basligi_mi(aday) and not gezinme_basligi_mi(aday):
        return aday

    return None


#: Kurum sitelerinin gövdesinde geçen menü, kategori ve bölüm başlıkları. Büyük harfli
#: oldukları için "resmî başlık" denetiminden geçerler ama belgenin adı değildirler.
#: Karşılaştırma Türkçe katlamayla ve TAM eşleşmeyle yapılır: gerçek bir duyurunun
#: adında bu kelimeler geçebilir ("Çalışan ve işveren primlerine ilişkin duyuru"),
#: yalnızca satırın KENDİSİ menü başlığıysa elenir.
_GEZINME_BASLIKLARI = frozenset({
    "calisan ve isveren", "kurumsal", "menu", "ana menu", "hizli erisim",
    "duyurular", "haberler", "mevzuat", "istatistikler", "e hizmetler",
    "sikca sorulan sorular", "iletisim", "hakkimizda", "site haritasi",
    "genel bilgi", "kategoriler", "baglantilar", "arama", "giris",
    "sosyal guvenlik kurumu", "emekli", "isveren", "sigortali", "genel saglik sigortasi",
})


def gezinme_basligi_mi(satir: str) -> bool:
    """Satır bir menü/kategori başlığı mı? Belgenin adı olamaz."""
    return katla(satir) in _GEZINME_BASLIKLARI


#: Tek başına başlık sayılmayan, belgenin türünü bildiren satırlar.
_BELGE_TURLERI = frozenset({
    "TEBLİĞ", "TEBLİĞLER", "YÖNETMELİK", "YÖNETMELİKLER", "GENELGE", "KARAR",
    "KARARLAR", "KANUN", "KANUNLAR", "TÜZÜK", "YÖNERGE", "ANAYASA MAHKEMESİ KARARLARI",
})


def guess_category(title: str, text: str) -> str:
    haystack = f"{title}\n{text[:8000]}".lower()

    for category, keywords in _CATEGORY_KEYWORDS:
        if any(keyword in haystack for keyword in keywords):
            return category

    return "Other"


def _kaynak_kategorisi(
    client: GovAiClient,
    source_id: str,
    document: dict[str, Any],
) -> str | None:
    """Kaynağın kategorisi — **veritabanındaki kayıttan**.

    Mesajdaki ``sourceCategory`` yalnızca kaynak okunamadığında ve mesajda
    gerçekten varsa kullanılır; uydurma yapılmaz. İkisi de yoksa ``None`` döner
    ve çağıran fırsat kaydı açmaz.
    """
    try:
        source = client.get_source(source_id)
    except Exception:  # noqa: BLE001 — kaynak okunamazsa tahmin edilmez
        log.exception("source_lookup_failed", source_id=source_id)
        return document.get("sourceCategory")

    if source is None:
        log.warning("source_not_found_for_document", source_id=source_id)
        return document.get("sourceCategory")

    kategori = source.get("category")

    # Mesajla veritabanı ayrışıyorsa bu bir uyarıdır: kuyrukta eski mesaj olabilir.
    mesajdaki = document.get("sourceCategory")
    if mesajdaki and kategori and mesajdaki != kategori:
        log.warning(
            "source_category_mismatch",
            source_id=source_id,
            mesajdaki=mesajdaki,
            veritabani=kategori,
        )

    return kategori


def process_document(client: GovAiClient, document: dict[str, Any]) -> None:
    """Bir dokümanı ayrıştırır ve fırsat kaydına çevirir."""
    url = document["url"]
    source_id = document["sourceId"]
    document_id = document["documentId"]

    log.info("parse_started", document_id=document_id, url=url)

    raw = document.get("rawContent")
    media_type = document.get("mediaType", "text/html")

    if raw is not None:
        extracted = extract_document(raw.encode("utf-8"), media_type)
    else:
        # Ham içerik mesajda taşınmıyorsa kaynaktan yeniden indirilir.
        #
        # EUR-Lex tarayıcı adresi otomatik isteklere boş gövdeli HTTP 202 döndürüyor.
        # Koruma AŞILMAZ; AB Yayın Ofisi'nin resmî CELLAR ucuna geçilir.
        resmi = eurlex.metin_indir_adresten(url)

        if resmi is not None:
            icerik, media_type = resmi
            extracted = extract_document(icerik, media_type)
            log.info("eurlex_cellar_uzerinden_alindi", document_id=document_id)
        else:
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

    # Metin katmanı yok ya da EKSİK: uydurma metin ÜRETİLMEZ, belge insana bırakılır.
    #
    # Eksik katman da buraya düşer. Künyesi okunup gövdesi okunamayan bir karar için
    # "tam ayrıştırıldı" demek, kanıt zincirini künyeden üretilmiş parçalara dayandırır;
    # kararın içeriği hiç okunmamış olduğu hâlde okunmuş sayılır.
    if extracted.needs_ocr:
        log.warning(
            "parse_needs_ocr",
            document_id=document_id,
            url=url,
            reason=extracted.ocr_reason,
        )
        client.record_parse_result(
            document_id,
            status="NeedsOcr",
            page_count=extracted.page_count,
            error=extracted.ocr_reason or "Taranmış PDF; metin katmanı yok.",
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
        title=belge_basligi(document.get("title"), text),
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

    # Mevzuat kaynağından gelen belge FIRSAT KATALOĞUNA YAZILMAZ.
    # Mevzuata başvurulmaz; uyulur. Kaydı sunucu, kaynağın kategorisine bakarak
    # RegulatoryChange olarak açar (bkz. SourceService.TryRecordRegulatoryChangeAsync).
    #
    # Kategori MESAJDAN OKUNMAZ. Mesajdaki alan eksik kalırsa mevzuat belgesi
    # fırsat kataloğuna yazılmaya çalışılırdı; sunucu bunu reddeder ama ayrıştırma
    # da hataya düşerdi. Doğru kaynak veritabanındaki Source kaydıdır.
    kategori = _kaynak_kategorisi(client, source_id, document)

    if kategori is None:
        # Kaynak okunamadıysa TAHMİN EDİLMEZ. Kanıt parçaları zaten kaydedildi;
        # fırsat kaydı açmak için kategorinin bilinmesi şarttır.
        log.warning("source_category_unknown_skipping_opportunity", document_id=document_id)
        return

    if kategori in REGULATION_CATEGORIES:
        log.info(
            "regulation_document_not_an_opportunity",
            document_id=document_id,
            category=kategori,
        )
        return

    title = document.get("title") or text.splitlines()[0][:300]

    # Toplu bölüm başlığı bir ilan DEĞİLDİR: o sayfada onlarca ayrı ilan vardır.
    # Sunucu da reddediyor (SectionHeading) ve kaynak doğru odur; buradaki denetim
    # yalnızca gereksiz bir tur atmayı önler. Kanıt parçaları zaten kaydedildi.
    if toplu_bolum_basligi(title):
        log.info("section_heading_not_an_opportunity", document_id=document_id, title=title)
        return

    # İhale ilanları düzenli bir biçim izler: başlık ilk satırlarda (birden fazla
    # satıra bölünebilir), ihaleyi açan idare "…den:" ile biten satırdadır. Genel yol
    # başlığı ilk satırdan alıp kurum olarak KAYNAĞIN adını yazıyordu; oysa bir
    # danışman için asıl bilgi ihaleyi açan idaredir ve o belgenin içinde yazılıdır.
    ihale = ihale_alanlari(text) if kategori in TENDER_CATEGORIES else IhaleAlanlari()

    if ihale.baslik:
        title = ihale.baslik

    # Konuya alaka: bağlantı süzgecinden geçmiş ama içeriği kurumsal sayfa çıkmış
    # belge burada elenir. Kayıt açılmaz; belge kaynağında durur, uydurma fırsat
    # üretilmez.
    if alakasiz_baslik_mi(title):
        log.info("parse_skipped_irrelevant", document_id=document_id, title=title[:120])
        client.record_parse_result(
            document_id,
            status="Skipped",
            error="Başlık kurumsal sayfa başlığı; çağrı kaydı açılmadı.",
        )
        return

    # Liste sayfası tek bir çağrı DEĞİLDİR ve kayıt açılmamalıdır.
    #
    # Sahada iki KOSGEB liste sayfası kataloğa girdi ve ikisi de aynı adı taşıdı —
    # kurum her sayfaya aynı <title>'ı koyuyor, dolayısıyla iki farklı sayfa mükerrer
    # kayıt gibi göründü. Kanıt parçaları zaten kaydedildi; elenen tek şey uydurma
    # çağrı kaydıdır.
    if liste_sayfasi_basligi_mi(title, document.get("sourceName") or ""):
        log.info("parse_skipped_list_page", document_id=document_id, title=title[:120])
        client.record_parse_result(
            document_id,
            status="Skipped",
            error="Liste sayfası; tek bir çağrı değildir, kayıt açılmadı.",
        )
        return

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
        # İhaleyi açan idare belgeden gelir; bulunamazsa kaynağın adına düşülür.
        "publisher": (
            ihale.kurum
            or document.get("publisher")
            or document.get("sourceName")
            or "Bilinmiyor"
        ),
        "publishedAt": document.get("collectedAt"),
        # Bütçe ve mevzuat dayanağı metinden okunur; okunamazsa None gider ve
        # sunucu mevcut değeri korur. Boş bir bütçe nesnesi GÖNDERİLMEZ: kaydı
        # "bütçesi girilmiş ama sıfır" hâline getirir ve veri kalitesini yanıltır.
        # Bütçe TÜRÜYLE birlikte gider: hibe ile kredi aynı belgede geçer ve
        # karışırsa kullanıcıya 20 milyonluk kredi, hibe gibi gösterilir.
        "budget": _butce.get("legacy") if (_butce := turlu_butce(text)) else None,
        "budgetItems": _butce.get("items", []) if _butce else [],
        "budgetRates": _butce.get("rates", []) if _butce else [],
        "legalBasis": dayanak_metni(text),
        "summary": extraction.summary or text[:1500],
        "sourceUrl": url,
        "deadline": extraction.deadline or ihale.ihale_tarihi,
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
