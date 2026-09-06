# API sözleşmesi

Taban adres: `http://localhost:8080` · Swagger: `/swagger` (yalnızca Development)

Tüm uçlar `/api/auth/login` dışında JWT ister: `Authorization: Bearer <token>`.
Enum'lar JSON'da **string** olarak taşınır. Hatalar RFC 7807 `ProblemDetails` formatındadır.

## Yetki politikaları

| Politika | Roller |
|---|---|
| `Read` | Oturum açmış tüm kullanıcılar |
| `Operate` | SuperAdmin, CompanyManager, OperationUser, Consultant |
| `ManageCompany` | SuperAdmin, CompanyManager |
| `SuperAdmin` | SuperAdmin |

## Hata kodları

| Durum | Ne zaman |
|---|---|
| 400 | Girdi doğrulama hatası (`ValidationProblemDetails`, alan bazlı) |
| 401 | Jeton yok, geçersiz veya süresi dolmuş |
| 403 | Rol veya firma kapsamı yetersiz |
| 404 | Kayıt yok |
| 422 | İş kuralı ihlali (`DomainException`) |
| 500 | Beklenmeyen hata — ayrıntı sızdırılmaz, `correlationId` döner |

---

## `/api/auth`

| Metot | Yol | Yetki | Açıklama |
|---|---|---|---|
| POST | `/login` | Anonim | E-posta + parola → JWT |
| GET | `/me` | Read | Oturumdaki kullanıcının bilgileri |

```http
POST /api/auth/login
{ "email": "admin@govai.local", "password": "..." }

200 OK
{
  "accessToken": "eyJ...",
  "expiresAt": "2026-08-15T13:56:00+00:00",
  "refreshToken": "...",
  "user": { "id": "...", "role": "SuperAdmin", ... }
}
```

## `/api/sources` — kaynak tanımı, tarama takvimi, veri çekme logları

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/sources?onlyEnabled=true` | Read |
| GET | `/api/sources/{id}` | Read |
| POST | `/api/sources` | SuperAdmin |
| PUT | `/api/sources/{id}` | SuperAdmin |
| POST | `/api/sources/{id}/enabled?enabled=false` | SuperAdmin |
| POST | `/api/sources/{id}/crawl` | Operate |
| POST | `/api/sources/documents` | Operate |
| POST | `/api/sources/{id}/runs` | Operate |

`POST /documents` collector worker'ının giriş noktasıdır. İçerik hash'i değişmediyse
`contentChanged: false` döner ve hiçbir iş kuyruğa alınmaz.

## `/api/opportunities` — çağrı listesi, filtreleme, detay

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/opportunities` | Read |
| GET | `/api/opportunities/{id}` | Read |
| POST | `/api/opportunities` | Operate |
| PUT | `/api/opportunities/{id}/rules/{ruleId}` | Operate |
| POST | `/api/opportunities/{id}/review` | Operate |

Arama parametreleri: `search`, `categories[]`, `sourceTypes[]`, `onlyOpen`, `onlyReviewed`,
`publishedAfter`, `deadlineBefore`, `sort`, `page`, `pageSize`.

`PUT /rules/{ruleId}` danışmanın otomatik çıkarılan kuralı düzeltmesidir; elle düzeltilen
kurallar sonraki otomatik çıkarımlarda korunur.

## `/api/company-profile` — firma kartı, ERP eşitleme

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/company-profile` | Read |
| GET | `/api/company-profile/{id}` | Read |
| POST | `/api/company-profile` | ManageCompany |
| PUT | `/api/company-profile/{id}` | ManageCompany |
| POST | `/api/company-profile/erp-sync` | ManageCompany |
| DELETE | `/api/company-profile/{id}` | ManageCompany |

ERP eşitlemesi **kısmi**dir: yalnızca gönderilen bölümler güncellenir.

```http
POST /api/company-profile/erp-sync
{
  "taxNumber": "1234567890",
  "sourceSystem": "Logo",
  "workforce": { "employeeCount": 42, "womenEmployeeCount": 16, ... }
}

200 OK
{ "companyId": "...", "profileVersion": 7, "updatedSections": ["Workforce"], "rescoringQueued": true }
```

`rescoringQueued: true` ise profil değişmiştir ve skorlar yeniden hesaplanmak üzere kuyruğa alınmıştır.

## `/api/reference` — form önerileri (sektör ve NACE)

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/reference/sectors?q=` | Read |
| GET | `/api/reference/nace?q=&sector=` | Read |

Firma kartındaki sektör ve NACE alanları **yazılmaz, seçilir**. Bu uçlar arayüzün
öneri listesini besler; `POST /api/companies` aynı kataloğu kullanarak doğrular, bu
yüzden listede görünen her değer kayıtta da geçerlidir.

