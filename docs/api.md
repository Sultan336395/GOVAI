# API sözleşmesi

Taban adres: `http://localhost:8080` · Swagger: `/swagger` (yalnızca Development)

Tüm uçlar `/api/auth/login` dışında JWT ister: `Authorization: Bearer <token>`.
Enum'lar JSON'da **string** olarak taşınır. Hatalar RFC 7807 `ProblemDetails` formatındadır.

## Yetki politikaları

İki kademe vardır ve karıştırılmamalıdır. **Politika** jetondaki kiracı/platform rolüne
bakar ve ucun kapısıdır; **firma izni** (`CompanyPermission`) o firmadaki üyelikten
veritabanında okunur ve asıl kararı verir. Aşağıdaki tabloların ikisi de geçerlidir:
bölüm tablolarındaki `Read` / `Operate` / `ManageProfile` değerleri firma iznidir,
politikanın kendisi değil.

| Politika | Roller |
|---|---|
| `Read` | Oturum açmış tüm kullanıcılar |
| `Operate` | SuperAdmin, CompanyManager, OperationUser, Consultant |
| `ManageCompany` | SuperAdmin, CompanyManager |
| `SuperAdmin` | SuperAdmin |
| `CompanyData` | Kiracı rolleri (ReadOnly dahil). Platform rolleri ve worker **hariç** |
| `ManageTenantCompanies` | Kiracı rolleri. Asıl karar üyelikten verilir |
| `Rescore` | Kiracı operasyon rolleri + worker |
| `PlatformCatalog` | PlatformCatalogManager |
| `PlatformReview` | PlatformCatalogManager, PlatformReviewer |
| `SystemIngest` | SystemIngest (worker) + PlatformCatalogManager |

`CompanyData` platform rollerini bilinçli olarak dışarıda bırakır: veri toplama ve
katalog işletimi kimliği müşteri verisini görmez.

| Firma izni | Hangi firma rolleri karşılar |
|---|---|
| `Read` | Owner, Manager, Expert, Viewer |
| `Operate` | Owner, Manager, Expert |
| `ManageProfile` | Owner, Manager |
| `ViewMembers` | Owner, Manager |
| `ManageMembers` | Owner |

Matris tek yerde tanımlıdır: `CompanyAccessGuard.Satisfies`. Uzman ve görüntüleyicinin
firmadaki diğer kişilerin adını ve e-postasını görmesi için bir gerekçe yoktur; bu
yüzden `ViewMembers` listeyi **okumayı** da sınırlar.

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
takılmaz.

`sector` birden çok kez verilebilir ve liste o sektörlerin kodlarıyla **sınırlanır**
(sıralanmaz, filtrelenir). Sektör ile NACE kodunun çelişmesi böyle önlenir: kullanıcı
sektörüne ait olmayan bir kodu göremez, `POST /api/companies` de aynı kısıtı doğrular.

`sector` verilip `q` verilmezse (ya da `q` üç karakterden kısaysa) o sektörün **tüm**
kodları koda göre sıralı döner ve kırpılmaz. Arayüz alana tıklandığı anda bu listeyi
gösterir. `sector` da yoksa boş döner — tüm katalog tek listede sunulmaz.

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

`/api/reports/companies/{companyId}/dashboard/insights` (Read) dört ek bölüm döner:
`actions`, `profile`, `funnel`, `trend`, `activity` ve boş bölümlerin sebebini yazan
`notes`. Ayrı bir uçtur: ana dashboard sayıları bu bölümler hesaplanamasa da görünür.

Profil doluluğu form alanlarını değil **kural motorunun okuduğu** alanları sayar;
her eksik alan o alana bakan kuralda `Unknown` demektir. Sertifika listesi ölçüye
girmez — boş küme "belgemiz yok" anlamına gelen geçerli bir cevaptır.

Huni basamakları daralan bir dizi **değildir**: üst iki basamak sistemin kararı,
alt üçü firmanın beyanıdır; firma sistemin uygun görmediği bir ihaleyi de takibe
alabilir.

