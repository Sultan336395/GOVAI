
namespace GovAI.Domain.Evidence;

/// <summary>
/// Bir kanıtın ne olduğu. Eskime hızı türe göre değişir.
///
/// <para>
/// Tür ayrımı keyfî değil: mali müşavir onaylı bir bilanço ile firmanın kendi beyanı
/// aynı hızda eskimez, ERP'den çekilen anlık görüntü ise ikisinden de hızlı eskir
/// çünkü kaynağı sürekli değişen bir sistemdir.
/// </para>
/// </summary>
public enum EvidenceKind
{
    /// <summary>Sertifika veya belge (ISO, CE, yeterlilik). Çoğunda açık geçerlilik tarihi vardır.</summary>
    Certificate = 1,

    /// <summary>Mali tablo verisi. Doğrulama durumu eskime hızını belirler.</summary>
    FinancialStatement = 2,

    /// <summary>Personel ve profil beyanı. Firma kendisi girer, kendiliğinden tazelenmez.</summary>
    WorkforceDeclaration = 3,

    /// <summary>ERP'den çekilen anlık görüntü. Hızlı eskir ama otomatik tazelenir.</summary>
    ErpSnapshot = 4,

    /// <summary>Bir kuralın dayandığı resmî belge hükmü. Zamanla değil, metin değişince geçersizleşir.</summary>
    RegulationClause = 5
}

/// <summary>Kanıtın bugünkü durumu.</summary>
public enum EvidenceStatus
{
    /// <summary>
    /// Ne zaman elde edildiği bilinmiyor; eskiyip eskimediği <b>hesaplanamaz</b>.
    ///
    /// <para>
    /// "Güvenilmez" DEĞİLDİR. Bu, §2.2'deki ayrımın kanıt tarafındaki karşılığıdır:
    /// tarihi bilinmeyen bir belgeyi çürük saymak, firmayı elinde olmayan bir eksikten
    /// cezalandırmak olurdu. Doğru cevap "tarihini girin"dir.
    /// </para>
    /// </summary>
    Bilinmiyor = 0,

    /// <summary>Güvenilir; karar bu kanıta dayandırılabilir.</summary>
    Guvenilir = 1,

    /// <summary>Hâlâ geçerli ama tazelenmesi gerekiyor.</summary>
    Zayifliyor = 2,

    /// <summary>Biçimsel olarak duruyor ama artık karar dayanağı sayılmamalı.</summary>
    Guvenilmez = 3,

    /// <summary>Süresi dolmuş ya da dayandığı metin değişmiş. Kesin olarak geçersiz.</summary>
    Gecersiz = 4
}

/// <summary>Mali verinin doğrulanma biçimi; eskime hızını doğrudan etkiler.</summary>
public enum EvidenceAssurance
{
    /// <summary>Firmanın kendi beyanı.</summary>
    SelfDeclared = 0,

    /// <summary>Belge görülerek kontrol edilmiş.</summary>
    DocumentChecked = 1,

    /// <summary>Mali müşavir / bağımsız denetim onaylı.</summary>
    Verified = 2
}

/// <summary>Motora verilen kanıt kaydı. Alan modelinden bağımsızdır; saf girdi.</summary>
public sealed record EvidenceSnapshot
{
    public required EvidenceKind Kind { get; init; }

    /// <summary>Kullanıcıya gösterilecek ad ("ISO 9001", "2025 mali yılı").</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Kanıtın elde edildiği / son tazelendiği gün. <c>null</c> ise eskime hesaplanamaz
    /// ve sonuç <see cref="EvidenceStatus.Bilinmiyor"/> olur.
    /// </summary>
    public DateOnly? ObservedOn { get; init; }

    /// <summary>Açık geçerlilik bitişi varsa. Sertifikalarda olur, beyanlarda olmaz.</summary>
    public DateOnly? ValidUntil { get; init; }

    /// <summary>Mali kanıtın doğrulanma biçimi; diğer türlerde yok sayılır.</summary>
    public EvidenceAssurance Assurance { get; init; } = EvidenceAssurance.SelfDeclared;

    /// <summary>
    /// Kanıtın dayandığı resmî metnin daha yeni bir sürümü yayımlandı mı?
    ///
    /// <para>
    /// Bu bir eskime değil <b>olaydır</b>: mevzuat değiştiği anda eski hükme dayanan
    /// kanıt yaşından bağımsız olarak geçersizdir. Dün bağlanmış olması onu kurtarmaz.
    /// </para>
    /// </summary>
    public bool SourceSuperseded { get; init; }
}

/// <summary>Bir kanıtın güvenilirlik değerlendirmesi.</summary>
public sealed record EvidenceReliability
{
    public required string Label { get; init; }

    public required EvidenceKind Kind { get; init; }

