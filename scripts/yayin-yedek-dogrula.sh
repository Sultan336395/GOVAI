#!/usr/bin/env bash
#
# Yedeği GERÇEKTEN geri yükleyerek doğrular.
#
# NEDEN: Alınmış ama geri yüklenebildiği kanıtlanmamış bir yedek, yedek değildir.
# Bozuk bir dump ancak felaket anında fark edilir ve o an çok geçtir.
#
# NASIL: Her iki biçim AYRI birer GEÇİCİ veritabanına yüklenir, tablo listesi ve
# satır sayıları canlı veritabanıyla karşılaştırılır, sonra YALNIZCA bu iki geçici
# veritabanı düşürülür.
#
# NE YAPMAZ:
#   * Canlı veritabanına HİÇBİR ŞEY yazmaz; yalnızca okur ve sayar.
#   * Container durdurmaz, yeniden başlatmaz, silmez.
#   * Kendi oluşturmadığı hiçbir veritabanını düşürmez. Geçici adlar
#     "_dogrulama_test_" öneki taşır ve düşürme yalnızca bu deseni kabul eder.
#
# KULLANIM:
#   bash scripts/yayin-yedek-dogrula.sh /opt/govai/yedek/yayin-oncesi-20260908-120000

set -euo pipefail

CONTAINER="${GOVAI_PG_CONTAINER:-govai-postgres-1}"
TEMEL="${1:?Yedek dosyalarının ortak ön eki verilmeli (uzantısız).}"

DUMP="${TEMEL}.dump"
SQL="${TEMEL}.sql"
OZET="${TEMEL}.sha256"

# Geçici veritabanı adları: test amacı taşıdıkları adlarından okunur.
DAMGA="$(date +%Y%m%d%H%M%S)"
DB_CUSTOM="govai_dogrulama_test_c_${DAMGA}"
DB_PLAIN="govai_dogrulama_test_p_${DAMGA}"

hata() { echo "HATA: $*" >&2; exit 1; }

for dosya in "$DUMP" "$SQL"; do
  [ -s "$dosya" ] || hata "Yedek dosyası yok ya da boş: $dosya"
done

PROJE="$(docker inspect -f '{{ index .Config.Labels "com.docker.compose.project" }}' "$CONTAINER")"
[ "$PROJE" = "govai" ] || hata "Container '$CONTAINER' govai projesine ait değil. Durduruldu."

PGUSER="$(docker exec "$CONTAINER" printenv POSTGRES_USER)"
PGDB="$(docker exec "$CONTAINER" printenv POSTGRES_DB)"

psql_calistir() {
  docker exec -i "$CONTAINER" psql -U "$PGUSER" -d "$1" -tAq -v ON_ERROR_STOP=1
}

# Ancak kendi ürettiğimiz desendeki adlar düşürülebilir. Bu koruma olmadan bir
# yazım hatası canlı veritabanını düşürebilirdi.
dusur() {
  local ad="$1"

  case "$ad" in
    govai_dogrulama_test_*) ;;
    *) hata "Güvenlik kilidi: '$ad' geçici doğrulama veritabanı deseninde değil, düşürülmedi." ;;
  esac

  docker exec "$CONTAINER" psql -U "$PGUSER" -d postgres -q \
    -c "DROP DATABASE IF EXISTS \"$ad\";" >/dev/null
}

temizle() {
  echo
  echo "Geçici veritabanları düşürülüyor…"
  dusur "$DB_CUSTOM"
  dusur "$DB_PLAIN"
  echo "Temizlik tamam. Canlı veritabanına dokunulmadı."
}

trap temizle EXIT

echo "1/5 SHA-256 doğrulanıyor…"
if [ -s "$OZET" ]; then
  (cd "$(dirname "$TEMEL")" && sha256sum -c "$(basename "$OZET")") || hata "Özet tutmuyor; yedek bozulmuş."
else
  echo "  UYARI: özet dosyası yok, atlanıyor."
fi