## `/api/reports/weekly` — otonom haftalık rapor

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/reports/weekly/companies/{companyId}` | Read |
| GET | `/api/reports/weekly/{reportId}` | Read |
| POST | `/api/reports/weekly/companies/{companyId}` | Read |
| GET | `/api/reports/weekly/{reportId}/pdf` | Read |
| GET | `/api/reports/weekly/{reportId}/excel` | Read |
| GET | `/api/reports/weekly/{reportId}/questions` | Read |
| POST | `/api/reports/weekly/{reportId}/questions` | Operate |
| POST | `/api/reports/weekly-batch` | SystemIngest |

Rapor bir **anlık görüntüdür**: üretildiği andaki değerlendirmeleri taşır ve sonradan
yeniden hesaplanmaz. `POST .../companies/{companyId}` aynı hafta için ikinci kayıt
açmaz, mevcut kaydı günceller.

Toplu üretimi (`weekly-batch`) kullanıcı değil zamanlayıcı çağırır ve kiracının
tamamına dokunur; bu yüzden ayrı ve daha dar bir yetki altındadır.

### Rapora soru sorma

`POST .../questions` gövdesi yalnızca **anahtar** alır, serbest metin almaz:

```json
{ "questionKey": "BasvuruOnceligi", "parentInquiryId": null }
```

Tanınmayan anahtar 404 döner ve kayıt açılmaz. Her rapor için **5 soru hakkı** vardır;
hak cevap üretilince harcanır. Daha önce sorulmuş bir soru yeniden açılırsa kayıtlı
cevap döner ve hak harcanmaz. Hak bittiğinde yeni soru 400 ile reddedilir.

Cevap sunucuda **rapordan hesaplanır**; model çağrısı yoktur, bu yüzden uç OpenAI
anahtarı tanımlı olmadan da çalışır.

## `/api/tenders` — ihale başvuru takibi

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/tenders/companies/{companyId}` | Read |
| POST | `/api/tenders/companies/{companyId}` | Operate |
| POST | `/api/tenders/{pursuitId}/status` | Operate |

Takip firmanın **kendi beyanıdır**; skoru, kararı ve sıralamayı etkilemez. Tahta
satırlarında sistemin kendi değerlendirmesi (`assessment`) ayrıca döner — hiç
değerlendirilmemiş bir ihalede `null`'dır, uydurma bir puan yazılmaz.

Aşamalar: `Inceleniyor`, `Hazirlaniyor`, `TeklifVerildi`, `Sonuclandi`, `Vazgecildi`.
Geçişler serbesttir (geriye dönüş ve yeniden açma dâhil); her değişiklik `history`
altına kim ve ne zaman bilgisiyle yazılır.

`Sonuclandi` aşamasında `outcome` (`Kazanildi` / `Kaybedildi` / `Iptal`) **zorunludur**;
verilmezse 422 döner. Başka aşamada sonuç yazmak da 422 ile reddedilir.

Aynı firma aynı ihaleyi iki kez takibe alamaz: ikinci istek yeni kayıt açmaz, mevcut
kaydı döner. Karantinadaki bir çağrı takibe alınamaz.

Takip **silinmez**; bırakılan süreç `Vazgecildi` olur, böylece "bu ihaleye neden
girmedik" sorusunun cevabı geçmişiyle kalır.

## `/api/calibration` — uzman görüşü ve karar doğruluğu

| Metot | Yol | Yetki |
|---|---|---|
| POST | `/api/calibration/verdicts` | Operate |
| GET | `/api/calibration/companies/{companyId}/verdicts` | Read |
| POST | `/api/calibration/companies/{companyId}/ai-review` | Operate |
| GET | `/api/calibration/report` | Read |
| POST | `/api/calibration/ai-review-batch` | SystemIngest |

Uzman görüşü karar mekanizmasının **girdisi değil denetçisidir**: skoru, kararı ve
ağırlıkları değiştirmez. Etkileseydi aynı firma–çağrı çifti geçmişte kimin baktığına
göre farklı puan alırdı ve kayan puanın dayanağı çağrı metninde bulunmazdı.

Aynı değerlendirme için ikinci kayıt açılmaz, mevcut kayıt güncellenir. Kayıt
**silinmez** — uyumsuz çıkanların silinebilmesi, raporu istenen sonuca göre
şekillendirmeyi mümkün kılardı.