    public required EvidenceStatus Status { get; init; }

    /// <summary>0..1 arası güvenilirlik. Durum <see cref="EvidenceStatus.Bilinmiyor"/> ise <c>null</c>.</summary>
    public decimal? Score { get; init; }

    /// <summary>Kanıtın yaşı (gün). Gözlem tarihi yoksa <c>null</c>.</summary>
    public int? AgeDays { get; init; }

    /// <summary>Güvenilirliğin yarıya inmesi için geçmesi gereken gün sayısı.</summary>
    public required int HalfLifeDays { get; init; }

    /// <summary>
    /// Güvenilirliğin <see cref="EvidenceHalfLife.WeakeningThreshold"/> eşiğine düşeceği gün.
    /// Süre dolmuşsa ya da hesaplanamıyorsa <c>null</c>.
    /// </summary>
    public DateOnly? WeakensOn { get; init; }

    /// <summary>Kararın <b>neden</b> böyle olduğu. Her sonuç somut bir olguya dayanır.</summary>
    public required string Reason { get; init; }

    /// <summary>Karar bu kanıta dayandırılabilir mi?</summary>
    public bool IsDependable => Status is EvidenceStatus.Guvenilir or EvidenceStatus.Zayifliyor;
}

/// <summary>
/// Kanıtların eskime süresini hesaplar.
///
/// <para>
/// <b>Çözdüğü problem:</b> uyum sistemleri "belge var mı?" sorusunu cevaplar ve orada
/// durur. Oysa bugün elde olan bir kanıt üç ay sonra hâlâ doğru olmayabilir — çalışan
/// değişir, sistem güncellenir, sözleşme yenilenir, sertifika biter, mevzuat değişir.
/// "Belge mevcut" ile "belge hâlâ güvenilir" arasındaki fark, denetimde ortaya çıkana
/// kadar görünmez.
/// </para>
///
/// <para>
/// <b>Neden alan katmanında ve saf:</b> §2.1 gereği burada yapay zekâ, ağ çağrısı,
/// rastgelelik ve <c>DateTime.Now</c> yoktur. Aynı kanıt aynı gün için her zaman aynı
/// skoru alır ve sonuç somut bir olguya (geçerlilik tarihi, gözlem tarihi, metin
/// sürümü) geri izlenir. Skor "model böyle dedi" diye açıklanamaz.
/// </para>
///
/// <para>
/// <b>Eksik veri kanıtı çürütmez:</b> gözlem tarihi bilinmiyorsa sonuç
/// <see cref="EvidenceStatus.Bilinmiyor"/>'dur, "güvenilmez" değil. Ürünün üçüncü
/// iddiasının kanıt tarafındaki karşılığı budur.
/// </para>
/// </summary>
public static class EvidenceHalfLife
{
    /// <summary>Bu skorun altında kanıt tazelenmelidir.</summary>
    public const decimal WeakeningThreshold = 0.70m;

    /// <summary>Bu skorun altında kanıt karar dayanağı sayılmaz.</summary>
    public const decimal UndependableThreshold = 0.30m;

    /// <summary>
    /// Sertifikanın geçerliliği bitmeden önce "tazele" denmeye başlanacak gün sayısı.
    ///
    /// <para>
    /// Yenileme süreci gün almaz: denetim randevusu, belge hazırlığı ve kurum yanıtı
    /// haftalar sürer. Bitiş gününde uyarmak, uyarmamakla aynı kapıya çıkardı.
    /// </para>
    /// </summary>
    public const int CertificateRenewalWindowDays = 90;

    /// <summary>
    /// Tür bazlı yarı ömürler (gün).
    ///
    /// <para>
    /// Sayılar kanıtın <b>ne kadar hızlı gerçeklikten kopabileceğine</b> göre seçildi:
    /// ERP anlık görüntüsü sürekli değişen bir sistemin fotoğrafıdır ve en hızlı eskir;
    /// firma beyanı bir yıl içinde büyük ölçüde kaymış olabilir; mali müşavir onaylı
    /// veri bir mali yıl boyunca dayanır.
    /// </para>
    /// </summary>
    public static int HalfLifeFor(EvidenceKind kind, EvidenceAssurance assurance) => kind switch
    {
        // Süresi yazılı belgede yaş değil KALAN SÜRE belirleyicidir; yarı ömür yalnızca
        // tarihi hiç yazılmamış belgeler için geriye kalan ölçüttür.
        EvidenceKind.Certificate => 365,

        EvidenceKind.FinancialStatement => assurance switch
        {
            EvidenceAssurance.Verified => 730,
            EvidenceAssurance.DocumentChecked => 545,
            _ => 365
        },

        EvidenceKind.WorkforceDeclaration => 180,
        EvidenceKind.ErpSnapshot => 30,

        // Mevzuat hükmü zamanla değil metin değişince geçersizleşir; yarı ömür yalnızca
        // "uzun süredir kimse bakmadı" uyarısı üretir.
        EvidenceKind.RegulationClause => 1095,

        _ => 365
    };

