"""EUR-Lex / CELLAR resmî makine erişimi.

**Neden ayrı bir toplayıcı var:** ``eur-lex.europa.eu`` tarayıcı arayüzü otomatik
isteklere boş gövdeli ``HTTP 202`` döndürüyor (bot koruması). Bu koruma
**aşılmıyor**. Bunun yerine AB Yayın Ofisi'nin makine erişimi için resmî olarak
duyurduğu yol kullanılıyor:

* **SPARQL ucu** — ``https://publications.europa.eu/webapi/rdf/sparql``
  Künye (CELEX, başlık, belge tarihi, yürürlük tarihi, belge türü) buradan gelir.
* **CELLAR REST** — ``https://publications.europa.eu/resource/celex/{CELEX}``
  Belgenin metni içerik pazarlığıyla (``Accept: application/xhtml+xml``) buradan alınır.

Kaynak: EUR-Lex "Technical information" ve Publications Office CELLAR sayfaları.

Kurallar:

* Alan hiçbir koşulda uydurulmaz. Yürürlük tarihi CELLAR'da yoksa ``None`` kalır ve
  sunucu tarafında ``NotProvided`` olarak işaretlenir.
* Kanonik adres, kullanıcıya gösterilecek **resmî EUR-Lex adresidir**; içeriğin
  indirildiği CELLAR adresi ayrıca kaydedilir.
* Sorgu her zaman ``LIMIT`` ile çalışır (uçta 60 saniyelik zaman aşımı vardır).
"""

from __future__ import annotations

import json
import re
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import Any

from govai_workers.api_client import GovAiClient
from govai_workers.config import settings
from govai_workers.logging_setup import get_logger

log = get_logger(__name__)

#: AB Yayın Ofisi'nin resmî SPARQL ucu.
SPARQL_UCU = "https://publications.europa.eu/webapi/rdf/sparql"

#: Belge metninin alındığı resmî REST ucu.
CELLAR_UCU = "https://publications.europa.eu/resource/celex"

#: Kullanıcıya gösterilen resmî EUR-Lex adresi.
EURLEX_ADRESI = "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:"

#: Bir turda alınacak en fazla belge. Uçta 60 sn zaman aşımı var; küçük tutulur.
VARSAYILAN_LIMIT = 5

_SORGU = """
PREFIX cdm: <http://publications.europa.eu/ontology/cdm#>

SELECT DISTINCT ?celex ?baslik ?belgeTarihi ?yururluk ?tur WHERE {{
  ?work cdm:resource_legal_id_celex ?celex .
  ?work cdm:work_date_document ?belgeTarihi .
  ?expr cdm:expression_belongs_to_work ?work .
  ?expr cdm:expression_uses_language
        <http://publications.europa.eu/resource/authority/language/ENG> .
  ?expr cdm:expression_title ?baslik .
  OPTIONAL {{ ?work cdm:resource_legal_date_entry-into-force ?yururluk }}
  OPTIONAL {{ ?work cdm:work_has_resource-type ?tur }}
  FILTER(STRSTARTS(STR(?celex), "{onek}"))
  FILTER(!CONTAINS(STR(?celex), "R("))
}}
ORDER BY DESC(?belgeTarihi)
LIMIT {limit}
"""


@dataclass(frozen=True)
class AbMevzuati:
    """CELLAR'dan gelen tek bir AB mevzuat kaydının künyesi."""

    celex: str
    baslik: str
    belge_tarihi: str
    #: Belgede yazmıyorsa None kalır — tahmin edilmez.
    yururluk_tarihi: str | None
    belge_turu: str | None

    @property
    def resmi_adres(self) -> str:
        return f"{EURLEX_ADRESI}{self.celex}"

    @property
    def cellar_adresi(self) -> str:
        return f"{CELLAR_UCU}/{self.celex}"


def _basliklar() -> dict[str, str]:
    return {"User-Agent": settings.crawl_user_agent}


def kunye_listele(onek: str = "32026R", limit: int = VARSAYILAN_LIMIT) -> list[AbMevzuati]:
    """Resmî SPARQL ucundan mevzuat künyelerini çeker.

    ``onek`` bir CELEX önekidir: ``32026R`` = 2026 yılı yönetmelikleri (regulations),
    ``32026L`` direktifler, ``32026D`` kararlar.
    """
    sorgu = _SORGU.format(onek=onek, limit=limit)
    veri = urllib.parse.urlencode({"query": sorgu, "format": "application/sparql-results+json"})

    istek = urllib.request.Request(
        f"{SPARQL_UCU}?{veri}",
        headers={"Accept": "application/sparql-results+json", **_basliklar()},
    )

    with urllib.request.urlopen(istek, timeout=90) as yanit:  # noqa: S310 — sabit resmî uç
        govde = json.loads(yanit.read().decode("utf-8"))

    kayitlar: dict[str, AbMevzuati] = {}

    for satir in govde["results"]["bindings"]:
        celex = satir["celex"]["value"]

        # Aynı CELEX birden çok yürürlük satırıyla dönebilir; ilki (en yenisi) alınır.
        if celex in kayitlar:
            continue

        tur = satir.get("tur", {}).get("value")

        kayitlar[celex] = AbMevzuati(
            celex=celex,
            baslik=satir["baslik"]["value"],
            belge_tarihi=satir["belgeTarihi"]["value"],
            yururluk_tarihi=satir.get("yururluk", {}).get("value"),
            belge_turu=tur.rsplit("/", 1)[-1] if tur else None,
        )

    log.info("eurlex_kunye_alindi", onek=onek, adet=len(kayitlar))
    return list(kayitlar.values())


