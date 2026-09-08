#!/usr/bin/env bash
#
# OpenAI API anahtarını güvenli biçimde devreye alır.
#
# NEDEN BU BETİK VAR: anahtarı devreye almak dosya düzenlemeyi, izin ayarlamayı ve
# doğru compose komutunu bilmeyi gerektiriyordu. Üçünden biri yanlış yapılırsa ya
# anahtar çalışmaz ya da dosya herkese okunur kalır. Betik üçünü de kendisi yapar.
#
# NE YAPAR:
#   1. Anahtarı EKRANDA GÖSTERMEDEN sorar (yazarken hiçbir karakter görünmez).
#   2. .env dosyasına yazar ve izni 600'e çeker.
#   3. YALNIZCA API servisini yeniden oluşturur.
#   4. Anahtarın devreye girdiğini doğrular.
#
# NE YAPMAZ:
#   * Anahtarı ekrana, loga ya da komut geçmişine yazmaz.
#   * Veritabanı, Redis ve RabbitMQ'ya dokunmaz.
#   * govai dışındaki hiçbir container'a dokunmaz.
#   * Anahtarı depoya (git) yazmaz — .env zaten .gitignore içindedir.
#
# KULLANIM (sunucuda):
#   bash /opt/govai/scripts/anahtar-gir.sh
#
# KAPATMAK İÇİN:
#   bash /opt/govai/scripts/anahtar-gir.sh --kapat

set -euo pipefail

ENV_DOSYASI="${GOVAI_ENV_FILE:-/opt/govai/deploy/.env}"
COMPOSE_DIZINI="$(dirname "$ENV_DOSYASI")"
API_CONTAINER="govai-api-1"

hata() { echo "HATA: $*" >&2; exit 1; }

# Yazarken hiçbir şey görünmesin; anahtar terminal geçmişine de düşmesin.
gizli_oku() {
    local soru="$1" cevap=""
    printf '%s' "$soru" >&2
    stty -echo 2>/dev/null || true
    IFS= read -r cevap
    stty echo 2>/dev/null || true
    printf '\n' >&2
    printf '%s' "$cevap"
}

# ── Ön kontroller ──────────────────────────────────────────────────────────

[ -f "$ENV_DOSYASI" ] || hata "Ayar dosyası bulunamadı: $ENV_DOSYASI"

docker inspect "$API_CONTAINER" >/dev/null 2>&1 \
    || hata "API container'ı bulunamadı: $API_CONTAINER"

PROJE="$(docker inspect -f '{{ index .Config.Labels "com.docker.compose.project" }}' "$API_CONTAINER")"
[ "$PROJE" = "govai" ] || hata "'$API_CONTAINER' govai projesine ait değil. Durduruldu."

# Çalışan sürümün etiketi imajın kendisinden okunur. Elle yazılsaydı, yanlış bir
# etiket compose'u derlemeye düşürür ve sunucuda derleme başlatırdı.
ETIKET="$(docker inspect -f '{{.Config.Image}}' "$API_CONTAINER" | sed 's/.*://')"
[ -n "$ETIKET" ] || hata "Çalışan API imajının etiketi okunamadı."

# ── Değeri .env içine yaz ya da güncelle ───────────────────────────────────

ayarla() {
    local ad="$1" deger="$2"

    # Aynı anahtar iki kez yazılmamalı: varsa satır değiştirilir, yoksa eklenir.
    if grep -q "^${ad}=" "$ENV_DOSYASI"; then
        # Değer sed'e argüman olarak GEÇİRİLMEZ (özel karakterler ve süreç listesinde
        # görünme riski). Python ile yerinde değiştirilir.
        ad="$ad" deger="$deger" dosya="$ENV_DOSYASI" python3 - <<'PY'
import os

ad = os.environ["ad"]
deger = os.environ["deger"]
dosya = os.environ["dosya"]

with open(dosya, encoding="utf-8") as f:
    satirlar = f.readlines()

with open(dosya, "w", encoding="utf-8") as f:
    for satir in satirlar:
        if satir.startswith(ad + "="):
            f.write(f"{ad}={deger}\n")
        else:
            f.write(satir)
PY
    else
        printf '%s=%s\n' "$ad" "$deger" >> "$ENV_DOSYASI"
    fi
}

servisi_yenile() {
    echo
    echo "API servisi yeniden oluşturuluyor (yalnızca API; veritabanına dokunulmuyor)…"

    (
        cd "$COMPOSE_DIZINI"
        GOVAI_RELEASE_TAG="$ETIKET" docker compose -p govai \
            -f docker-compose.yml \
            -f docker-compose.release.yml \
            -f docker-compose.limits.yml \
            up -d --no-deps api
    )

    echo
    printf 'Servisin hazır olması bekleniyor'
    for _ in $(seq 1 30); do
        if curl -fsS --max-time 3 http://127.0.0.1:8080/health >/dev/null 2>&1; then
            printf ' — hazır.\n'
            return 0
        fi
        printf '.'
        sleep 2
    done

    printf '\n'
    hata "API sağlık kontrolüne yanıt vermedi. Geri almak için: bash $0 --kapat"
}