`ai-review` yapay zekâdan ikinci görüş toplar ve bu görüş de hiçbir skoru
değiştirmez; yalnızca ayrışan vakaları insana işaret eder. Anahtar tanımlı değilse
görüş **uydurulmaz**: yanıt `aiEnabled: false` döner ve hiçbir kayıt açılmaz.

`report` iki kaynağı **ayrı** özetler (`human`, `ai`) ve ortak toplam alanı **yoktur**:
danışman görüşü kalibrasyon ölçütüdür, yapay zekâ görüşü yalnızca taramadır. İkisi
aynı metni okur ve aynı yanlışı birlikte yapabilirler; yüksek uyum oranı doğruluk
değil ortak körlük olabilir. Örneklem 20'nin altındaysa rapor "yorumlanamaz"
işaretlenir.

Ayrıntı: `docs/adr/0005`.

## `/api/erp` — ERP bağlantısı ve veri çekme

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/erp/field-map-defaults/{vendor}` | Read |
| GET | `/api/erp/companies/{companyId}/connection` | Read |
| PUT | `/api/erp/companies/{companyId}/connection` | ManageProfile |
| POST | `/api/erp/companies/{companyId}/connection/enabled` | ManageProfile |
| DELETE | `/api/erp/companies/{companyId}/connection` | ManageProfile |
| POST | `/api/erp/companies/{companyId}/pull` | ManageProfile |
| POST | `/api/erp/pull-batch` | SystemIngest |

Bağlantı kurulmamışsa `GET .../connection` **204** döner; bu bir hata değildir.

Alan eşlemesi panelden düzenlenir ve ürün varsayılanları
`field-map-defaults/{vendor}` ile **sunucudan** alınır — arayüze kopyalanmış ikinci
bir varsayılan listesi, iki taraf ayrıştığında sessizce yanlış alan okurdu.

**Eşlemede aranıp ERP yanıtında bulunamayan alan `null` kalır, `0` yazılmaz.** Sıfır
yazmak firmayı "hiç kadın çalışanı yok" diye kaydeder ve o firma kadın istihdamı
şartı arayan her çağrıdan elenir. Aynı sebeple bir bölümün tamamı boşsa o bölüm hiç
gönderilmez.

Kimlik bilgisi AES-GCM ile şifrelenir ve **hiçbir yanıtta dönmez**; ERP sunucusunun
hata gövdesi de kullanıcıya yansıtılmaz (gövde kimlik ya da personel verisi
taşıyabilir). Özel IP'lere yalnızca "kurum içi" beyanıyla gidilir; bulut metadata
adresi (169.254.169.254) **beyanla dahi** açılmaz.

Ardışık beş başarısızlıkta bağlantı kendiliğinden devre dışı kalır.

Ayrıntı: `docs/adr/0006`.

## `/api/notifications`

| Metot | Yol | Yetki |
|---|---|---|
| GET | `/api/notifications` | Read |
| POST | `/api/notifications/{id}/read` | Read |
| POST | `/api/notifications/dispatch` | SystemIngest |

`dispatch` ucunu bir kullanıcı değil **zamanlayıcı worker'ı** çağırır; bu yüzden
yetkisi `SystemIngest`'tir. Kiracı yöneticisine açmak, bir kullanıcının bütün
kiracının bildirim gönderimini tetikleyebilmesi demek olurdu.

`dispatch` cevabı işlenen sayıyı değil **ne olduğunu** döner: `processedCount`,
`sentCount`, `failedCount`, `skippedCount`.

"Gönderildi" damgası yalnızca **gerçekten gönderilince** basılır. SMTP
yapılandırılmamışsa e-posta bildirimi `skipped` sayılır ve deneme hakkı harcanmaz;
yapılandırma tamamlandığında birikmiş bildirimler gider. Başarısız gönderim sebebiyle
birlikte kayda yazılır ve en fazla üç kez denenir.

Alıcılar bildirimde saklanmaz, gönderim anında çözülür: firmanın **etkin** üyeleri,
görüntüleyici rolü hariç. Firma bağlantısı olmayan sistem uyarıları kiracı
yöneticilerine gider.

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
