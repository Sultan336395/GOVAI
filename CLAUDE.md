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

### 2.2.1 Sektör uyumu sıralamanın birincil ölçütüdür

Eşleştirme önce sektöre bakar, sonra alt ölçütlere (personel, ciro, işletme yaşı,
personel yapısı). Bunu üç parça birlikte sağlar; birini bozmak diğerlerini işlevsiz
bırakır:

| Parça | Nerede | Ne yapar |
|---|---|---|
| `UnverifiedSectorScore = 0.5` | `EligibilityEngine` | Sektör kuralı olmayan çağrıya tam puan VERMEZ |
| `SectorFit` (Matched / Unverified / NotMatched) | `EligibilityAssessment` kolonu | Sıralamanın birincil anahtarı |
| `parser/sektor.py` | worker | İlanın konusundan NACE kuralı üretir |

`UnconstrainedDimensionScore = 1.0` yalnızca **diğer** boyutlar içindir; sektöre
uygulanırsa kuralsız her ilan listenin başına çıkar. Gerekçe ve tablo: `docs/scoring.md` §2.1–2.2.
Koruyan testler: `SectorFitTests` (13), `SectorRankingTests` (9), `test_sektor.py` (26).

Sektör uyumsuzluğu **eleme değildir**: kayıt gizlenmez, kararı `NotEligible` yapılmaz;
listenin sonuna iner ve "sektör uyumu doğrulanamadı"/"sektör uyumsuz" etiketiyle görünür.

### 2.2.2 Sektör ve NACE alanları yazılmaz, seçilir

Firma kartındaki **ana sektör**, **ana NACE kodu**, **diğer NACE kodları** ve **alt
sektörler** serbest metin değildir. `ActivityCatalog` tek kaynaktır: arayüz öneriyi
`/api/reference/sectors` ve `/api/reference/nace` uçlarından alır, `CompanyRegistryService`
kaydı aynı katalogla doğrular. İki taraf ayrı listelerden beslenirse arayüzde geçerli
görünen bir seçim sunucuda reddedilir.

Gerekçe sahadan: bir firmanın sektörü "inşaat" yazarken NACE kodu `2562` (metal işleme)
kalmıştı. Motor NACE koduna bakar; firma kendi sektöründeki ihalelerde "uyumsuz" görünür.
Serbest metin ayrıca aynı sektörün onlarca yazımını üretir ve hiçbiri eşleşmez.

İki yazım biçimini karıştırma: katalog kodu **okunaklı** gösterir (`25.62`), alan modeli
kodu **noktasız** saklar (`2562`, `NaceCode.Normalize`) ve eşleştirme o biçim üzerinden
çalışır. `ActivityCatalog.NormalizeNace` gösterim biçimini üretir, saklama biçimini değil.

**İki alanın tek tek geçerli olması yetmez; birbirini de tutmalıdır.** Sahada sektörü
"İnşaat ve taahhüt" seçilmiş bir firmaya beton ürünleri kodu (23.61) atandı; ikisi de
katalogdaydı ama farklı faaliyetleri anlatıyordu ve firma kendi sektöründeki ihalelerde
"uyumsuz" göründü. İki hat birlikte korur:

1. `/api/reference/nace` **yalnızca** verilen sektörlerin kodlarını döner — kullanıcı
   tutmayan kodu göremez, dolayısıyla seçemez. Sorgu verilmezse o sektörün tamamı döner:
   alana tıklamak yeter, ayrıca arama yapmak gerekmez (`minChars={0}`).
2. `ValidateSectorConsistency` ana kodu ana sektöre, diğer kodları ana sektör veya beyan
   edilen alt sektörlere karşı doğrular.

Ana sektör değişince arayüz NACE seçimlerini boşaltır: eski kodlar yeni sektöre ait
değildir ve kayıtta reddedilirlerdi.

**Profil değişince skor tazelenir.** `CompanyRegistryService.UpdateAsync` yeniden
skorlamayı kuyruğa bırakır. Bu tetikleme yalnızca ERP yolunda vardı; panel yolunda
olmadığı için kullanıcı sektörünü düzeltiyor, liste eski sektörle hesaplanmış hâlde
kalıyordu.

