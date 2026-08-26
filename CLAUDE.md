# CLAUDE.md — GOVAI üzerinde çalışırken

Bu dosya, projeyi ilk kez devralan bir Claude oturumu için yazılmıştır.
Kod yazmadan önce buradaki **bozulmaz kuralları** ve **çapraz sözleşmeleri** oku;
gerisi kodun kendisinde ve `docs/` altında.

---

## 1. Bu proje nedir

GOVAI, şirketlerin ERP/İK/muhasebe verisini resmî **teşvik, hibe ve ihale** çağrılarıyla
eşleştiren; uygunluğu, eksik koşulları ve başvuru yapılabilirliğini **açıklanabilir**
biçimde skorlayan bir karar destek platformudur.

Müşteri: TalentHub İnsan Kaynakları Danışmanlık (Mersin Teknopark).
Takvim: 01.06.2026 – 31.05.2027. Kaynak proje dosyası TalentHub tarafından hazırlanmıştır ve
teknopark/teşvik başvurusunda kullanılacaktır — bu yüzden **dokümantasyon kalitesi kodun kendisi
kadar önemlidir**. Mimari kararlar `docs/adr/` altında gerekçeleriyle kayıtlıdır.

Ürünün ticari savunma hattı üç iddiadır. Kod bu iddiaları korumak üzere yazılmıştır:

1. Skor **deterministiktir** — aynı girdi her zaman aynı çıktıyı verir.
2. Skor **açıklanabilirdir** — her puan, çağrı metnindeki cümleye kadar geri izlenir.
3. Sistem **eksik veriyle firmayı elemez** — "bilmiyorum" ile "hayır" ayrı şeylerdir.

---

## 2. Bozulmaz kurallar

Bunları ihlal eden bir değişiklik, testleri geçse bile üründe gerileme sayılır.

### 2.1 Yapay zekâ karar vermez

`GovAI.Domain` içinde **hiçbir AI çağrısı, HTTP isteği, rastgelelik veya `DateTime.Now` olamaz.**
Skoru ve kararı yalnızca `EligibilityEngine` üretir; girdisi firma profili, çağrı kuralları ve
dışarıdan verilen `asOf` zamanıdır.

AI iki yerde ve yalnızca yardımcı olarak kullanılır:

| Nerede | Ne yapar | Ne yapamaz |
|---|---|---|
| `OpenAiExplanationClient.ExtractRulesAsync` | Metinden kural **taslağı** çıkarır | Kuralı üretime sokamaz — danışman onayına düşer |
| `OpenAiExplanationClient.GenerateExecutiveSummaryAsync` | Hesaplanmış skoru anlatıya çevirir | Skoru veya kararı değiştiremez |

OpenAI anahtarı yoksa sistem çalışmaya devam eder: kural çıkarımı deterministik kalıplara,
özetler `BuildFallbackSummary`'ye düşer. **Bu davranışı kaldırma.** Gerekçe: `docs/adr/0002`.

### 2.2 Eksik veri firmayı elemez

`0` her zaman "sıfır" demek değildir; çoğu alanda "girilmedi" demektir.
`CompanyFieldResolver` bu ayrımı yapar:

| Alan | Boş değer | Sonuç |
|---|---|---|
| `Financials.*` | `0` | `Unknown` |
| `Workforce.*` | `EmployeeCount == 0` | Tüm personel alanları `Unknown` |
| `Company.NaceCodes`, `Cities`, `Nuts2Codes` | boş küme | `Unknown` |
| `Company.Certificates` | boş küme | **Bilinen değer** — "belgemiz yok" geçerli bir cevaptır |

Sertifika istisnası bilinçlidir; onu da `Unknown` yaparsan "ISO 9001 eksik, temin edin"
aksiyonu kaybolur. Gerekçe: `docs/adr/0003`. Koruyan test:
`Eksik_firma_verisi_karari_belirsiz_yapar_ve_veri_boslugu_raporlanir`.

