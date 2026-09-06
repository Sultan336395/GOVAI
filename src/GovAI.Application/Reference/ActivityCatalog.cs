using GovAI.Application.Common;

namespace GovAI.Application.Reference;

/// <summary>Seçilebilir bir NACE kodu: kanonik kod, resmî tanım ve bağlı olduğu sektör.</summary>
public sealed record NaceOption(string Code, string Title, string Sector);

/// <summary>Seçilebilir bir faaliyet sektörü ve kapsadığı NACE bölümleri (iki haneli).</summary>
public sealed record SectorOption(string Name, IReadOnlyList<string> Divisions);

/// <summary>
/// Firma faaliyet alanı referans kataloğu — sektör listesi ve NACE Rev. 2 kodları.
///
/// <para>
/// Neden var: sektör ve NACE kodu serbest metin olarak giriliyordu. Sonucu sahada
/// görüldü — bir firmanın sektörü "inşaat" yazarken NACE kodu <c>2562</c> (metal
/// işleme) kalmıştı. Motor NACE koduna bakar; ikisi çelişince firma kendi sektöründeki
/// çağrılarda "sektör uyumsuz" görünür. Serbest metin ayrıca "İnşaat", "inşaat",
/// "İnşaat Taahhüt" gibi aynı şeyin onlarca yazımını üretir ve hiçbiri eşleşmez.
/// </para>
///
/// <para>
/// Bu yüzden iki alan da <b>seçilir, yazılmaz</b>. Katalog tek kaynaktır: arayüz
/// öneriyi buradan alır, sunucu kaydı buna göre doğrular. İki taraf ayrı listelerden
/// beslenseydi arayüzde geçerli görünen bir seçim sunucuda reddedilirdi.
/// </para>
///
/// <para>
/// Kapsam: NACE Rev. 2'nin <b>88 bölümünün tamamı</b> (iki haneli) ve yaygın kullanılan
/// dört haneli sınıflar. Bir firma tam sınıfını bulamazsa bölümünü seçebilir; eşleştirme
/// zaten önek mantığıyla çalışır (<c>NaceCode.MatchStrength</c>). Liste genişletilebilir —
/// eksik bir sınıf eklendiğinde arayüz ve doğrulama birlikte güncellenmiş olur.
/// </para>
/// </summary>
public static class ActivityCatalog
{
    /// <summary>Öneri listesinin açılması için gereken en az karakter sayısı.</summary>
    public const int MinimumQueryLength = 3;

    /// <summary>Bir aramada dönen en fazla öneri sayısı.</summary>
    public const int MaxResults = 25;

    // ─────────────────────────── Sektörler ───────────────────────────
    //
    // Her sektör bir veya birkaç NACE bölümünü kapsar. Eşleme iki işe yarar:
    // sektör seçildiğinde NACE önerileri daraltılır, ve "sektörüm inşaat ama kodum
    // metal işleme" çelişkisi kayıt anında görülebilir hâle gelir.

