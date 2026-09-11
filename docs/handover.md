# Devir notu

**Tarih:** 15.08.2026 · **Durum:** Altyapı kurulumu tamamlandı, geliştirmeye hazır
**Devreden:** Claude Opus 5 oturumu (Windows 11, yerel) · **Devralan:** yeni ortam
**Güncelleme:** 19.08.2026 — uçtan uca `docker compose` provası yapıldı ve geçti (§2.1)

Bu belge, projeyi GitHub'dan çekip devam edecek kişi/oturum için yazılmıştır.
Günlük çalışma kuralları `CLAUDE.md`'dedir; burada **ne yapıldı, ne doğrulandı,
nereden devam edilir** anlatılır.

---

## 1. Ne teslim ediliyor

Proje dosyasında sayılan 10 modülün **çalışan iskeleti**. İş mantığı gerçek, veri sahte:
kural motoru ve skorlama tam olarak yazıldı ve testlendi; kaynak tarayıcıları ve
ERP adaptörleri gerçek kurumlara/sistemlere bağlanmayı bekliyor.

| Katman | Durum | Not |
|---|---|---|
| Domain (kural motoru + skorlama) | **Tamam** | 13 operatör, 7 boyut, 29 test |
| Application (use-case servisleri) | **Tamam** | 8 servis, MediatR yok |
| Persistence (EF Core + PostgreSQL) | **Tamam** | 18 tablo, ilk migration üretildi |
| Infrastructure | **Tamam** | OpenAI, Redis, RabbitMQ, JWT, PDF/Excel |
| API (18 uç grubu) | **Tamam** | Swagger, rol ve firma izni iki kademeli, otomatik audit log |
| Python worker'ları | **İskelet** | Akış uçtan uca yazıldı; kurum bazlı seçiciler yok |
| Web paneli | **Tamam** | 27 ekran, gerçek API sözleşmesine bağlı |
| docker-compose + CI | **Doğrulandı** | 19.08.2026'da sekiz servis birlikte ayağa kalktı (§2.1) |

---

## 2. Ne doğrulandı

Bu makinede fiilen çalıştırılıp geçtiği görüldü:

| Kontrol | Sonuç |
|---|---|
| `dotnet build -c Release` | Başarılı, **0 uyarı** |
| `dotnet test` | **34/34** geçti (29 domain + 5 application) |
| `dotnet dotnet-ef migrations has-pending-model-changes` | Bekleyen değişiklik yok |
| `pytest -q` (workers) | **12/12** geçti |
| `ruff check .` (workers) | Temiz |
| `npm run lint` (web) | Temiz (`--max-warnings 0`) |
| `npm run typecheck` (web) | Temiz |
| `npm run build` (web) | Başarılı |
| `python scripts/check_contract_parity.py` | C#/Python sözleşmeleri senkron |
| `dotnet tool restore` | `dotnet-ef` geri yükleniyor |

Ayrıca **GitHub Actions'ta beş işin beşi de yeşil** (koşu `32040784548`): .NET derleme+test,
Python lint+test, web derleme+lint, sözleşme senkronu ve üç Docker imajının derlenmesi.
İmajlar: `govai-web` 52.7 MB, `govai-worker` 184 MB, `govai-api` 371 MB.

### 2.1 Uçtan uca orkestrasyon — doğrulandı (19.08.2026)

15.08 teslimindeki tek açık uç, `docker compose up` ile servislerin **birlikte** ayağa
kalkmasıydı; o makinede Docker CLI yoktu. Prova yeni bir ortamda yapıldı ve geçti
(Windows 11 Home · .NET SDK 10.0.400 · Node 24.19.0 · Python 3.12.10 · WSL2 2.7.12 ·
Docker Engine 29.7.2 / Compose v5.4.0):

| Kontrol | Sonuç |
|---|---|
| `docker compose up -d --build` | Sekiz servisin tamamı ayakta; postgres/redis/rabbitmq `healthy` |
| API açılışı | Migration açılışta uygulandı, `/health` → `Healthy` |
| Seed | 1 firma, 5 kaynak, 3 çağrı yüklendi |
| `POST /api/auth/login` | Seed hesabıyla JWT alındı |
| Panel (`:5180`) | Giriş, panel, eşleşme listesi ve gerekçe ekranı gerçek veriyle çalışıyor |
| "Yeniden skorla" | Üç çağrı 0,37 sn'de değerlendirildi, ortalama skor 87,19 |
| Worker'lar | collector/parser doğru kuyrukları tüketiyor, scheduler üç işi kaydetti |
| Gerçek tarama | Çukurova Kalkınma Ajansı'ndan 50 doküman alındı; EKAP TLS el sıkışmasında hata verdi ve hata sayacı arttı — kaynak sağlığı mantığı çalışıyor |
| Yönetici özeti (OpenAI anahtarı **yok**) | Kural tabanlı yedek metin üretildi; ADR-0002 davranışı doğrulandı |