### 2.3 Skor ağırlıklarının toplamı 1.0'dır

`ScoreWeights` yapıcısı bunu doğrular ve ihlalde `DomainException` atar.
Yeni bir destek türü profili eklerken toplamı kontrol et; `Skor_agirliklarinin_toplami_daima_bir_olmalidir`
testi tüm `SupportCategory` değerlerini tarar.

Varsayılan formül proje dosyasından birebir alınmıştır, keyfî değildir:

```
0.25 sectorMatch + 0.20 financialFit + 0.15 employeeFit + 0.15 documentReadiness
+ 0.10 regionalCompliance + 0.10 technicalQualification + 0.05 timingScore
```

### 2.4 Onion bağımlılık yönü

```
Api → Infrastructure / Persistence → Application → Domain
```

- `Domain` hiçbir NuGet paketine bağımlı değildir. Öyle kalmalı.
- `Application` EF Core'u **tanımaz**; dış dünyayı yalnızca arayüzlerle bilir.
  `IQueryable` sızdırma; filtreleri `OpportunityQuery` / `AssessmentQuery` gibi sorgu
  nesnelerine taşı.
- **MediatR kullanılmaz.** Bu kullanıcının tüm .NET projelerinde geçerli tercihidir.
  Her use-case düz bir servis sınıfıdır, controller onu doğrudan çağırır. Gerekçe: `docs/adr/0001`.

### 2.5 Worker'lar veritabanına dokunmaz

Python worker'ları yalnızca REST API üzerinden konuşur (`govai_workers/api_client.py`).
Doğrudan DB erişimi eklersen tekilleştirme, yetki ve bildirim kuralları atlanır.

---

## 3. Çapraz sözleşmeler — derleyicinin koruyamadığı yerler

Burası en kolay ve en sessiz bozulan yer. Üç sözleşme iki ayrı yığında tanımlıdır:

| Sözleşme | C# tarafı | Python / TS tarafı | Bozulursa |
|---|---|---|---|
| Alan beyaz listesi | `CompanyFieldResolver.SupportedFields` | `rule_extractor.ALLOWED_FIELDS` | AI tanınmayan alanla kural üretir, motor sessizce `Unknown` sayar |
| Kuyruk adları | `QueueNames` | `messaging.RoutingKeys` | Mesajlar hiçbir tüketiciye ulaşmaz, hata da vermez |
| API tipleri | Controller DTO'ları | `web/src/api/types.ts` | Panel `undefined` gösterir |

İlk ikisi otomatik denetlenir:

```bash
python scripts/check_contract_parity.py
```

CI'da `contracts` işi olarak her push'ta koşar. **C# tarafı kaynak doğrudur** — uyuşmazlıkta
Python'u ona uydur, tersini yapma.

Üçüncüsü (TS tipleri) elle senkronize edilir. Bir DTO'ya alan eklersen
`web/src/api/types.ts` içindeki karşılığını da güncelle.

---

## 4. Komutlar

Solution dosyası **`GovAI.slnx`**'tir (yeni XML formatı), `.sln` değil.
`dotnet build GovAI.sln` hata verir; argümansız kullan.

```bash
dotnet build -c Release          # tüm .NET projeleri
dotnet test                      # 153 test (29 domain + 5 application + 119 API)
```

```bash
cd workers && .venv/Scripts/python -m pytest -q      # 53 test
cd workers && .venv/Scripts/python -m ruff check .   # lint (satır sınırı 100)
```

Linux/macOS'ta yol `workers/.venv/bin/python`'dır. Sanal ortam depoda değildir:

```bash
cd workers && python -m venv .venv && .venv/bin/pip install -e ".[dev]"
```

```bash
cd web && npm ci && npm run lint && npm run typecheck && npm run test && npm run build
```

