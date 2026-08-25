"""Kanıt parçası üretimi.

DeepTech motoru ileride bir iddiada bulunduğunda "bu cümle şu belgenin şu sürümünün şu
paragrafında geçiyor" diyebilmelidir. Bunun için ayrıştırılmış metin, konumu korunarak
parçalara bölünür.

Kurallar:

* Parça metni **yalnızca** ayrıştırıcının ürettiği normalize metinden gelir. Yapay zekâ
  üretimi hiçbir metin kanıt olarak kaydedilmez.
* Boş, çok kısa veya yalnızca noktalama/sayı içeren parçalar atılır — kanıt sayılmazlar.
* ``start_offset``/``end_offset`` normalize metnin karakter aralığını gösterir; parça
  metnine bakılarak kaynağa geri gidilebilir.
* PDF'lerde sayfa numarası korunur.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

#: Bir parçanın kanıt sayılması için gereken en az karakter.
MIN_CHUNK_LENGTH = 30

#: Bir parçanın bölünmeden önce ulaşabileceği en fazla karakter.
MAX_CHUNK_LENGTH = 2000

#: Sayfa ayracı; PDF çıkarımı sayfaları bu işaretle ayırır.
PAGE_BREAK = "\f"

#: Başlık gibi duran satırlar: kısa, cümle noktalamasıyla BİTMEYEN, az kelimeli.
#: Kural bilinçli olarak dar: bir paragrafı yanlışlıkla başlık sayarsak o metin
#: kanıt parçası olmaz ve kaynağa geri gösterilemez.
_HEADING_MAX_LENGTH = 80
_HEADING_MAX_WORDS = 8
_SENTENCE_END = (".", "!", "?", ";")

#: Anlamlı sayılmayan parçalar: yalnızca sayı, noktalama veya tek kelime.
_MEANINGLESS = re.compile(r"^[\W\d_]+$", re.UNICODE)


@dataclass(frozen=True, slots=True)
class EvidenceChunk:
    sequence_number: int
    text: str
    start_offset: int
    end_offset: int
    page_number: int | None = None
    section_title: str | None = None
    paragraph_number: int | None = None

    def to_payload(self) -> dict[str, object]:
        return {
            "sequenceNumber": self.sequence_number,
            "text": self.text,
            "startOffset": self.start_offset,
            "endOffset": self.end_offset,
            "pageNumber": self.page_number,
            "sectionTitle": self.section_title,
            "paragraphNumber": self.paragraph_number,
        }


def _looks_like_heading(text: str) -> bool:
    """Tek satırlık, kısa ve cümle gibi bitmeyen metin bölüm başlığı sayılır."""
    if "\n" in text or len(text) > _HEADING_MAX_LENGTH:
        return False

    if text.endswith(_SENTENCE_END):
        return False

    return len(text.split()) <= _HEADING_MAX_WORDS


def _is_meaningful(text: str) -> bool:
    stripped = text.strip()

    if len(stripped) < MIN_CHUNK_LENGTH:
        return False

    if _MEANINGLESS.match(stripped):
        return False

    # En az iki kelime olmayan bir parça alıntılanabilir bir kanıt değildir.
    return len(stripped.split()) >= 3


def _split_long(text: str, start: int) -> list[tuple[str, int, int]]:
    """Çok uzun paragrafı cümle sınırlarında böler; konum bilgisi korunur."""
    if len(text) <= MAX_CHUNK_LENGTH:
        return [(text, start, start + len(text))]

    pieces: list[tuple[str, int, int]] = []
    cursor = 0

    while cursor < len(text):
        window = text[cursor : cursor + MAX_CHUNK_LENGTH]

        if cursor + MAX_CHUNK_LENGTH < len(text):
            # Cümle sonunda kesmeye çalış; bulunamazsa boşlukta kes.
            boundary = max(window.rfind(". "), window.rfind("? "), window.rfind("! "))
            if boundary < MIN_CHUNK_LENGTH:
                boundary = window.rfind(" ")
            if boundary < MIN_CHUNK_LENGTH:
                boundary = len(window) - 1
            window = window[: boundary + 1]

        pieces.append((window, start + cursor, start + cursor + len(window)))
        cursor += len(window)

    return pieces


def build_chunks(normalized_text: str) -> list[EvidenceChunk]:
    """Normalize metni konumu korunmuş kanıt parçalarına böler.

    Sayfa ayracı (``\\f``) varsa sayfa numarası izlenir. Kısa ve başlık gibi duran
    satırlar bölüm başlığı olarak taşınır, ayrı kanıt parçası yapılmaz.
    """
    if not normalized_text or not normalized_text.strip():
        return []

    chunks: list[EvidenceChunk] = []
    sequence = 0
    section_title: str | None = None
    paragraph_number = 0

    has_pages = PAGE_BREAK in normalized_text
    page_offset = 0

    # Önce sayfalara, sonra paragraflara bölünür: sayfa numarası böylece kesin kalır.
    for page_index, page_text in enumerate(normalized_text.split(PAGE_BREAK), start=1):
        page_number = page_index if has_pages else None
        offset = 0

        for block in re.split(r"\n{2,}", page_text):
            block_start = page_text.find(block, offset)
            if block_start == -1:
                block_start = offset
            offset = block_start + len(block)

            cleaned = block.strip()
            if not cleaned:
                continue

            # Başlık satırları bölümü adlandırır; kendileri kanıt parçası olmaz.
            if _looks_like_heading(cleaned):
                section_title = cleaned
                paragraph_number = 0
                continue

            for piece, start, end in _split_long(cleaned, page_offset + block_start):
                if not _is_meaningful(piece):
                    continue

                paragraph_number += 1
                chunks.append(
                    EvidenceChunk(
                        sequence_number=sequence,
                        text=piece.strip(),
                        start_offset=start,
                        end_offset=end,
                        page_number=page_number,
                        section_title=section_title,
                        paragraph_number=paragraph_number,
                    )
                )
                sequence += 1

        # Sonraki sayfanın konumları tam metne göre hesaplanır (+1 form-feed).
        page_offset += len(page_text) + 1

    return chunks
