# ADR-0005: Uzman görüşü skoru değiştirmez, skoru ölçer

- **Durum:** Kabul edildi
- **Tarih:** 2026-09-11

## Bağlam

Proje dosyası, ürünün Ar-Ge iddiasını açıkça tarif ediyor: skor modeli "proje başlangıcında
tamamen çözülmüş, hazır bir algoritma değildir"; farklı fırsat türlerinde test edilmesi, yanlış
pozitif ve yanlış negatif eşleşmelerin analiz edilmesi ve **uzman değerlendirmeleriyle
karşılaştırılarak** iteratif olarak iyileştirilmesi gerekir.

Bu iddianın kanıtlanabilmesi için sistemin kendi isabetini ölçebilmesi gerekiyordu. Ölçüm
olmadan ağırlık kalibrasyonu bir görüş alışverişine dönüşür: hangi ağırlığın neden
değiştirildiği kayıt altına alınamaz ve değişikliğin iyileştirme mi gerileme mi olduğu
söylenemez.

Ölçüm eklenirken iki yol vardı ve ikisi birbirini dışlıyordu:

1. **Uzman görüşünü karar mekanizmasına girdi yapmak.** Danışmanın kararı skoru düzeltir,
   sistem zamanla "öğrenir".
2. **Uzman görüşünü karar mekanizmasının denetçisi yapmak.** Skor hiç değişmez; uzman kaydı
   yalnızca isabeti ölçmek için saklanır.

## Karar

İkinci yol seçildi. **Uzman görüşü hiçbir skoru, kararı veya ağırlığı değiştirmez.**

`ExpertVerdict` kaydı, danışmanın kararını sistemin o anki kararı ve skoruyla birlikte saklar.
`CalibrationReport` bu kayıtlardan uyum oranını, yanlış pozitif/negatif sayılarını, karışıklık
matrisini, ayrışma sebeplerini ve skorun ayrım gücünü hesaplar. Rapor **öneri üretmez**; hangi
ağırlığın nasıl değiştirileceği insan kararıdır.

## Gerekçe

Birinci yol ürünün ilk iki iddiasını birden bozardı:

- **Determinizm.** Aynı girdi her zaman aynı çıktıyı vermelidir. Uzman görüşünün skoru
  etkilemesi, aynı firma–çağrı çiftinin geçmişte kimin baktığına göre farklı puan alması
  demektir. "Skorum neden değişti" sorusu cevaplanamaz hâle gelir.
- **Açıklanabilirlik.** Her puan çağrı metnindeki cümleye kadar geri izlenebilmelidir. Uzman
  görüşüyle kaymış bir puanın dayanağı metinde yoktur; kanıt zinciri kopar.

Denetçi konumu ayrıca kalibrasyonun kendisini dürüst tutar: ölçülen ile ölçen aynı şey olursa
ölçüm anlamını yitirir.

## Sonuçlar

**Kayıt silinmez.** `IExpertVerdictRepository` bilerek silme yöntemi sunmaz. Uyumsuz çıkan
kayıtların silinebilmesi, raporu istenen sonuca göre şekillendirmeyi mümkün kılardı.

**Sistem kararı kopyalanarak saklanır.** Değerlendirme sonradan yeniden hesaplansa bile
karşılaştırma o anın fotoğrafı olarak kalır; aksi hâlde bugünün skoruyla dünün uzman görüşü
kıyaslanmış olurdu.

**Ayrışma sebebi zorunludur ve sabit listeden seçilir.** Serbest metin sayılamaz. "Kural yanlış
çıkarılmış" ile "ağırlık yanlış" ayrı düzeltmeler gerektirir; tek başlık altında toplamak hangi
işin yapılacağını belirsizleştirir.

**Eksik veriden doğan ayrışma ayrı sayılır.** Ayrışmanın sebebi çoğu zaman modelin yanlışlığı
değil verinin yokluğudur. İkisini ayırmadan yapılan kalibrasyon, veri toplama sorununu ağırlık
sorunu sanarak ağırlıkları bozar.

**Yetersiz örneklem yeterliymiş gibi sunulmaz.** `MinimumSampleSize = 20` altındaki ölçümler
üretilir ama "yorumlanamaz" işaretiyle gelir. Üç vakayla hesaplanan bir "%33 hata oranı"
istatistik değil gürültüdür.

**Kural çıkarım kalitesi var olan veriden ölçülür.** Danışman zaten ürünün akışında hatalı
kuralları düzeltiyor (`OpportunityRule.IsManuallyOverridden`). Ayrı bir veri toplama ekranı
kurmak yerine bu alan sayılır: ölçüm için yeni iş yaratmamak, ölçümün sürdürülebilirliğinin ön
koşuludur.

## Koruyan testler

`CalibrationReportTests` (14) ölçümün kendisini sabitler: hata türleri, yönü, örneklem uyarısı,
determinizm ve sebep zorunluluğu. `CalibrationTests` (9) sınırları korur: uzman görüşü kaydı
sonrası skorun **değişmediği**, aynı değerlendirme için ikinci kayıt açılmadığı, başka kiracının
göremediği ve platform rollerinin giremediği.
