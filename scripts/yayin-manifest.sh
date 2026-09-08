#!/usr/bin/env bash
#
# Yayın öncesi/sonrası durum manifesti.
#
# NEDEN: "Yayından sonra bir şey mi bozuldu?" sorusu ancak yayından ÖNCEKİ sayılar
# yazılıysa cevaplanabilir. Bu betik iki kez çalıştırılır — yayından önce ve sonra —
# ve iki çıktı karşılaştırılır.
#
# NE YAPMAZ: Hiçbir şey yazmaz, değiştirmez, yeniden başlatmaz. Yalnızca okur.
#
# KULLANIM:
#   bash scripts/yayin-manifest.sh > /opt/govai/yedek/manifest-once.txt
#   # … yayın …
#   bash scripts/yayin-manifest.sh > /opt/govai/yedek/manifest-sonra.txt
#   diff /opt/govai/yedek/manifest-once.txt /opt/govai/yedek/manifest-sonra.txt

set -euo pipefail

CONTAINER="${GOVAI_PG_CONTAINER:-govai-postgres-1}"
API_URL="${GOVAI_API_URL:-http://127.0.0.1:8080}"

PGUSER="$(docker exec "$CONTAINER" printenv POSTGRES_USER)"
PGDB="$(docker exec "$CONTAINER" printenv POSTGRES_DB)"

sorgu() {
  docker exec -i "$CONTAINER" psql -U "$PGUSER" -d "$PGDB" -tAq -v ON_ERROR_STOP=1
}

echo "# GOVAI durum manifesti"
echo "# Üretildi: $(date -Is)"
echo

echo "## Çalışan govai container'ları ve imaj digest'leri"
# YALNIZCA govai projesi. Sunucudaki diğer container'lar listelenmez bile.
docker ps --filter "label=com.docker.compose.project=govai" --format '{{.Names}}\t{{.Image}}\t{{.Status}}' \
  | sort \
  | while IFS=$'\t' read -r ad imaj durum; do
      digest="$(docker inspect -f '{{index .RepoDigests 0}}' "$ad" 2>/dev/null || echo '(digest yok — yerel imaj)')"
      printf '%-28s %-40s %s\n' "$ad" "$imaj" "$durum"
      printf '%-28s %s\n' "" "$digest"
    done
echo

echo "## Uygulanmış migration'lar"
echo 'SELECT migration_id FROM govai.__ef_migrations_history ORDER BY migration_id;' | sorgu | sed 's/^/  /'
echo
echo -n "  Toplam: "
echo 'SELECT count(*) FROM govai.__ef_migrations_history;' | sorgu
echo

echo "## Tablo ve satır sayıları"
sorgu <<'SQL' | sed 's/^/  /'
SELECT format('%-40s %s', c.relname,
              (xpath('/row/c/text()',
                     query_to_xml(format('SELECT count(*) AS c FROM govai.%I', c.relname),
                                  false, true, '')))[1]::text)
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'govai' AND c.relkind = 'r'
ORDER BY c.relname;
SQL
echo

echo "## Katalog sağlığı"
sorgu <<'SQL' | sed 's/^/  /'
SELECT format('%-46s %s', etiket, adet) FROM (
  SELECT 'Yayımlanabilir fırsat' AS etiket, count(*) AS adet, 1 AS s
    FROM govai.opportunities WHERE quarantine_reason = 0 AND is_deleted = false
  UNION ALL SELECT 'Karantinadaki fırsat', count(*), 2
    FROM govai.opportunities WHERE quarantine_reason <> 0
  UNION ALL SELECT 'Karantinadaki mevzuat kaydı', count(*), 3
    FROM govai.regulatory_changes WHERE status = 3 -- RegulatoryChangeStatus.Quarantined
  UNION ALL SELECT 'Kural (toplam)', count(*), 4 FROM govai.opportunity_rules
  UNION ALL SELECT 'Kanıt bağlantısı olan kural', count(DISTINCT opportunity_rule_id), 5
    FROM govai.opportunity_rule_evidence
  UNION ALL SELECT 'Kural-kanıt bağlantısı (satır)', count(*), 6
    FROM govai.opportunity_rule_evidence
  UNION ALL SELECT 'Güncel değerlendirme', count(*), 7
    FROM govai.eligibility_assessments WHERE is_latest = true
  UNION ALL SELECT 'Güncel analiz çalıştırması', count(*), 8
    FROM govai.analysis_runs WHERE is_latest = true
) t ORDER BY s;
SQL
echo

echo "## Servis sağlığı"
if curl -fsS --max-time 5 "${API_URL}/health" >/dev/null 2>&1; then
  echo "  API /health          : SAĞLIKLI"
else
  echo "  API /health          : YANITSIZ"
fi

docker ps --filter "label=com.docker.compose.project=govai" --format '{{.Names}}\t{{.Status}}' \
  | sort | sed 's/^/  /'