durum_yaz() {
    echo
    echo "── Durum ───────────────────────────────────────────────"

    docker exec "$API_CONTAINER" sh -c '
        for v in GOVAI_AI_ENABLED GOVAI_AI_PROVIDER GOVAI_AI_MODEL; do
            printf "  %-22s %s\n" "$v" "$(printenv $v)"
        done
        if [ -n "$(printenv GOVAI_AI_API_KEY)" ]; then
            printf "  %-22s %s\n" "GOVAI_AI_API_KEY" "girildi (değeri gösterilmiyor)"
        else
            printf "  %-22s %s\n" "GOVAI_AI_API_KEY" "boş"
        fi
    '
    echo "────────────────────────────────────────────────────────"
}

# ── Kapatma yolu ───────────────────────────────────────────────────────────

if [ "${1:-}" = "--kapat" ]; then
    echo "Yapay zekâ kapatılıyor; anahtar siliniyor."

    ayarla GOVAI_AI_PROVIDER none
    ayarla GOVAI_AI_API_KEY ""
    ayarla GOVAI_AI_MODEL ""
    ayarla OPENAI_API_KEY ""
    chmod 600 "$ENV_DOSYASI"

    servisi_yenile
    durum_yaz

    echo
    echo "Kapatıldı. Sistem kural tabanlı analize döndü."
    exit 0
fi

# ── Anahtar girme yolu ─────────────────────────────────────────────────────

cat <<'BILGI'
OpenAI anahtarını devreye alma
==============================

Anahtarı yazarken EKRANDA HİÇBİR KARAKTER GÖRÜNMEYECEK. Bu normaldir.
Yapıştırıp Enter'a basmanız yeterli.

Anahtar yalnızca sunucudaki ayar dosyasına yazılır; ekrana, loga veya
veritabanına yazılmaz.

BILGI

ANAHTAR="$(gizli_oku 'OpenAI API anahtarı: ')"

[ -n "$ANAHTAR" ] || hata "Anahtar boş. Hiçbir şey değiştirilmedi."

# Kaba biçim denetimi. Amaç doğrulamak değil, kazayla yanlış bir şey
# (parola, e-posta, boşluklu metin) yapıştırılmasını yakalamak.
case "$ANAHTAR" in
    *" "*|*"	"*) hata "Anahtarda boşluk var. Yanlış bir şey yapıştırılmış olabilir." ;;
    sk-*) ;;
    *) hata "Anahtar 'sk-' ile başlamıyor. Yanlış değer girilmiş olabilir." ;;
esac

[ "${#ANAHTAR}" -ge 20 ] || hata "Anahtar beklenenden kısa. Hiçbir şey değiştirilmedi."

echo
echo "Model adını OpenAI hesabınızdaki listeden yazın."
echo "Bilmiyorsanız Enter'a basın; şimdilik boş kalır ve sistem kural tabanlı çalışmayı sürdürür."
printf 'Model adı: '
IFS= read -r MODEL

ayarla GOVAI_AI_API_KEY "$ANAHTAR"
ayarla OPENAI_API_KEY "$ANAHTAR"

if [ -n "$MODEL" ]; then
    ayarla GOVAI_AI_PROVIDER openai
    ayarla GOVAI_AI_MODEL "$MODEL"
else
    # Model adı olmadan sağlayıcı AÇILMAZ. Yarım yapılandırma, her analizde
    # başarısız bir istek denemesi üretir ve kullanıcıya "hibrit" vaat ederdi.
    ayarla GOVAI_AI_PROVIDER none
fi

chmod 600 "$ENV_DOSYASI"
unset ANAHTAR

servisi_yenile
durum_yaz

echo
if [ -n "$MODEL" ]; then
    cat <<'SON'
Anahtar devrede. Panelde analiz açtığınızda artık "Hibrit analiz" ve bir
yapay zekâ güven değeri göreceksiniz.

Model yine de kendi başına karar veremez: her iddiası resmî belgedeki
kanıta karşı doğrulanır, doğrulanamayan iddia size gösterilmez.

Kapatmak isterseniz:  bash /opt/govai/scripts/anahtar-gir.sh --kapat
SON
else
    cat <<'SON'
Anahtar kaydedildi ama MODEL ADI GİRİLMEDİĞİ için yapay zekâ açılmadı.
Sistem kural tabanlı çalışmaya devam ediyor ve ekranda bunu açıkça yazıyor.

Model adını öğrendiğinizde aynı komutu yeniden çalıştırın.
SON
fi
