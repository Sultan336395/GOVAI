# ADR-0006: ERP verisi çekilir, itilmeyi beklemez

- **Durum:** Kabul edildi
- **Tarih:** 2026-09-11

## Bağlam

Faz 1'den beri `/api/company-profile/erp-sync` ucu var ve **itme** yönünde çalışıyor:
firmanın ERP tarafındaki bir geliştirici veriyi GOVAI'ye gönderiyor. Sözleşme doğru
kurulmuştu — doğrulama, profil sürümleme, denetim kaydı ve yeniden skorlama tetiklemesi
hepsi çalışıyordu.

Sahada bu uç **hiç kullanılmadı**. Sebebi teknik değil örgütsel: firmanın ERP'sine kod
yazacak biri yok, olsa da önceliği bu değil. Sonuç: ciro ve personel kırılımı elle
giriliyor, elle girilen veri eskiyor ve skorlar eski profille hesaplanıyor. Ürünün en
temel vaadi — "firmanın gerçek verisiyle eşleştirme" — pratikte bir form doldurma
alıştırmasına dönüşüyor.

## Karar

**Çekme** yolu açıldı: GOVAI, firmanın izin verdiği bir uçtan veriyi kendisi okur.
`ErpConnection` kaydı firma başına bir bağlantı tutar; gece turu (02:45) bütün etkin
bağlantıları dolaşır.

Çekilen veri **doğrudan kaydedilmez**: mevcut `SyncFromErpAsync` hattından geçer. İkinci
bir yazma yolu açmak, doğrulama ya da yeniden skorlama tetiklemesinden birinin ERP
yolunda atlanması demek olurdu.

## Gerekçe

### Ürüne özel istemci yerine JSON uç

Logo, Netsis ve SAP'ın kendi protokollerini konuşan istemciler yazmak ilk bakışta daha
"entegre" görünür. Sahada tutmaz: her kurulumun sürümü, modülleri ve özel alanları
farklıdır; ürüne özel istemci her müşteride yeniden yazılır ve sürüm yükseltmesinde kırılır.

JSON uç, müşterinin BT ekibinin **bir kez** açtığı ve içeriğini kendi kontrol ettiği tek
noktadır. Ürün adı yalnızca varsayılan alan eşlemesini seçer; listede olmayan bir ürün de
`GenericRest` ile bağlanabilir.

### Eşleme bağlantı bazında saklanır

Aynı bilgi her üründe başka adla durur (`personel.kadin` / `workforce.femaleHeadcount`).
Sabit kodlanmış tek şema, ilk farklı kurulumda çöker. Varsayılanlar **başlangıç
değeridir, garanti değildir**; "Şimdi Dene ve Çek" hangi alanların bulunduğunu ve
hangilerinin bulunamadığını söyler, kullanıcı eşlemeyi kendi kurulumuna göre düzeltir.

### Bulunamayan alan eksik sayılır, sıfır yazılmaz

Bu ADR-0003'ün ERP yolundaki karşılığıdır. ERP'de olmayan bir alanı `0` yazmak, firmayı
"hiç kadın çalışanı yok" diye kaydetmek olurdu — ve o firma kadın istihdamı şartı arayan
her çağrıdan elenirdi.

Aynı sebeple bir bölümün tamamı boşsa o bölüm hiç gönderilmez: ERP bordro modülü
kullanmayan bir firmada elle girilmiş doğru personel verisi, ERP yüzünden silinemez.

### Kurum içi adresler beyanla açılır

GOVAI kullanıcıdan gelen adreslerde özel IP bloklarını engeller (SSRF koruması). Ama kurum
içi bir Logo sunucusu tam da özel IP'dedir; engeli topyekûn uygulamak entegrasyonu
imkânsız kılardı.

Çözüm ne "engeli kaldır" ne "entegrasyondan vazgeç" oldu: firma yöneticisi bağlantının
kurum içi olduğunu **açıkça beyan eder** ve beyan kayda geçer. Beyan yoksa özel adrese
gidilmez ve hata mesajı ne yapılması gerektiğini söyler.

