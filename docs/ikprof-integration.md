# IKPROF entegrasyonu — API sözleşmesi ve alan eşlemesi

IKPROF, TalentHub'ın web ve mobil çalışan ERP/CRM/HRM yazılımıdır. GOVAI'nin ilk ERP
entegrasyonu buradan denenecek; ileride başka ERP'ler de bağlanacağı için bağlantı
**IKPROF'a özel yazılmaz**, bir liman (port) arkasına konur ve IKPROF yalnızca ilk
uyarlayıcıdır (adapter).

> **Durum:** Sözleşme ve iskele hazır, **gerçek bağlantı yok.** IKPROF tarafının API
> bilgileri gelmedi; bu belgede tanımlı uçlar GOVAI tarafında karşılanır, IKPROF tarafı
> ise sahte (mock) uyarlayıcıyla temsil edilir. "Bağlantı kuruldu" diye raporlanmaz.

---

## 1. Kapsam

MVP iki yönlüdür ama **dar**dır. Kapsamı daraltan şey teknik değil hukukidir: aktarılan
her kişisel veri, aktarılmasa da işin yürüdüğü ölçüde fazladır.

### 1.1 IKPROF → GOVAI (yalnızca şirket seviyesi)

Amaç: firma profilini elle doldurma yükünü kaldırmak. GOVAI'nin skorlaması zaten
şirket seviyesinde çalışır; çalışan kırılımına ihtiyacı yoktur.

### 1.2 GOVAI → IKPROF

Amaç: mevzuat ve fırsat bilgisini çalışanın günlük kullandığı ekrana taşımak.

### 1.3 MVP'de **aktarılmayacak** veriler

Bunlar sözleşmede **hiç tanımlı değildir** — "gönderilmiyor" değil, alanı yoktur:

- Çalışan adı, soyadı
- T.C. kimlik numarası
- Bireysel ücret, maaş bordrosu
- Banka ve IBAN bilgisi
- Sağlık, sendika üyeliği, ceza mahkûmiyeti gibi özel nitelikli kişisel veriler

Sözleşmede alan olmaması bilinçli bir korumadır: alan varsa bir gün dolar.

---

## 2. Güvenlik modeli

| Konu | Karar |
|---|---|
| Kimlik | OAuth2 **client credentials** (makine kimliği). Kullanıcı jetonu kullanılmaz; entegrasyon bir kişinin oturumuna bağlanmaz. |
| Kapsam (scope) | `ikprof.company.write` (gelen), `ikprof.signal.read` (giden). Tek bir istemci ikisini de alabilir ama uçlar ayrı yetkilendirilir. |
| Kiracı yalıtımı | Jeton tek bir `tenantId` taşır. Başka kiracının şirketine yazma denemesi **404** döner — 403 değil: varlığın var olduğu bile sızdırılmaz. |
| Şirket yalıtımı | `externalCompanyId` → GOVAI `companyId` eşlemesi kiracı içinde tekildir. Eşleme yoksa kayıt açılmaz; sessizce yeni şirket oluşturulmaz. |
| Idempotency | Her istek `Idempotency-Key` başlığı taşır. Aynı anahtar 24 saat içinde tekrar gelirse **ilk sonucun kopyası** döner, ikinci kayıt açılmaz. |
| Yeniden deneme | 5xx ve ağ hatalarında üstel geri çekilme, en fazla 5 deneme. Sonrasında mesaj ölü mektup kuyruğuna gider ve operatöre görünür. |
| Denetim kaydı | Her başarılı yazma `Integration.CompanySnapshotApplied` olarak audit log'a düşer. **Gerçekleşmeyen işlem yazılmaz.** |
| Onay | Şirket sahibi entegrasyonu panelden açmadan hiçbir veri kabul edilmez. Onay geri alınabilir; alındığında akış durur. |
| Sır yönetimi | `clientSecret` ve jetonlar **loglanmaz**, hata mesajına ve rapora girmez. Yapılandırmada saklanır, kod ve veritabanında düz metin tutulmaz. |
| Veri minimizasyonu | Sözleşmede tanımlı olmayan alan gövdede gelse bile **yok sayılır**, saklanmaz. |
| Silme | GOVAI, IKPROF'ta veri silemez. IKPROF'un GOVAI'de silebileceği tek şey kendi gönderdiği eşlemedir. |

---

## 3. IKPROF → GOVAI

### `POST /api/integrations/ikprof/company-snapshot`

Başlıklar: `Authorization: Bearer <token>`, `Idempotency-Key: <uuid>`

```json
{
  "externalCompanyId": "IKPROF-4821",
  "taxNumber": "1234567890",
  "legalName": "Örnek Üretim ve Teknoloji A.Ş.",
  "mainSector": "Makine ve ekipman imalatı",
  "naceCodes": ["28.41", "62.01"],
  "enterpriseSizeHint": "Small",
  "cities": ["Mersin"],
  "departments": [{ "code": "URT", "name": "Üretim", "employeeCount": 30 }],
  "employeeCount": 42,
  "employment": { "womenEmployeeCount": 14, "youngEmployeeCount": 9, "disabledEmployeeCount": 1, "rAndDEmployeeCount": 7 },
  "financials": { "annualRevenue": 50000000, "balanceSize": 30000000, "currency": "TRY", "fiscalYear": 2025 },
  "observedAt": "2026-09-07T06:00:00Z"
}
```