    private static readonly SectorOption[] SectorTable =
    [
        new("Tarım ve hayvancılık", ["01", "03"]),
        new("Ormancılık ve orman ürünleri", ["02"]),
        new("Madencilik ve taş ocakçılığı", ["05", "06", "07", "08", "09"]),
        new("Gıda ve içecek üretimi", ["10", "11", "12"]),
        new("Tekstil, hazır giyim ve deri", ["13", "14", "15"]),
        new("Ağaç ürünleri ve mobilya", ["16", "31"]),
        new("Kâğıt ve ambalaj", ["17"]),
        new("Matbaa ve basım", ["18"]),
        new("Kimya ve plastik", ["19", "20", "22"]),
        new("İlaç ve tıbbi ürünler", ["21", "32"]),
        new("Yapı malzemeleri ve cam", ["23"]),
        new("Metal sanayi ve fabrikasyon", ["24", "25"]),
        new("Elektronik ve optik ürünler", ["26"]),
        new("Elektrikli teçhizat", ["27"]),
        new("Makine ve ekipman imalatı", ["28", "33"]),
        new("Otomotiv ve ulaşım araçları", ["29", "30"]),
        new("Enerji ve doğal gaz", ["35"]),
        new("Su, atık ve çevre hizmetleri", ["36", "37", "38", "39"]),
        new("İnşaat ve taahhüt", ["41", "42", "43"]),
        new("Toptan ticaret", ["46"]),
        new("Perakende ticaret ve motorlu taşıt satışı", ["45", "47"]),
        new("Taşımacılık ve lojistik", ["49", "50", "51", "52", "53"]),
        new("Turizm ve konaklama", ["55", "79"]),
        new("Yiyecek ve içecek hizmetleri", ["56"]),
        new("Yayıncılık, medya ve içerik üretimi", ["58", "59", "60"]),
        new("Telekomünikasyon", ["61"]),
        new("Bilişim ve yazılım", ["62", "63"]),
        new("Finans ve sigorta", ["64", "65", "66"]),
        new("Gayrimenkul", ["68"]),
        new("Hukuk, muhasebe ve işletme danışmanlığı", ["69", "70"]),
        new("Mimarlık, mühendislik ve teknik test", ["71"]),
        new("Araştırma ve geliştirme", ["72"]),
        new("Reklam ve pazarlama", ["73", "74"]),
        new("Veterinerlik", ["75"]),
        new("Kiralama ve leasing", ["77"]),
        new("İnsan kaynakları ve istihdam", ["78"]),
        new("Güvenlik hizmetleri", ["80"]),
        new("Temizlik, peyzaj ve tesis yönetimi", ["81", "82"]),
        new("Kamu yönetimi", ["84"]),
        new("Eğitim", ["85"]),
        new("Sağlık ve sosyal hizmetler", ["86", "87", "88"]),
        new("Kültür, sanat, spor ve eğlence", ["90", "91", "92", "93"]),
        new("Diğer hizmetler ve onarım", ["94", "95", "96", "97", "98", "99"]),
    ];

    // ─────────────────────────── NACE Rev. 2 ───────────────────────────
    //
    // "kod|tanım" biçiminde. İki haneli kayıtlar bölüm, dört haneli kayıtlar sınıftır.
    // Bölümlerin tamamı listededir; sınıflar yaygın kullanıma göre seçilmiştir.

