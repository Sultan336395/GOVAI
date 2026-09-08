#!/usr/bin/env bash
#
# GOVAI yayın öncesi tam veritabanı yedeği.
#
# İKİ BİÇİM ALINIR ve ikisi de gereklidir:
#   * custom (-Fc) — seçmeli geri yükleme yapılabilir, sıkıştırılmıştır.
#   * plain (.sql) — okunabilir; custom biçimi bozulursa tek çare budur ve
#     pg_restore sürümü uyuşmazlığından etkilenmez.
#
# NE YAPMAZ:
#   * Hiçbir container'ı durdurmaz, yeniden başlatmaz, yeniden oluşturmaz.
#   * Hiçbir veritabanına YAZMAZ. pg_dump salt okurdur.
#   * govai dışındaki hiçbir container'a dokunmaz.
#   * Parolayı ekrana, loga ya da komut satırına yazmaz — container'ın kendi
#     ortamındaki POSTGRES_PASSWORD kullanılır ve dışarı çıkmaz.
#
# KULLANIM (sunucuda):
#   bash scripts/yayin-yedek-al.sh [etiket]
#
# Etiket verilmezse tarih-saat kullanılır.

set -euo pipefail

CONTAINER="${GOVAI_PG_CONTAINER:-govai-postgres-1}"
YEDEK_DIZINI="${GOVAI_BACKUP_DIR:-/opt/govai/yedek}"
ETIKET="${1:-$(date +%Y%m%d-%H%M%S)}"

# Yedek dosyası adı: ne olduğunu adından okuyabilmek için.
TEMEL="${YEDEK_DIZINI}/yayin-oncesi-${ETIKET}"
DUMP="${TEMEL}.dump"
SQL="${TEMEL}.sql"

hata() { echo "HATA: $*" >&2; exit 1; }

command -v docker >/dev/null || hata "docker bulunamadı."

docker inspect "$CONTAINER" >/dev/null 2>&1 \
  || hata "Container bulunamadı: $CONTAINER (GOVAI_PG_CONTAINER ile değiştirilebilir)."

# Container'ın gerçekten GOVAI'ye ait olduğunu doğrula. Yanlış bir ada karşı
# çalıştırmak, başka bir uygulamanın veritabanını okumak olurdu.
PROJE="$(docker inspect -f '{{ index .Config.Labels "com.docker.compose.project" }}' "$CONTAINER")"
[ "$PROJE" = "govai" ] || hata "Container '$CONTAINER' govai projesine ait değil (proje: '$PROJE'). Durduruldu."

mkdir -p "$YEDEK_DIZINI"
chmod 700 "$YEDEK_DIZINI"

# Kullanıcı ve veritabanı adı container'ın kendi ortamından okunur; burada
# varsayılan uydurulmaz.
PGUSER="$(docker exec "$CONTAINER" printenv POSTGRES_USER)"
PGDB="$(docker exec "$CONTAINER" printenv POSTGRES_DB)"

echo "Kaynak     : ${CONTAINER} → ${PGDB}"
echo "Hedef dizin: ${YEDEK_DIZINI}"
echo

echo "1/4 custom biçim alınıyor…"
docker exec "$CONTAINER" pg_dump -U "$PGUSER" -d "$PGDB" -Fc --no-owner --no-privileges > "$DUMP"

echo "2/4 düz SQL alınıyor…"
docker exec "$CONTAINER" pg_dump -U "$PGUSER" -d "$PGDB" --no-owner --no-privileges > "$SQL"

echo "3/4 izinler kısıtlanıyor…"
chmod 600 "$DUMP" "$SQL"

echo "4/4 özetler hesaplanıyor…"
sha256sum "$DUMP" "$SQL" | tee "${TEMEL}.sha256"
chmod 600 "${TEMEL}.sha256"

echo
echo "Boyutlar:"
ls -lh "$DUMP" "$SQL" | awk '{print "  " $9 "  " $5}'

# Boş bir dosya "yedek alındı" sayılmamalı.
for dosya in "$DUMP" "$SQL"; do
  [ -s "$dosya" ] || hata "Yedek boş: $dosya"
done

echo
echo "Yedek tamam. SIRADAKİ ADIM ZORUNLU:"
echo "  bash scripts/yayin-yedek-dogrula.sh ${TEMEL}"
echo
echo "Doğrulanmamış yedek yedek sayılmaz; geri yüklenebildiği kanıtlanmadan yayın başlatma."
