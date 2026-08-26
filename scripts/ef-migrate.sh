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
    # Veritabanına hiç bağlanmayan komutlar için.
    export GOVAI_EF_CONNECTION_STRING="model-only"
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
    ;;

  *)
    echo "HATA: bilinmeyen hedef '$HEDEF'. Geçerli değerler: model-only, preview" >&2
    exit 1
    ;;
esac

# Üretim onayı bu betikten asla verilmez.
unset GOVAI_EF_ALLOW_PRODUCTION

cd "$KOK"
exec dotnet dotnet-ef "$@" --project src/GovAI.Persistence --startup-project src/GovAI.Api
