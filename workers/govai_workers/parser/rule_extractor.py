"""Kural çıkarımı (Ar-Ge çekirdeği).

İki katmanlı çalışır:

1. **Deterministik ön çıkarım** — "asgari 10 çalışan", "%30 kadın istihdamı", "TR62 bölgesi"
   gibi kalıplar düzenli ifadelerle yakalanır. Bu kurallar 1.0 güvenle üretilir, çünkü
   metinde birebir yazılıdır.
2. **LLM çıkarımı** — geri kalan serbest metin, alan adı beyaz listesiyle kısıtlanmış bir
   prompt ile modele verilir. Model yalnızca izin verilen alanları kullanabilir; liste dışına
   çıkan kurallar sessizce elenir.

İkisi birleştirilir, çakışmada deterministik kural kazanır. Sonuç her hâlükârda danışman
onayına düşer — hiçbir kural doğrudan üretime girmez.
"""

from __future__ import annotations

import json
import re
from dataclasses import asdict, dataclass, field
from typing import Any

from govai_workers.config import settings
from govai_workers.logging_setup import get_logger
from govai_workers.parser.sektor import sektor_bul

log = get_logger(__name__)

# .NET tarafındaki CompanyFieldResolver.SupportedFields ile eşleşmelidir.
ALLOWED_FIELDS: dict[str, str] = {
    "Company.LegalType": "Hukuki yapı",
    "Company.Size": "KOBİ ölçeği",
    "Company.AgeInYears": "Kuruluştan bu yana geçen yıl",
    "Company.ExportFlag": "İhracat yapıyor mu",
    "Company.TechnologyFlag": "Teknoloji/Ar-Ge merkezi statüsü",
    "Company.IsInTechnopark": "Teknoparkta yerleşik mi",
    "Company.PreviousSuccessfulApplications": "Geçmişte kabul almış başvuru sayısı",
    "Company.NaceCodes": "NACE kodları",
    "Company.Certificates": "Geçerli sertifika kodları",
    "Company.Cities": "Faaliyet illeri",
    "Company.Nuts2Codes": "İstatistiki bölge kodları",
    "Workforce.EmployeeCount": "Toplam çalışan sayısı",
    "Workforce.WomenEmployeeCount": "Kadın çalışan sayısı",
    "Workforce.WomenEmployeeRate": "Kadın çalışan oranı (0..1)",
    "Workforce.YoungEmployeeCount": "Genç çalışan sayısı (yaş sınırı firmanın beyanına göre)",
    "Workforce.YoungEmployeeMaxAge": "Firmanın genç çalışan sayarken kullandığı azami yaş",
    "Workforce.YoungEmployeeRate": "Genç çalışan oranı (0..1)",
    "Workforce.RAndDEmployeeCount": "Ar-Ge personeli sayısı",
    "Workforce.RAndDEmployeeRate": "Ar-Ge personeli oranı (0..1)",
    "Workforce.DisabledEmployeeCount": "Engelli çalışan sayısı",
    "Financials.AnnualRevenue": "Yıllık ciro",
    "Financials.BalanceSize": "Bilanço büyüklüğü",
    "Financials.Equity": "Özkaynak",
    "Financials.ExportRevenue": "İhracat cirosu",
    "Financials.ExportRatio": "İhracatın ciroya oranı (0..1)",
    "Financials.FiscalYear": "Mali verinin yılı",
    "Financials.AnnualIncome": "Yıllık gelir",
    "Financials.AnnualExpense": "Yıllık gider",
    "Financials.NetProfitOrLoss": "Net kâr/zarar",
}

VALID_OPERATORS = {
    "Equals", "NotEquals", "GreaterThan", "GreaterThanOrEqual", "LessThan",
    "LessThanOrEqual", "In", "NotIn", "ContainsAll", "ContainsAny", "NaceMatch",
    "IsTrue", "IsFalse",
}

VALID_DIMENSIONS = {
    "Sector", "Financial", "Employment", "Documentation", "Region",
    "TechnicalQualification", "Timing",
}

VALID_SEVERITIES = {"Blocking", "Major", "Minor", "Bonus"}


