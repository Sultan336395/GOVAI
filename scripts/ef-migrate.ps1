<#
.SYNOPSIS
GOVAI — güvenli EF Core migration çalıştırıcısı.

.DESCRIPTION
EF komutları GOVAI'de sessiz bir varsayılana düşmez: hedef her zaman açıkça verilir
(bkz. src/GovAI.Persistence/Design/EfMigrationTarget.cs). Bu betik doğru ortam
değişkenlerini kurar ve parolayı komut satırında görünmeyecek biçimde okur.

5180 (müşterinin canlı veritabanı) bilerek desteklenmez. Oraya gerçekten çalışmak
gerekiyorsa önce yedek al, sonra değişkenleri elle ver:
  $env:GOVAI_EF_CONNECTION_STRING = "Host=localhost;Port=5432;Database=govai;..."
  $env:GOVAI_EF_ALLOW_PRODUCTION  = "EVET-5180-VERITABANINI-DEGISTIR"

.EXAMPLE
scripts\ef-migrate.ps1 model-only migrations has-pending-model-changes

.EXAMPLE
scripts\ef-migrate.ps1 model-only migrations add YeniMigration

.EXAMPLE
scripts\ef-migrate.ps1 preview database update
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('model-only', 'preview')]
    [string] $Hedef,

    [Parameter(Mandatory = $true, Position = 1, ValueFromRemainingArguments = $true)]
    [string[]] $EfArgumanlari
)

$ErrorActionPreference = 'Stop'
$kok = Split-Path -Parent $PSScriptRoot

function Get-EnvDegeri {
    param([string] $Dosya, [string] $Anahtar)

    foreach ($satir in Get-Content -Path $Dosya -Encoding utf8) {
        if ($satir -match "^$Anahtar=(.*)$") {
            return $Matches[1].Trim().Trim('"').Trim("'")
        }
    }

    return $null
}

if ($Hedef -eq 'model-only') {
    # Veritabanına hiç bağlanmayan komutlar için.
    $env:GOVAI_EF_CONNECTION_STRING = 'model-only'
}
else {
    $envDosyasi = Join-Path $kok 'deploy\.env.preview'

    if (-not (Test-Path $envDosyasi)) {
        throw "HATA: $envDosyasi bulunamadı."
    }

    # Parola dosyadan okunur; ekrana veya komut satırına yazılmaz.
    $parola = Get-EnvDegeri -Dosya $envDosyasi -Anahtar 'POSTGRES_PASSWORD'
    $port = Get-EnvDegeri -Dosya $envDosyasi -Anahtar 'POSTGRES_PORT'

    if (-not $port) { $port = '15437' }

    if (-not $parola) {
        throw "HATA: $envDosyasi içinde POSTGRES_PASSWORD yok."
    }

    if ($port -eq '5432') {
        throw 'HATA: önizleme portu 5432 olamaz — orası 5180 ortamının veritabanı.'
    }

    $env:GOVAI_EF_CONNECTION_STRING =
        "Host=localhost;Port=$port;Database=govai;Username=govai;Password=$parola"
}

# Üretim onayı bu betikten asla verilmez.
Remove-Item Env:\GOVAI_EF_ALLOW_PRODUCTION -ErrorAction SilentlyContinue

Push-Location $kok
try {
    & dotnet dotnet-ef @EfArgumanlari --project src/GovAI.Persistence --startup-project src/GovAI.Api
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