    /// <summary>
    /// Bir kanıtın belirli bir gündeki güvenilirliğini hesaplar.
    ///
    /// <para>
    /// Sıra önemlidir: önce kesin geçersizlik sebepleri (metin değişti, süre doldu),
    /// sonra hesaplanabilirlik, en son eskime. Ters sırada süresi dolmuş bir belge
    /// "biraz zayıflamış" görünürdü.
    /// </para>
    /// </summary>
    public static EvidenceReliability Evaluate(EvidenceSnapshot snapshot, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var halfLife = HalfLifeFor(snapshot.Kind, snapshot.Assurance);

        // 1. Dayandığı metin değiştiyse yaş önemsizdir.
        if (snapshot.SourceSuperseded)
        {
            return Sonuc(snapshot, EvidenceStatus.Gecersiz, 0m, null, halfLife, null,
                "Dayandığı resmî metnin daha yeni bir sürümü var; kanıt bu hâliyle kullanılamaz.");
        }

        // 2. Açık geçerlilik bitişi geçmişse kesin geçersizdir.
        if (snapshot.ValidUntil is { } bitis && bitis < asOf)
        {
            var gecenGun = bitis.DayNumber - asOf.DayNumber;

            return Sonuc(snapshot, EvidenceStatus.Gecersiz, 0m, null, halfLife, bitis,
                $"Geçerliliği {-gecenGun} gün önce doldu ({bitis:dd.MM.yyyy}).");
        }

        // 3. Süresi yazılı ve devam ediyorsa ölçüt KALAN SÜREDİR.
        if (snapshot.ValidUntil is { } gecerlilik)
        {
            return KalanSureyeGore(snapshot, gecerlilik, asOf, halfLife);
        }

        // 4. Tarihi bilinmeyen kanıt çürütülmez; hesaplanamaz.
        if (snapshot.ObservedOn is not { } gozlem)
        {
            return Sonuc(snapshot, EvidenceStatus.Bilinmiyor, null, null, halfLife, null,
                "Kanıtın hangi tarihte elde edildiği bilinmiyor; tazeliği hesaplanamıyor.");
        }

        // 5. Geriye eskime kalır.
        return YasaGore(snapshot, gozlem, asOf, halfLife);
    }

    /// <summary>
    /// Süresi yazılı belgeler. Yaş değil kalan süre ölçülür: dün alınmış ama yarın
    /// bitecek bir belge "taze" değildir.
    /// </summary>
    private static EvidenceReliability KalanSureyeGore(
        EvidenceSnapshot snapshot, DateOnly gecerlilik, DateOnly asOf, int halfLife)
    {
        var kalanGun = gecerlilik.DayNumber - asOf.DayNumber;
        var yas = snapshot.ObservedOn is { } g ? asOf.DayNumber - g.DayNumber : (int?)null;

        // Yenileme penceresine girene kadar tam güven; sonra bitişe doğru doğrusal iner.
        var skor = kalanGun >= CertificateRenewalWindowDays
            ? 1m
            : Math.Round((decimal)kalanGun / CertificateRenewalWindowDays, 4);

        var zayiflama = gecerlilik.AddDays(-CertificateRenewalWindowDays);

        var durum = DurumBelirle(skor);
        var aciklama = kalanGun >= CertificateRenewalWindowDays
            ? $"Geçerlilik {gecerlilik:dd.MM.yyyy} tarihine kadar; yenileme penceresine {kalanGun - CertificateRenewalWindowDays} gün var."
            : $"Geçerliliğin bitmesine {kalanGun} gün kaldı ({gecerlilik:dd.MM.yyyy}); yenileme süreci şimdi başlamalı.";

        return Sonuc(snapshot, durum, skor, yas, halfLife, zayiflama > asOf ? zayiflama : null, aciklama);
    }

