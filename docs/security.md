# Güvenlik temeli

Bu belge Faz 0 kapsamında kurulan güvenlik modelini anlatır: kiracı izolasyonu,
şirket erişim kontrolü ve JWT imza anahtarı yönetimi.

---

## 1. JWT imza anahtarı

Anahtar **kod tabanında üretilmez ve saklanmaz**. Ortam değişkeninden veya güvenli bir
secret deposundan gelmelidir.

```bash
# Docker / compose
Jwt__SigningKey=<en az 32 karakter, rastgele üretilmiş>
```

`deploy/.env` içinde `JWT_SIGNING_KEY` olarak verilir; compose bunu API kapsayıcısına
`Jwt__SigningKey` adıyla aktarır. Üretmek için:

```bash
openssl rand -base64 48
```

### Uygulama şu durumlarda açılmayı reddeder

`JwtOptionsValidator` (`src/GovAI.Infrastructure/Options/JwtOptionsValidator.cs`)
`ValidateOnStart` ile bağlıdır; kurallardan biri ihlal edilirse uygulama başlamaz:

| Durum | Hangi ortamda reddedilir |
|---|---|
| Anahtar boş veya yalnızca boşluk | Hepsinde |
| 32 karakterden kısa | Hepsinde |
| İçinde `CHANGE_ME`, `changeme`, `your-secret`, `secret-key-here` geçiyor | Hepsinde |
| İçinde `LOCAL-DEV-ONLY` geçiyor (depodaki geliştirme anahtarı) | Development **dışındaki** her ortamda |

Uzunluk kontrolü tek başına yeterli değildi: depodaki örnek anahtar
`CHANGE_ME_AT_LEAST_32_CHARACTERS_LONG_SECRET` 45 karakter olduğu için `[MinLength(32)]`
doğrulamasını geçiyor ve üretimde fark edilmeden kullanılabiliyordu.

**Neden sessiz bir risk:** Veritabanı parolası unutulursa uygulama açılışta patlar ve
hemen fark edilir. Yanlış JWT anahtarı ise hiçbir hata üretmez — sistem, herkesin
GitHub'da görebildiği bir anahtarla imzalanmış jetonları kabul ederek çalışmaya devam eder.

### Anahtar değiştiğinde

Mevcut tüm oturumlar geçersizleşir ve kullanıcıların yeniden giriş yapması gerekir.
Bu beklenen davranıştır.

---

## 2. Kiracı izolasyonu — iki katmanlı savunma

### Katman 1: veritabanı sorgu filtresi

`GovAiDbContext.ApplyTenantFilters` kiracıya ait her varlığa zorunlu bir kiracı sınırı
uygular. Filtre, bağlam oluşturulurken çözülen tek bir kiracı kimliğine bakar.

Kiracı çözülemezse değer `Guid.Empty` kalır ve **hiçbir satır eşleşmez**. Bu bilinçlidir:
kimliği belirsiz bir bağlam veri göremez. Yaklaşım "bilgi yoksa izin ver" değil,
**"açıkça doğrulanamıyorsa reddet"** biçimindedir.

| Kiracıya ait (filtrelenir) | Ortak katalog (filtrelenmez) |
|---|---|
| `companies` | `sources` |
| `users` | `source_documents` |
| `eligibility_assessments` | `opportunities` |
| `scenario_simulations` | `opportunity_rules` |
| `notifications` | `opportunity_documents` |
| `audit_log` | |

Ortak katalog, resmî çağrı ve kaynak verisidir; tasarım gereği tüm kiracılar tarafından
paylaşılır (bkz. `docs/data-model.md`).

Firma alt tabloları (`company_nace_codes`, `company_locations`, `company_certificates`,
`company_investments`) kendi `DbSet`'lerine sahip değildir; yalnızca `Company` üzerinden
yüklenirler ve onun filtresine tabidirler.

### Katman 2: servis katmanı erişim kapısı

`CompanyAccessGuard` (`src/GovAI.Application/Common/CompanyAccessGuard.cs`) şirket
kimliğiyle çalışan her use-case'in geçmek zorunda olduğu tek kapıdır. Dört soruyu sırayla
cevaplar:

1. İstek bir kiracıya bağlı mı?
2. Şirket gerçekten var mı?
3. Şirket bu kiracıya mı ait?
4. Kullanıcının bu şirket için açık yetkisi var mı?

