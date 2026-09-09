#!/usr/bin/env bash
#
# Katalog bakım işlemlerini tek komutla uygular.
#
# NEDEN BU BETİK VAR: bu işlemler yalnızca Platform İnceleyicisi yetkisiyle
# çalışır ve o hesabın parolası TASARIM GEREĞİ yalnızca sizde. Panelden tıklamak
# da geçerli bir yoldur; bu betik, işlemleri doğru sırayla ve tek oturumda
# yapmak isteyenler içindir.
#
# PAROLA: ekranda GÖSTERİLMEDEN sorulur, yalnızca oturum açmak için kullanılır ve
# hiçbir yere yazılmaz — ne dosyaya, ne loga, ne komut geçmişine.
#
# ONAY KAPISI KORUNUR: her adımda önce plan gösterilir, siz onaylamadan hiçbir
# değişiklik uygulanmaz. Sunucu da aynı kuralı ayrıca uygular (plan parmak izi),
# bu yüzden betik onayı atlayamaz.
#
# SIRA ÖNEMLİ: katalog düzeltme İKİ KEZ çalıştırılır. Birinci geçiş başlığı resmî
# belgeden düzeltir; düzelen başlık kaydın gerçek konusunu ortaya çıkarır ve
# ikinci geçiş, konusu işveren mevzuatı olmayan kaydı katalogdan çıkarır. Tek
# geçişte bu görülemez, çünkü eşleştirme kaydın O ANKİ başlığına bakar.
#
# KULLANIM (sunucuda):
#   bash /opt/govai/scripts/bakim-uygula.sh

set -euo pipefail

API="${GOVAI_API_URL:-http://127.0.0.1:8080}"
EPOSTA="${1:-inceleyici@govai.local}"

hata() { echo "HATA: $*" >&2; exit 1; }

command -v python3 >/dev/null || hata "python3 bulunamadı."
command -v curl >/dev/null || hata "curl bulunamadı."

curl -fsS --max-time 10 "${API}/health" >/dev/null 2>&1 || hata "API yanıt vermiyor: ${API}"

gizli_oku() {
    local soru="$1" cevap=""
    printf '%s' "$soru" >&2
    stty -echo 2>/dev/null || true
    IFS= read -r cevap
    stty echo 2>/dev/null || true
    printf '\n' >&2
    printf '%s' "$cevap"
}

# JSON'dan tek bir alan okur. jq her sunucuda yok; python3 var.
alan() {
    python3 -c "
import json, sys
try:
    veri = json.load(sys.stdin)
except Exception:
    sys.exit(1)
for parca in sys.argv[1].split('.'):
    if veri is None:
        break
    veri = veri.get(parca) if isinstance(veri, dict) else None
print('' if veri is None else veri)
" "$1"
}

# ── Oturum ──────────────────────────────────────────────────────────────────

cat <<'BILGI'
Katalog bakım işlemleri
=======================

Parolanızı yazarken EKRANDA HİÇBİR KARAKTER GÖRÜNMEYECEK. Bu normaldir.

Parola yalnızca oturum açmak için kullanılır; hiçbir yere kaydedilmez.

BILGI

echo "Hesap: ${EPOSTA}"
PAROLA="$(gizli_oku 'Parola: ')"
[ -n "$PAROLA" ] || hata "Parola boş. Hiçbir şey değiştirilmedi."

GOVDE="$(python3 -c "
import json, sys
print(json.dumps({'email': sys.argv[1], 'password': sys.argv[2]}))
" "$EPOSTA" "$PAROLA")"

unset PAROLA

YANIT="$(curl -fsS --max-time 20 -X POST "${API}/api/auth/login" \
    -H 'Content-Type: application/json' -d "$GOVDE" 2>/dev/null)" \
    || hata "Giriş başarısız. Parola yanlış olabilir; hiçbir şey değiştirilmedi."

unset GOVDE

JETON="$(printf '%s' "$YANIT" | alan accessToken)"
[ -n "$JETON" ] || hata "Oturum jetonu alınamadı."

ROL="$(printf '%s' "$YANIT" | alan user.role)"
unset YANIT

echo "Giriş başarılı. Rol: ${ROL:-bilinmiyor}"
echo

