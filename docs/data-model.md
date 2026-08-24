# Veri modeli

PostgreSQL şeması: `govai`. Tablo ve kolon adları snake_case'dir
(`EFCore.NamingConventions` ile otomatik dönüştürülür).

## 1. Merkezdeki iki veri kümesi

Proje dosyasındaki tasarım korunmuştur: kurumsal profil verisi ve fırsat/çağrı verisi kural
motorunda buluşur.

```
   companies  ──────┐                    ┌────── opportunities
   (CompanyProfile) │                    │       (OpportunityRule)
                    ▼                    ▼
              ┌──────────────────────────────┐
              │   eligibility_assessments    │
              │   + assessment_dimensions    │
              └──────────────────────────────┘
```

## 2. Tablolar

### Kimlik ve kiracı

| Tablo | Açıklama |
|---|---|
| `tenants` | Müşteri hesabı. Danışmanlık senaryosunda bir kiracı birden çok firmayı yönetir. Paket ve firma kotası burada. |
| `users` | Platform kullanıcısı. `role` kiracı içinde beş seviyeden biri, ayrıca üç platform işletim rolü (≥10) vardır ve bunlar kiracı yöneticisi tarafından atanamaz. `scoped_company_ids` (jsonb) Faz 1'den beri yalnızca geriye uyumluluk içindir. |
| `user_companies` | **Şirket erişiminin tek kaynağı** (Faz 1). Kullanıcı–şirket üyeliği ve şirket içi rolü. `is_default = true` için kısmi tekil indeks: bir kullanıcının en fazla bir varsayılan şirketi olur. |
| `company_invitations` | E-posta ile şirkete davet. Jetonun yalnızca SHA-256 **özeti** saklanır; açık metin bir kez, oluşturma yanıtında döner. |

### Firma profili

| Tablo | Açıklama |
|---|---|
| `companies` | Firma kartı. `workforce` ve `financials` sahipli (owned) tip olarak aynı tabloda kolonlara açılır. `profile_version` eşzamanlılık jetonudur ve her anlamlı değişiklikte artar. |
| `company_nace_codes` | Faaliyet kodları. `(company_id, code)` benzersiz. |
| `company_locations` | Tesis/şube. `nuts2_code` kalkınma ajansı çağrılarında kritik; indekslidir. |
| `company_certificates` | Belgeler. `valid_until` null ise süresiz. |
| `company_investments` | Devam eden/planlanan yatırımlar. |
| `company_groups` | Şirket grubu / holding (Faz 1). Kiracıya bağlıdır. |
| `company_verification_requests` | Vergi numarası başka bir çalışma alanında kayıtlıysa açılan talep (Faz 1). Karşı tarafa **bilerek hiçbir yabancı anahtar tutmaz**; yalnızca vergi numarasını ve talebi açan tarafı bilir. |

`companies` üzerindeki `(tenant_id, tax_number)` benzersiz indeksi aynı kiracıda mükerrer
firma kaydını engeller.

Faz 1 ile `companies` ayrıca grup ve ana–bağlı şirket bağını taşır: `group_id`,
`parent_company_id`, `relationship_type`, `is_head_company`. Döngüsel bağ
(A → B → A) servis katmanında reddedilir.

### Kaynak ve doküman

| Tablo | Açıklama |
|---|---|
| `sources` | İzlenen resmî kaynak. `cron_expression` tarama takvimi, `configuration_json` (jsonb) kaynağa özgü CSS seçicileri. `consecutive_failure_count` 5'e ulaşınca kaynak otomatik devre dışı kalır. |
| `source_documents` | Ham doküman. `(source_id, url)` benzersiz — aynı ilan tek kayıttır, değişiklikler `revision` ile izlenir. `content_hash` (SHA-256) gereksiz yeniden işlemeyi önler. |

### Fırsat

| Tablo | Açıklama |
|---|---|
| `opportunities` | Çağrı künyesi. `budget` sahipli tip. `source_document_id` üzerinde **filtreli** benzersiz indeks (`WHERE source_document_id IS NOT NULL`) — parser aynı dokümanı yeniden işlediğinde kopya kayıt oluşmaz, elle girilen çağrılar ise bu kısıttan muaftır. |
| `opportunity_rules` | Makine-değerlendirilebilir koşullar. `source_excerpt` metindeki dayanağı saklar; `is_manually_overridden` danışman düzeltmesini korur. |
| `opportunity_documents` | Başvuru dosyasında istenen evraklar. |

### Değerlendirme

| Tablo | Açıklama |
|---|---|
| `eligibility_assessments` | Bir firma–çağrı çiftinin belirli andaki sonucu. Her hesaplama yeni kayıt üretir; eskisi `is_latest = false` olur. `detail_json` (jsonb) kural sonuçlarının ve belge listesinin tam dökümüdür. |
| `assessment_dimensions` | Yedi boyutun puanı, ağırlığı, katkısı ve gerekçesi. Dashboard grafiklerini besler. |
| `scenario_simulations` | "What-if" senaryosu ve özet sonucu. `changes_json` uygulanan değişiklikler. |
| `scenario_impacts` | Senaryonun tek bir fırsat üzerindeki etkisi. |

Kritik indeks: `(company_id, is_latest, final_score)` — dashboard'ın ana sorgusunu
tek indeks taramasıyla karşılar.

### Bildirim ve denetim