Yukarıdaki tablo da aynı ortamda baştan koşuldu ve aynı sonuçları verdi.

**Provada bulunan tek hata:** `web/src/api/client.ts` yalnızca 204'ü gövdesiz sayıyor,
diğer tüm 2xx yanıtlarda JSON gövde bekliyordu. `POST /api/sources/{id}/crawl` **202 Accepted**
ve boş gövde döndüğü için "Şimdi tara" düğmesi `Unexpected end of JSON input` hatası veriyordu.
İstemci, gövdesiz yanıtları durum koduna bakmadan tolere edecek biçimde düzeltildi.

### 2.2 Faz 1 — çoklu şirket ve şirket grubu (25.08.2026)

Dal: `feature/multi-company-foundation` (main'e birleştirilmedi, GitHub'a gönderilmedi).

| Kontrol | Sonuç |
|---|---|
| `dotnet build -c Release` | Başarılı, **0 uyarı** |
| `dotnet test` (bellek içi) | **111/111** (29 domain + 5 application + 77 API) |
| `dotnet test` (gerçek PostgreSQL 17) | **111/111** |
| `migrations has-pending-model-changes` | Bekleyen değişiklik yok |
| Migration: veri üzerinde uygula → geri al → tekrar uygula | Veri korundu, üyelikler geri dolduruldu, geri alma temiz |
| `npm run lint` / `typecheck` / `build` (web) | Temiz |
| `check_contract_parity.py` | Senkron |
| İzole önizleme ortamı (`govai-faz1`) | Sekiz servis ayakta; mevcut `govai` ortamına dokunulmadı |
| Panelden şirket ekleme, mükerrer engelleme, şirket değiştirme | Tarayıcıda fiilen denendi ve geçti |

**Testler iki sağlayıcıda da koşulmalıdır.** Faz 1'de üç veri hatası yalnızca gerçek
PostgreSQL koşusunda ortaya çıktı: kısmi tekil indeks çakışması, yabancı anahtar sırası
ve yükseltmede sütun varsayılanı. Bellek içi sağlayıcı bunların hiçbirini göremez.

```bash
GOVAI_TEST_POSTGRES="Host=localhost;Port=15438;Database=postgres;Username=govai;Password=..." dotnet test
```

**İzole önizleme ortamı** çalışan kuruluma dokunmadan ayağa kaldırılır:

```bash
cd deploy && docker compose -p govai-faz1 --env-file .env.preview up -d --build
```

`.env.preview` ayrı portlar kullanır (web 5181, api 8081, postgres 15437, redis 6381,
rabbitmq 5674/15674). Proje adı farklı olduğu için volume'ler de ayrışır.

---

## 3. İlk 30 dakikada yapılması gereken

Sırayla:

```bash
git clone <repo> && cd GOVAI
dotnet tool restore
dotnet build -c Release && dotnet test
```

```bash
cd workers && python -m venv .venv && .venv/bin/pip install -e ".[dev]" && .venv/bin/pytest -q
```

```bash
cd web && npm ci && npm run lint && npm run typecheck && npm run build
```

Üçü de yeşilse teslim doğru gelmiştir. Sonra:

```bash
cd deploy && cp .env.example .env
# .env içinde POSTGRES_PASSWORD, RABBITMQ_PASSWORD, JWT_SIGNING_KEY (>=32 karakter) doldur
# demo verisi için: SEED_ENABLED=true ve SEED_ADMIN_PASSWORD=...
docker compose up -d --build
```

Beklenen sonuç: http://localhost:5180 açılır, seed hesabıyla giriş yapılır,
panelde bir demo firma ve üç demo çağrı görünür, "Yeniden skorla" butonu skor üretir.

**Bu adım 19.08.2026'da yapıldı ve beklenen sonucu verdi (§2.1).** Takılırsan ilk bakılacak
yerler: `.env` değişkenlerinin API'ye geçmesi, migration'ın açılışta uygulanması, worker'ların
API'ye kimlik doğrulaması (`GOVAI_API_PASSWORD` seed parolasıyla aynı olmalı — compose bunu
`SEED_ADMIN_PASSWORD`'ten türettiği için elle eşitlemek gerekmez) ve hibrit geliştirmede
`POSTGRES_PASSWORD` ile `appsettings.Development.json` parolasının eşleşmesi.

Seed skor üretmez: ilk açılışta `eligibility_assessments` boştur ve eşleşme listesi boş gelir.
Panelde bir kez "Yeniden skorla"ya basılması (veya
`POST /api/eligibility/companies/{id}/rescore`) gerekir — bu beklenen davranıştır, hata değil.