`npm run lint` **`--max-warnings 0`** ile çalışır; uyarı da hatadır.
`npm run test` vitest'i tek seferlik koşturur; şu an yalnızca `navigation.ts`
(sol menünün rol görünürlüğü) kapsanır — ekran testleri hâlâ yok.

### EF Core

`dotnet-ef` yerel araç olarak kurulu (`.config/dotnet-tools.json`), bu yüzden **çift `dotnet`**.

**EF komutları açık bir hedef olmadan çalışmaz.** Sebep: `appsettings.Development.json`
içindeki bağlantı `localhost:5432`'dir ve orası **çalışan 5180 ortamının** Postgres'idir.
Faz 2'de iki migration bu yüzden istemeden müşterinin veritabanına uygulandı. Artık
`GovAiDesignTimeDbContextFactory` devrede: hedef yalnızca `GOVAI_EF_CONNECTION_STRING`
ile verilir, verilmezse komut açıklayıcı bir hatayla durur ve hiçbir varsayılana düşmez.

Hazır betikler bunu senin için kurar:

```bash
scripts/ef-migrate.sh model-only migrations add <Ad>
scripts/ef-migrate.sh model-only migrations has-pending-model-changes
scripts/ef-migrate.sh preview    database update
scripts/ef-migrate.sh preview    migrations list
```

Windows'ta `scripts\ef-migrate.ps1` aynı arayüzü sunar.

| Hedef | Ne yapar |
|---|---|
| `model-only` | Veritabanına **hiç bağlanmaz** (adres `.invalid`, çözümlenemez). `migrations add`, `has-pending-model-changes`, `migrations script` için. CI bunu kullanır. |
| `preview` | `deploy/.env.preview`'dan parolayı okuyup **15437** portundaki önizleme veritabanına bağlanır. |

5180'in veritabanı (`localhost:5432`) **korunan hedeftir**. Oraya gitmek için bağlantıya
ek olarak `GOVAI_EF_ALLOW_PRODUCTION=EVET-5180-VERITABANINI-DEGISTIR` gerekir; betikler bu
onayı asla vermez. Önce yedek al.

Bağlantı dizesi hiçbir yerde bütün olarak yazılmaz — ne hatada, ne logda. EF komutu
yalnızca `sunucu:port/veritabanı` satırını basar. Kilit `EfMigrationTargetTests` ile
korunur (13 test).

Son komut CI'da da koşar: model ile migration'lar ayrışırsa build kırılır.

### Çalıştırma

```bash
cd deploy && cp .env.example .env && docker compose up -d --build
```

`.env` içinde `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD`, `JWT_SIGNING_KEY` (≥32 karakter) zorunludur.
Demo verisi için `SEED_ENABLED=true` + `SEED_ADMIN_PASSWORD=...`.