@dataclass(slots=True)
class ExtractedRule:
    field: str
    operator: str
    value: str
    dimension: str
    severity: str
    humanReadable: str  # noqa: N815 - API sözleşmesi camelCase
    sourceExcerpt: str | None = None  # noqa: N815
    confidence: float = 0.5
    #: Koşulun belge metnindeki karakter aralığı.
    #:
    #: Worker kanıt parçalarının kimliklerini bilmez — parçalar API tarafında
    #: oluşturulur. Bu yüzden koşul ile kanıt arasındaki bağ sunucuda, aralık
    #: çakışmasıyla kurulur. ``None`` ise kural kanıtsız kalır: deterministik motor
    #: onu kullanmaya devam eder, yapay zekâ o kural için resmî kaynağa dayalı iddia
    #: üretemez.
    startOffset: int | None = None  # noqa: N815
    endOffset: int | None = None  # noqa: N815


@dataclass(slots=True)
class ExtractionResult:
    rules: list[ExtractedRule] = field(default_factory=list)
    documents: list[dict[str, Any]] = field(default_factory=list)
    confidence: float = 0.0
    summary: str | None = None
    deadline: str | None = None
    detected_category: str | None = None


# ---------------------------------------------------------------------------
# 1. Deterministik kalıplar
# ---------------------------------------------------------------------------

_MIN_EMPLOYEE = re.compile(
    r"(?:asgari|en\s+az|minimum)\s+(?P<count>\d{1,5})\s*(?:adet\s+)?(?:sigortal[ıi]|çal[ıi][şs]an|personel)",
    re.IGNORECASE,
)

_MAX_EMPLOYEE = re.compile(
    r"(?:en\s+fazla|azami|maksimum)\s+(?P<count>\d{1,5})\s*(?:sigortal[ıi]|çal[ıi][şs]an|personel)",
    re.IGNORECASE,
)

_WOMEN_RATE = re.compile(
    r"kad[ıi]n\s+(?:çal[ıi][şs]an|istihdam|personel)\w*\s*(?:oran[ıi])?\s*"
    r"(?:en\s+az|asgari|minimum)?\s*%\s*(?P<rate>\d{1,3})",
    re.IGNORECASE,
)

_NUTS2 = re.compile(r"\bTR[0-9]{1,2}[0-9A-Z]?\b")

_ISO_CERT = re.compile(r"\bISO\s*[-]?\s*(?P<number>9001|14001|27001|45001|50001)\b", re.IGNORECASE)