    /// <summary>
    /// Süresi yazılı olmayan kanıtlar için üstel eskime: skor = 0,5 ^ (yaş / yarıömür).
    ///
    /// <para>
    /// Üstel seçildi çünkü güvenilirlik bir günde bitmez, kademeli azalır; doğrusal bir
    /// model ise yarıömrün iki katında sıfıra düşürüp "hiç bilgi yok" derdi — oysa iki
    /// yıllık bir beyan bile hiç beyan olmamasından iyidir.
    /// </para>
    /// </summary>
    private static EvidenceReliability YasaGore(
        EvidenceSnapshot snapshot, DateOnly gozlem, DateOnly asOf, int halfLife)
    {
        var yas = asOf.DayNumber - gozlem.DayNumber;

        // Gelecek tarihli gözlem veri hatasıdır; kanıtı ödüllendirmek yerine yaşı
        // sıfırlanır ve tam güven verilir.
        var etkinYas = Math.Max(yas, 0);

        var skor = (decimal)Math.Pow(0.5, (double)etkinYas / halfLife);
        skor = Math.Round(Math.Clamp(skor, 0m, 1m), 4);

        var zayiflamaGunu = ZayiflamaGunu(gozlem, halfLife);
        var durum = DurumBelirle(skor);

        var aciklama = durum switch
        {
            EvidenceStatus.Guvenilir =>
                $"{etkinYas} gün önce elde edildi; bu tür kanıt {halfLife} günde yarı güvenilirliğe iner.",
            EvidenceStatus.Zayifliyor =>
                $"{etkinYas} günlük; tazelenmezse karar dayanağı olmaktan çıkacak.",
            _ =>
                $"{etkinYas} günlük; bu tür kanıt için fazla eski, yeniden doğrulanmalı."
        };

        return Sonuc(snapshot, durum, skor, yas, halfLife, zayiflamaGunu > asOf ? zayiflamaGunu : null, aciklama);
    }

    /// <summary>Skorun zayıflama eşiğine indiği gün; üstel modelin tersinden çözülür.</summary>
    private static DateOnly ZayiflamaGunu(DateOnly gozlem, int halfLife)
    {
        var gun = halfLife * Math.Log((double)WeakeningThreshold) / Math.Log(0.5);

        return gozlem.AddDays((int)Math.Round(gun));
    }

    private static EvidenceStatus DurumBelirle(decimal skor) => skor switch
    {
        >= WeakeningThreshold => EvidenceStatus.Guvenilir,
        >= UndependableThreshold => EvidenceStatus.Zayifliyor,
        _ => EvidenceStatus.Guvenilmez
    };

    private static EvidenceReliability Sonuc(
        EvidenceSnapshot snapshot,
        EvidenceStatus durum,
        decimal? skor,
        int? yas,
        int halfLife,
        DateOnly? zayiflar,
        string aciklama) =>
        new()
        {
            Label = snapshot.Label,
            Kind = snapshot.Kind,
            Status = durum,
            Score = skor,
            AgeDays = yas,
            HalfLifeDays = halfLife,
            WeakensOn = zayiflar,
            Reason = aciklama,
        };

    /// <summary>
    /// Bir firmanın kanıt portföyünün özeti.
    ///
    /// <para>
    /// Ortalama skor <b>bilinmeyenleri dışarıda bırakır</b>: tarihi girilmemiş kanıtları
    /// sıfır sayıp ortalamayı düşürmek, firmayı veri eksikliğinden cezalandırmak olurdu.
    /// Bunun yerine kaç kanıtın hesaplanamadığı ayrıca raporlanır.
    /// </para>
    /// </summary>
    public static EvidencePortfolio Summarize(IReadOnlyCollection<EvidenceReliability> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var olculebilir = items.Where(i => i.Score is not null).ToList();

        return new EvidencePortfolio
        {
            Total = items.Count,
            Dependable = items.Count(i => i.IsDependable),
            Weakening = items.Count(i => i.Status == EvidenceStatus.Zayifliyor),
            Undependable = items.Count(i => i.Status == EvidenceStatus.Guvenilmez),
            Expired = items.Count(i => i.Status == EvidenceStatus.Gecersiz),
            Unknown = items.Count(i => i.Status == EvidenceStatus.Bilinmiyor),
            AverageScore = olculebilir.Count == 0
                ? null
                : Math.Round(olculebilir.Average(i => i.Score!.Value), 4),
        };
    }
}

/// <summary>Firmanın kanıt portföyünün tek bakışta özeti.</summary>
public sealed record EvidencePortfolio
{
    public required int Total { get; init; }

    /// <summary>Karar dayanağı olabilecek kanıt sayısı (güvenilir + zayıflıyor).</summary>
    public required int Dependable { get; init; }

    public required int Weakening { get; init; }

    public required int Undependable { get; init; }

    public required int Expired { get; init; }

    /// <summary>Tazeliği hesaplanamayan kanıt sayısı; eksiklik değil, ölçülemezlik.</summary>
    public required int Unknown { get; init; }

    /// <summary>Ölçülebilen kanıtların ortalama güvenilirliği. Hiçbiri ölçülemiyorsa <c>null</c>.</summary>
    public decimal? AverageScore { get; init; }

    /// <summary>Acil ilgi isteyen kanıt sayısı: süresi dolmuş ya da artık dayanak sayılamayan.</summary>
    public int NeedsAttention => Undependable + Expired;
}