**Bulut metadata adresi (169.254.169.254) bu beyanla dahi açılmaz.** Orası bir ERP değil,
sunucunun kendi kimlik bilgilerinin durduğu yerdir; oraya gitmek GOVAI'nin kendi bulut
kimliğini sızdırmak olurdu.

### Kimlik şifrelenir, özetlenmez

ERP kimliği parola gibi özetlenemez: ERP'ye **gönderilmesi** gerekir. AES-GCM ile
şifrelenir — yalnızca gizlemekle kalmaz, bütünlüğü de doğrular; kurcalanmış bir kayıt
sessizce bozuk kimlik üretip ERP'ye gönderilmez.

Anahtar, JWT imzalama anahtarından HKDF ile ve ayrı bir bağlam etiketiyle türetilir. Ayrı
bir anahtar daha iyi olurdu, ama yapılandırmaya ikinci bir zorunlu sır eklemek kurulumu
karmaşıklaştırır ve sahada "geçici olarak" zayıf bir değerle doldurulmasına yol açar.

### Bağlantı yalnızca okur

ERP'ye yazan bir yol bilerek yoktur. Müşterinin muhasebe ve bordro kayıtlarına yazma
yetkisi istemek, entegrasyonun riskini faydasının çok ötesine taşır ve satışın önündeki
en büyük engel olurdu.

## Sonuçlar

**Üst üste beş başarısızlıkta bağlantı kendiliğinden durur.** Yanlış kimlikle her gece
denemeye devam etmek, müşterinin ERP'sinde hesabı kilitletir. Bağlantı kendiliğinden
açılmaz; kullanıcı sorunu görüp ayarları kaydettiğinde açılır.

**"Değişiklik yok" hata sayılmaz.** ERP'ye ulaşıldı ve veri okundu, yalnızca profil zaten
güncel. Bunu hata saymak sağlıklı bir bağlantıyı beş gecede kapatırdı.

**Kimlik hiçbir yanıtta dönmez.** Ekran yalnızca "kayıtlı" bilgisini görür; boş bırakılan
alan mevcut kimliği korur. Gösterilen bir sır, ekran görüntüsüne ve tarayıcı geçmişine düşer.

**ERP hata gövdesi kullanıcıya yansıtılmaz.** Gövde kimlik bilgisi ya da personel verisi
içerebilir; yalnızca durum koduna göre ayıklanmış mesaj gösterilir.

**Gece sırası: ERP çekme (02:45) → skorlama (03:30) → ikinci görüş (04:15).** Profil önce
tazelenir, skorlar sonra o güncel veriyle hesaplanır. Ters sırada firma, dün düzelttiği
eksiğin sonucunu bir gün sonra görürdü.

### Ödenen bedel

**Gerçek bir Logo/Netsis/SAP kurulumuna karşı doğrulanmadı.** Üretici varsayılan
eşlemeleri, ürünlerin yaygın alan adlandırmasına dayanır; gerçek kurulumda doğrulanması
ve büyük olasılıkla düzeltilmesi gerekir. "Şimdi Dene ve Çek" tam da bunun için vardır.

**Müşterinin bir JSON uç açması gerekir.** Bu bir iştir ve sıfır değildir; ama ERP'ye kod
yazmaktan çok daha küçüktür ve bir kez yapılır.

## Koruyan testler

`ErpFieldMapTests` (4) ve `ErpConnectionTests` — alan modeli (5): varsayılan eşlemeler,
bilinmeyen üründe ad uydurulmaması, hata sayacı ve kendiliğinden durma.

`ErpConnectionTests` — API (12): kimliğin yanıtta dönmemesi, veritabanında açık
saklanmaması, boş gönderimin mevcudu silmemesi, firma başına tek bağlantı, kurum içi
adresin beyansız reddi, **metadata adresine beyanla dahi gidilmemesi**, kiracı yalıtımı ve
platform rollerinin girememesi.