Koruyan testler: `ActivityCatalogTests` (29), `ActivityReferenceTests` (13),
`SectorConsistencyTests` (11), `turkce.test.ts` (8).

### 2.2.3 Uzman görüşü skoru değiştirmez, skoru ölçer

Danışmanın bir değerlendirmeye verdiği kendi kararı (`ExpertVerdict`) karar mekanizmasının
**girdisi değil denetçisidir**. Skoru, kararı veya ağırlıkları değiştirmez.

Sebebi ürünün ilk iki iddiasıdır. Uzman görüşü skoru etkileseydi aynı firma–çağrı çifti
geçmişte kimin baktığına göre farklı puan alırdı (determinizm gider) ve kayan puanın dayanağı
çağrı metninde bulunmazdı (açıklanabilirlik gider).

`CalibrationReport` bu kayıtlardan uyum oranını, yanlış pozitif/negatif sayılarını, karışıklık
matrisini ve skorun ayrım gücünü hesaplar. Rapor **öneri üretmez**; hangi ağırlığın nasıl
değişeceği insan kararıdır. Sayılara bakıp otomatik ağırlık değiştiren bir mekanizma eklemek,
korunmaya çalışılan iki iddiayı da bozar.

Üç ayrım bilinçlidir ve kaldırılmamalıdır:

| Ayrım | Neden |
|---|---|
| Sistem kararı **kopyalanarak** saklanır | Değerlendirme yeniden hesaplanırsa bugünün skoruyla dünün uzman görüşü kıyaslanmış olurdu |
| Eksik veriden doğan ayrışma ayrı sayılır | Veri toplama sorununu ağırlık sorunu sanıp ağırlıkları bozmamak için |
| `MinimumSampleSize = 20` altı "yorumlanamaz" işaretlenir | Üç vakayla hesaplanan "%33 hata oranı" istatistik değil gürültüdür |

Kayıt **silinmez**: uyumsuz çıkanların silinebilmesi, raporu istenen sonuca göre şekillendirmeyi
mümkün kılardı.

**İkinci görüşü yapay zekâ da verebilir** (`VerdictSource.Ai`) ve gece turu bunu kendiliğinden
toplar — insan değerlendirmesi seyrektir, ölçüm birikmezse kalibrasyon hiç yapılamaz. Ama iki
kaynak **eşdeğer değildir ve hiçbir yerde toplanmaz**: danışman görüşü kalibrasyon ölçütüdür,
yapay zekâ görüşü yalnızca taramadır (nereye bakılmalı). Kural motoru da ikinci görüşü veren
model de aynı metni okur; aynı yanlışı birlikte yapabilirler ve yüksek uyum oranı doğruluk
değil ortak körlük olabilir. Bu yüzden rapor iki ayrı özet döner ve **ortak toplam alanı
yoktur**.

Modele **hesaplanmış skor verilmez** (yoksa onaylama eğilimine girer, görüş sistemin kendi
cevabının yankısı olur) ve **eksik alanlar açıkça bildirilir** (yoksa bilinmeyeni "hayır" sayıp
firmayı eler, §2.2 çiğnenir). Anahtar yoksa görüş **uydurulmaz**.

Gerekçe: `docs/adr/0005`. Koruyan testler: `CalibrationReportTests` (14), `CalibrationTests` (9),
`AiSecondOpinionTests` (7).

### 2.2.4 ERP'de bulunamayan alan sıfır yazılmaz

ERP bağlantısı firmanın ciro, personel kırılımı ve belgelerini kendi sisteminden **çeker**
(`ErpPullService`, gece 02:45). Çekilen veri doğrudan kaydedilmez; mevcut
`SyncFromErpAsync` hattından geçer — ikinci bir yazma yolu, doğrulama ya da yeniden
skorlama tetiklemesinden birinin ERP yolunda atlanması demek olurdu.