def extract_deterministic(text: str, title: str = "") -> list[ExtractedRule]:
    """Metinde birebir yazan sayısal/kategorik koşulları çıkarır.

    ``title`` sektör çıkarımı için gerekir: ilanın konusu başlıkta yazar, gövdede geçen
    aynı kelime çoğu zaman adres veya idarenin adıdır.
    """
    rules: list[ExtractedRule] = []

    if match := _MIN_EMPLOYEE.search(text):
        rules.append(ExtractedRule(
            field="Workforce.EmployeeCount",
            operator="GreaterThanOrEqual",
            value=match.group("count"),
            dimension="Employment",
            severity="Blocking",
            humanReadable=f"Asgari {match.group('count')} çalışan şartı.",
            sourceExcerpt=_excerpt(text, match.start(), match.end()),
            confidence=1.0,
            startOffset=match.start(),
            endOffset=match.end(),
        ))

    if match := _MAX_EMPLOYEE.search(text):
        rules.append(ExtractedRule(
            field="Workforce.EmployeeCount",
            operator="LessThanOrEqual",
            value=match.group("count"),
            dimension="Employment",
            severity="Blocking",
            humanReadable=f"En fazla {match.group('count')} çalışan şartı.",
            sourceExcerpt=_excerpt(text, match.start(), match.end()),
            confidence=1.0,
            startOffset=match.start(),
            endOffset=match.end(),
        ))

    if match := _WOMEN_RATE.search(text):
        rate = int(match.group("rate")) / 100
        rules.append(ExtractedRule(
            field="Workforce.WomenEmployeeRate",
            operator="GreaterThanOrEqual",
            value=f"{rate:.2f}",
            dimension="Employment",
            severity="Major",
            humanReadable=f"Kadın çalışan oranı en az %{match.group('rate')} olmalıdır.",
            sourceExcerpt=_excerpt(text, match.start(), match.end()),
            confidence=0.9,
            startOffset=match.start(),
            endOffset=match.end(),
        ))

    if regions := sorted({m.group(0).upper() for m in _NUTS2.finditer(text)}):
        rules.append(ExtractedRule(
            field="Company.Nuts2Codes",
            operator="ContainsAny",
            value=",".join(regions),
            dimension="Region",
            severity="Blocking",
            humanReadable=(
                f"Firma şu bölgelerden birinde faaliyet göstermelidir: {', '.join(regions)}."
            ),
            sourceExcerpt=_excerpt(text, *_first_span(_NUTS2, text)),
            confidence=0.85,
            startOffset=_first_span(_NUTS2, text)[0],
            endOffset=_first_span(_NUTS2, text)[1],
        ))

    if certs := sorted({f"ISO{m.group('number')}" for m in _ISO_CERT.finditer(text)}):
        rules.append(ExtractedRule(
            field="Company.Certificates",
            operator="ContainsAll",
            value=",".join(certs),
            dimension="Documentation",
            severity="Minor",
            humanReadable=f"Şu belgeler puanlamada dikkate alınır: {', '.join(certs)}.",
            sourceExcerpt=_excerpt(text, *_first_span(_ISO_CERT, text)),
            confidence=0.8,
            startOffset=_first_span(_ISO_CERT, text)[0],
            endOffset=_first_span(_ISO_CERT, text)[1],
        ))

    # Sektör kuralı en sona eklenir; diğer kalıplar metinde birebir yazan koşullardır,
    # bu ise ilanın KONUSUNDAN çıkarımdır. Ayrımı severity ve confidence taşır:
    # engelleyici değil "Major", güven 1.0 değil 0.7. Sektör tanınmazsa kural üretilmez —
    # motor o hâlde boyutu "doğrulanamadı" sayar, tam puan vermez.
    if sektor := sektor_bul(title, text):
        rules.append(ExtractedRule(
            field="Company.NaceCodes",
            operator="NaceMatch",
            value=",".join(sektor.nace),
            dimension="Sector",
            severity="Major",
            humanReadable=(
                f"İlanın konusu {sektor.ad.lower()} alanındadır; "
                "firmanın NACE kodları bu alanla uyumlu olmalıdır."
            ),
            sourceExcerpt=title.strip() or None,
            confidence=0.7,
        ))

    return rules


def _first_span(pattern: re.Pattern[str], text: str) -> tuple[int, int]:
    match = pattern.search(text)
    return (match.start(), match.end()) if match else (0, 0)


def _excerpt(text: str, start: int, end: int, window: int = 160) -> str:
    left = max(0, start - window)
    right = min(len(text), end + window)
    return text[left:right].strip()


# ---------------------------------------------------------------------------
# 2. LLM çıkarımı
# ---------------------------------------------------------------------------

_SYSTEM_PROMPT = """Sen Türkiye'deki resmî teşvik, hibe ve ihale çağrılarını
analiz eden bir uzmansın. Görevin, verilen çağrı metninden makine tarafından
değerlendirilebilir başvuru koşullarını çıkarmaktır.

Kurallar:
- YALNIZCA sana verilen alan adlarını kullan. Listede olmayan alan adı ÜRETME.
- operator: {operators}
- dimension: {dimensions}
- severity: {severities} (Blocking = sağlanmazsa başvuru reddedilir)
- Sayıları nokta ondalık ayracıyla, binlik ayraç olmadan yaz.
- Oranları 0..1 aralığında yaz (%30 -> 0.30).
- Liste değerlerini virgülle ayır.
- sourceExcerpt alanına koşulun dayandığı cümleyi birebir koy.
- Metinde açıkça yazmayan bir koşulu UYDURMA. Emin değilsen ekleme veya confidence düşür.
- Yanıtını yalnızca geçerli JSON olarak ver."""