| Tablo | Açıklama |
|---|---|
| `notifications` | `deduplication_key` **benzersizdir** — aynı uyarı iki kez üretilemez. Anahtar formatı: `deadline:{opportunityId}:{companyId}:7d`. |
| `audit_log` | Yalnızca ekleme yapılan denetim kaydı. Kullanıcı, IP, user-agent, korelasyon kimliği ve zaman damgası. |

## 3. Doküman modelinin kod karşılığı

Proje dosyasındaki taslak modelin nereye düştüğü:

| Doküman alanı | Kod |
|---|---|
| `companyId` | `companies.id` |
| `legalType` | `companies.legal_type` |
| `naceCodes[]` | `company_nace_codes` |
| `locations[]` | `company_locations` |
| `employeeCount` | `companies.employee_count` (`Workforce`) |
| `womenEmployeeRate` | `Workforce.WomenEmployeeRate` — sayıdan türetilir, saklanmaz |
| `youngEmployeeRate` | `Workforce.YoungEmployeeRate` — türetilir |
| `rAndDEmployeeCount` | `companies.rnd_employee_count` |
| `annualRevenue` / `balanceSize` | `companies.annual_revenue` / `balance_size` |
| `exportFlag` / `technologyFlag` | `companies.export_flag` / `technology_flag` |
| `certificates[]` | `company_certificates` |
| `activeInvestments[]` | `company_investments` |
| `opportunityId` | `opportunities.id` |
| `sourceType` / `supportCategory` | `opportunities.source_type` / `support_category` |
| `eligibleNaceCodes[]` | `opportunity_rules` (`NaceMatch` operatörlü kural) |
| `minEmployeeCount` / `maxRevenue` | `opportunity_rules` (`GreaterThanOrEqual` / `LessThanOrEqual`) |
| `regionConstraints[]` | `opportunity_rules` (`ContainsAny`, `Company.Nuts2Codes`) |
| `requiredCertificates[]` | `opportunity_rules` (`ContainsAll`) + `opportunity_documents` |
| `deadline` / `budgetRange` | `opportunities.deadline` / `budget_min`,`budget_max` |
| `documentChecklist[]` | `opportunity_documents` |

**Neden ayrı kolon değil de genel kural tablosu:** dokümandaki `minEmployeeCount`, `maxRevenue`
gibi sabit alanlar her yeni destek tipinde şema değişikliği gerektirirdi. Kural tablosu,
"yeni sektörler veya destek tipleri eklendiğinde sistemin baştan yazılmadan genişletilebilmesi"
hedefini karşılar. Oranlar (`womenEmployeeRate`) da saklanmaz, sayılardan türetilir —
böylece iki alanın birbiriyle çelişmesi mümkün değildir.

## 4. Ortak davranışlar

| Arayüz | Etki |
|---|---|
| `IAuditable` | `created_at/by`, `updated_at/by` `SaveChanges` sırasında otomatik doldurulur |
| `ISoftDeletable` | Silme isteği `is_deleted = true`'ya çevrilir; global sorgu filtresi bu kayıtları gizler |
| `ITenantScoped` | `tenant_id` taşır; servis katmanı kiracı sınırını uygular |

Kimlikler `Guid.CreateVersion7()` ile üretilir — zaman sıralı UUID, B-tree indeks
parçalanmasını rastgele GUID'lere kıyasla belirgin biçimde azaltır.

Anahtarları **domain üretir, veritabanı değil.** Bu, modelde açıkça
`ValueGenerated.Never` olarak bildirilir (`GovAiDbContext.ApplyDomainGeneratedKeys`).
EF'in Guid anahtarlar için varsayılan kabulü "ekleme sırasında üretilir"dir ve bu sessiz
bir veri kaybına yol açar: izlenen bir kök nesnenin koleksiyonuna yeni bir alt kayıt
eklendiğinde EF, anahtarın dolu olmasına bakıp kaydı `Added` değil `Modified` sayar;
INSERT hiç üretilmez ve var olmayan satıra giden UPDATE `DbUpdateConcurrencyException`
atar. Firma profiline ilk konum veya NACE kodu eklenirken tetikleniyordu.

## 5. Migration

İlk şema: `src/GovAI.Persistence/Migrations/20260815115649_InitialSchema.cs` (18 tablo).

Faz 1: `20260824205224_MultiCompanyFoundation.cs` — 4 yeni tablo, `companies` üzerinde 19
yeni sütun, 13 indeks. Migration ayrıca:

- Mevcut her firma için sahibine `CompanyOwner` üyeliği geri doldurur
  (`created_by = 'migration:MultiCompanyFoundation'`), böylece yükseltmeden sonra kimse
  erişimini kaybetmez.
- `companies.tenant_id` için eksik olan yabancı anahtarı ekler. Öksüz kayıt varsa
  **bilerek durur**; sessizce atlamak kısıtı anlamsız kılardı.
- `Down()` dört tabloyu ve tüm yeni sütunları geri alır, geri doldurduğu satırları siler.

Gerçek PostgreSQL 17 üzerinde veri ile uygula–geri al–tekrar uygula sınandı: veri korunur.

CI, `migrations has-pending-model-changes` ile modelin migration'larla uyumlu kaldığını doğrular.

```bash
dotnet dotnet-ef migrations add <Ad> --project src/GovAI.Persistence --startup-project src/GovAI.Api
```

CI, `migrations has-pending-model-changes` ile modelin migration'larla uyumlu kaldığını doğrular.