| Servis | Docker | Yerel geliştirme |
|---|---|---|
| API | 8080 | `dotnet run --project src/GovAI.Api` → 8080 |
| Web | 5180 | `cd web && npm run dev` → 5173 (proxy ile 8080'e) |

---

## 5. Kod konvansiyonları

- **Tanımlayıcılar İngilizce, yorumlar ve kullanıcıya görünen metinler Türkçe.**
  Bu kasıtlıdır: kod uluslararası araçlarla uyumlu kalır, ürün Türk kullanıcıya hitap eder.
- Yorum yalnızca **neden** için yazılır, **ne** için değil. Mevcut yorum yoğunluğunu koru —
  ne artır ne azalt.
- C#: file-scoped namespace, birincil kurucu (primary constructor), `sealed` varsayılan.
  `.editorconfig` bunları zorlar; `Migrations/` klasörü kod stili denetiminden muaftır.
- Python: `from __future__ import annotations`, tip ipuçları zorunlu, satır ≤ 100.
- TypeScript: `strict`, `noUnusedLocals`. `@/` takma adı `web/src/`'yi gösterir.
- React: context nesneleri ve hook'lar `app/contexts.ts` içindedir, provider'lar ayrı `.tsx`
  dosyalarındadır. Bu ayrım fast-refresh lint kuralı içindir; hook'u provider dosyasına geri taşıma.

---

## 6. Nerede ne var

```
src/GovAI.Domain/Eligibility/    ← ürünün kalbi: kural motoru
src/GovAI.Domain/Scoring/        ← ağırlıklar, boyut puanları, zamanlama skoru
src/GovAI.Application/           ← use-case servisleri (her modül kendi klasöründe)
src/GovAI.Persistence/           ← EF Core yapılandırmaları, repository'ler, seed
src/GovAI.Infrastructure/        ← OpenAI, Redis, RabbitMQ, JWT, PDF/Excel
src/GovAI.Api/Controllers/       ← 8 endpoint grubu
workers/govai_workers/           ← collector, parser, rule_extractor, scheduler
web/src/pages/                   ← 8 ekran
docs/                            ← mimari, veri modeli, API, skorlama, ADR'ler, yol haritası
scripts/                         ← sözleşme senkron denetimi
```

Okuma sırası önerisi:
`docs/architecture.md` → `docs/scoring.md` → `EligibilityEngine.cs` → `docs/adr/`.

Proje dosyasındaki 10 modülün kod karşılıkları `docs/architecture.md` §3'te tablo hâlinde.

---

## 7. Bilinen boşluklar

Bunlar hata değil, bilinçli ertelemedir. Tamamı gerekçesi ve hedef ayıyla
`docs/roadmap.md`'de listelidir.

| Konu | Durum |
|---|---|
| OCR | Taranmış PDF'lerde metin çıkmaz; parser bunu loglar |
| Refresh token | Üretiliyor ama sunucuda saklanmıyor; süre dolunca yeniden giriş |
| E-posta / webhook gönderimi | Bildirim üretiliyor ve kuyruğa bırakılıyor, gerçek adaptör yok |
| ERP adaptörleri | `/erp-sync` sözleşmesi hazır, Logo/Netsis/SAP tarafı yok |
| Ağırlık kalibrasyonu | Uzman görüşüne dayalı; gerçek başvuru sonucu verisiyle kalibre edilmedi |
| Kod bölme | Bundle ~714 KB, tek parça |

`docker compose up` ile servislerin **birlikte** ayağa kalkması 19.08.2026'da denendi ve
geçti; ayrıntı `docs/handover.md` §2.1'de. Bu artık açık uç değildir.

CI'da `actions/setup-dotnet` ve `docker/setup-buildx-action` bilerek kullanılmıyor;
gerekçe `.github/workflows/ci.yml` içindeki notlarda. "Sadeleştirme" amacıyla standart
action'lara geri dönmeden önce oradaki geçmişi oku.

---

## 8. Değişiklik yaparken

1. Skorlama davranışını değiştiren her düzenleme `tests/GovAI.Domain.Tests` ile doğrulanmalı.
   Bu testler ürünün davranış sözleşmesidir; bir testi "düzeltmek" için beklentiyi
   gevşetmeden önce davranışın gerçekten yanlış olduğundan emin ol.
2. Kural motoruna yeni bir alan eklerken **üç yeri birden** güncelle:
   `CompanyFieldResolver.SupportedFields`, `Resolve` switch'i, `rule_extractor.ALLOWED_FIELDS`.
   Sonra `python scripts/check_contract_parity.py` çalıştır.
3. Mimariyi etkileyen bir karar verirsen `docs/adr/` altına yeni bir ADR ekle.
   Mevcut ADR'leri değiştirme; karar değiştiyse yenisini yaz ve eskisini "Değiştirildi" işaretle.
4. Commit mesajları Türkçe, gövde "ne ve neden" anlatır.

## 9. Kullanıcı hakkında

İletişim Türkçedir. Kullanıcı .NET tarafında Onion mimarisi ve MediatR'sız düz servisleri
tüm projelerinde standart olarak uygular — bunu her seferinde yeniden sormaya gerek yok.
