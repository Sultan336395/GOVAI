# Skorlama metodolojisi

Bu doküman kural motorunun ve skorlayıcının davranışını tanımlar. Kod karşılığı
`src/GovAI.Domain/Eligibility/` ve `src/GovAI.Domain/Scoring/` altındadır.

## 1. Formül

```
Final Opportunity Score =
    0.25 × sectorMatch
  + 0.20 × financialFit
  + 0.15 × employeeFit
  + 0.15 × documentReadiness
  + 0.10 × regionalCompliance
  + 0.10 × technicalQualification
  + 0.05 × timingScore
```

Sonuç 0–100 aralığına ölçeklenir. Ağırlıkların toplamı her zaman 1.0'dır; `ScoreWeights`
yapıcısı bunu doğrular ve ihlalde `DomainException` fırlatır (bir testle de korunur).

### Destek türüne göre ağırlıklar

| Boyut | Varsayılan | İhale | İstihdam teşviki |
|---|---|---|---|
| sectorMatch | 0.25 | 0.15 | 0.20 |
| financialFit | 0.20 | 0.20 | 0.15 |
| employeeFit | 0.15 | 0.10 | **0.30** |
| documentReadiness | 0.15 | **0.20** | 0.15 |
| regionalCompliance | 0.10 | 0.10 | 0.10 |
| technicalQualification | 0.10 | **0.20** | 0.05 |
| timingScore | 0.05 | 0.05 | 0.05 |

Gerekçe: ihalede teknik yeterlilik ve evrak eksiksizliği elenme sebebidir; istihdam
teşviklerinde ise neredeyse tüm koşullar personel yapısı üzerinden tanımlanır.

## 2. Boyut puanı nasıl hesaplanır

Her boyut, o boyuta ait kuralların **ciddiyet ağırlıklı ortalamasıdır**:

| Ciddiyet | Ağırlık | Anlamı |
|---|---|---|
| `Blocking` | 3 | Sağlanmazsa başvuru reddedilir |
| `Major` | 2 | Skoru ciddi düşürür |
| `Minor` | 1 | Bilgilendirici |
| `Bonus` | 0 | Ortalamaya girmez; sağlanırsa +0.05 (en fazla +0.10) |

Kural sonucunun değeri:

| Sonuç | Değer |
|---|---|
| `Satisfied` | `Strength` (ikili kurallarda 1.0, NACE gibi dereceli kurallarda 0.6–1.0) |
| `NotSatisfied` | `Strength` (kısmi eşleşmede kısmi kredi, genelde 0) |
| `Unknown` | 0.5 — veri eksikliği ne tam ödül ne tam ceza |
| `NotApplicable` | Hesaba katılmaz |

**Kuralı olmayan boyut 1.0 alır**: çağrı o boyutta kısıt koymuyorsa bu firma lehinedir.
**Tek istisna `sectorMatch`'tir** — gerekçesi §2.1'de.

### 2.1 sectorMatch neden istisna

Diğer boyutlarda "kural yok" gerçekten *"çağrı bu boyutta kısıt koymuyor"* demektir:
belge istemeyen bir çağrıda belge hazırlığı sorun değildir. Sektörde ise kural yokluğu
"her sektör kabul" anlamına **gelmez**; yalnızca çağrı metninden sektörün
çıkarılamadığını gösterir.

Fark ürünü bozdu: ihale ilanlarından hiç sektör kuralı çıkmadığı için makine imalatı
yapan bir firmaya "Didi soğuk çay nakliye", inşaat firmasına "Sivas YHT Garı buz çözme"
ihalesi 88 puanla "uygun" olarak önerildi. Bu yüzden **kuralsız sektör boyutu 0.5 alır**
(`UnverifiedSectorScore`) — `Unknown` kural kredisiyle aynı mantık: ne tam ödül ne tam ceza.

### 2.2 Sektör uyumu ve liste sıralaması

Her değerlendirme üç değerli bir `SectorFit` taşır ve kayıtla birlikte saklanır:

