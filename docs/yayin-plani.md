# Faz 3 yayın planı

Bu belge, Faz 3 çalışmasının canlıya alınma sırasıdır. **"Yayınla" komutu gelmeden
hiçbir adım uygulanmaz.** Adımlar sırasıyla ve atlanmadan yürütülür; bir adım
başarısızsa sonrakine geçilmez, §6'daki geri dönüş planı uygulanır.

Kapsam yalnızca **govai** projesidir. Sunucudaki diğer container'lara, volume'lara,
imajlara ve veritabanlarına dokunulmaz. PostgreSQL, Redis ve RabbitMQ yeniden
başlatılmaz, yeniden oluşturulmaz, imajları çekilmez, yapılandırmaları değiştirilmez.

---

## 1. Yayın kapsamı

| Ne | Nerede |
|---|---|
| Kural–kanıt geriye dönük bağlama | `RuleEvidenceBackfillService`, `/api/opportunities/rule-evidence/backfill` |
| KOSGEB / SGK katalog onarımı | `CatalogRepairService`, `/api/sources/catalog-repair` |
| OpenAI üretim sağlayıcısı | `OpenAiAnalysisProvider`, `GOVAI_AI_*` |
| Güven göstergesi ayrımı | `AnalysisMapper`, `AnalysisPanel.tsx` |
| Migration | `KuralKanitBaglantisi`, `SirketKiraciIliskisi`, `ModelKullanimMiktari` |

Üç migration da **eklemelidir**: yeni tablo ve yeni nullable kolonlar. Hiçbiri kolon
düşürmez, tip daraltmaz veya veri dönüştürmez. Bu yüzden eski uygulama imajı yeni
şemayla çalışmaya devam edebilir — geri dönüş planının dayandığı gerçek budur.

---

## 2. Yayın öncesi güvenlik kapısı

Aşağıdaki dördü tamamlanmadan yayın başlatılmaz.

### 2.1 Tam yedek

```bash
bash scripts/yayin-yedek-al.sh faz3
```

İki biçim birden alınır (`-Fc` custom ve düz `.sql`), izinler `600`'e çekilir,
SHA-256 özetleri yazılır. Betik salt okurdur; hiçbir container'a dokunmaz.

### 2.2 Yedeğin gerçekten geri yüklenerek doğrulanması

```bash
bash scripts/yayin-yedek-dogrula.sh /opt/govai/yedek/yayin-oncesi-faz3
```

Her iki biçim **ayrı birer geçici veritabanına** yüklenir, tablo ve satır sayıları
canlıyla karşılaştırılır, sonra yalnızca o iki geçici veritabanı düşürülür. Düşürme
işlemi `govai_dogrulama_test_*` desenine kilitlidir; başka bir ad verilse betik
durur.

> Alınmış ama geri yüklenebildiği kanıtlanmamış bir yedek, yedek değildir.

### 2.3 Mevcut sürüm ve imaj digest kaydı

```bash
bash scripts/yayin-manifest.sh > /opt/govai/yedek/manifest-once.txt
```

Manifest çalışan govai container'larını, imaj digest'lerini, uygulanmış
migration'ları, tablo/satır sayılarını ve katalog sağlığını yazar. Yayından sonra
aynı betik tekrar çalıştırılıp `diff` alınır.

Ek olarak, geri dönüşün hedefi olacak **çalışan imajlara değişmez etiket** verilir
(bkz. `deploy/docker-compose.rollback.yml`). Etiketsiz bir imaj yeniden derlemede
isimsiz kalır ve geri dönüş imkânsızlaşır.

### 2.4 Değişmez etiketli yeni imajlar

İmajlar **GitHub Actions'ta** üretilir; sunucuda derleme yapılmaz. Etiket, yayınlanan
commit'in SHA'sıdır:

```bash
export GOVAI_RELEASE_TAG=<commit SHA>
docker image inspect govai-api:$GOVAI_RELEASE_TAG    >/dev/null
docker image inspect govai-web:$GOVAI_RELEASE_TAG    >/dev/null
docker image inspect govai-worker:$GOVAI_RELEASE_TAG >/dev/null
```

