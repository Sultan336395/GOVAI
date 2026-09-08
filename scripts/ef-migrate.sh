#!/usr/bin/env bash
# GOVAI — güvenli EF Core migration çalıştırıcısı.
#
# EF komutları GOVAI'de sessiz bir varsayılana düşmez: hedef her zaman açıkça verilir
# (bkz. src/GovAI.Persistence/Design/EfMigrationTarget.cs). Bu betik doğru ortam
# değişkenlerini kurar ve parolayı komut satırında görünmeyecek biçimde okur.
#
# Kullanım:
#   scripts/ef-migrate.sh model-only  migrations has-pending-model-changes
#   scripts/ef-migrate.sh model-only  migrations add YeniMigration
#   scripts/ef-migrate.sh preview     database update
#   scripts/ef-migrate.sh preview     migrations list
#   scripts/ef-migrate.sh test        database update   (GOVAI_TEST_POSTGRES gerekir)
#
# 5180 (müşterinin canlı veritabanı) bilerek desteklenmez. Oraya gerçekten çalışmak
# gerekiyorsa önce yedek al, sonra değişkenleri elle ver:
#   export GOVAI_EF_CONNECTION_STRING="Host=localhost;Port=5432;Database=govai;..."
#   export GOVAI_EF_ALLOW_PRODUCTION="EVET-5180-VERITABANINI-DEGISTIR"

set -euo pipefail

KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HEDEF="${1:-}"
shift || true

if [[ -z "$HEDEF" || $# -eq 0 ]]; then
  sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
  exit 1
fi

case "$HEDEF" in
  model-only)
    # Veritabanına hiç bağlanmayan komutlar için. Ortam beyanı gerekmez:
    # bağlantı zaten çözümlenemeyen bir adrese gidiyor.
    export GOVAI_EF_CONNECTION_STRING="model-only"
    unset GOVAI_EF_ENVIRONMENT
    ;;

  preview|onizleme)
    ENV_DOSYASI="$KOK/deploy/.env.preview"

    if [[ ! -f "$ENV_DOSYASI" ]]; then
      echo "HATA: $ENV_DOSYASI bulunamadı." >&2
      exit 1
    fi

    # Parola dosyadan okunur; ekrana veya komut satırına yazılmaz.
    PAROLA="$(grep -E '^POSTGRES_PASSWORD=' "$ENV_DOSYASI" | cut -d= -f2- | tr -d '\r"'"'")"
    PORT="$(grep -E '^POSTGRES_PORT=' "$ENV_DOSYASI" | cut -d= -f2- | tr -d '\r"'"'")"
    PORT="${PORT:-15437}"

    if [[ -z "$PAROLA" ]]; then
      echo "HATA: $ENV_DOSYASI içinde POSTGRES_PASSWORD yok." >&2
      exit 1
    fi

    if [[ "$PORT" == "5432" ]]; then
      echo "HATA: önizleme portu 5432 olamaz — orası 5180 ortamının veritabanı." >&2
      exit 1
    fi

    export GOVAI_EF_CONNECTION_STRING="Host=localhost;Port=$PORT;Database=govai;Username=govai;Password=$PAROLA"

    # Ortam beyanı: bağlantı gerçekten önizlemeye gitmiyorsa komut durur.
    export GOVAI_EF_ENVIRONMENT="Preview"
    ;;

  test|gecici-test)
    # Geçici, izole test veritabanı. Adres GOVAI_TEST_POSTGRES ile verilir; ad
    # denetimi EfMigrationTarget.RequireEphemeralTestTarget tarafından yapılır:
    # veritabanı adı "_test" içermeli ve port 5432/15437 OLMAMALIDIR. Böylece
    # "test çalıştırıyorum" diyen bir komut gerçek veritabanına ulaşamaz.
    if [[ -z "${GOVAI_TEST_POSTGRES:-}" ]]; then
      echo "HATA: GOVAI_TEST_POSTGRES tanımlı değil." >&2
      echo "Örnek: export GOVAI_TEST_POSTGRES='Host=127.0.0.1;Port=55432;Username=postgres;Password=...'" >&2
      exit 1
    fi

    VERITABANI="${GOVAI_TEST_DATABASE:-govai_migration_test}"

    case "$VERITABANI" in
      *_test*) ;;
      *)
        echo "HATA: test veritabanının adı '_test' içermelidir; gelen: $VERITABANI" >&2
        exit 1
        ;;
    esac

    export GOVAI_EF_CONNECTION_STRING="${GOVAI_TEST_POSTGRES};Database=${VERITABANI}"
    export GOVAI_EF_ENVIRONMENT="EphemeralTest"
    ;;

  *)
    echo "HATA: bilinmeyen hedef '$HEDEF'. Geçerli değerler: model-only, preview, test" >&2
    exit 1
    ;;
esac

# Üretim onayı bu betikten asla verilmez.
unset GOVAI_EF_ALLOW_PRODUCTION

cd "$KOK"
exec dotnet dotnet-ef "$@" --project src/GovAI.Persistence --startup-project src/GovAI.Api
