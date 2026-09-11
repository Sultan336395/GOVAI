# ADR-0007: ERP kimliği paylaşılan sırla değil, imzalı kısa ömürlü beyanla kurulur

- **Durum:** Kabul edildi
- **Tarih:** 2026-09-12
- **Değiştirir:** `docs/ikprof-integration.md` §2'deki "OAuth2 client credentials
  (makine kimliği)" kararı

## Bağlam

Müşteri çalışanlarının GOVAI hesabı ve parolası **olmayacak**; kendi ERP'lerindeki
GOVAI modülünü kullanacaklar (bkz. CLAUDE.md §2.2.8). Bu, ERP'nin GOVAI'ye kendi adına
bağlanmasını gerektiriyor.

Bugüne kadar GOVAI'ye gelen her istek bir **GOVAI kullanıcısına** verilmiş JWT
taşıyordu ve kiracı o kullanıcıdan okunuyordu. ERP tarafı için böyle bir kullanıcı yok
ve olmamalı: e-posta almak ya da bildirim okumak için herkese GOVAI hesabı açmak, tam
da kaçınılmak istenen şey.

İlk akla gelen çözüm şirket başına bir **API anahtarı** vermek. Reddedildi.

## Karar

Şirket başına bir **servis kimliği** tanımlanır. GOVAI o kimliğin yalnızca **açık**
anahtarını saklar. ERP her istekte en çok 5 dakikalık, tek kullanımlık, kendi özel
anahtarıyla imzaladığı bir **beyan** üretir ve bunu 10 dakikalık bir GOVAI jetonuna
çevirir.

İki özne türü vardır:

| Özne | Beyandaki `sub` | Ne zaman |
|---|---|---|
| Servis | `iss` ile aynı | ERP'nin arka plan işi |
| Kullanıcı | ERP kullanıcısının kimliği | ERP'de oturum açmış çalışan modülü açtığında |

Kullanıcının GOVAI hesabı yoktur; kimliği yalnızca denetim izinde kullanılır.

## Neden düz makine anahtarı değil

| Paylaşılan sır | İmzalı beyan |
|---|---|
| GOVAI sırrı **saklamak zorunda**; yedek, log ve yapılandırmada sızabilir | GOVAI yalnızca açık anahtarı tutar; yedeği okuyan o şirket adına **konuşamaz** |
| Sızdığında **süresiz** kullanılabilir | Beyan en çok 5 dakika, jeton 10 dakika geçerli |
| Sızdığı **anlaşılmaz** | Her beyan tek kullanımlıktır; tekrar denemesi kayda düşer |
| Değiştirmek kesinti demek | Birden çok anahtar aynı anda etkin olabilir; değişim kesintisiz |
| Kurulumda "anahtarı bir kez gösteriyoruz" adımı var | Gösterilecek sır **yok** |

Son satır küçük görünür ama sahada en sık sızma yolu odur: bir kez gösterilen sır ekran
görüntüsüne, sohbete ve bilet sistemine düşer.

## Kapatılan saldırılar

| Saldırı | Nasıl kapatıldı |
|---|---|
| **Algoritma karıştırma** — başlığa `HS256` yazıp saklanan açık anahtarı HMAC sırrı gibi kullanmak | Başlıktaki `alg`, anahtarın **kayıtlı** algoritmasıyla birebir karşılaştırılır |
| `alg: none` | Aynı karşılaştırma eler |
| **Tekrar oynatma** | `jti` veritabanında benzersiz indeksle tutulur |
| **Ömür uzatma** | İmza geçerli olsa da 300 saniyeyi (5 dakika) aşan beyan reddedilir |
| **Alıcı karıştırma** | `aud` tam eşleşir; başka servis için üretilmiş beyan kabul edilmez |
| **Kimlik tarama** | Bilinmeyen istemci, devre dışı kimlik, bozuk imza ve süresi dolmuş beyan **aynı** cevabı alır |
| **Yetki genişletme** | ERP jetonunun alıcısı ve şeması panel jetonundan ayrıdır |
| **Kiracı atlama** | Kiracı ve şirket **kimlik kaydından** okunur, beyandan değil |

Tekrar kaydı bilerek **önbellekte değil veritabanındadır**: Redis kapalıyken koruma
sessizce kalkardı ve bir güvenlik kontrolünün en kötü başarısızlık biçimi budur —
hata vermeden devre dışı kalmak.

## Sonuçları

**İyi:** GOVAI'de saklanan hiçbir değer tek başına bir şirket adına konuşmaya yetmez.
İptal dakikalar içinde etkilidir. Anahtar değişimi kesintisizdir. Kim (hangi ERP
kullanıcısı) ne zaman baktı, kayıtta durur.

**Bedeli:** ERP tarafının JWT imzalayabilmesi gerekir. Düz anahtarda `curl` yeterdi;
burada birkaç satır kriptografi kodu yazılır. İlk uyarlama IKPROF'ta yapıldı
(`GovAiAssertionSigner`) ve başka ERP'ler için örnek olacak.

**Bilinen tuzak:** OpenSSL ES256 imzasını DER kodlu döndürür, JWT ise ham `r||s`
bekler. Dönüştürülmezse beyan üretici tarafta sorunsuz görünür ve doğrulayıcıda
**sessizce** reddedilir. Dönüşümün doğruluğu `ErpAssertionVerifierTests.EI14` ile dil
bağımsız olarak sınanır.

## Değerlendirilen alternatifler

**Karşılıklı TLS (mTLS).** Daha güçlü ama ters vekil sunucu yapılandırması gerektirir
ve müşteri ERP'lerinin çoğu paylaşımlı barındırmadadır; sertifika yönetimi entegrasyonu
kurulamaz hale getirirdi.

**OAuth2 client credentials (paylaşılan sır).** Yukarıdaki tabloda reddedilme
gerekçeleri var. Kolay olan bu; `docs/ikprof-integration.md` önce bunu seçmişti.

**IP kısıtı.** Tek başına kimlik değildir ve bulut ERP'lerde adres değişkendir; ek
katman olarak sonradan eklenebilir.