Üçü de yerelde bulunmuyorsa yayın **başlatılmaz**: compose eksik imajda `build:`
bölümüne düşer ve sunucuda derleme başlatır.

---

## 3. Veri sayım manifesti — beklenen değerler

Yayın sonrası şu satırlar kontrol edilir (`manifest-sonra.txt`):

| Satır | Beklenen değişim |
|---|---|
| Yayımlanabilir fırsat | KOSGEB onarımı kadar **azalır** (2) |
| Karantinadaki fırsat | 2 **artar** |
| Karantinadaki mevzuat kaydı | SGK onarımı kadar **artar** (en çok 3) |
| Kural (toplam) | **değişmez** — onarım kural silmez |
| Kanıt bağlantısı olan kural | geriye dönük bağlama kadar **artar** |
| Güncel değerlendirme | karantinaya alınan kayıtların değerlendirmeleri kadar **azalır** (silinmez, geçmişe alınır) |

Kural sayısının azalması, değerlendirme sayısının sıfırlanması ya da tablo sayısının
değişmesi **kabul edilemez**; görülürse §6 uygulanır.

---

## 4. "Yayınla" sonrası uygulanacak sıra

Sıra değiştirilmeden uygulanır.

1. Tam SQL ve custom-format yedeği al (§2.1).
2. Her iki yedeği ayrı geçici veritabanına **gerçekten geri yükleyerek** doğrula (§2.2).
3. Migration, tablo ve kayıt sayılarını kaydet (§2.3).
4. Değişmez etiketli uygulama imajlarını hazırla ve varlıklarını doğrula (§2.4).
5. Servisleri kontrollü güncelle — **servis servis, `--no-deps` ile**:

   ```bash
   docker compose -p govai -f deploy/docker-compose.yml -f deploy/docker-compose.release.yml \
     -f deploy/docker-compose.limits.yml up -d --no-deps api
   ```

   Sonra `web`, ardından worker'lar. Veri servisleri (`postgres`, `redis`, `rabbitmq`)
   bu komutlara **hiç girmez**.
6. Migration'ların tamamlandığını doğrula: `__ef_migrations_history` **16** kayıt olmalı.
7. API, web ve worker sağlık kontrolleri: `/health`, web'in açılışı, worker loglarında
   bağlantı hatası olmaması.
8. `GET /api/sources/catalog-repair/plan` — beklenen KOSGEB (2) ve SGK (5) kayıtlarını
   listeden **gözle doğrula**. Beklenmeyen bir kayıt varsa uygulama adımına geçme.
9. Plan doğruysa `POST /api/sources/catalog-repair/apply`.
10. `apply`'ı **ikinci kez** çalıştır; `changed: 0` bekle.
11. `GET /api/opportunities/rule-evidence/backfill/plan?batchSize=200` — mevcut ham
    içeriklerden kanıt bağlama planını çalıştır.
12. Planı kontrol ettikten sonra `POST /api/opportunities/rule-evidence/backfill/apply`.
    `hasMore: true` ise `nextCursor` ile turlara devam et.
13. Aynı işlemi **ikinci kez** çalıştır; `evidenceLinksCreated: 0` ve
    `alreadyBoundCount` dolu olmalı — mükerrer bağlantı oluşmadığının kanıtı.
14. `needsRedownload` raporlanan belgeler için, **yalnızca doğrulanmış resmî
    kaynaklardan** ve kaynağın kendi tarama planıyla kontrollü yeniden toplama yap.
    Anti-bot önlemi aşılmaz, tarayıcı taklidi yapılmaz.
15. Bir KOSGEB fırsatı için şirket–fırsat analizini çalıştır.
16. Bir SGK işveren duyurusu için şirket–mevzuat etki analizini çalıştır.
17. Aynı iki analizi tekrar çalıştır; ikinci `analysis_run` **oluşmamalı**.
18. `GOVAI_AI_API_KEY` mevcutsa tek bir gerçek hibrit analiz smoke testi yap.
19. Anahtar yoksa ekranda **"Kural tabanlı analiz"** ve **"Yapay zekâ güveni:
    Kullanılamıyor"** yazdığını doğrula.