#: Resmî EUR-Lex adresinden CELEX numarasını çıkarır.
_CELEX_KALIBI = re.compile(r"[?&]uri=CELEX:([0-9A-Z()]+)", re.IGNORECASE)


def celex_bul(adres: str) -> str | None:
    """Bu adres bir resmî EUR-Lex belge adresi mi? Öyleyse CELEX numarası.

    Ana bilgisayar adı **tam olarak** karşılaştırılır. Alt dize araması yapılsaydı
    ``eur-lex.europa.eu.saldirgan.example`` gibi benzeyen bir adres resmî sayılırdı.
    """
    host = (urllib.parse.urlparse(adres).hostname or "").lower().rstrip(".")

    if host != "eur-lex.europa.eu" and not host.endswith(".eur-lex.europa.eu"):
        return None

    eslesme = _CELEX_KALIBI.search(adres)
    return eslesme.group(1) if eslesme else None


def metin_indir_adresten(adres: str) -> tuple[bytes, str] | None:
    """Resmî EUR-Lex adresi için metni CELLAR'dan indirir.

    Ayrıştırıcı da bunu kullanır: EUR-Lex tarayıcı adresi otomatik isteklere boş
    gövdeli HTTP 202 döndürdüğü için genel indirici oradan içerik alamaz. Koruma
    aşılmaz; resmî makine erişim yoluna geçilir.
    """
    celex = celex_bul(adres)

    if celex is None:
        return None

    sonuc = _cellar_indir(f"{CELLAR_UCU}/{celex}", celex)
    return (sonuc[0].encode("utf-8"), sonuc[1]) if sonuc else None


def metin_indir(kayit: AbMevzuati) -> tuple[str, str] | None:
    """Belgenin metnini CELLAR'dan indirir. ``(içerik, mime)`` döner."""
    return _cellar_indir(kayit.cellar_adresi, kayit.celex)


def _cellar_indir(adres: str, celex: str) -> tuple[str, str] | None:
    for kabul in ("application/xhtml+xml", "text/html", "application/xml"):
        istek = urllib.request.Request(
            adres,
            headers={"Accept": kabul, "Accept-Language": "eng", **_basliklar()},
        )

        try:
            with urllib.request.urlopen(istek, timeout=120) as yanit:  # noqa: S310
                ham = yanit.read()
        except urllib.error.HTTPError as hata:
            log.debug("eurlex_icerik_reddedildi", celex=celex, kabul=kabul, kod=hata.code)
            continue

        if not ham:
            continue

        if len(ham) > settings.crawl_max_document_bytes:
            log.warning("eurlex_belge_cok_buyuk", celex=celex, bayt=len(ham))
            return None

        return ham.decode("utf-8", "replace"), kabul.split(";")[0]

    log.warning("eurlex_icerik_alinamadi", celex=celex)
    return None


def topla(
    client: GovAiClient,
    source_id: str,
    onek: str = "32026R",
    limit: int = VARSAYILAN_LIMIT,
) -> dict[str, int]:
    """Künyeleri ve metinleri alıp belge olarak sisteme bırakır."""
    alinan = 0
    atlanan = 0

    for kayit in kunye_listele(onek, limit):
        icerik = metin_indir(kayit)

        if icerik is None:
            atlanan += 1
            continue

        metin, mime = icerik

        sonuc: dict[str, Any] = client.ingest_document(
            source_id=source_id,
            # Kullanıcıya gösterilecek adres resmî EUR-Lex adresidir.
            url=kayit.resmi_adres,
            canonical_url=kayit.resmi_adres,
            title=kayit.baslik,
            raw_content=metin,
            media_type=mime,
            charset="utf-8",
            http_status_code=200,
        )

        alinan += 1
        log.info(
            "eurlex_belge_kaydedildi",
            celex=kayit.celex,
            document_id=sonuc.get("documentId"),
            yururluk=kayit.yururluk_tarihi or "belirtilmemiş",
        )

    return {"alinan": alinan, "atlanan": atlanan}
