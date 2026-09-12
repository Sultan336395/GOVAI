# GOVAI derin teknoloji çekirdeği — bugünkü durum ve yol haritası

Bu belge altı derin teknoloji iddiasının **koddaki karşılığını** ve gerçekçi mesafesini
yazar. Amaç tanıtım metni üretmek değil; hangi iddianın bugün savunulabilir olduğunu,
hangisinin ne kadar yol istediğini ve neyin önünde **veri** engeli olduğunu ayırmak.

Ölçü şudur: bir değerlendirici "bunu gösterin" derse ekranda ya da testte gösterilebilen
şey **var**, gösterilemeyen şey **yol haritası**dır. İkisi aynı cümlede yazılmaz.

---

## 1. Özet tablo

| İddia | Durum | Nerede |
|---|---|---|
| Kanıta bağlı nöro-sembolik yapay zekâ | **Çalışıyor** | `Domain/Analysis`, `OpportunityRuleEvidence` |
| Belirsizlik hesaplama | **Çalışıyor** | `ConfidenceCalculator`, `RegulationConfidence` |
| Karşı-olgusal simülasyon | **Çalışıyor** | `ScenarioSimulation`, `ComplianceLeverage` |
| Zamansal mevzuat bilgisi | **Kısmen** — sürümleme var, graf yok | `SourceDocumentVersion`, `RegulatoryChange` |
| Biçimsel doğrulama | **Kısmen** — motor deterministik, kanıt yok | `EligibilityEngine`, `DeterministicValidators` |
| Karşılaştırmalı başarı ölçümü | **Altyapı hazır, örneklem sıfır** | `ExpertVerdict`, `CalibrationReport` |
| Özel eğitilmiş çok dilli hukuk modeli | **Yok** — eğitim verisi yok | — |
| Özgün mevzuat–ERP ontolojisi | **Kısmen** — alan beyaz listesi var, ontoloji yok | `CompanyFieldResolver.SupportedFields` |
| ERP süreç madenciliği | **Veri yolu açıldı, madencilik yok** | `ErpProcessEvent` |
| Nedensel çıkarım | **Yok** — gözlem birikmiyor | — |

---

## 2. Bugün savunulabilir olanlar

### 2.1 Kanıta bağlı nöro-sembolik mimari

Bu bir hedef değil, çalışan yapı. Model **önerir**, kural motoru **karar verir**
(ADR-0002). Aradaki hat kodda somut:

- `AiClaim` — modelin iddiası, kararın kendisi değil
- `DeterministicValidators` — iddiayı kurala karşı sınar
- `DecisionMerger` — iki tarafı birleştirir, çelişkide kuralı üstün tutar
- `ExplainableScore` — puanın hangi koşuldan geldiğini taşır
- `OpportunityRuleEvidence` — kuralı resmî belgedeki cümleye bağlar; üç rol ayrımıyla
  (doğrudan kanıt / koşul cümlesi / yalnızca bağlam)
- `PromptInjectionGuard` — belge metninin modele talimat vermesini engeller

**Gösterilebilir:** bir skorun hangi cümleye dayandığı ekranda görülür. OpenAI anahtarı
olmadan da sistem çalışır; bu, modelin karar vermediğinin kanıtıdır.

**Bilinen sınır:** 30 kuraldan 12'sinin kanıt bağlantısı var. Geri kalanı için
"dayanak gösterilemiyor" denir, uydurulmaz.

### 2.2 Kanıt eskimesi (Evidence Half-Life)

Uyum araçlarının durduğu yerden devam eder: "belge var" ile "belge hâlâ güvenilir"
ayrımını ölçer. Ayrıntı ve gerekçeler ADR-0008'de.

**Gösterilebilir:** firma profilinde her kanıtın güvenilirlik skoru, yaşı, yarı ömrü ve
**gerekçesi**. Süresi dolmuş belge geçersiz, yakında bitecek belge zayıflıyor görünür.

**Bilinen sınır:** yarı ömür sayıları gerekçeli ama **kalibre edilmemiş**
varsayımlardır. Gerçek denetim vakalarıyla ölçülmeleri gerekir.

### 2.3 Uyumun fırsata çevrilmesi (Compliance-to-Opportunity)

"Şu koşulu sağlamıyorsun" cümlesini "şunu kapatırsan şu çağrılara girersin"e çevirir.
Rakiplerden ayrışma noktası burada somutlaşır: yalnızca uyuma bakan bir ürün bu hesabı
yapamaz, çünkü fırsat ve ihale tarafı yoktur.

**Gösterilebilir:** panoda her eksik, açtığı çağrılar ve bilinen tutarla birlikte.
"Açılır" yalnızca başka engel kalmadığında yazılır.

**Bilinen sınır:** eksiği kapatmanın **maliyeti** modellenmedi; getiri gösteriliyor,
maliyet gösterilmiyor ve bu ekranda belli.

---

## 3. GOVAI Regulatory Causal Twin