**Sorgu filtresi var diye bu katman kaldırılamaz.** Repository'ler ileride filtresiz bir
yol açarsa (ham SQL, saklı yordam) tek koruma bu katman kalır.

Başka kiracıya ait bir şirket kimliği verildiğinde `NotFoundException` atılır,
`ForbiddenException` değil: "yasak" cevabı o kimliğin var olduğunu doğrular ve kimlik
sayımı (enumeration) yapılmasına izin verirdi.

---

## 3. Firma erişim kapsamı — `company_scope` claim'i

Jetonlar kapsamı **her zaman açıkça** taşır:

| `company_scope` | Anlamı |
|---|---|
| `tenant` | Kullanıcı kendi kiracısındaki tüm firmalara erişebilir. Kiracı sınırı ayrıca uygulanır. |
| `list` | Kullanıcı yalnızca `scoped_companies` listesindeki firmalara erişebilir. |
| _(yok)_ | **Erişim yok.** Eski biçimli veya elle üretilmiş jeton kabul edilmez. |

Eskiden claim'in yokluğu "sınırsız erişim" sayılıyordu; kimliksiz istekler de tüm
firmalara erişebiliyordu. İkisi de kaldırıldı.

### Faz 1: erişimin kaynağı jeton değil, üyeliktir

Jetondaki liste artık **yalnızca istemci kolaylığıdır.** Yetki kararı her istekte
`user_companies` tablosundan okunur (`CompanyAccessGuard`).

Sebebi bir güvenlik gereğidir: bir kullanıcının şirket erişimi kaldırıldığında elindeki
jeton hâlâ geçerli kalır. Karar veritabanından verilmezse o jeton erişmeye devam ederdi.
Koruyan test: `Faz1-O. Üyelik kaldırılınca eski jeton erişim sağlayamaz`.

Aktif şirket de istemcide seçilmez: `POST /api/auth/active-company` üyeliği doğrular ve
aktif şirketi **kendi yazdığı** yeni bir jeton döner.

| Şirket rolü | Görüntüler | Analiz çalıştırır | Profili değiştirir | Kullanıcı yönetir |
|---|---|---|---|---|
| CompanyOwner | ✔ | ✔ | ✔ | ✔ |
| CompanyManager | ✔ | ✔ | ✔ | — |
| CompanyExpert | ✔ | ✔ | — | — |
| CompanyViewer | ✔ | — | — | — |

Matris tek yerde tanımlıdır: `CompanyAccessGuard.Satisfies`. Panel aynı matrisi
`companyPermissions` içinde tekrarlar; bu yalnızca kullanıcıyı boş yere uğraştırmamak
içindir, karar her zaman sunucudadır.

Erişim reddinde `NotFoundException` atılır, `ForbiddenException` değil: "yasak" cevabı o
kimliğin var olduğunu doğrular ve kimlik sayımına izin verirdi. Tek istisna, üyeliği olan
ama **rolü yetmeyen** kullanıcıdır; orada şirketin varlığı zaten bilindiği için 403 döner.

---

## 4. Sorgu filtresini atlayan tek yol

Kod tabanında `IgnoreQueryFilters()` **iki yerde** kullanılır. İkisi de burada
gerekçelendirilmiştir; üçüncüsü eklenemez.

### 4.1 `UserRepository.GetByEmailAsync`

`src/GovAI.Persistence/Repositories/AssessmentRepositories.cs`

Zorunludur, çünkü:

- Girişte kiracı, kullanıcı bulunmadan bilinemez; jetondaki kiracı bu sorgudan doğar.
- `users.email` şemada **global benzersizdir**; mükerrer kontrolü de kiracılar arası
  olmak zorundadır, aksi hâlde temiz doğrulama hatası yerine veritabanı kısıt ihlali alınır.

Korumalar:

- Yalnızca `AuthenticationService.LoginAsync` ve `CreateUserAsync` çağırır.
- Bulunan kullanıcı çağırana açılmaz; parola doğrulaması ve jeton üretimi dışında kullanılmaz.
- Filtre tümüyle kalktığı için **yumuşak silme koşulu elle geri konur** — aksi hâlde
  silinmiş bir hesap yeniden giriş yapabilirdi.

### 4.2 `CrossTenantCompanyLookup` (Faz 1)

`src/GovAI.Persistence/Repositories/MultiCompanyRepositories.cs`

Zorunludur, çünkü panelden eklenen bir vergi numarasının **başka bir çalışma alanında**
kayıtlı olup olmadığı bilinmeden mükerrer tüzel kişilik engellenemez.