Yanıt: `200` (uygulandı) / `202` (kuyruğa alındı) / `409` (eşleme yok) / `422` (doğrulama).

### Alan eşleme tablosu

| IKPROF alanı | GOVAI karşılığı | Not |
|---|---|---|
| `externalCompanyId` | `CompanyIntegrationLink.ExternalId` | Eşlemenin anahtarı; şirket oluşturmaz |
| `taxNumber` | `Company.TaxNumber` | Yalnızca **doğrulama** için; eşleşmezse istek reddedilir, üzerine yazılmaz |
| `legalName` | `Company.LegalName` | |
| `mainSector` | `Company.MainSector` | `ActivityCatalog`'da yoksa **reddedilir** |
| `naceCodes[]` | `Company.NaceCodes` | İlki birincil. Katalog dışı kod ve sektörle tutmayan kod reddedilir |
| `enterpriseSizeHint` | — | **Yok sayılır.** Ölçek çalışan ve cirodan türetilir; dışarıdan kabul edilmez |
| `cities[]` | `Company.Locations[].City` | İlki merkez sayılır |
| `departments[]` | `Company.Departments` | Yalnızca kod, ad ve sayı; kişi yok |
| `employeeCount` | `Workforce.EmployeeCount` | |
| `employment.womenEmployeeCount` | `Workforce.WomenEmployeeCount` | |
| `employment.youngEmployeeCount` | `Workforce.YoungEmployeeCount` | Panelde girilemiyor; ERP'den gelmesi bu boşluğu kapatır |
| `employment.disabledEmployeeCount` | `Workforce.DisabledEmployeeCount` | Aynı boşluk |
| `employment.rAndDEmployeeCount` | `Workforce.RAndDEmployeeCount` | |
| `financials.annualRevenue` | `Financials.AnnualRevenue` | |
| `financials.balanceSize` | `Financials.BalanceSize` | |
| `financials.currency` | `Financials.Currency` | |
| `financials.fiscalYear` | `Financials.FiscalYear` | |
| `observedAt` | `CompanyIntegrationLink.LastSnapshotAt` | Eski damgalı anlık görüntü yeniyi ezmez |

Profil değiştiğinde `ProfileVersion` artar ve yeniden skorlama kuyruğa girer — panelden
yapılan düzenlemeyle aynı yol.

---

## 4. GOVAI → IKPROF

GOVAI **çağırmaz**, IKPROF çeker (pull). Böylece GOVAI'nin IKPROF ağına erişmesi
gerekmez ve saldırı yüzeyi küçülür.

### `GET /api/integrations/ikprof/signals?companyId=&since=&cursor=`

```json
{
  "items": [
    {
      "signalId": "01a0-...",
      "type": "RegulatoryChange",
      "companyId": "01a0-...",
      "title": "SGK Sağlık Uygulama Tebliğinde Değişiklik",
      "summary": "…",
      "deadline": null,
      "link": "https://govai.yuppi.cloud/regulatory-changes/01a0-...",
      "publishedAt": "2026-08-29T00:00:00Z"
    }
  ],
  "nextCursor": "…"
}
```

| Sinyal türü | İçerik |
|---|---|
| `RegulatoryChange` | Mevzuat değişikliği uyarısı |
| `OpportunityMatch` | Şirkete eşleşen fon, hibe, teşvik, ihale |
| `Deadline` | Yaklaşan son başvuru tarihi |
| `ComplianceTask` | Uyum görevi |
| `Report` | Rapor bağlantısı |

Her sinyal yalnızca **bağlantı ve künye** taşır; müşteri verisi gövdede gitmez.

---

## 5. Bu fazda ne yapıldı

| | |
|---|---|
| Sözleşme, alan eşlemesi, güvenlik modeli | Bu belge |
| Liman (port) ve DTO'lar | `GovAI.Application/Integrations` |
| Sahte uyarlayıcı (mock) | `GovAI.Infrastructure/Integrations` |
| Idempotency, kiracı/şirket yalıtımı, veri minimizasyonu testleri | `GovAI.Application.Tests` |
| **Gerçek IKPROF bağlantısı** | **Yok** — API bilgileri gelmedi |

## 6. Açık kalanlar

- IKPROF'un OAuth2 sunucu adresi, `clientId` ve scope adları
- `externalCompanyId` eşlemesinin ilk kez nasıl kurulacağı (panelden mi, davetle mi)
- Sinyal çekme sıklığı ve `since` penceresi
- `youngEmployeeCount` / `disabledEmployeeCount` panelde de girilebilmeli mi (bugün girilemiyor)