cagir() {
    local yontem="$1" yol="$2" veri="${3:-}"

    if [ -n "$veri" ]; then
        curl -fsS --max-time 120 -X "$yontem" "${API}${yol}" \
            -H "Authorization: Bearer ${JETON}" \
            -H 'Content-Type: application/json' -d "$veri"
    else
        curl -fsS --max-time 120 -X "$yontem" "${API}${yol}" \
            -H "Authorization: Bearer ${JETON}"
    fi
}

onayla() {
    local soru="$1" cevap=""
    printf '%s [e/H]: ' "$soru"
    IFS= read -r cevap
    [ "$cevap" = "e" ] || [ "$cevap" = "E" ]
}

# ── 1–2. Katalog düzeltme (iki geçiş) ───────────────────────────────────────

katalog_gecisi() {
    local sira="$1"

    echo "── Katalog Düzeltme · ${sira}. geçiş ─────────────────────────────"

    local plan
    plan="$(cagir GET /api/sources/catalog-repair/plan)" || hata "Plan alınamadı."

    local degisecek ozet
    degisecek="$(printf '%s' "$plan" | alan willChangeCount)"
    ozet="$(printf '%s' "$plan" | alan planHash)"

    if [ "${degisecek:-0}" = "0" ]; then
        echo "   Değişecek kayıt yok."
        echo
        return 0
    fi

    echo "   ${degisecek} kayıt değişecek:"
    printf '%s' "$plan" | python3 -c "
import json, sys
for m in json.load(sys.stdin)['matches']:
    if m['willChange']:
        eylem = 'katalogdan çıkar' if m['action'] == 'Quarantine' else 'başlığı düzelt'
        print(f\"     - [{eylem}] {m['currentTitle'][:62]}\")
        if m.get('proposedTitle'):
            print(f\"       yeni başlığı: {m['proposedTitle'][:62]}\")
"
    echo

    if ! onayla "   Uygulansın mı?"; then
        echo "   Atlandı."
        echo
        return 0
    fi

    local sonuc
    sonuc="$(cagir POST /api/sources/catalog-repair/apply \
        "$(python3 -c "import json,sys; print(json.dumps({'planHash': sys.argv[1]}))" "$ozet")")" \
        || hata "Uygulama başarısız."

    echo "   Sonuç: $(printf '%s' "$sonuc" | alan changed) kayıt değişti."
    echo "   Geri almak için çalıştırma kimliği: $(printf '%s' "$sonuc" | alan runId)"
    echo
}

katalog_gecisi 1
katalog_gecisi 2

# ── 3. Dayanak eşleştirme ───────────────────────────────────────────────────

echo "── Dayanak Eşleştirme ───────────────────────────────────────────"

plan="$(cagir GET '/api/opportunities/rule-evidence/backfill/plan?batchSize=500')" \
    || hata "Plan alınamadı."

baglanacak="$(printf '%s' "$plan" | alan boundCount)"
ozet="$(printf '%s' "$plan" | alan planHash)"

echo "   İncelenen çağrı        : $(printf '%s' "$plan" | alan totalExamined)"
echo "   Dayanağı bulunan       : ${baglanacak:-0}"
echo "   Dayanağı zaten olan    : $(printf '%s' "$plan" | alan alreadyBoundCount)"
echo "   Dayanağı bulunamayan   : $(printf '%s' "$plan" | alan noEvidenceCount)"
echo "   Kaynağı doğrulanmamış  : $(printf '%s' "$plan" | alan skippedUnverifiedSourceCount)"
echo "   İncelemede olduğu için atlanan: $(printf '%s' "$plan" | alan skippedQuarantinedCount)"
echo

if [ "${baglanacak:-0}" = "0" ]; then
    echo "   Eşleştirilecek çağrı yok."
elif onayla "   Uygulansın mı?"; then
    sonuc="$(cagir POST '/api/opportunities/rule-evidence/backfill/apply?batchSize=500' \
        "$(python3 -c "import json,sys; print(json.dumps({'planHash': sys.argv[1]}))" "$ozet")")" \
        || hata "Uygulama başarısız."

    echo "   Sonuç: $(printf '%s' "$sonuc" | alan boundCount) çağrının koşulları eşleştirildi."
    echo "   Geri almak için çalıştırma kimliği: $(printf '%s' "$sonuc" | alan runId)"
else
    echo "   Atlandı."
fi

unset JETON

echo
echo "Bitti. Yapılan her işlem denetim kaydına geçti ve panelden geri alınabilir."