    private static readonly string[] NaceTable =
    [
        // A — Tarım, ormancılık ve balıkçılık
        "01|Bitkisel ve hayvansal üretim ile avcılık ve ilgili hizmet faaliyetleri",
        "01.11|Tahıl, baklagil ve yağlı tohum yetiştiriciliği",
        "01.13|Sebze, kavun-karpuz, kök ve yumru sebzelerin yetiştiriciliği",
        "01.21|Üzüm yetiştiriciliği",
        "01.24|Yumuşak çekirdekli ve sert çekirdekli meyve yetiştiriciliği",
        "01.41|Sütü sağılan büyükbaş hayvan yetiştiriciliği",
        "01.43|At ve diğer tek tırnaklıların yetiştiriciliği",
        "01.45|Koyun ve keçi yetiştiriciliği",
        "01.47|Kümes hayvanlarının yetiştiriciliği",
        "01.61|Bitkisel üretimi destekleyici faaliyetler",
        "02|Ormancılık ile endüstriyel ve yakacak odun üretimi",
        "02.10|Orman yetiştirme ve diğer ormancılık faaliyetleri",
        "02.20|Endüstriyel ve yakacak odun üretimi",
        "02.40|Ormancılık için destekleyici hizmet faaliyetleri",
        "03|Balıkçılık ve su ürünleri yetiştiriciliği",
        "03.11|Deniz balıkçılığı",
        "03.21|Deniz ürünleri yetiştiriciliği",

        // B — Madencilik ve taş ocakçılığı
        "05|Kömür ve linyit çıkartılması",
        "06|Ham petrol ve doğal gaz çıkarımı",
        "07|Metal cevherleri madenciliği",
        "08|Diğer madencilik ve taş ocakçılığı",
        "08.11|Süs ve yapı taşları ile kireç taşı, alçı taşı, tebeşir ve kayrak taşı ocakçılığı",
        "08.12|Çakıl ve kum ocakçılığı ile kil ve kaolin çıkarımı",
        "09|Madenciliği destekleyici hizmet faaliyetleri",

        // C — İmalat
        "10|Gıda ürünlerinin imalatı",
        "10.11|Etin işlenmesi ve saklanması",
        "10.39|Meyve ve sebzelerin başka yerde sınıflandırılmamış işlenmesi ve saklanması",
        "10.41|Sıvı ve katı yağ imalatı",
        "10.51|Süthane işletmeciliği ve peynir imalatı",
        "10.61|Öğütülmüş tahıl ürünleri imalatı",
        "10.71|Ekmek, taze pastane ürünleri ve taze kek imalatı",
        "10.83|Çay ve kahve işlenmesi",
        "10.91|Çiftlik hayvanları için hazır yem imalatı",
        "11|İçeceklerin imalatı",
        "11.07|Alkolsüz içecek imalatı ile maden suyu ve diğer şişelenmiş suların üretimi",
        "12|Tütün ürünleri imalatı",
        "13|Tekstil ürünlerinin imalatı",
        "13.10|Tekstil elyafının hazırlanması ve bükülmesi",
        "13.20|Dokuma",
        "13.92|Giyim eşyası dışındaki hazır tekstil ürünleri imalatı",
        "14|Giyim eşyalarının imalatı",
        "14.13|Diğer dış giyim eşyası imalatı",
        "14.19|Diğer giyim eşyaları ve aksesuarlarının imalatı",
        "15|Deri ve ilgili ürünlerin imalatı",
        "15.20|Ayakkabı imalatı",
        "16|Ağaç, ağaç ürünleri ve mantar ürünleri imalatı (mobilya hariç)",
        "16.10|Ağaçların biçilmesi ve planyalanması",
        "16.23|Diğer bina doğramacılığı ve marangozluk ürünlerinin imalatı",
        "17|Kâğıt ve kâğıt ürünlerinin imalatı",
        "17.21|Oluklu kâğıt ve mukavva ile kâğıt ve mukavvadan ambalaj imalatı",
        "18|Kayıtlı medyanın basılması ve çoğaltılması",
        "18.11|Gazetelerin basımı",
        "18.12|Diğer matbaacılık",
        "19|Kok kömürü ve rafine edilmiş petrol ürünleri imalatı",
        "20|Kimyasalların ve kimyasal ürünlerin imalatı",
        "20.30|Boya, vernik ve benzeri kaplayıcı maddeler ile matbaa mürekkebi ve macun imalatı",
        "20.41|Sabun, deterjan, temizlik ve parlatıcı maddeler imalatı",
        "20.59|Başka yerde sınıflandırılmamış diğer kimyasal ürünlerin imalatı",
        "21|Temel eczacılık ürünlerinin ve eczacılığa ilişkin malzemelerin imalatı",
        "21.20|Eczacılığa ilişkin ilaçların imalatı",
        "22|Kauçuk ve plastik ürünlerin imalatı",
        "22.19|Diğer kauçuk ürünleri imalatı",
        "22.21|Plastik tabaka, levha, tüp ve profil imalatı",
        "22.22|Plastikten ambalaj malzemeleri imalatı",
        "22.23|Plastik inşaat malzemesi imalatı",
        "23|Diğer metalik olmayan mineral ürünlerin imalatı",
        "23.11|Düz cam imalatı",
        "23.32|Fırınlanmış kilden tuğla, karo ve inşaat malzemeleri imalatı",
        "23.51|Çimento imalatı",
        "23.61|İnşaat amaçlı beton ürünlerin imalatı",
        "23.63|Hazır beton imalatı",
        "23.70|Taş ve mermerin kesilmesi, şekil verilmesi ve bitirilmesi",
        "24|Ana metal sanayii",
        "24.10|Ana demir ve çelik ürünleri ile ferro alaşımların imalatı",
        "24.33|Soğuk şekillendirme veya katlama",
        "24.51|Demir döküm",
        "25|Fabrikasyon metal ürünleri imalatı (makine ve teçhizat hariç)",
        "25.11|Metal yapı ve yapı parçaları imalatı",
        "25.12|Metalden kapı ve pencere imalatı",
        "25.29|Diğer metal tank, rezervuar ve konteyner imalatı",
        "25.61|Metallerin işlenmesi ve kaplanması",
        "25.62|Metallerin makinede işlenmesi ve şekil verilmesi",
        "25.73|El aletleri imalatı",
        "25.99|Başka yerde sınıflandırılmamış diğer fabrikasyon metal ürünlerin imalatı",
        "26|Bilgisayarların, elektronik ve optik ürünlerin imalatı",
        "26.11|Elektronik bileşenlerin imalatı",
        "26.20|Bilgisayar ve bilgisayar çevre birimleri imalatı",
        "26.30|İletişim ekipmanlarının imalatı",
        "26.51|Ölçme, test ve seyrüsefer amaçlı alet ve cihazların imalatı",
        "27|Elektrikli teçhizat imalatı",
        "27.12|Elektrik dağıtım ve kontrol cihazları imalatı",
        "27.32|Diğer elektronik ve elektrik telleri ile kablolarının imalatı",
        "27.40|Elektrikli aydınlatma ekipmanlarının imalatı",
        "27.51|Elektrikli ev aletlerinin imalatı",
        "28|Başka yerde sınıflandırılmamış makine ve ekipman imalatı",
        "28.11|Motor ve türbin imalatı (hava taşıtı, motorlu taşıt ve motosiklet motorları hariç)",
        "28.13|Diğer pompa ve kompresörlerin imalatı",
        "28.22|Kaldırma ve taşıma ekipmanları imalatı",
        "28.25|Soğutma ve havalandırma donanımlarının imalatı (evde kullanıma yönelik olanlar hariç)",
        "28.29|Başka yerde sınıflandırılmamış diğer genel amaçlı makinelerin imalatı",
        "28.41|Metal işleme makineleri imalatı",
        "28.49|Diğer takım tezgâhlarının imalatı",
        "28.93|Gıda, içecek ve tütün işleme makinelerinin imalatı",
        "28.99|Başka yerde sınıflandırılmamış diğer özel amaçlı makinelerin imalatı",
        "29|Motorlu kara taşıtı, treyler (römork) ve yarı treyler (yarı römork) imalatı",
        "29.20|Motorlu kara taşıtları karoseri imalatı; treyler ve yarı treyler imalatı",
        "29.32|Motorlu kara taşıtları için diğer parça ve aksesuarların imalatı",
        "30|Diğer ulaşım araçlarının imalatı",
        "31|Mobilya imalatı",
        "31.01|Büro ve mağaza mobilyaları imalatı",
        "31.09|Diğer mobilyaların imalatı",
        "32|Diğer imalatlar",
        "32.50|Tıbbi ve dişçilik ile ilgili araç ve gereçlerin imalatı",
        "33|Makine ve ekipmanların kurulumu ve onarımı",
        "33.12|Makinelerin bakım ve onarımı",
        "33.14|Elektrikli ekipmanların bakım ve onarımı",
        "33.17|Diğer ulaşım ekipmanlarının bakım ve onarımı",
        "33.20|Sanayi makine ve ekipmanlarının kurulumu",

        // D–E — Enerji, su ve atık
        "35|Elektrik, gaz, buhar ve iklimlendirme üretimi ve dağıtımı",
        "35.11|Elektrik üretimi",
        "35.13|Elektriğin dağıtımı",
        "35.22|Gaz yakıtların ana şebeke üzerinden dağıtımı",
        "36|Suyun toplanması, arıtılması ve dağıtılması",
        "37|Kanalizasyon",
        "38|Atığın toplanması, ıslahı ve bertaraf edilmesi; maddelerin geri kazanımı",
        "38.11|Tehlikeli olmayan atıkların toplanması",
        "38.32|Tasnif edilmiş atıkların geri kazanımı",
        "39|İyileştirme faaliyetleri ve diğer atık yönetimi hizmetleri",

        // F — İnşaat
        "41|Bina inşaatı",
        "41.10|Bina projelerinin geliştirilmesi",
        "41.20|İkamet amaçlı olan veya olmayan binaların inşaatı",
        "42|Bina dışı yapıların inşaatı",
        "42.11|Kara yolları ve otoyolların inşaatı",
        "42.12|Demir yolları ve metroların inşaatı",
        "42.21|Akışkan projelerinin (boru hattı) inşaatı",
        "42.22|Elektrik ve telekomünikasyon için yapıların inşaatı",
        "42.99|Başka yerde sınıflandırılmamış diğer bina dışı yapıların inşaatı",
        "43|Özel inşaat faaliyetleri",
        "43.11|Yıkım işleri",
        "43.12|Şantiye hazırlama işleri",
        "43.21|Elektrik tesisatı işleri",
        "43.22|Sıhhi tesisat, ısıtma ve iklimlendirme tesisatı işleri",
        "43.31|Sıva işleri",
        "43.32|Doğrama tesisatı",
        "43.33|Yer ve duvar kaplama işleri",
        "43.39|Diğer bina tamamlama ve bitirme işleri",
        "43.99|Başka yerde sınıflandırılmamış diğer özel inşaat faaliyetleri",

        // G — Ticaret
        "45|Motorlu kara taşıtlarının ve motosikletlerin toptan ve perakende ticareti ile onarımı",
        "45.20|Motorlu kara taşıtlarının bakım ve onarımı",
        "46|Toptan ticaret (motorlu kara taşıtları ve motosikletler hariç)",
        "46.19|Çeşitli malların toptan ticareti (belirli bir mala tahsis edilmemiş)",
        "46.30|Gıda, içecek ve tütün toptan ticareti",
        "46.46|Eczacılık ürünlerinin toptan ticareti",
        "46.49|Diğer ev eşyalarının toptan ticareti",
        "46.51|Bilgisayar, bilgisayar çevre birimleri ve yazılımların toptan ticareti",
        "46.65|Büro mobilyalarının toptan ticareti",
        "46.69|Başka yerde sınıflandırılmamış diğer makine ve ekipmanların toptan ticareti",
        "46.71|Katı, sıvı ve gaz yakıtlar ile ilgili ürünlerin toptan ticareti",
        "46.73|Ağaç, inşaat malzemesi ve sıhhi teçhizatın toptan ticareti",
        "47|Perakende ticaret (motorlu kara taşıtları ve motosikletler hariç)",
        "47.11|Belirli bir mala tahsis edilmemiş mağazalarda gıda, içecek veya tütün ağırlıklı perakende ticaret",
        "47.30|Belirli bir mala tahsis edilmiş mağazalarda otomotiv yakıtının perakende ticareti",
        "47.91|Posta yoluyla veya internet üzerinden yapılan perakende ticaret",

        // H — Taşımacılık ve depolama
        "49|Kara taşımacılığı ve boru hattı taşımacılığı",
        "49.10|Şehirler arası yolcu taşımacılığı (demir yolu ile)",
        "49.20|Demir yolu ile yük taşımacılığı",
        "49.31|Şehir içi veya banliyö kara taşımacılığı (yolcu)",
        "49.39|Başka yerde sınıflandırılmamış diğer kara yolu yolcu taşımacılığı",
        "49.41|Kara yolu ile yük taşımacılığı",
        "50|Su yolu taşımacılığı",
        "51|Havayolu taşımacılığı",
        "52|Taşımacılık için depolama ve destekleyici faaliyetler",
        "52.10|Depolama ve ambarlama",
        "52.29|Taşımacılık için diğer destekleyici faaliyetler",
        "53|Posta ve kurye faaliyetleri",
        "53.20|Diğer posta ve kurye faaliyetleri",

        // I — Konaklama ve yiyecek hizmeti
        "55|Konaklama",
        "55.10|Oteller ve benzeri konaklama yerleri",
        "56|Yiyecek ve içecek hizmeti faaliyetleri",
        "56.10|Lokantalar ve seyyar yemek hizmeti faaliyetleri",
        "56.29|Diğer yiyecek hizmeti faaliyetleri",

        // J — Bilgi ve iletişim
        "58|Yayımcılık faaliyetleri",
        "58.29|Diğer yazılım programlarının yayımlanması",
        "59|Sinema filmi, video ve televizyon programları yapımcılığı, ses kaydı ve müzik yayımlama faaliyetleri",
        "60|Programcılık ve yayıncılık faaliyetleri",
        "61|Telekomünikasyon",
        "61.10|Kablolu telekomünikasyon faaliyetleri",
        "61.20|Kablosuz telekomünikasyon faaliyetleri",
        "62|Bilgisayar programlama, danışmanlık ve ilgili faaliyetler",
        "62.01|Bilgisayar programlama faaliyetleri",
        "62.02|Bilgisayar danışmanlık faaliyetleri",
        "62.03|Bilgisayar tesisleri yönetim faaliyetleri",
        "62.09|Diğer bilgi teknolojisi ve bilgisayar hizmet faaliyetleri",
        "63|Bilgi hizmet faaliyetleri",
        "63.11|Veri işleme, barındırma ve ilgili faaliyetler",

        // K — Finans ve sigorta
        "64|Finansal hizmet faaliyetleri (sigorta ve emeklilik fonları hariç)",
        "65|Sigorta, reasürans ve emeklilik fonları (zorunlu sosyal güvenlik hariç)",
        "66|Finansal hizmetler ile sigorta faaliyetleri için yardımcı faaliyetler",

        // L — Gayrimenkul
        "68|Gayrimenkul faaliyetleri",
        "68.20|Kendine ait veya kiralanan gayrimenkulün kiraya verilmesi ve işletilmesi",

        // M — Mesleki, bilimsel ve teknik faaliyetler
        "69|Hukuki ve muhasebe faaliyetleri",
        "69.20|Muhasebe, defter tutma ve denetim faaliyetleri; vergi danışmanlığı",
        "70|İdare merkezi faaliyetleri; idari danışmanlık faaliyetleri",
        "70.22|İşletme ve diğer idari danışmanlık faaliyetleri",
        "71|Mimarlık ve mühendislik faaliyetleri; teknik test ve analiz faaliyetleri",
        "71.11|Mimarlık faaliyetleri",
        "71.12|Mühendislik faaliyetleri ve ilgili teknik danışmanlık",
        "71.20|Teknik test ve analiz faaliyetleri",
        "72|Bilimsel araştırma ve geliştirme faaliyetleri",
        "72.19|Doğa bilimleri ve mühendislikle ilgili diğer araştırma ve deneysel geliştirme faaliyetleri",
        "73|Reklamcılık ve piyasa araştırması",
        "73.11|Reklam ajanslarının faaliyetleri",
        "74|Diğer mesleki, bilimsel ve teknik faaliyetler",
        "75|Veterinerlik hizmetleri",

        // N — İdari ve destek hizmet faaliyetleri
        "77|Kiralama ve leasing faaliyetleri",
        "77.11|Otomobil ve hafif motorlu kara taşıtlarının kiralanması ve leasingi",
        "77.32|İnşaat ve inşaat mühendisliği makinelerinin kiralanması ve leasingi",
        "78|İstihdam faaliyetleri",
        "78.10|İş bulma acentelerinin faaliyetleri",
        "79|Seyahat acentesi, tur operatörü ve diğer rezervasyon hizmetleri ile ilgili faaliyetler",
        "79.11|Seyahat acentesi faaliyetleri",
        "80|Güvenlik ve soruşturma faaliyetleri",
        "80.10|Özel güvenlik faaliyetleri",
        "81|Binalar ve çevre düzenlemesi faaliyetleri",
        "81.21|Binaların genel temizliği",
        "81.29|Diğer bina ve endüstriyel temizlik faaliyetleri",
        "81.30|Peyzaj bakım ve düzenleme faaliyetleri",
        "82|Büro yönetimi, büro desteği ve iş desteği ile ilgili diğer faaliyetler",
        "82.11|Kombine büro yönetim hizmeti faaliyetleri",

        // O–Q — Kamu, eğitim, sağlık
        "84|Kamu yönetimi ve savunma; zorunlu sosyal güvenlik",
        "85|Eğitim",
        "85.59|Başka yerde sınıflandırılmamış diğer eğitim",
        "86|İnsan sağlığı hizmetleri",
        "86.10|Hastane hizmetleri",
        "86.21|Genel hekimlik uygulama faaliyetleri",
        "86.23|Diş hekimliği uygulama faaliyetleri",
        "87|Yatılı bakım faaliyetleri",
        "88|Barınacak yer sağlanmaksızın verilen sosyal hizmetler",

        // R–U — Kültür, diğer hizmetler
        "90|Yaratıcı sanatlar, gösteri sanatları ve eğlence faaliyetleri",
        "91|Kütüphaneler, arşivler, müzeler ve diğer kültürel faaliyetler",
        "92|Kumar ve müşterek bahis faaliyetleri",
        "93|Spor faaliyetleri ile eğlence ve dinlence faaliyetleri",
        "94|Üye olunan kuruluşların faaliyetleri",
        "95|Bilgisayarların, kişisel eşyaların ve ev eşyalarının onarımı",
        "95.11|Bilgisayarların ve bilgisayar çevre birimlerinin onarımı",
        "96|Diğer hizmet faaliyetleri",
        "96.01|Tekstil ve kürk ürünlerinin yıkanması ve (kuru) temizlenmesi",
        "97|Ev içi çalışan personelin işverenleri olarak hane halklarının faaliyetleri",
        "98|Hane halkları tarafından kendi kullanımlarına yönelik üretilen ayrım yapılmamış mal ve hizmetler",
        "99|Uluslararası örgütler ve temsilciliklerinin faaliyetleri",
    ];

