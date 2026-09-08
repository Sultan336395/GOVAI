#!/usr/bin/env bash
#
# Platform İnceleyicisi için tek kullanımlık aktivasyon bağlantısı üretir.
#
# PAROLA SORULMAZ ve ÜRETİLMEZ. Betik yalnızca bağlantıyı basar; parolayı, bağlantıyı
# açan kişi kendi tarayıcısında belirler. Böylece parola hiçbir noktada — kodda,
# ayar dosyasında, veritabanında, logda ya da bu betiğin çıktısında — bulunmaz.
#
# NASIL ÇALIŞIR:
#   1. Rastgele bir bootstrap sırrı üretir ve .env'e YAZAR.
#   2. Yalnızca API servisini yeniden oluşturur (sır açılışta okunuyor).
#   3. Sırla birlikte aktivasyon ucunu çağırır ve bağlantıyı alır.
#   4. Sırrı .env'den SİLER ve API'yi tekrar yeniden oluşturur.
#
# 4. adım zorunludur: sır açık kalırsa, onu bilen herkes platform hesabı açabilir.
# Betik hata alsa bile sır temizlenir (trap).
#
# NE YAPMAZ:
#   * Veritabanı, Redis, RabbitMQ ve diğer container'lara dokunmaz.
#   * Var olan hesabı ikinci kez açmaz — aynı hesap yeniden kullanılır.
#   * Bağlantıyı loga ya da dosyaya yazmaz; yalnızca ekrana basar.
#
# KULLANIM (sunucuda):
#   bash /opt/govai/scripts/inceleyici-baglantisi.sh

set -euo pipefail

ENV_DOSYASI="${GOVAI_ENV_FILE:-/opt/govai/deploy/.env}"
COMPOSE_DIZINI="$(dirname "$ENV_DOSYASI")"
API_CONTAINER="govai-api-1"
API_URL="${GOVAI_API_URL:-http://127.0.0.1:8080}"
WEB_URL="${GOVAI_WEB_URL:-https://govai.yuppi.cloud}"
EPOSTA="${1:-inceleyici@govai.local}"
AD="${2:-Platform İnceleyicisi}"

DEGISKEN="GOVAI_PLATFORM_BOOTSTRAP_SECRET"

hata() { echo "HATA: $*" >&2; exit 1; }

docker inspect "$API_CONTAINER" >/dev/null 2>&1 \
    || hata "API container'ı bulunamadı: $API_CONTAINER"

PROJE="$(docker inspect -f '{{ index .Config.Labels "com.docker.compose.project" }}' "$API_CONTAINER")"
[ "$PROJE" = "govai" ] || hata "'$API_CONTAINER' govai projesine ait değil. Durduruldu."

[ -f "$ENV_DOSYASI" ] || hata "Ayar dosyası bulunamadı: $ENV_DOSYASI"

# Etiket çalışan imajdan okunur; elle yazılan yanlış bir etiket compose'u derlemeye
# düşürür ve sunucuda derleme başlatırdı.
ETIKET="$(docker inspect -f '{{.Config.Image}}' "$API_CONTAINER" | sed 's/.*://')"
[ -n "$ETIKET" ] || hata "Çalışan API imajının etiketi okunamadı."

ayarla() {
    local ad="$1" deger="$2"

    if grep -q "^${ad}=" "$ENV_DOSYASI"; then
        ad="$ad" deger="$deger" dosya="$ENV_DOSYASI" python3 - <<'PY'
import os

ad, deger, dosya = os.environ["ad"], os.environ["deger"], os.environ["dosya"]

with open(dosya, encoding="utf-8") as f:
    satirlar = f.readlines()

with open(dosya, "w", encoding="utf-8") as f:
    for satir in satirlar:
        f.write(f"{ad}={deger}\n" if satir.startswith(ad + "=") else satir)
PY
    else
        printf '%s=%s\n' "$ad" "$deger" >> "$ENV_DOSYASI"
    fi

    chmod 600 "$ENV_DOSYASI"
}

apiyi_yenile() {
    (
        cd "$COMPOSE_DIZINI"
        GOVAI_RELEASE_TAG="$ETIKET" docker compose -p govai \
            -f docker-compose.yml \
            -f docker-compose.release.yml \
            -f docker-compose.limits.yml \
            up -d --no-deps api >/dev/null 2>&1
    )

    for _ in $(seq 1 30); do
        if curl -fsS --max-time 3 "${API_URL}/health" >/dev/null 2>&1; then
            return 0
        fi
        sleep 2
    done

    hata "API sağlık kontrolüne yanıt vermedi."
}

# Sır HER DURUMDA temizlenir: betik yarıda kesilse bile açık kalmamalı.
temizle() {
    echo
    echo "Bootstrap sırrı siliniyor…"
    ayarla "$DEGISKEN" ""
    apiyi_yenile
    echo "Sır temizlendi; hesap açma yolu yeniden kapalı."
}

trap temizle EXIT

echo "Hesap  : ${EPOSTA}"
echo "Rol    : PlatformReviewer"
echo

echo "1/3 Hesap açma yolu geçici olarak açılıyor…"
SIR="$(python3 -c 'import secrets; print(secrets.token_urlsafe(32))')"
ayarla "$DEGISKEN" "$SIR"
apiyi_yenile

echo "2/3 Aktivasyon bağlantısı üretiliyor…"
YANIT="$(curl -fsS --max-time 20 -X POST "${API_URL}/api/platform/activations" \
    -H 'Content-Type: application/json' \
    -H "X-GovAI-Platform-Bootstrap: ${SIR}" \
    -d "$(python3 -c "
import json, sys
print(json.dumps({'email': sys.argv[1], 'fullName': sys.argv[2]}))
" "$EPOSTA" "$AD")")" || hata "Aktivasyon üretilemedi."

unset SIR

JETON="$(printf '%s' "$YANIT" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')"
BITIS="$(printf '%s' "$YANIT" | python3 -c 'import json,sys; print(json.load(sys.stdin)["expiresAt"])')"
YENI="$(printf '%s' "$YANIT" | python3 -c 'import json,sys; print(json.load(sys.stdin)["userCreated"])')"

echo "3/3 Hazır."
echo
echo "════════════════════════════════════════════════════════════════"
echo " AKTİVASYON BAĞLANTISI"
echo "════════════════════════════════════════════════════════════════"
echo
echo "  ${WEB_URL}/activate/${JETON}"
echo
echo "════════════════════════════════════════════════════════════════"
echo
echo "  Hesap        : ${EPOSTA}"
if [ "$YENI" = "True" ]; then
    echo "  Durum        : yeni hesap açıldı (pasif; parola belirlenince etkinleşir)"
else
    echo "  Durum        : var olan hesap kullanıldı, ikinci hesap AÇILMADI"
fi
echo "  Geçerlilik   : ${BITIS} (24 saat)"
echo "  Kullanım     : TEK SEFERLİK — açıp parolayı belirledikten sonra geçersizleşir"
echo
echo "  Parolayı açılan sayfada SİZ belirleyeceksiniz. Bağlantıyı kimseyle"
echo "  paylaşmayın; bağlantıyı açan kişi hesabın parolasını belirleyebilir."
echo

unset JETON YANIT