20. Masaüstü ve mobil kullanıcı ekranlarını kontrol et.
21. Tenant yalıtımı, karantina, bildirim ve skor davranışlarını doğrula.
22. API, worker ve tarayıcı loglarını kontrol et — secret ya da kişisel veri
    görünmemeli.
23. Kritik hata varsa §6'yı uygula; yayını başarılı gösterme.

---

## 5. Canlı kabul şartları

Yayın, **hepsi** geçerse başarılı sayılır:

- [ ] Migration'lar hatasız; `__ef_migrations_history` beklenen sayıda.
- [ ] Servisler sağlıklı (`/health`, worker logları temiz).
- [ ] KOSGEB'deki iki hatalı kayıt karantinada.
- [ ] Beş SGK kaydı düzeltilmiş ya da karantinada.
- [ ] Bu kayıtlar katalog, eşleşme, skor, bildirim ve raporda görünmüyor.
- [ ] Mevcut fırsat kriterlerinin bağlanabilenleri gerçek kanıt parçalarına bağlı.
- [ ] Kanıt bağlantısı resmî belge sürümüne ve hash'e gidiyor.
- [ ] Aynı onarım ikinci kez çalıştırıldığında değişiklik üretmiyor.
- [ ] Aynı analiz ikinci kayıt ve ikinci model çağrısı üretmiyor.
- [ ] Karantinadaki belge modele gönderilmiyor.
- [ ] Zorunlu `Unknown` kriter kesin "Uygun" göstermiyor.
- [ ] OpenAI kapalıysa hiçbir yerde "hibrit" ifadesi kullanılmıyor.
- [ ] OpenAI açıksa model cevabı kanıt doğrulamasından geçiyor.
- [ ] Kullanıcı ekranında puan, güven, gerekçe ve resmî kanıt bağlantıları açılıyor.
- [ ] Tenant verileri birbirine karışmıyor.
- [ ] Loglarda secret veya kişisel veri yok.

---

## 6. Geri dönüş planı

Sıra önemlidir: **önce uygulama, sonra (ve ancak gerekirse) veri.**

### 6.1 Uygulama imajlarına dön

```bash
docker compose -p govai -f deploy/docker-compose.yml -f deploy/docker-compose.rollback.yml \
  up -d --no-deps api web collector parser scoring scheduler
```

Eski imajlar değişmez etiketlidir ve yeniden derlenmez. Veri servislerine dokunulmaz.

### 6.2 Migration'ları ACELEYLE geri alma

Üç migration da eklemelidir; eski uygulama yeni şemayla çalışır. `migrations remove`
ya da `database update <öncekiSürüm>` **çalıştırılmaz**: geri alma yeni tabloyu ve
kolonları düşürür, yani geri dönüşün kendisi veri kaybı üretir.

Şema geri alınacaksa bu ayrı bir karardır ve ayrıca onay ister.

### 6.3 Veri bozulması görülürse

Yalnızca §2.2'de **doğrulanmış** yedek kullanılır. Geri yükleme hedefi önce geçici bir
veritabanı olur, karşılaştırma yapılır, ancak ondan sonra gerçek veritabanı hedef
alınır ve bu ayrıca onay ister.

### 6.4 Yarım kalan onarım

Hem katalog onarımı hem kanıt bağlama **idempotenttir**. Yayın yarıda kesilirse aynı
uç yeniden çağrılır: tamamlananlar atlanır, kalanlar işlenir, mükerrer kayıt oluşmaz.
Kanıt bağlama ayrıca `nextCursor` ile kaldığı yerden sürer.

### 6.5 Sınır

Geri dönüş sırasında da başka uygulama, container, volume ya da veriye dokunulmaz.

---

## 7. Kullanıcıdan beklenen tek dış işlem

`GOVAI_AI_API_KEY` — OpenAI API anahtarı. Sunucudaki `/opt/govai/.env` dosyasına
(izin `600`) `GOVAI_AI_PROVIDER=openai` ve `GOVAI_AI_MODEL=<model adı>` ile birlikte
eklenir. Anahtar sohbete, depoya, compose dosyasına ya da loga yazılmaz.

Anahtar verilmezse sistem kural tabanlı çalışmaya devam eder ve bunu ekranda
açıkça söyler; yayın bu yüzden ertelenmez.