| Değer | Ne zaman | Listedeki yeri |
|---|---|---|
| `Matched` | Çağrının sektör koşulu var ve firma karşılıyor | En üstte |
| `Unverified` | Çağrıda sektör koşulu yok **ya da** firmanın NACE verisi eksik | Ortada, "sektör uyumu doğrulanamadı" etiketiyle |
| `NotMatched` | Çağrının sektör koşulu var ve firma karşılamıyor | En altta |

Liste **önce bu değere**, sonra kullanıcının seçtiği ölçüte (skor / son başvuru / tarih)
göre sıralanır. Alt ölçütler — personel sayısı, ciro, işletme yaşı, kadın ve genç çalışan
yapısı — zaten `FinalScore` içinde ağırlıklarıyla toplanmıştır; sektör grubunun *içinde*
konuşurlar.

İki kural bunu dengeler:

- **Uyumsuzluk eleme değildir.** Kayıt gizlenmez, kararı `NotEligible` yapılmaz; en alta
  iner ve etiketlenir. Firma hukuken teklif verebilir, kararı kullanıcı verir.
- **"Bilmiyorum" ile "hayır" ayrıdır.** Firmanın NACE kodu girilmemişse sonuç
  `NotMatched` değil `Unverified`'dır (CLAUDE.md §2.2).

Sıralama veritabanında, sayfalamadan **önce** yapılır (`AssessmentRepository`); bellekte
sıralansaydı ilk sayfa doğru, sonraki sayfalar yanlış görünürdü.

İlan tarafındaki eksik `workers/govai_workers/parser/sektor.py` ile kapatılır: ilanın
konusu Türkçe anahtar kelimelerle tanınır ve NACE kuralı üretilir. Üretilen kural
`Blocking` değil `Major`dır — sektör ilanın *konusundan* çıkarımdır, metinde yazan bir
yeterlilik şartı değil.

### İki özel boyut

**documentReadiness** — Çağrının belge listesi firmanın geçerli sertifikalarıyla karşılaştırılır.
Zorunlu belgeler tam, opsiyonel belgeler yarım ağırlıkla sayılır. Süresi dolmuş belge 0 değil
%50 kredi alır (yenilenmesi kısa sürer). **Belge listesi hiç çıkarılamadıysa nötr 0.5 uygulanır** —
1.0 vermek "belgeleriniz tamam" yanılgısı yaratırdı.

**timingScore** — Son başvuru tarihinden hesaplanır. Amaç yalnızca "süre var mı" değil,
"hazırlık için makul süre var mı":

| Kalan gün | Puan | Gerekçe |
|---|---|---|
| < 0 | 0.00 | Süre doldu |
| 0 | 0.10 | Bugün son gün |
| 1–7 | 0.35 | Hazırlık süresi çok kısıtlı |
| 8–14 | 0.60 | Hızlı hareket gerekli |
| 15–30 | 0.85 | Yeterli süre |
| 31–90 | 1.00 | İdeal aralık |
| > 90 | 0.80 | Aciliyet düşük, takibe alınır |
| Tarih yok | 0.70 | Sürekli açık çağrı |

## 3. Karar (verdict)

```
Engelleyici bir koşul sağlanmıyor        → NotEligible   (skor 0'a sıfırlanır)
Kuralların %40'ından fazlası "Unknown"   → Indeterminate
Sağlanmayan koşul veya eksik zorunlu belge → ConditionallyEligible
Aksi hâlde                                → Eligible
```

Engelleyici ihlalde skorun sıfırlanması bilinçlidir: yüksek boyut puanları, firmanın hukuken
başvuramayacağı bir çağrıyı listede yukarı taşımamalıdır.

## 4. Skor güveni

Skorun yanında 0–1 arası bir **güven** değeri döner:

```
confidence = 0.5 × veri doluluğu + 0.5 × kural çıkarım güveni
```

- **Veri doluluğu**: `1 − (Unknown sonuçlanan kural / toplam kural)`
- **Kural çıkarım güveni**: parser/AI'ın çağrı metnini ne kadar güvenle ayrıştırdığı.
  Danışman çağrıyı onayladıysa bu bileşen 1.0'a çıkar.

