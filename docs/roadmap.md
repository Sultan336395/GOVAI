# 12 aylık yol haritası

Proje takvimi: **01.06.2026 – 31.05.2027**

Bu tablo proje dosyasındaki iş-zaman planını, kurulan altyapıda hangi bileşenlerin
hazır olduğu ve neyin yapılacağı ile birlikte gösterir.

| Ay | Odak | Altyapıda hazır | Yapılacak |
|---|---|---|---|
| 1 | İhtiyaç analizi, kullanıcı senaryoları, veri kaynaklarının haritalanması, mimari netleştirme | Katmanlı mimari, ADR'ler, `sources` modeli | Gerçek kaynak envanteri, kurum bazlı seçici konfigürasyonları |
| 2 | Kurumsal ve fırsat veri modeli, yetki ve güvenlik tasarımı | 18 tablo, ilk migration, 5 rol, JWT, audit log | KVKK veri envanteri, saklama süreleri, SSO kararı |
| 3 | .NET çekirdek altyapı, kullanıcı yönetimi, firma kartı, temel API iskeleti | Onion katmanları, 8 endpoint grubu, Swagger, sağlık kontrolü | Kullanıcı davet akışı, parola sıfırlama, e-posta altyapısı |
| 4 | Kaynak tarama servisleri, parser yapısı, veri toplama iş akışları | Collector (robots.txt uyumlu), PDF/HTML çıkarıcı, hash tabanlı tekilleştirme | Taranmış PDF'ler için OCR, kurum bazlı seçici kütüphanesi |
| ERP adaptörleri | Çekme yolu kuruldu: bağlantı kaydı, alan eşlemesi, gece turu ve deneme çekimi. Logo/Netsis/SAP varsayılan eşlemeleri gerçek bir kurulumda doğrulanmadı; pilot müşteride teyit edilecek. | Kısmen tamam |
| 6 | Kural motorunun ilk sürümü, metinlerden koşul çıkarımı | `RuleEvaluator` (13 operatör), deterministik kalıplar, alan beyaz listesi | Kalıp kütüphanesinin gerçek metinlerle genişletilmesi |
| Ağırlık kalibrasyonu | Ölçüm altyapısı kuruldu: `ExpertVerdict` kaydı ve `CalibrationReport` (uyum oranı, yanlış pozitif/negatif, karışıklık matrisi, skor ayrım gücü). Ağırlıkların gerçek vaka verisiyle yeniden kalibre edilmesi örneklem 20 vakayı geçtiğinde yapılacak. | Ölçülebilir |
| 8 | AI açıklama servisi, anlam eşleştirme, yönetici özetleri | OpenAI istemcisi, JSON şemalı kural çıkarımı, kural tabanlı yedek | Prompt iyileştirme, çıktı kalitesi ölçümü, maliyet takibi |
| 9 | Dashboard, raporlama, PDF/Excel çıktıları, bildirim servisleri | React paneli (8 ekran), QuestPDF + ClosedXML, bildirim tekilleştirme | E-posta/webhook gönderim entegrasyonu, rapor şablonları |
| 10 | Pilot müşteri kurulumu, senaryo testleri, performans ve güvenlik | docker-compose, CI, sağlık kontrolleri, root olmayan kapsayıcılar | Sızma testi, yük testi, pilot firma verisiyle doğrulama |
| 11 | Kullanıcı geri bildirimi, revizyonlar, kural kütüphanesi zenginleştirme | Danışman onay akışı, kural elle düzeltme | Sektörel kural setleri, istisna yönetimi ekranları |
| 12 | Canlıya geçiş, dokümantasyon, demo ortamı, ticarileştirme paketleri | Mimari/API/veri modeli dokümanları, seed verisi | Üretim dağıtım hattı, yedekleme/geri yükleme provası, paket tanımları |

## Kritik teknik borçlar

Altyapı kurulurken bilinçli olarak sonraya bırakılanlar:

| Konu | Neden bırakıldı | Ne zaman |
|---|---|---|
| **OCR** | Taranmış PDF'ler kaynakların azınlığı; Tesseract bağımlılığı imaj boyutunu ciddi büyütüyor | Ay 4 |
| **Refresh token akışı** | `CreateRefreshToken()` üretiliyor ama sunucuda saklanmıyor; şu an jeton süresi dolunca yeniden giriş gerekiyor | Ay 3 |
| **E-posta/webhook gönderimi** | Bildirimler üretiliyor ve kuyruğa bırakılıyor; gerçek gönderim adaptörü yok | Ay 9 |
| **Kural ağırlığı kalibrasyonu** | Gerçek başvuru sonucu verisi olmadan istatistiksel kalibrasyon mümkün değil | Ay 10–11 |
| **Web paneli birim testleri** | Ekranlar API sözleşmesine bağlı; sözleşme oturmadan test yazmak erken | Ay 9 |
| **Kod bölme (code splitting)** | Bundle 714 KB; tek kullanıcılı kurumsal panelde kabul edilebilir | Ay 9 |
| **Postgres tam metin arama** | `pg_trgm` ve `unaccent` eklentileri kuruldu, henüz kullanılmıyor | Ay 6 |

## Ticarileşme bileşenleri

Altyapıda karşılığı olan noktalar:

- **SaaS abonelik** — `Tenant.Plan` ve `MaxCompanies` kotası hazır; ödeme entegrasyonu yok.
- **Beyaz etiket** — çok kiracılı model ve `Consultant` rolünün firma kapsamı hazır.
- **API lisanslama** — REST API dokümante ve rol bazlı korumalı; API anahtarı/kota katmanı yok.
- **ERP modülü** — `/erp-sync` sözleşmesi hazır; ERP tarafı adaptörleri yazılacak.