Korumalar:

- Yalnızca `bool` döner. Karşı tarafın adı, kimliği, kiracısı veya herhangi bir alanı
  çağırana **hiç ulaşmaz**.
- Sonuç kullanıcıya da açılmaz: yanıt yalnızca "doğrulama veya bağlantı talebi gereklidir"
  der ve bir talep kaydı açar. Şirket oluşturulmaz.
- Açılan talep kaydı karşı tarafa **hiçbir yabancı anahtar tutmaz.**
- Koruyan test: `Faz1-E. Başka çalışma alanındaki vergi numarası bilgi sızdırmaz` —
  yanıt gövdesinde karşı tarafın adının, şirket kimliğinin ve kiracı kimliğinin
  geçmediğini ayrı ayrı doğrular.

Yeni bir `IgnoreQueryFilters()` eklenmesi gerekiyorsa, gerekçesi bu belgeye yazılmalı ve
izolasyonu doğrulayan bir test eklenmelidir.

---

## 5. Sistem aktörü

Arka plan işleri anonim kullanıcı gibi davranmaz.

- **Python worker'ları** gerçek bir hesapla `/api/auth/login` üzerinden giriş yapar ve
  normal kullanıcılar gibi jeton taşır. Ayrıcalıkları yoktur; kiracı sınırına tabidirler.
  Faz 1'den beri bu hesabın rolü `SystemIngest`'tir: yalnızca veri toplama uçlarını
  kullanabilir; kullanıcı, rol, kiracı veya şirket yönetemez ve şirket raporlarını
  okuyamaz (`Policies.CompanyData` platform rollerini dışarıda bırakır).
  Kimlik bilgileri koda ve depoya yazılmaz; yalnızca `.env` üzerinden gelir.

  Tek muafiyeti gece toplu skorlama işidir: bu şirketlerde insan üyeliği yoktur ve
  olmamalıdır, bu yüzden `CompanyAccessGuard` bu aktör için üyelik aramaz. Muafiyet
  kiracı sınırını **aşmaz** ve uç seviyesinde okuma yolları zaten kapalıdır.
  Koruyan test: `T3. SystemIngest şirket raporlarını okuyamaz`.
- **Açılış seed'i** (`DatabaseSeeder`) HTTP bağlamı olmadan çalışır. Yalnızca kiracıya
  bağlı olmayan tablolara okuma yapar (`tenants`); diğer işlemleri yazmadır ve yazma
  sorgu filtresinden etkilenmez. Bu nedenle **filtre atlamasına ihtiyaç duymaz.**
- `SystemCurrentUser` sınıfı DI'da `ICurrentUser` olarak kayıtlı **değildir**; bir HTTP
  isteğinden erişilemez.

---

## 6. Koruyan testler

`tests/GovAI.Api.Tests/` — gerçek HTTP hattı, gerçek kimlik doğrulama, gerçek sorgu
filtreleri; yalnızca veritabanı sağlayıcısı bellek içi sağlayıcıyla değiştirilir.

| Dosya | Kapsam |
|---|---|
| `TenantIsolationTests.cs` | Kiracılar arası okuma/yazma denemeleri (senaryo A–J, L, M) |
| `ClaimAndJwtSecurityTests.cs` | Kapsam claim'i eksik jeton (K), JWT anahtar kuralları (N, O) |
| `PlatformRoleTests.cs` | Platform rolleri: katalog yazma, danışman onayı, worker sınırları (R–T4) |
| `MultiCompanyTests.cs` | Çoklu şirket: üyelik, rol matrisi, aktif şirket, grup, davet (Faz1-A–Q) |

Testler bellek içi sağlayıcıyla **ve** gerçek PostgreSQL 17 ile ayrı ayrı koşturulur:

```bash
dotnet test                                              # bellek içi
GOVAI_TEST_POSTGRES="Host=...;Database=postgres;..." dotnet test   # gerçek PostgreSQL
```

İkisi de gereklidir. Kısmi tekil indeks, yabancı anahtar sırası ve `jsonb` davranışı
bellek içi sağlayıcıda hiç sınanmaz; Faz 1'de üç veri hatası yalnızca gerçek
PostgreSQL koşusunda ortaya çıktı.

Bu testler düzeltme öncesi koda karşı çalıştırıldığında **16 tanesi başarısız olur**;
düzeltmeden sonra tamamı geçer.