**Eşlemede aranıp ERP yanıtında bulunamayan alan `null` kalır, `0` yazılmaz.** Bu, §2.2'nin
ERP yolundaki karşılığıdır: sıfır yazmak firmayı "hiç kadın çalışanı yok" diye kaydeder ve
o firma kadın istihdamı şartı arayan her çağrıdan elenir. Aynı sebeple bir bölümün tamamı
boşsa o bölüm hiç gönderilmez; ERP bordro modülü kullanmayan bir firmada elle girilmiş
doğru veri silinemez.

İki güvenlik kararı kaldırılmamalıdır:

| Karar | Neden |
|---|---|
| Özel IP'lere **yalnızca** "kurum içi" beyanıyla gidilir | Kurum içi ERP özel IP'dedir; engeli topyekûn uygulamak entegrasyonu imkânsız kılar, topyekûn kaldırmak SSRF açar |
| Bulut metadata adresi (169.254.169.254) **beyanla dahi** açılmaz | Orası ERP değil, sunucunun kendi bulut kimliğinin durduğu yer |

Kimlik bilgisi AES-GCM ile **şifrelenir** (özetlenemez — ERP'ye gönderilmesi gerekir),
hiçbir yanıtta dönmez ve ERP'nin hata gövdesi kullanıcıya yansıtılmaz (gövde kimlik ya da
personel verisi taşıyabilir).

Gece sırası: **ERP çekme (02:45) → skorlama (03:30) → ikinci görüş (04:15).** Profil önce
tazelenir; ters sırada firma dün düzelttiği eksiğin sonucunu bir gün sonra görür.

Gerekçe: `docs/adr/0006`. Koruyan testler: `ErpFieldMapTests` (4), `ErpConnectionTests`
alan modeli (5) ve API (12).

### 2.2.5 Rapora soru sorulur, soru yazılmaz

Haftalık raporun soru bölümü kullanıcının yazdığı metni **kabul etmez**;
`POST /api/reports/weekly/{id}/questions` yalnızca `questionKey` alır. Sorular da
cevaplar da rapordan **deterministik** üretilir (`ReportQuestionCatalog`,
`ReportAnswerBuilder`); model çağrısı yoktur.

Üç sebeple böyle:

| Karar | Neden |
|---|---|
| Serbest metin yok | Metin, cevabı raporun dışına taşır ve modele yönlendirme (prompt injection) kapısı açardı |
| Model çağrısı yok | Cevap raporla çelişemez, aynı soru her seferinde aynı cevabı verir, anahtar tanımlı değilken de çalışır |
| Anahtar sabit kümeden | Her sorunun rapordaki hangi veriden cevaplanacağı önceden bellidir |

Hak **rapor bazındadır** (`ReportInquiryQuota.PerReport = 5`) ve **cevap üretilince**
harcanır. Firma düzeyinde tek havuz olsaydı yoğun bir hafta sonraki haftanın raporunu
sorusuz bırakırdı. Sorulmuş bir soruyu yeniden açmak harcamaz; aksi hâlde kullanıcı
okuduğu cevaba geri dönmekten çekinirdi. Kayıt **silinmez** — silinebilse hak sayacı
sıfırlanabilirdi.

Koruyan testler: `ReportQuestionTests` (14), `ReportInquiryTests` (9),
`cevapMetni.test.ts` (8).

### 2.2.6 İhale takibi skoru değiştirmez

`TenderPursuit` firmanın başvuru sürecine dair **kendi beyanıdır**: skoru, kararı ve
sıralamayı etkilemez. Etkileseydi firma bir ihaleyi işaretleyerek kendi puanını
yükseltebilirdi — §2.1'in iki iddiası da düşerdi.

Takip ekranında sistemin değerlendirmesi ayrıca gösterilir; amaç tersidir: hazırlığa
alınan bir ihalede "sektör uyumu doğrulanamadı" uyarısının zamanında görülmesi.
Değerlendirilmemiş ihalede alan `null` kalır, uydurma puan yazılmaz.