    /// <summary>
    /// Kullanıcının kullandığı kelime ile resmî tanımdaki kelime her zaman aynı değildir.
    /// "Nakliye" arayan bir kullanıcı hiçbir sonuç görmez, çünkü NACE tanımı "taşımacılık"
    /// der. Bu tablo aramayı kullanıcının diline açar; katalog verisini değiştirmez.
    /// Anahtarlar ve değerler <b>katlanmış</b> yazılır.
    /// </summary>
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.Ordinal)
    {
        ["nakliye"] = ["tasimacilik"],
        ["nakliyat"] = ["tasimacilik"],
        ["kargo"] = ["kurye", "tasimacilik"],
        ["lojistik"] = ["depolama", "tasimacilik"],
        ["otomotiv"] = ["motorlu kara tasit"],
        ["tir"] = ["yuk tasimaciligi"],
        ["market"] = ["perakende"],
        ["magaza"] = ["perakende"],
        ["catering"] = ["yiyecek hizmeti"],
        ["yemekhane"] = ["yiyecek hizmeti"],
        ["muteahhit"] = ["insaat"],
        ["taahhut"] = ["insaat"],
        ["mermer"] = ["tas"],
        ["madencilik"] = ["maden"],
        ["enerji"] = ["elektrik"],
        ["gunes"] = ["elektrik uretimi"],
        ["turizm"] = ["konaklama", "seyahat"],
        ["otel"] = ["konaklama"],
        ["saglik"] = ["hekimlik", "hastane"],
        ["ilac"] = ["eczacilik"],
        ["medikal"] = ["tibbi"],
        ["bilisim"] = ["bilgisayar"],
        ["donanim"] = ["bilgisayar"],
        ["web"] = ["bilgisayar programlama"],
        ["muhasebe"] = ["muhasebe"],
        ["danismanlik"] = ["danismanlik"],
        ["guvenlik"] = ["guvenlik"],
        ["temizlik"] = ["temizli"],
        ["ihracat"] = ["toptan ticaret"],
        ["ambalaj"] = ["ambalaj"],
        ["demiryolu"] = ["demir yolu"],
        ["karayolu"] = ["kara yolu"],
        ["kuyumcu"] = ["perakende"],
        ["egitim"] = ["egitim"],
        ["kres"] = ["egitim"],
        ["ariza"] = ["onarim"],
        ["servis"] = ["onarim"],
        ["bakim"] = ["bakim"],
    };

    private static readonly Dictionary<string, string> DivisionToSector =
        SectorTable
            .SelectMany(s => s.Divisions.Select(d => (Division: d, s.Name)))
            .ToDictionary(x => x.Division, x => x.Name, StringComparer.Ordinal);

    private static readonly NaceOption[] NaceOptions =
    [
        .. NaceTable.Select(row =>
        {
            var parts = row.Split('|', 2);
            var code = parts[0];
            var division = code[..2];

            return new NaceOption(code, parts[1], DivisionToSector.GetValueOrDefault(division, "Diğer hizmetler ve onarım"));
        })
    ];

    private static readonly Dictionary<string, NaceOption> NaceByDigits =
        NaceOptions.ToDictionary(o => Digits(o.Code), o => o, StringComparer.Ordinal);

    private static readonly Dictionary<string, SectorOption> SectorByFolded =
        SectorTable.ToDictionary(s => TurkceMetin.BasligiKatla(s.Name), s => s, StringComparer.Ordinal);

    public static IReadOnlyList<SectorOption> Sectors => SectorTable;

    public static IReadOnlyList<NaceOption> NaceCodes => NaceOptions;

    /// <summary>Koddaki nokta, boşluk ve tireyi atar: "25.62", "2562" ve "25 62" aynı koddur.</summary>
    private static string Digits(string code) => new([.. code.Where(char.IsDigit)]);

    /// <summary>
    /// Sektör önerileri. Sorgu <see cref="MinimumQueryLength"/> karakterden kısaysa tüm liste
    /// döner — sektör listesi kısadır, kullanıcı yazmadan da gezinebilmelidir.
    /// </summary>
    public static IReadOnlyList<SectorOption> SearchSectors(string? query)
    {
        var folded = TurkceMetin.BasligiKatla(query ?? string.Empty);

        if (folded.Length < MinimumQueryLength)
        {
            return SectorTable;
        }

        return
        [
            .. SectorTable
                .Where(s => TurkceMetin.BasligiKatla(s.Name).Contains(folded, StringComparison.Ordinal))
                .Take(MaxResults)
        ];
    }

    /// <summary>
    /// NACE önerileri. Sorgu koda da tanıma da uyabilir: "256" kodu, "yazılım" tanımı arar.
    ///
    /// <para>
    /// Sıralama kasıtlıdır: önce koda göre eşleşenler, sonra tanımı sorguyla başlayanlar,
    /// en sonda tanımın içinde geçenler. Kullanıcı kodunu biliyorsa ilk satırda görmelidir.
    /// <paramref name="sector"/> verilirse o sektörün kodları öne alınır — ama diğerleri
    /// gizlenmez: bir firmanın kodu seçtiği sektörün dışında kalabilir ve bunu görmelidir.
    /// </para>
    /// </summary>
    public static IReadOnlyList<NaceOption> SearchNace(string? query, string? sector = null)
    {
        var raw = (query ?? string.Empty).Trim();
        var folded = TurkceMetin.BasligiKatla(raw);
        var digits = Digits(raw);

        if (folded.Length < MinimumQueryLength && digits.Length < 2)
        {
            return [];
        }

        var sectorName = sector is null ? null : NormalizeSector(sector);

        return
        [
            .. NaceOptions
                .Select(option => (Option: option, Rank: Rank(option, folded, digits, sectorName)))
                .Where(x => x.Rank < int.MaxValue)
                .OrderBy(x => x.Rank)
                .ThenBy(x => x.Option.Code, StringComparer.Ordinal)
                .Take(MaxResults)
                .Select(x => x.Option)
        ];
    }

    private static int Rank(NaceOption option, string folded, string digits, string? sectorName)
    {
        var sectorBonus = sectorName is not null && option.Sector == sectorName ? 0 : 1;
        var codeDigits = Digits(option.Code);

        if (digits.Length >= 2 && codeDigits.StartsWith(digits, StringComparison.Ordinal))
        {
            return (sectorBonus * 10) + 1;
        }

        if (folded.Length >= MinimumQueryLength)
        {
            var title = TurkceMetin.BasligiKatla(option.Title);

            if (title.StartsWith(folded, StringComparison.Ordinal))
            {
                return (sectorBonus * 10) + 2;
            }

            if (Contains(title, folded))
            {
                return (sectorBonus * 10) + 3;
            }

            // Kullanıcının kelimesi resmî tanımdakinden farklı olabilir ("nakliye" /
            // "taşımacılık"). Eş anlamlı eşleşme en arkada sıralanır; doğrudan
            // eşleşenlerin önüne geçmemeli.
            if (Synonyms.TryGetValue(folded, out var esAnlamlilar)
                && esAnlamlilar.Any(term => Contains(title, term)))
            {
                return (sectorBonus * 10) + 4;
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Boşluğa duyarsız içerme. NACE tanımları "demir yolu" ve "kara yolu" yazar;
    /// kullanıcı bitişik yazar. Boşluk farkı yüzünden sonuç bulunamaması kabul edilemez.
    /// </summary>
    private static bool Contains(string title, string term) =>
        title.Contains(term, StringComparison.Ordinal)
        || title.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Contains(term.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

    /// <summary>Sektör adını katalogdaki kanonik yazımına çevirir; katalogda yoksa <c>null</c>.</summary>
    public static string? NormalizeSector(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : SectorByFolded.GetValueOrDefault(TurkceMetin.BasligiKatla(name))?.Name;

    /// <summary>
    /// NACE kodunu katalogdaki okunaklı yazıma çevirir ("2562" → "25.62"); katalogda
    /// yoksa <c>null</c> döner, yani doğrulama işlevi de görür.
    ///
    /// <para>
    /// Noktalı yazım <b>gösterim</b> içindir. Alan modeli kodu noktasız saklar
    /// (<c>CompanyNaceCode.Code</c>, <c>NaceCode.Normalize</c>) ve eşleştirme o biçim
    /// üzerinden çalışır; buradaki nokta saklanan veriyi değiştirmez, yalnızca
    /// kullanıcının listede gördüğü kodu okunaklı yapar.
    /// </para>
    /// </summary>
    public static string? NormalizeNace(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : NaceByDigits.GetValueOrDefault(Digits(code))?.Code;
}