---

## 4. Devam için önerilen sıra

Yol haritasının tamamı `docs/roadmap.md`'de. Compose provası tamamlandığına göre (§2.1)
bugünkü noktadan bakınca en mantıklı ilk iki iş:

**1. Gerçek bir kaynağı bağla.** Tek bir kurumla başla — örneğin Çukurova Kalkınma Ajansı.
`sources` kaydındaki `configurationJson` alanına CSS seçicilerini yaz:

```json
{
  "listUrl": "/duyurular",
  "linkSelector": "a.duyuru-link",
  "titleSelector": "h1.baslik",
  "contentSelector": "div.icerik",
  "urlPattern": "duyuru|cagri",
  "maxPages": 3
}
```

Sonra `govai-parser --url <ilan-adresi>` ile kural çıkarımının o kurumun metninde ne
ürettiğine bak. Kalıp kütüphanesini (`rule_extractor.py`) gerçek metne göre genişlet.
Bu, ürünün en çok emek isteyen ve en çok değer üreten kısmıdır.

**2. Danışman onay ekranını tamamla.** API tarafı hazır
(`PUT /api/opportunities/{id}/rules/{ruleId}`, `POST /{id}/review`), panelde karşılığı yok.
Kural kalitesi danışman onayına bağlı olduğu için bu, pilot öncesi zorunlu adımdır.

---

## 5. Karar verilmesi gereken açık konular

Bunlar teknik değil iş kararlarıdır; devralan tarafın müşteriyle netleştirmesi gerekir.

| Konu | Neden şimdi | Varsayılan davranış |
|---|---|---|
| **Kimlik doğrulama** | Kurumsal SSO mu, yerel hesap mı? `AppUser.ExternalSubjectId` hazır ama OIDC akışı yazılmadı | Yerel hesap + JWT |
| **AI maliyeti** | Her yeni çağrı bir LLM çağrısı demek; aylık bütçe sınırı ve model tercihi netleşmeli | `gpt-4.1` çıkarım, `gpt-4.1-mini` özet |
| **Veri saklama süresi** | KVKK envanteri çıkarılmadı; audit log ve değerlendirme geçmişi süresiz büyüyor | Sınırsız |
| **Çok kiracılılık derinliği** | Danışmanlık şirketi senaryosu (bir kiracı, çok firma) modellendi ama fiyatlandırma/kota kuralları belirsiz | `Tenant.MaxCompanies` |
| **.NET sürümü** | Proje dosyası .NET 8 diyor, kod .NET 10 hedefliyor | `docs/adr/0004`'te gerekçeli; teknopark dokümanında "C# / .NET (LTS)" önerilir |

---

## 6. Tuzaklar

Bu projede zaman kaybettirebilecek, koda bakınca hemen görünmeyen noktalar:

- **Solution dosyası `GovAI.slnx`** (yeni XML formatı). `dotnet build GovAI.sln` "proje dosyası yok"
  hatası verir. Argümansız kullan.
- **`dotnet-ef` yerel araçtır** → komut `dotnet dotnet-ef ...` (çift `dotnet`).
  Önce `dotnet tool restore`.
- **Alan beyaz listesi iki yerde tanımlı** (C# ve Python). Birini güncelleyip diğerini unutmak
  sessiz skor bozulmasına yol açar; `scripts/check_contract_parity.py` bunu yakalar, CI'da koşar.
- **Web'de `--max-warnings 0`** — lint uyarısı build'i kırar.
- **React context'leri `app/contexts.ts`'te**, provider'lar ayrı dosyalarda. Hook'u provider
  dosyasına geri taşırsan fast-refresh lint kuralı patlar.
- **`Guid.CreateVersion7()`** .NET 9+ gerektirir. .NET 8'e dönülecekse değiştirilmeli.
- **Seed parolası ile worker parolası aynı olmalı** (`SEED_ADMIN_PASSWORD` ↔ `GOVAI_API_PASSWORD`),
  yoksa worker'lar API'ye giriş yapamaz.

---

## 7. Belge haritası

| Soru | Belge |
|---|---|
| Nasıl çalıştırırım? | `README.md` |
| Kod yazarken nelere dikkat etmeliyim? | `CLAUDE.md` |
| Sistem nasıl kurgulandı? | `docs/architecture.md` |
| Skor nasıl hesaplanıyor? | `docs/scoring.md` |
| Tablolar ve alanlar? | `docs/data-model.md` |
| Hangi endpoint ne yapar? | `docs/api.md` |
| Bu neden böyle yapılmış? | `docs/adr/` |
| Sırada ne var? | `docs/roadmap.md` |