İki karar bilinçlidir:

| Karar | Neden |
|---|---|
| Aşama geçişleri **serbesttir** | Katı boru hattı sahayla çatışır: teklif verdikten sonra sisteme girme, hazırlıktan incelemeye dönme, iptal edilip yenilenen ihaleyi yeniden açma olağandır. Doğruluk, geçişi kısıtlamakla değil her değişikliği geçmişe yazmakla korunur |
| `Sonuclandi` **sonuç zorunlu** kılar | "Sonuçlandı" tek başına kazanılan ile kaybedilen ihaleyi aynı satırda gösterirdi |

Kayıt **silinmez**; bırakılan takip `Vazgecildi` olur.

Koruyan testler: `TenderPursuitTests` alan modeli (13) ve API (10).

### 2.2.7 "Gönderildi" damgası gerçekten gönderilince basılır

Bildirim kaydı, kanalına **ulaştığı** an gönderilmiş sayılır. Eskiden e-posta kuyruğa
bırakılır bırakılmaz `MarkSent` çağrılıyordu; kuyruğun ucunda tüketici olmadığı için
hiç ulaşmayan hatırlatmalar panelde "gönderildi" görünüyordu — kullanıcının almadığı
bir uyarıyı almış sanması demekti.

| Durum | Ne olur |
|---|---|
| Gönderildi | `MarkSent`; hata alanı temizlenir |
| Denendi, başarısız | `MarkFailed(sebep)`; `SentAt` boş kalır, en fazla üç kez denenir |
| SMTP yapılandırılmamış | Hiç denenmez; **deneme hakkı harcanmaz**, bildirim bekler |
| Alıcı yok | Başarısız sayılır; "gönderildi" yazılmaz |

Son satır önemlidir: hak harcansaydı, SMTP sonradan tanımlandığında birikmiş
hatırlatmalar üç denemeyi çoktan doldurmuş olur ve hiç gitmezdi.

Alıcılar bildirimde **saklanmaz**, gönderim anında çözülür. Saklansaydı liste kayıt
oluşturulduğu andaki ekiple donardı: ayrılan kişiye posta gider, yeni gelen hiçbir
hatırlatma almazdı. Görüntüleyici rolü listede yoktur; tanım gereği pasif izleyicidir.

Şifresiz SMTP desteklenmez ve SMTP sunucusunun hata gövdesi kullanıcıya
yansıtılmaz — gövde kullanıcı adı ve iç sunucu adları taşıyabilir.

**Hangi tür e-postaya gider:** `NotificationChannelPolicy` tek karar yeridir.
Listede yalnızca `DeadlineApproaching` ve `DocumentMissing` vardır; ikisi de zamana
bağlıdır ve kaçırılırsa fırsat kapanır. `NewMatch` ve `ScoreChanged` her yeniden
skorlamada üretilebilir — e-postaya çevrilseydi kutu dolar, insanlar kuralla filtreler
ve asıl iki uyarı da onlarla birlikte görünmez olurdu. Varsayılan `InApp`'tir: yeni bir
tür eklemek kendiliğinden posta göndermeye başlamaz.

Kanal **görünürlüğü belirlemez**; panel listesi kanala bakmaz. E-posta kanalındaki
bildirim panelde de durur, yalnızca ayrıca posta olarak da gider.

**Bilinen sınır:** gönderim turu çağıran oturumun kiracısıyla sınırlıdır (EF kiracı
süzgeci). Tek kiracılı kurulumda sorun değildir; çok kiracılıya geçildiğinde tur
kiracı başına çalıştırılmalıdır.

Koruyan testler: `NotificationDispatchTests` (7), `NotificationChannelPolicyTests` (8),
`DocumentMissingNotificationTests` (6).

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

Burası en kolay ve en sessiz bozulan yer. Dört sözleşme iki ayrı yığında tanımlıdır:

| Sözleşme | C# tarafı | Python / TS tarafı | Bozulursa |
|---|---|---|---|
| Alan beyaz listesi | `CompanyFieldResolver.SupportedFields` | `rule_extractor.ALLOWED_FIELDS` | AI tanınmayan alanla kural üretir, motor sessizce `Unknown` sayar |
| Kuyruk adları | `QueueNames` | `messaging.RoutingKeys` | Mesajlar hiçbir tüketiciye ulaşmaz, hata da vermez |
| API tipleri | Controller DTO'ları | `web/src/api/types.ts` | Panel `undefined` gösterir |
| Ayrıştırma durumları | `DocumentParseStatus` | `parser/runner.py` içindeki `status=` | API 400 döner, mesaj ölü kuyruğa düşer |

İlk ikisi otomatik denetlenir:

```bash
python scripts/check_contract_parity.py
```

CI'da `contracts` işi olarak her push'ta koşar. **C# tarafı kaynak doğrudur** — uyuşmazlıkta
Python'u ona uydur, tersini yapma.

Üçüncüsü (TS tipleri) elle senkronize edilir. Bir DTO'ya alan eklersen
`web/src/api/types.ts` içindeki karşılığını da güncelle.

Dördüncüsü `workers/tests/test_ayristirma_durumlari.py` ile denetlenir: worker'ın bildirdiği
her `status` değeri C# enum'unda aranır. Sahada `Skipped` bir süre yalnızca Python tarafında
vardı; API her liste sayfasında 400 döndü ve eleme hiç kaydedilmedi.

---

## 4. Komutlar

Solution dosyası **`GovAI.slnx`**'tir (yeni XML formatı), `.sln` değil.
`dotnet build GovAI.sln` hata verir; argümansız kullan.

```bash
dotnet build -c Release          # tüm .NET projeleri
dotnet test                      # 1068 test (251 domain + 435 application + 382 API)
```

```bash
cd workers && .venv/Scripts/python -m pytest -q      # 458 test
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
`npm run test` vitest'i tek seferlik koşturur (166 test); şu an yalnızca `lib/`
altındaki saf fonksiyonlar kapsanır (menü görünürlüğü, parola kuralı, etiket
haritaları) — ekran testleri hâlâ yok.

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
korunur (22 test): hedef açıkça verilmeli, beyan edilen ortam gerçek
hedefle tutmalı, üretim ayrıca tam onay istemeli.

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
src/GovAI.Domain/Calibration/    ← uzman görüşüyle karşılaştırma; skoru ÖLÇER, değiştirmez
src/GovAI.Application/           ← use-case servisleri (her modül kendi klasöründe)
src/GovAI.Persistence/           ← EF Core yapılandırmaları, repository'ler, seed
src/GovAI.Infrastructure/        ← OpenAI, Redis, RabbitMQ, JWT, PDF/Excel
src/GovAI.Api/Controllers/       ← 18 uç grubu (26 denetleyici)
workers/govai_workers/           ← collector, parser, rule_extractor, scheduler
web/src/pages/                   ← 27 ekran (28 rota); menü tanımı app/navigation.ts
docs/                            ← mimari, veri modeli, API, skorlama, ADR'ler, yol haritası
scripts/                         ← sözleşme senkron denetimi
```

Okuma sırası önerisi:
`docs/architecture.md` → `docs/scoring.md` → `EligibilityEngine.cs` → `docs/adr/`.

Proje dosyasındaki 10 modülün kod karşılıkları `docs/architecture.md` §3'te tablo hâlinde.

---

## 6.1 Resmî kaynaklara erişim (Faz 2)

Her resmî kurum aynı şekilde taranamaz. Üç ayrı yol vardır ve hangisinin
kullanıldığı kaynağın kaydında görünür:

| Yol | Ne zaman | Nerede |
|---|---|---|
| Genel HTML taraması | Kurum statik bağlantı veriyorsa | `collector/crawler.py` |
| Resmî makine erişimi | Kurum API/SPARQL/açık veri sunuyorsa | `collector/eurlex.py` |
| Kontrollü manuel içe aktarma | Hiçbiri yoksa | `ManualImportService` |