`sectors` sorgusuz çağrılırsa tüm liste döner (liste kısadır). `nace` en az **3 karakter**
ister; sorgu koda (`256`) da tanıma (`yazılım`) da uyar ve Türkçe büyük/küçük harf farkına
takılmaz. `sector` verilirse o sektörün kodları öne alınır, diğerleri gizlenmez.

```json
[{ "code": "25.62", "title": "Metallerin makinede işlenmesi ve şekil verilmesi", "sector": "Metal sanayi ve fabrikasyon" }]
```

Koddaki nokta gösterim içindir; kayıt noktasız saklanır (`2562`).

## `/api/eligibility` — uygunluk analizi, eksik koşullar, gerekçe

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/eligibility/companies/{companyId}/matches` | Read |
| GET | `/api/eligibility/{assessmentId}` | Read |
| POST | `/api/eligibility/evaluate` | Operate |
| POST | `/api/eligibility/companies/{companyId}/rescore` | Operate |
| POST | `/api/eligibility/{assessmentId}/summary` | Operate |

`GET /companies/{companyId}/matches` listesi **önce sektör uyumuna**, sonra
`sort` parametresine göre sıralanır. Her satır bir `sectorFit` alanı taşır:
`Matched` → `Unverified` → `NotMatched`. Sektörü tutmayan kayıt listeden çıkarılmaz,
en altta kalır (gerekçe: `docs/scoring.md` §2.2).

`GET /{assessmentId}` ürünün açıklanabilirlik vaadinin karşılığıdır:

```json
{
  "finalScore": 78.4,
  "confidence": 0.86,
  "verdict": "ConditionallyEligible",
  "sectorFit": "Matched",
  "dimensions": [
    {
      "dimension": "Employment",
      "dimensionLabel": "Personel yapısı",
      "value": 0.83,
      "weight": 0.15,
      "contribution": 0.1245,
      "rationale": "3 koşuldan 2 tanesi sağlanıyor."
    }
  ],
  "blockingFailures": [],
  "missingConditions": [
    {
      "requirement": "En az 3 Ar-Ge personeli.",
      "expectedValue": ">= 3",
      "actualValue": "2",
      "suggestedAction": "En az 3 Ar-Ge personeli — mevcut 2, hedef 3. Aradaki farkı kapatın.",
      "sourceExcerpt": "Proje ekibinde en az 3 Ar-Ge personeli görevlendirilmelidir."
    }
  ],
  "dataGaps": [],
  "documentChecklist": [ ... ]
}
```

## `/api/scoring` — puanlama detayı, sıralama, simülasyon

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/scoring/weights` | Read |
| GET | `/api/scoring/companies/{companyId}/ranking?top=20` | Read |
| POST | `/api/scoring/companies/{companyId}/simulate?persist=true` | Operate |
| GET | `/api/scoring/companies/{companyId}/simulations` | Read |

```http
POST /api/scoring/companies/{id}/simulate
{ "name": "Personel 15'e çıkarsa", "employeeCount": 15, "addCertificateCodes": ["ISO9001"] }

200 OK
{
  "baselineEligibleCount": 3,
  "simulatedEligibleCount": 7,
  "eligibleCountDelta": 4,
  "averageScoreDelta": 11.6,
  "newlyEligible": [ { "opportunityTitle": "...", "delta": 24.5 } ]
}
```

Simülasyon firma kaydına **dokunmaz**.

## `/api/reports` — PDF, Excel, dashboard veri setleri

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/reports/companies/{companyId}/dashboard` | Read |
| GET | `/api/reports/companies/{companyId}/export/excel` | Read |
| GET | `/api/reports/companies/{companyId}/export/pdf` | Read |

Dışa aktarım uçları dosya döner (`Content-Disposition: attachment`).

## `/api/notifications`

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/notifications` | Read |
| POST | `/api/notifications/{id}/read` | Read |
| POST | `/api/notifications/dispatch` | SuperAdmin |

## `/api/admin` — kullanıcı yönetimi, audit log

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/admin/users` | SuperAdmin |
| POST | `/api/admin/users` | SuperAdmin |
| PUT | `/api/admin/users/{id}/role?role=Consultant` | SuperAdmin |
| PUT | `/api/admin/users/{id}/active?isActive=false` | SuperAdmin |
| GET | `/api/admin/audit-log` | SuperAdmin |

## `/health`

Kimlik doğrulaması gerektirmez. PostgreSQL erişilebilirliğini kontrol eder;
Docker healthcheck ve yük dengeleyici tarafından kullanılır.

---

## Sayfalama

Liste uçları ortak zarf döner:

```json
{ "items": [], "totalCount": 0, "page": 1, "pageSize": 25, "totalPages": 0 }
```

`pageSize` üst sınırı 200'dür.
