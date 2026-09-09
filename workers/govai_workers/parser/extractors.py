"""Metin çıkarımı ve normalizasyon.

Resmî metinler PDF veya HTML olarak gelir; ikisi de kural çıkarımına verilmeden önce
gürültüden arındırılmış düz metne çevrilir. Normalizasyon aynı zamanda içerik özetinin
(hash) kararlı olmasını sağlar: menü/tarih değişimi yüzünden ilan "değişti" sanılmaz.
"""

from __future__ import annotations

import io
import re
import unicodedata
from dataclasses import dataclass

from bs4 import BeautifulSoup

from govai_workers.collector.fetcher import metni_coz
from govai_workers.logging_setup import get_logger

log = get_logger(__name__)

_NOISE_SELECTORS = [
    "script", "style", "nav", "header", "footer", "noscript",
    "iframe", "form", "aside", ".breadcrumb", ".menu", ".navbar",
    ".cookie", ".social", "#header", "#footer",
]

_WHITESPACE = re.compile(r"[ \t ]+")
_BLANK_LINES = re.compile(r"\n{3,}")


@dataclass(frozen=True, slots=True)
class ExtractedDocument:
    """Ayrıştırma çıktısı ve kanıt için gereken künye."""

    text: str
    page_count: int | None = None
    needs_ocr: bool = False
    error: str | None = None
    #: ``needs_ocr`` neden işaretlendi? Rapora ve karantina notuna birebir geçer.
    ocr_reason: str | None = None

    @property
    def succeeded(self) -> bool:
        return bool(self.text.strip()) and not self.needs_ocr and self.error is None


#: Sayfa başına bu kadar karakterin altında kalan bir PDF metin katmanı KULLANILAMAZ.
#:
#: Gerekçe: Cumhurbaşkanı kararlarının PDF'lerinden yalnızca künye çıkabiliyor —
#: başlık, karar numarası ve imza; 142-214 karakter. Kararın asıl gövdesi (maddeler,
#: ekler, tablolar) metin katmanında yok. Eski kural yalnızca TAMAMEN boş metni
#: taranmış sayıyordu, bu yüzden sistem "tam ayrıştırıldı, OCR gerekmedi" diyordu.
#: Bu bir yalandır: kanıt parçaları künyeden üretilir, kararın içeriği hiç okunmamıştır.
#:
#: Eşik ihtiyatlı seçildi. Resmî Gazete'nin gerçek metin katmanı olan sayfaları binlerce
#: karakter verir; künye 200 civarındadır. Arada geniş bir boşluk vardır. Yanlış
#: işaretlenen belge SİLİNMEZ, insana bırakılır — güvenli yön budur.
MIN_CHARS_PER_PAGE = 250


def extract_text(content: bytes, media_type: str) -> str:
    """Ham baytları düz metne çevirir (geriye uyumlu sade arayüz)."""
    return extract_document(content, media_type).text


def extract_document(
    content: bytes,
    media_type: str,
    charset: str | None = None,
) -> ExtractedDocument:
    """Metni künyesiyle birlikte çıkarır.

    PDF sayfaları form-feed ile ayrılır; böylece kanıt parçalarında sayfa numarası
    korunabilir. Metin katmanı olmayan taranmış PDF için **uydurma metin üretilmez**,
    ``needs_ocr`` işaretlenir.

    ``charset`` indiricinin belirlediği karakter kümesidir. Verilmezse gövdeden
    çıkarılır — ama <b>karar yine tek bir yerde</b> verilir (``metni_coz``), burada
    ikinci bir tahmin yapılmaz.
    """
    if "pdf" in media_type.lower():
        return _extract_pdf_document(content)

    text = _extract_html(content, charset)
    return ExtractedDocument(text=text, error=None if text.strip() else "HTML metni boş.")


def _extract_pdf(content: bytes) -> str:
    return _extract_pdf_document(content).text