**TLS uyumu.** Bazı resmî sunucular Python'un varsayılan ayarlarıyla konuşmuyor.
`collector/tls.py` bunu alan adı bazlı ve dar bir politikayla çözer:

- `resmigazete.gov.tr` — sunucu ara sertifikayı göndermiyor. Eksik halka,
  sertifikanın içinde yazan resmî CA adresinden çekilir ve SHA-256 parmak izi
  sabittir. Kök hâlâ sistemin güven deposundan gelir.
- `kik.gov.tr` — sunucu yalnızca eski RSA anahtar değişimli şifre takımını kabul
  ediyor; OpenSSL'in varsayılanı da kabul eder.

**Sertifika doğrulaması hiçbir koşulda kapatılmaz.** `baglam_olustur` iki `assert`
ile bunu zorlar ve `test_collector_tls.py` politikayı korur. Listede olmayan hiçbir
sunucu etkilenmez.

**Manuel içe aktarma bir tarama değildir.** Kaynak doğrulanmış sayılmaz, sağlığı
değişmez; kayıt `DocumentOrigin.ManualImport` olur ve ekranda "Elle aktarıldı"
görünür. Adres kaynağın resmî alan adında olmak zorundadır ve içerik
yapıştırılmaz — sistem indirir (SSRF korumalı).

**Yayın takvimi.** Resmî Gazete hafta sonu ve tatillerde yayımlanmaz. "Bugün
yayın yok" bir arıza DEĞİLDİR: `collector/publication.py` kaynağın arşivini geriye
tarar ve üç durumu ayırır — `NoNewContent` (sağlıklı), `SelectorBroken`,
`Unreachable`. Hiçbir tarih koda yazılmaz; arşiv adresi kaynağın
`archiveUrlTemplate` yapılandırmasından gelir.

**Kısa olmak eleme sebebi değildir.** PDF metin çıkarımı bir Cumhurbaşkanı
kararından yalnızca başlık, karar numarası ve imzayı getirebilir (~150 karakter).
`Screen()` resmî belge izi taşıyan kısa metinleri geçirir; karar ayrıştırıcıya
bırakılır. Türkçe karşılaştırma katlanır — `ToUpperInvariant` noktasız `ı` ile
noktalı `I`'yı eşleştirmez.

**Karakter kümesi.** Resmî Gazete kümeyi HTTP başlığında değil `<meta>` etiketinde
bildirir (Windows-1254). İndirici meta etiketini de okur, çözülen metni makullük
denetiminden geçirir ve hiçbir kümeyle temiz çözülemeyen içeriği KAYDETMEZ —
bozuk metin resmî kanıt olamaz.

**Mevzuat fırsat değildir.** Mevzuat kategorili kaynaktan fırsat kaydı açılamaz;
`OpportunityService.UpsertAsync` bunu 400 ile reddeder. Worker da aynı ayrımı
gözetir ama ona güvenilmez.

---

## 7. Bilinen boşluklar

Bunlar hata değil, bilinçli ertelemedir. Tamamı gerekçesi ve hedef ayıyla
`docs/roadmap.md`'de listelidir.

| Konu | Durum |
|---|---|
| OCR | Taranmış PDF'lerde metin çıkmaz; parser bunu loglar |
| Refresh token | Üretiliyor ama sunucuda saklanmıyor; süre dolunca yeniden giriş |
| Webhook gönderimi | Bildirim kuyruğa bırakılıyor, gerçek adaptör yok (e-posta adaptörü Faz 4'te eklendi) |
| ERP adaptörleri | Çekme yolu kuruldu (`/api/erp`); ürün varsayılan eşlemeleri **gerçek kurulumda doğrulanmadı** — "Şimdi Dene ve Çek" bunun için var |
| Ağırlık kalibrasyonu | Ölçüm altyapısı hazır (`/api/calibration`); ağırlıklar henüz gerçek vaka verisiyle kalibre edilmedi — örneklem birikiyor |
| Kod bölme | Bundle ~862 KB, tek parça |

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