echo "2/5 geçici veritabanları oluşturuluyor…"
docker exec "$CONTAINER" psql -U "$PGUSER" -d postgres -q -c "CREATE DATABASE \"$DB_CUSTOM\";" >/dev/null
docker exec "$CONTAINER" psql -U "$PGUSER" -d postgres -q -c "CREATE DATABASE \"$DB_PLAIN\";" >/dev/null

echo "3/5 custom biçim geri yükleniyor → ${DB_CUSTOM}"
docker exec -i "$CONTAINER" pg_restore -U "$PGUSER" -d "$DB_CUSTOM" --no-owner --no-privileges < "$DUMP" \
  || hata "custom biçim geri yüklenemedi."

echo "4/5 düz SQL geri yükleniyor → ${DB_PLAIN}"
docker exec -i "$CONTAINER" psql -U "$PGUSER" -d "$DB_PLAIN" -q -v ON_ERROR_STOP=1 < "$SQL" >/dev/null \
  || hata "düz SQL geri yüklenemedi."

echo "5/5 sayımlar karşılaştırılıyor…"

TABLO_SORGU="SELECT count(*) FROM information_schema.tables WHERE table_schema='govai';"

SATIR_SORGU="
SELECT string_agg(satir, E'\n' ORDER BY satir) FROM (
  SELECT format('%s=%s', c.relname, c.reltuples::bigint) AS satir
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
  WHERE n.nspname = 'govai' AND c.relkind = 'r'
) t;"

# reltuples tahminidir; kesin sayım için tabloların kendisi sayılır.
kesin_sayim() {
  docker exec -i "$CONTAINER" psql -U "$PGUSER" -d "$1" -tAq -v ON_ERROR_STOP=1 <<'SQL'
SELECT string_agg(format('%s=%s', tablo, adet), E'\n' ORDER BY tablo)
FROM (
  SELECT c.relname AS tablo,
         (xpath('/row/c/text()',
                query_to_xml(format('SELECT count(*) AS c FROM govai.%I', c.relname),
                             false, true, '')))[1]::text::bigint AS adet
  FROM pg_class c
  JOIN pg_namespace n ON n.oid = c.relnamespace
  WHERE n.nspname = 'govai' AND c.relkind = 'r'
) s;
SQL
}

CANLI_TABLO="$(echo "$TABLO_SORGU" | psql_calistir "$PGDB")"
CUSTOM_TABLO="$(echo "$TABLO_SORGU" | psql_calistir "$DB_CUSTOM")"
PLAIN_TABLO="$(echo "$TABLO_SORGU" | psql_calistir "$DB_PLAIN")"

echo "  Tablo sayısı — canlı: ${CANLI_TABLO}, custom: ${CUSTOM_TABLO}, düz: ${PLAIN_TABLO}"

[ "$CANLI_TABLO" = "$CUSTOM_TABLO" ] || hata "custom yedekte tablo sayısı tutmuyor."
[ "$CANLI_TABLO" = "$PLAIN_TABLO" ] || hata "düz yedekte tablo sayısı tutmuyor."

CANLI_SAYIM="$(kesin_sayim "$PGDB")"
CUSTOM_SAYIM="$(kesin_sayim "$DB_CUSTOM")"
PLAIN_SAYIM="$(kesin_sayim "$DB_PLAIN")"

if [ "$CANLI_SAYIM" != "$CUSTOM_SAYIM" ]; then
  echo "--- canlı ile custom farkı ---"
  diff <(echo "$CANLI_SAYIM") <(echo "$CUSTOM_SAYIM") || true
  hata "custom yedekte satır sayıları tutmuyor."
fi

if [ "$CANLI_SAYIM" != "$PLAIN_SAYIM" ]; then
  echo "--- canlı ile düz SQL farkı ---"
  diff <(echo "$CANLI_SAYIM") <(echo "$PLAIN_SAYIM") || true
  hata "düz yedekte satır sayıları tutmuyor."
fi

echo
echo "Satır sayıları (canlı = custom = düz):"
echo "$CANLI_SAYIM" | sed 's/^/  /'

echo
echo "YEDEK DOĞRULANDI. Her iki biçim de geri yüklenebiliyor ve içerik birebir aynı."