def _extract_pdf_document(content: bytes) -> ExtractedDocument:
    try:
        from pypdf import PdfReader

        reader = PdfReader(io.BytesIO(content))
        pages = [page.extract_text() or "" for page in reader.pages]
        # Her sayfa AYRI normalize edilir, sonra form-feed ile birleştirilir.
        # Birleşik metni normalize etmek form-feed'i satır kırpmasında yok ederdi ve
        # kanıt parçaları sayfa numarasını kaybederdi.
        text = "\f".join(normalize(page) for page in pages)

        sayfa_sayisi = len(reader.pages)
        harf_sayisi = len(text.strip())

        if not text.strip():
            # Metin katmanı olmayan taranmış PDF. Uydurma metin ÜRETİLMEZ.
            log.warning("pdf_has_no_text_layer", pages=sayfa_sayisi)
            return ExtractedDocument(
                text="",
                page_count=sayfa_sayisi,
                needs_ocr=True,
                ocr_reason="PDF'in metin katmanı yok; taranmış görüntü.",
            )

        # Metin VAR ama gövde yok: künye çıkmış, kararın kendisi çıkmamış.
        # "Kısa olmak eleme sebebi değildir" kuralı burada geçerli DEĞİLDİR; belge
        # elenmiyor, eksik ayrıştırıldığı dürüstçe söyleniyor ve insana bırakılıyor.
        if sayfa_sayisi and harf_sayisi < MIN_CHARS_PER_PAGE * sayfa_sayisi:
            log.warning(
                "pdf_text_layer_partial",
                pages=sayfa_sayisi,
                chars=harf_sayisi,
                threshold=MIN_CHARS_PER_PAGE * sayfa_sayisi,
            )
            return ExtractedDocument(
                text=text,
                page_count=sayfa_sayisi,
                needs_ocr=True,
                ocr_reason=(
                    f"PDF'in metin katmanı eksik: {sayfa_sayisi} sayfadan yalnızca "
                    f"{harf_sayisi} karakter çıktı (sayfa başına en az "
                    f"{MIN_CHARS_PER_PAGE} beklenir). Büyük olasılıkla yalnızca künye "
                    f"okunabildi; belgenin gövdesi için OCR gerekiyor."
                ),
            )

        return ExtractedDocument(text=text, page_count=sayfa_sayisi)
    except Exception as exc:  # noqa: BLE001 - ayrıştırma hatası belgeyi silmemeli
        log.exception("pdf_extract_failed")
        return ExtractedDocument(text="", error=f"PDF ayrıştırılamadı: {exc}"[:500])


def _extract_html(content: bytes, charset: str | None = None) -> str:
    try:
        # BeautifulSoup'a BAYT DEĞİL METİN verilir.
        #
        # Bayt verildiğinde kümeyi kendi başına tahmin ediyor ve indiricinin özenle
        # verdiği kararı yok sayıyordu: aynı belge iki yerde iki farklı şekilde
        # çözülüyor, ayrıştırıcı bozuk metin üretiyordu. Çözme kararının tek sahibi
        # ``metni_coz``tur.
        soup = BeautifulSoup(metni_coz(content, charset), "lxml")

        for selector in _NOISE_SELECTORS:
            for node in soup.select(selector):
                node.decompose()

        # Tablolar koşul taşır (ör. destek oranı tabloları); satırları ayrıştırılabilir tut.
        for table in soup.find_all("table"):
            rows = []
            for tr in table.find_all("tr"):
                cells = [td.get_text(" ", strip=True) for td in tr.find_all(["td", "th"])]
                if any(cells):
                    rows.append(" | ".join(cells))
            table.replace_with("\n" + "\n".join(rows) + "\n")

        return normalize(soup.get_text("\n"))
    except Exception:
        log.exception("html_extract_failed")
        return ""


def normalize(text: str) -> str:
    """Unicode, boşluk ve satır sonlarını kararlı hâle getirir."""
    text = unicodedata.normalize("NFKC", text)
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    text = _WHITESPACE.sub(" ", text)
    text = "\n".join(line.strip() for line in text.split("\n"))
    text = _BLANK_LINES.sub("\n\n", text)
    return text.strip()


_DEADLINE_PATTERNS = [
    re.compile(
        r"son\s+(?:ba[sş]vuru|teklif\s+verme)\s*(?:tarihi|g[uü]n[uü])?\s*[:\-]?\s*"
        r"(?P<day>\d{1,2})[./](?P<month>\d{1,2})[./](?P<year>\d{4})",
        re.IGNORECASE,
    ),
    re.compile(
        r"(?P<day>\d{1,2})\s+(?P<month_name>ocak|şubat|subat|mart|nisan|mayıs|mayis|haziran|"
        r"temmuz|ağustos|agustos|eylül|eylul|ekim|kasım|kasim|aralık|aralik)\s+(?P<year>\d{4})"
        r".{0,40}?son\s+(?:ba[sş]vuru|teklif)",
        re.IGNORECASE | re.DOTALL,
    ),
]

_MONTHS = {
    "ocak": 1, "şubat": 2, "subat": 2, "mart": 3, "nisan": 4,
    "mayıs": 5, "mayis": 5, "haziran": 6, "temmuz": 7,
    "ağustos": 8, "agustos": 8, "eylül": 9, "eylul": 9,
    "ekim": 10, "kasım": 11, "kasim": 11, "aralık": 12, "aralik": 12,
}


def find_deadline(text: str) -> str | None:
    """Metinden son başvuru tarihini ISO-8601 olarak çıkarmaya çalışır.

    AI kural çıkarımı da tarihi döner; bu fonksiyon ucuz ve deterministik bir ön kontroldür.
    İkisi çeliştiğinde danışman onayı ekranında fark gösterilir.
    """
    for pattern in _DEADLINE_PATTERNS:
        match = pattern.search(text)
        if not match:
            continue

        groups = match.groupdict()
        year = int(groups["year"])
        day = int(groups["day"])
        month = (
            _MONTHS.get(groups["month_name"].lower())
            if groups.get("month_name")
            else int(groups["month"])
        )

        if month is None or not (1 <= month <= 12) or not (1 <= day <= 31):
            continue

        return f"{year:04d}-{month:02d}-{day:02d}T23:59:59+03:00"

    return None
