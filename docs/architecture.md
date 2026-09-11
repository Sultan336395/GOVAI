# Mimari

## 1. Genel bakış

GOVAI dört ayrı çalışma zamanından oluşur:

```
  Resmî kaynaklar                                    Kurumsal sistemler
  (Resmî Gazete, bakanlıklar,                        (ERP, İK, muhasebe)
   kalkınma ajansları, EKAP)                                 │
          │                                                  │
          ▼                                                  ▼
  ┌───────────────────┐                            ┌───────────────────┐
  │  Python worker'ları│                            │  ERP entegrasyonu │
  │  collector         │                            │  /api/company-    │
  │  parser            │                            │   profile/erp-sync│
  │  rule extractor    │                            └─────────┬─────────┘
  │  scheduler         │                                      │
  └─────────┬─────────┘                                       │
            │  REST                                           │
            ▼                                                 ▼
       ┌──────────────────────────────────────────────────────────┐
       │                    GovAI.Api (.NET 10)                    │
       │  ┌────────────────────────────────────────────────────┐  │
       │  │   Application — use-case servisleri (MediatR yok)   │  │
       │  │  ┌──────────────────────────────────────────────┐  │  │
       │  │  │  Domain — kural motoru + skorlama            │  │  │
       │  │  │  (deterministik, dış bağımlılıksız)          │  │  │
       │  │  └──────────────────────────────────────────────┘  │  │
       │  └────────────────────────────────────────────────────┘  │
       └───┬──────────────┬──────────────┬───────────────┬────────┘
           │              │              │               │
      PostgreSQL       Redis         RabbitMQ        OpenAI API
      (kalıcı veri)   (önbellek)     (kuyruk)        (AI katmanı)
                                          │
                                          ▼
                                  React + TypeScript paneli
```

## 2. Neden Onion mimarisi

Projenin ticari değeri kural motorunda ve skorlama metodolojisindedir (FMH kapsamında korunması
planlanan bileşenler). Bu mantığın veritabanına, HTTP'ye veya AI sağlayıcısına bağımlı olmaması
gerekir — hem test edilebilirlik hem de fikri mülkiyetin taşınabilirliği açısından.

Bağımlılık yönü daima içe doğrudur:

| Katman | Neye bağımlı | Neye bağımlı değil |
|---|---|---|
| `Domain` | Hiçbir şeye | EF Core, HTTP, AI, kuyruk |
| `Application` | `Domain` | EF Core, HTTP, somut altyapı |
| `Persistence` | `Application`, `Domain` | `Api` |
| `Infrastructure` | `Application`, `Domain` | `Api`, `Persistence` |
| `Api` | Hepsi | — |

`Application` dış dünyayı yalnızca arayüzlerle tanır (`ICompanyRepository`, `IAiExplanationClient`,
`IEventPublisher`, `ICacheService`, …). Somut uygulamalar `Persistence` ve `Infrastructure`
katmanlarındadır ve DI ile bağlanır.

### MediatR neden yok

Her use-case düz bir servis sınıfıdır. Controller `EligibilityService`'i doğrudan çağırır.
Gerekçe: bu proje ölçeğinde CQRS aracısı, yığına gezinmesi zor bir dolaylılık katmanı ekler;
"bu isteği kim işliyor" sorusunun cevabı IDE'de tek tıkla görünür olmalıdır.

## 3. Servis bileşenleri

Proje dosyasındaki bileşen listesinin kod karşılıkları:

| Doküman bileşeni | Kod karşılığı |
|---|---|
| Source Collector Service | `workers/govai_workers/collector/` |
| Document Parser Service | `workers/govai_workers/parser/extractors.py` |
| Company Profile Service | `GovAI.Application.Companies.CompanyProfileService` |
| Eligibility Rule Engine | `GovAI.Domain.Eligibility.RuleEvaluator` + `EligibilityEngine` |
| Scoring Service | `GovAI.Domain.Scoring` + `EligibilityEngine.BuildDimensionScores` |
| AI Explanation Service | `GovAI.Infrastructure.Ai.OpenAiExplanationClient` |
| Reporting Service | `GovAI.Application.Reporting.ReportingService` |
| Notification Service | `GovAI.Application.Notifications.NotificationService` |

Proje dosyasında adı geçmeyen, sonraki fazlarda eklenen bileşenler:

| Bileşen | Kod karşılığı | Ne yapar |
|---|---|---|
| Otonom haftalık rapor | `GovAI.Application.Reporting.WeeklyReportBuilder` | Firmaya haftalık anlık görüntü üretir; saftır, yeniden hesaplamaz |
| Rapora soru sorma | `ReportQuestionCatalog` + `ReportAnswerBuilder` | Soruyu da cevabı da rapordan deterministik üretir; model çağrısı yok |
| İhale başvuru takibi | `GovAI.Application.Tenders.TenderPursuitService` | Firmanın süreç beyanı; skoru **değiştirmez** |
| Dashboard içgörüleri | `GovAI.Application.Dashboard.DashboardInsightsBuilder` | Aksiyon, profil doluluğu, huni, hareket akışı; saftır |
| Kalibrasyon | `GovAI.Domain.Calibration.CalibrationReport` | Skoru **ölçer**, değiştirmez |
| Yapay zekâ ikinci görüşü | `GovAI.Application.Calibration.AiSecondOpinionService` | Ayrışan vakayı insana işaret eder; anahtar yoksa görüş uydurmaz |
| ERP veri çekme | `GovAI.Application.Integrations.ErpPullService` | Gece 02:45; bulunamayan alanı `null` bırakır, sıfır yazmaz |
| E-posta gönderimi | `GovAI.Infrastructure.Notifications.SmtpEmailSender` | Gerçek SMTP; yapılandırma yoksa `DisabledEmailSender` devreye girer ve "gönderildi" yazılmaz |

Üç tanesi bilerek **saf** (`WeeklyReportBuilder`, `DashboardInsightsBuilder`,
`ReportAnswerBuilder`): girdileri yüklenmiş kayıtlar ile `asOf`'tur, içeride ne veri
okunur ne tarih alınır. Böylece aynı girdi her zaman aynı çıktıyı verir ve davranış
sahte depo kurmadan sınanabilir.

## 4. Veri akışı: bir çağrının yolculuğu

```
1. scheduler          →  cron takvimi gelen kaynağı kuyruğa bırakır
2. collector          →  robots.txt'e uyarak sayfaları indirir
                      →  POST /api/sources/documents
3. API                →  içerik hash'i değiştiyse yeni sürüm açar, parse kuyruğuna bırakır
                         (değişmediyse hiçbir iş üretilmez)
4. parser             →  PDF/HTML → normalize metin
                      →  deterministik kalıplar + LLM ile koşul çıkarımı
                      →  POST /api/opportunities
5. API                →  fırsatı kaydeder, skorlama olayı yayınlar
6. EligibilityService →  her firma için kural motorunu çalıştırır
                      →  skoru, gerekçeyi ve belge listesini kalıcılaştırır
                      →  yeni eşleşme / skor değişimi / son tarih bildirimi üretir
7. Panel              →  önceliklendirilmiş listeyi ve "neden" ekranını gösterir
```

Adım 3'teki hash kontrolü kritiktir: aynı ilan her taramada yeniden ayrıştırılırsa hem AI
maliyeti hem de gereksiz skorlama yükü katlanır.

## 5. Ölçeklenebilirlik

- **Worker'lar yatay ölçeklenir.** Collector ve parser durumsuzdur; `docker compose up --scale parser=4`
  ile çoğaltılabilir. RabbitMQ prefetch değeri iş dağılımını dengeler.
- **Skorlama yükü olay tabanlıdır.** Firma profili veya çağrı değişmediği sürece yeniden hesaplama yapılmaz.
- **Okuma yoğun sorgular önbelleklenir.** Redis erişilemezse `NullCacheService` devreye girer;
  sistem yavaşlar ama durmaz.
- **İndeksler sorgu şekline göre tanımlıdır.** Dashboard'ın ana sorgusu
  `(company_id, is_latest, final_score)` bileşik indeksiyle karşılanır.

## 6. Hata dayanıklılığı

| Bileşen erişilemezse | Sistem davranışı |
|---|---|
| Redis | Önbellek atlanır, kaynaktan okunur |
| RabbitMQ | Olay loglanır, iş gece toplu turda telafi edilir |
| OpenAI | Kural çıkarımı deterministik kalıplara düşer, özetler kural tabanlı üretilir |
| Bir kaynak sitesi | O kaynak hatalı işaretlenir; 5 ardışık hatada otomatik devre dışı |
| PostgreSQL | API başlamaz (tek gerçek zorunlu bağımlılık) |

## 7. Güvenlik sınırları

- Worker'lar **veritabanına doğrudan erişmez**. Tek yol REST API'dir; böylece tekilleştirme,
  yetki ve bildirim kuralları atlanamaz.
- Kaynak çekme uçları (`/api/sources/documents`) `Operate` politikasıyla korunur ve
  kullanıcıya açık okuma uçlarından ayrıdır.
- Denetim kaydı `AuditActionFilter` ile controller seviyesinde otomatik yazılır;
  servis kodunda unutulma riski yoktur.

## 8. İlgili dokümanlar

- [Veri modeli](data-model.md)
- [API sözleşmesi](api.md)
- [Skorlama metodolojisi](scoring.md)
- [Mimari kararlar (ADR)](adr/)
- [12 aylık yol haritası](roadmap.md)