def extract_with_llm(title: str, text: str) -> ExtractionResult:
    """LLM ile serbest metinden koşul çıkarır. API anahtarı yoksa boş sonuç döner."""
    if not settings.rule_extraction_enabled or not settings.openai_api_key:
        log.info("llm_extraction_disabled")
        return ExtractionResult()

    try:
        from openai import OpenAI

        client = OpenAI(api_key=settings.openai_api_key)
        field_list = "\n".join(f"- {name}: {desc}" for name, desc in ALLOWED_FIELDS.items())

        system_prompt = _SYSTEM_PROMPT.format(
            operators=", ".join(sorted(VALID_OPERATORS)),
            dimensions=", ".join(sorted(VALID_DIMENSIONS)),
            severities=", ".join(sorted(VALID_SEVERITIES)),
        )

        schema = (
            '{"summary": "...", "detectedCategory": "...", '
            '"deadline": "ISO tarih veya null", "confidence": 0.0, '
            '"rules": [{"field":"","operator":"","value":"","dimension":"",'
            '"severity":"","humanReadable":"","sourceExcerpt":"","confidence":0.0}], '
            '"documents": [{"code":"","name":"","isMandatory":true,"issuingAuthority":""}]}'
        )

        user_prompt = (
            f"İzin verilen alanlar:\n{field_list}\n\n"
            f"Çağrı başlığı: {title}\n\n"
            f"Çağrı metni:\n{text[:40000]}\n\n"
            f"JSON şeması: {schema}"
        )

        response = client.chat.completions.create(
            model=settings.openai_extraction_model,
            temperature=0,
            response_format={"type": "json_object"},
            messages=[
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt},
            ],
        )

        payload = json.loads(response.choices[0].message.content or "{}")
        return _parse_llm_payload(payload)

    except Exception:
        log.exception("llm_extraction_failed")
        return ExtractionResult()


def _parse_llm_payload(payload: dict[str, Any]) -> ExtractionResult:
    rules: list[ExtractedRule] = []
    dropped = 0

    for raw in payload.get("rules", []):
        if not _is_valid_rule(raw):
            dropped += 1
            continue

        rules.append(ExtractedRule(
            field=raw["field"],
            operator=raw["operator"],
            value=str(raw["value"]),
            dimension=raw["dimension"],
            severity=raw["severity"],
            humanReadable=raw.get("humanReadable", raw["field"]),
            sourceExcerpt=raw.get("sourceExcerpt"),
            confidence=float(raw.get("confidence", 0.5)),
        ))

    if dropped:
        log.warning("llm_rules_dropped", count=dropped, reason="beyaz liste dışı veya eksik alan")

    return ExtractionResult(
        rules=rules,
        documents=[d for d in payload.get("documents", []) if d.get("code")],
        confidence=float(payload.get("confidence", 0.0)),
        summary=payload.get("summary"),
        deadline=payload.get("deadline"),
        detected_category=payload.get("detectedCategory"),
    )


def _is_valid_rule(raw: dict[str, Any]) -> bool:
    return (
        raw.get("field") in ALLOWED_FIELDS
        and raw.get("operator") in VALID_OPERATORS
        and raw.get("dimension") in VALID_DIMENSIONS
        and raw.get("severity") in VALID_SEVERITIES
        and raw.get("value") is not None
    )


# ---------------------------------------------------------------------------
# 3. Birleştirme
# ---------------------------------------------------------------------------


def extract_rules(title: str, text: str) -> ExtractionResult:
    """Deterministik ve LLM kurallarını birleştirir; çakışmada deterministik kural kazanır."""
    deterministic = extract_deterministic(text, title)
    llm = extract_with_llm(title, text)

    taken = {(rule.field, rule.operator) for rule in deterministic}
    merged = list(deterministic)
    merged.extend(rule for rule in llm.rules if (rule.field, rule.operator) not in taken)

    # Genel güven: deterministik kuralların payı arttıkça güven artar.
    overall = sum(rule.confidence for rule in merged) / len(merged) if merged else 0.0

    from govai_workers.parser.extractors import find_deadline

    return ExtractionResult(
        rules=merged,
        documents=llm.documents,
        confidence=round(min(overall, 1.0), 4),
        summary=llm.summary,
        deadline=llm.deadline or find_deadline(text),
        detected_category=llm.detected_category,
    )


def rules_to_payload(rules: list[ExtractedRule]) -> list[dict[str, Any]]:
    return [asdict(rule) for rule in rules]
