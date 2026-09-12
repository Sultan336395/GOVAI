# ADR-0008: Kanıtın tazeliği ölçülür, eksik ise fırsat karşılığıyla gösterilir

- **Durum:** Kabul edildi
- **Tarih:** 2026-09-12
- **İlgili:** ADR-0002 (AI karar verici değil), ADR-0003 (eksik veri elemez),
  ADR-0006 (ERP çekme yolu)

## Bağlam

GOVAI bugüne kadar iki soruyu cevaplıyordu: "bu çağrıya uygun muyum?" ve "neden?".
Sahada iki boşluk göründü.

**Birincisi zaman.** Sistem "ISO 9001 belgeniz var" diyordu. Belgenin üç ay sonra hâlâ
geçerli olup olmadığını, dayandığı mevzuatın değişip değişmediğini, ERP'den çekilen
personel sayısının ne kadar eski olduğunu söylemiyordu. Uyum araçlarının tamamı
"belge var mı?" sorusunda duruyor; oysa denetimde sorulan soru "belge **hâlâ** geçerli
mi?" oluyor. Aradaki fark denetim gününe kadar görünmüyor.

**İkincisi yön.** Sistem "şu koşulu sağlamıyorsun" diyordu ve orada duruyordu. Firmanın
sorduğu soru ise "hangisini kapatırsam ne kazanırım?". Eksik listesi bir sorun listesi;
firmanın ihtiyacı olan ise bir yatırım gerekçesi.

İkinci boşluk GOVAI'ye özgü bir fırsat da barındırıyor: yalnızca uyuma bakan bir ürün
bu hesabı **yapamaz**, çünkü elinde fırsat ve ihale tarafı yoktur. Bağ, iki tarafın
aynı motorda olmasından doğuyor.

## Karar

İki saf motor eklendi. İkisi de `GovAI.Domain` içindedir, deterministiktir ve model
çağırmaz (ADR-0002).

### Evidence Half-Life — kanıtın tazeliği

Her kanıt için 0..1 arası güvenilirlik, yarı ömür ve zayıflama günü hesaplanır.

| Karar | Gerekçe |
|---|---|
| Süresi yazılı belgede ölçüt **kalan süredir**, yaş değil | Yaşa bakılsaydı dün alınmış ama yarın bitecek belge "tertemiz" görünür, firma yenileme fırsatını kaçırırdı |
| Mevzuat değişikliği eskime değil **olaydır** | Dayandığı metnin yeni sürümü çıktıysa kanıt yaşından bağımsız geçersizdir; dün bağlanmış olması onu kurtarmaz |
| Eskime **üsteldir** | Doğrusal model yarıömrün iki katında sıfıra düşüp "hiç bilgi yok" derdi; iki yıllık bir beyan bile hiç beyan olmamasından iyidir |
| Güvence düzeyi hızı belirler | Mali müşavir onaylı veri firma beyanından yavaş, ERP anlık görüntüsü ikisinden de hızlı eskir — kaynağı sürekli değişen bir sistemdir |
| Tarihi bilinmeyen kanıt **hesaplanamaz**, güvenilmez değil | ADR-0003'ün kanıt tarafındaki karşılığı; çürük saymak firmayı elinde olmayan bir eksikten cezalandırmak olurdu |
| Ölçülemeyen kanıt portföy ortalamasına **karışmaz** | Sıfır sayılsaydı tarihi girilmemiş her belge skoru haksız yere düşürürdü |

### Compliance-to-Opportunity — eksiğin fırsat karşılığı

Her eksik için, kapatılırsa hangi çağrıların açılacağı ve ne kadar tutar anlamına
geldiği hesaplanır.

Motorun değeri hesapta değil **dürüstlüğünde**. Bir kez yanlış söylenen "6 milyon
açıldı" cümlesi ürünün güvenini bitirir. Üç kural bunu korur:

| Kural | Gerekçe |
|---|---|
| "Açılır" yalnızca o çağrıda **başka engel kalmadığında** yazılır | Üç engelden birini kapatıp hibeyi açılmış saymak yalan olurdu; kalan engel sayısı ayrıca bildirilir |
| Tutarı bilinmeyen çağrı toplama **katılmaz** | Sıfır sayıp toplamak sayıyı doğru ama eksik gösterirdi; hiç göstermemek bilgiyi kaybederdi. Doğrusu: topla ve eksik olduğunu söyle |
| Beyan eksiğinin getirisi **koşulludur** | Alan doldurulduğunda cevap "sağlamıyor" da çıkabilir; "doldur, hibe açılacak" demek kullanıcıyı yanıltırdı |

Eksikler üç türe ayrılır çünkü maliyetleri kıyaslanamaz: **beyan** girmek dakikalar,
**belge** temin etmek haftalar, **yetkinlik** kazanmak aylar sürer. Süresi dolmuş belge
de eksik sayılır — Evidence Half-Life'ın bulduğu durum tam olarak budur; saymasaydık
iki motor çelişirdi.

Değerlendirme kayıtlı skorlardan okunmaz, **yeniden hesaplanır**: kayıt boyut düzeyinde
saklanıyor, "hangi koşul eksik" ise kural düzeyinde bir sorudur. Motor deterministik
olduğu için bu yeniden hesap tutarsızlık üretmez.

### Süreç olay günlüğü — nedensel ikizin ham maddesi

ERP entegrasyonuna olay günlüğü çekme yolu eklendi (`ErpProcessEvent`). Bu commit
veriyi **toplar, çıkarım yapmaz**.

| Karar | Gerekçe |
|---|---|
| Kaynak alanında **kişi adı tutulmaz**, ERP'nin opak kodu tutulur | Süreç madenciliği için "aynı kaynak mı" yeter; ad tutmak uyum analizini çalışan izlemeye çevirir ve ayrı hukuki dayanak gerektirirdi |
| Günlük yalnızca **büyür** | Olay gerçekleşmiştir, sonradan değişmez; üzerine yazılabilseydi geçmiş ölçümler sessizce değişirdi |
| Bölüm eşlemede tanımlı değilse günlük **istenmez** | Çoğu ERP kurulumunda süreç günlüğü dışarı açılmaz; varsayılan olarak istememek doğru davranıştır |
| Bölüm yokluğu ile boş liste **ayrı** şeydir | CLAUDE.md §2.2.8 ile aynı gerekçe: yokluğu "olay olmadı" saymak arızayı veri gibi gösterirdi |
| Tur başına **5.000 olay** sınırı | Sınırsız çekme gece turunu saatlerce sürdürürdü; sınıra takılan tur eksik değil kısmidir |

## Sonuçlar

**Kazanılan.** Ürün artık üç soruyu cevaplıyor: uygun muyum, neden, ve **ne yaparsam
ne kazanırım**. Kanıtın tazeliği ölçülebilir hâle geldi; "belge var" ile "belge hâlâ
güvenilir" ekranda ayrı görünüyor.

**Kabul edilen maliyet.** Yarı ömür sayıları (365/545/730/180/30 gün) ve yenileme
penceresi (90 gün) bugün **gerekçeli ama kalibre edilmemiş** varsayımlardır. Gerçek
denetim vakalarıyla ölçülmeleri gerekir; kalibrasyon altyapısı (ADR-0005) bunun için
kullanılabilir ama örneklem henüz yok.

**Bilerek yapılmayan.** Eksiği kapatmanın **maliyeti** modellenmedi. "Bu belgeyi almak
40 bin liraya mal olur" demek için sektör ve kurum bazlı fiyat verisi gerekir; elimizde
yok ve uydurulmuş bir maliyet, dürüstlük kuralları titizce korunmuş bir motoru tek
başına değersizleştirirdi. Getiri gösteriliyor, maliyet gösterilmiyor ve bu ekranda
açıkça belli.

**Açık uç.** Nedensel çıkarım başlatılmadı. Engel algoritma değil gözlem sayısıdır;
birkaç haftalık günlükle üretilen "etki" tahmini istatistik değil gürültüdür (ADR-0005
§MinimumSampleSize ile aynı gerekçe).