Bu ayrım kullanıcıya iki farklı soruyu ayrı ayrı cevaplar: "skorum kaç" ve "bu skora ne kadar güvenebilirim".

## 5. Eksik veri ile sıfır arasındaki fark

Motorun en kritik davranışı budur. Doldurulmamış bir profil firmayı **elemez**:

| Alan | Boş değer | Yorum |
|---|---|---|
| `Financials.*` | 0 | "Girilmemiş" → `Unknown` |
| `Workforce.*` | `EmployeeCount = 0` | Tüm personel alanları `Unknown` |
| `Company.NaceCodes` | boş küme | `Unknown` — her firmanın NACE kodu vardır |
| `Company.Cities` / `Nuts2Codes` | boş küme | `Unknown` — her firmanın adresi vardır |
| `Company.Certificates` | boş küme | **Bilinen değer** — "belgemiz yok" geçerli bir cevaptır |

Sertifika istisnası önemlidir: onu da `Unknown` saymak, "ISO 9001 eksik" uyarısını yok ederdi.

## 6. NACE eşleşmesi

Resmî çağrılar çoğu zaman ana grubu ("25") verirken firma tam kodu ("25.62.01") taşır.
Eşleşme bu yüzden dereceli önek karşılaştırmasıdır:

| Firma kodu | Çağrı kodu | Güç | Yorum |
|---|---|---|---|
| 2562 | 2562 | 1.00 | Tam eşleşme |
| 256201 | 2562 | 0.95 | Firma daha spesifik |
| 2562 | 256 | 0.85 | Çağrı 3 haneli grup |
| 2562 | 25 | 0.75 | Çağrı ana grup |
| 2562 | 25620 | 0.60 | Firma daha genel — zayıf ama elenmez |
| 2562 | 62 | 0.00 | Eşleşme yok |

0.6 ve üzeri "sağlandı" sayılır; altındakiler sektörel uyumsuzluk olarak raporlanır.
Çağrı hiç NACE kısıtı koymuyorsa güç 1.0'dır — ancak çağrının hiç sektör **kuralı**
olmaması ayrı bir durumdur ve boyut puanı 1.0 değil 0.5 olur (§2.1).

## 7. Senaryo simülasyonu

`ScenarioSimulationService` firmanın **bellek içi bir kopyasını** üretir, üzerine senaryo
değişikliklerini uygular ve aynı deterministik motoru çalıştırır. Gerçek kayda dokunulmaz —
bu bir test değil, tasarımın kendisidir (`Senaryo_gercek_firma_kaydini_degistirmez` testiyle korunur).

Çıktı, her fırsat için taban skor, senaryo skoru, fark ve karar değişimini içerir;
`becameEligible` bayrağı senaryo sayesinde uygun hâle gelen fırsatları öne çıkarır.

## 8. Açıklanabilirlik zinciri

Her skor, kaynağına kadar geriye izlenebilir:

```
Nihai skor
  └── Boyut puanı (değer × ağırlık = katkı, + Türkçe gerekçe)
        └── Kural sonucu (beklenen değer, firmanın değeri, sonuç)
              └── sourceExcerpt — çağrı metnindeki orijinal cümle
```

`EligibilityAssessment.DetailJson` bu zincirin tamamını jsonb olarak saklar; profil sürümü
(`CompanyProfileVersion`) ile birlikte "skorum neden değişti" sorusu geriye dönük cevaplanabilir.

## 9. Bilinen sınırlar

- Ağırlıklar uzman görüşüne dayalıdır; gerçek başvuru sonuçlarıyla kalibre edilmemiştir.
  Pilot müşteri verisi biriktikçe (Ay 10–11) ağırlıklar gözden geçirilmelidir.
- Metin katmanı olmayan taranmış PDF'ler için OCR henüz yoktur; parser bunu loglar.
- Kural çıkarımının doğruluğu çağrı metninin diline duyarlıdır. Danışman onayı bu yüzden
  akışın zorunlu parçasıdır, opsiyonel bir özellik değil.