Hedef: yeni bir kanun geldiğinde hangi departmanın etkileneceğini, uyum maliyetini,
hareketsizlik riskini ve en ucuz önlemi simüle etmek.

### Bugün ne var

| Parça | Durum |
|---|---|
| Mevzuat etki analizi | `RegulationImpactEvaluator` — dört değerli mantık: bilinmiyor / kapsamda / kapsam dışı / muhtemelen kapsamda |
| Karşı-olgusal simülasyon | `ScenarioSimulation` — "şunu değiştirsem kaç fırsat açılır, skor ne olur" |
| Zamansal mevzuat | `SourceDocumentVersion` — metnin hangi sürümüne dayanıldığı |
| Süreç olay günlüğü | `ErpProcessEvent` — çekme yolu açık (ADR-0008) |

Dört değerli mantık bilinçlidir: hukukta "hayır" ile "belli değil" farklıdır ve sistem
**kesin hukuki tavsiye vermez**. En sık çıkan sonuç "muhtemelen kapsamda"dır; bu bir
eksiklik değil dürüstlüktür.

### Ne eksik

**Gözlem.** Süreç madenciliği vaka kimliği, faaliyet ve zaman damgası ister; çekme yolu
açıldı ama hiçbir müşteride henüz tanımlı değil. Nedensel çıkarım için ayrıca
**müdahale öncesi ve sonrası** gözlem gerekir: bir mevzuat yürürlüğe girdiğinde
süreçlerin nasıl değiştiğini görmeden "etki" hesaplanamaz.

**Maliyet verisi.** Uyum maliyeti hesabı bordro ve muhasebe kırılımı ister; ERP
eşlemesinde o alanlar tanımlı değil ve tanımlanmaları ayrı bir mahremiyet kararıdır.

### Gerçekçi sıra

1. En az bir müşteride olay günlüğü eşlemesini açmak ve **birikmeye başlamak**
2. Süreç keşfi: faaliyet sıklıkları, ortalama süreler, departman dağılımı — bunlar
   nedensel değil **betimleyici**dir ve kendi başlarına değerlidir
3. Bir mevzuat değişikliğinin öncesi–sonrası karşılaştırması
4. Ancak yeterli vaka biriktiğinde nedensel çıkarım

Bu sıra atlanamaz. Üçüncü adımdan önce "nedensel etki algoritması" demek, ölçülmemiş
bir şeyi ölçülmüş gibi sunmak olur.

---

## 4. Determinizm gerilimi ve çözümü

"Özel eğitilmiş hukuk modeli" ve "nedensel etki algoritması" ürünün ilk iki iddiasıyla
çatışabilir: skor **deterministik** ve **açıklanabilir** olmalıdır.

Çözüm mimaride zaten var ve yeni yetenekler de aynı hattan geçmelidir:

| Katman | Ne yapabilir | Ne yapamaz |
|---|---|---|
| Model | Kural taslağı çıkarır, metni özetler, ikinci görüş verir | Skoru ve kararı değiştiremez |
| Kural motoru | Skoru ve kararı üretir | Model çağıramaz, rastgelelik içeremez |
| Ölçüm | Skoru insan kararıyla kıyaslar | Ağırlıkları kendiliğinden değiştiremez |

Uyum maliyeti gibi bir **tahmin** üretilecekse, "şu belgede yazıyor" diye geri
izlenemediği sürece karar değil, belirsizliği açıkça yazılmış bir tahmin olarak
durmalıdır. Aksi hâlde ticari savunma hattı zayıflar.

---

## 5. Asıl darboğaz: veri seti

"Elle doğrulanmış bilimsel veri seti" ve "karşılaştırmalı başarı ölçümleri" için
altyapı **hazır**:

- `ExpertVerdict` — danışmanın kendi kararı, sistem kararının kopyasıyla birlikte
- `CalibrationReport` — uyum oranı, yanlış pozitif/negatif, karışıklık matrisi,
  skorun ayrım gücü
- İnsan ve yapay zekâ görüşleri **ayrı** raporlanır ve hiçbir yerde toplanmaz; ikisi
  aynı metni okuyup aynı yanlışı yapabilir, yüksek uyum doğruluk değil ortak körlük
  olabilir (ADR-0005)

Eksik olan tek şey **örneklem**. `MinimumSampleSize = 20` altındaki her rapor
"yorumlanamaz" işaretlenir ve bugün kayıt sayısı sıfırdır.

Bu, kod yazmadan bugün başlatılabilecek tek iştir ve bir değerlendiricinin ilk soracağı
şeydir. Her pilot değerlendirmede danışmanın kendi kararını girmesi, üç ay sonra
"sistemimizin doğruluğu şudur" cümlesini kurulabilir hâle getirir. Girilmezse o cümle
üç ay sonra da kurulamaz.

---

## 6. Bu belgeyi güncel tutmak

Bir yetenek "çalışıyor"a geçtiğinde tablo güncellenir ve **nerede görülebileceği**
yazılır. Gösterilemeyen hiçbir şey "çalışıyor" işaretlenmez; bu belgenin tek değeri
o ayrımı koruması.
