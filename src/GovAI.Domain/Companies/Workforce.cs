using GovAI.Domain.Common;

namespace GovAI.Domain.Companies;

/// <summary>
/// Firmanın personel yapısı. İstihdam teşviklerinin büyük bölümü bu alanlar üzerinden filtrelenir.
/// Değişmez (immutable) bir değer nesnesidir; İK/ERP eşitlemesi her seferinde yenisini üretir.
///
/// <para>
/// <b>Kırılım alanları boş bırakılabilir ve boş olmak "sıfır" demek değildir.</b>
/// Eskiden hepsi <c>int</c>'ti ve girilmemiş alan 0 olarak duruyordu; toplam çalışan
/// sayısı dolu olduğu sürece motor bu sıfırları <b>gerçek</b> sayıyordu. Sonuç sahada
/// şuydu: 50 kişilik pilot firma "kadın çalışanı yok" ve "Ar-Ge personeli yok" diye
/// kaydedilmiş, kadın istihdamı ve Ar-Ge şartı arayan çağrılardan <b>elenmişti</b> —
/// oysa yalnızca kırılım hiç girilmemişti. Bu, ürünün üçüncü iddiasının ("eksik veri
/// firmayı elemez") fiilen çiğnenmesiydi.
/// </para>
///
/// <para>
/// Artık her alan <b>açık beyan</b> ister: <c>null</c> "bilinmiyor", <c>0</c> ise
/// firmanın "gerçekten yok" beyanıdır. İkisi ayrı şeydir ve ayrı sonuç doğurur —
/// biri kararı belirsiz bırakır, diğeri koşulu sağlamadığını söyler.
/// </para>
/// </summary>
public sealed record Workforce
{
    /// <summary>
    /// Boş değer. Her çağrıda <b>yeni bir örnek</b> döner.
    ///
    /// Bilinçli olarak <c>static readonly</c> alan DEĞİLDİR: EF Core sahipli (owned)
    /// tipleri örnek kimliğine göre izler. Tek bir paylaşılan örnek iki farklı şirkete
    /// atandığında EF sahipliği karıştırır ve satırlardan birinin sütunlarını NULL yazar.
    /// Bu, finansal bilgisi girilmemiş ikinci şirket eklenirken
    /// "null value in column annual_revenue violates not-null constraint" hatası olarak
    /// ortaya çıkmıştı. Bellek içi sağlayıcı bu hatayı gizler; gerçek PostgreSQL yakalar.
    /// </summary>
    public static Workforce Empty => new(0, null, null, null, null);

    public Workforce(
        int employeeCount,
        int? womenEmployeeCount,
        int? youngEmployeeCount,
        int? rAndDEmployeeCount,
        int? disabledEmployeeCount,
        int? youngEmployeeMaxAge = null)
    {
        DomainException.ThrowIf(employeeCount < 0, "Çalışan sayısı negatif olamaz.");
        DomainException.ThrowIf(
            womenEmployeeCount < 0 || youngEmployeeCount < 0
            || rAndDEmployeeCount < 0 || disabledEmployeeCount < 0,
            "Personel kırılım sayıları negatif olamaz.");

        DomainException.ThrowIf(womenEmployeeCount > employeeCount, "Kadın çalışan sayısı toplam çalışan sayısını aşamaz.");
        DomainException.ThrowIf(youngEmployeeCount > employeeCount, "Genç çalışan sayısı toplam çalışan sayısını aşamaz.");
        DomainException.ThrowIf(rAndDEmployeeCount > employeeCount, "Ar-Ge personeli sayısı toplam çalışan sayısını aşamaz.");
        DomainException.ThrowIf(disabledEmployeeCount > employeeCount, "Engelli çalışan sayısı toplam çalışan sayısını aşamaz.");

        DomainException.ThrowIf(
            youngEmployeeMaxAge is not null and (< 15 or > 65),
            "Genç çalışan yaş sınırı 15 ile 65 arasında olmalıdır.");

        EmployeeCount = employeeCount;
        YoungEmployeeMaxAge = youngEmployeeMaxAge;
        WomenEmployeeCount = womenEmployeeCount;
        YoungEmployeeCount = youngEmployeeCount;
        RAndDEmployeeCount = rAndDEmployeeCount;
        DisabledEmployeeCount = disabledEmployeeCount;
    }

    /// <summary>
    /// Toplam çalışan sayısı. <c>0</c> burada hâlâ "girilmedi" demektir ve tüm personel
    /// alanlarını bilinmez yapar (CLAUDE.md §2.2); kaba süzgeç olarak korunur.
    /// </summary>
    public int EmployeeCount { get; init; }

    /// <summary><c>null</c> = beyan edilmedi. <c>0</c> = firma "kadın çalışanımız yok" diyor.</summary>
    public int? WomenEmployeeCount { get; init; }

    /// <summary>
    /// Genç çalışan sayısı; genç istihdam teşviklerinde kullanılır.
    /// <see cref="YoungEmployeeMaxAge"/> boşsa sayı tek başına anlamsızdır.
    /// </summary>
    public int? YoungEmployeeCount { get; init; }

    public int? RAndDEmployeeCount { get; init; }

    public int? DisabledEmployeeCount { get; init; }

    /// <summary>
    /// Firmanın <see cref="YoungEmployeeCount"/> sayarken kullandığı azami yaş.
    ///
    /// <para>
    /// Sistem "genç çalışan"ın ne demek olduğunu <b>varsaymaz</b>. Teşvik programları
    /// farklı sınırlar kullanır (29 altı, 25 altı, 30 altı) ve firmanın hangi sınıra
    /// göre saydığı bilinmeden "en az 5 genç çalışan" koşulu doğrulanamaz. Boşsa tanım
    /// bilinmiyordur ve karşılaştırma <b>doğrulanamaz</b> sayılır — "uygun" değil.
    /// </para>
    /// </summary>
    public int? YoungEmployeeMaxAge { get; init; }

    /// <summary>
    /// Firmanın genç tanımı, çağrının aradığı yaş sınırını karşılıyor mu?
    ///
    /// <para>
    /// Firma 29 yaş altını sayıyorsa 25 yaş altı arayan bir çağrı için bu sayı
    /// KULLANILAMAZ: 29 altındaki grup 25 altındakini kapsar ama eşit değildir, sayı
    /// olduğundan büyüktür. Ters yön güvenlidir — 25 altını sayan firma, 29 altı
    /// arayan çağrının koşulunu zaten sağlar.
    /// </para>
    ///
    /// <para>Tanım bilinmiyorsa <c>null</c> döner: "hayır" değil, "bilinmiyor".</para>
    /// </summary>
    public bool? YoungDefinitionSatisfies(int requiredMaxAge) =>
        YoungEmployeeMaxAge is null ? null : YoungEmployeeMaxAge <= requiredMaxAge;

    /// <summary><c>womenEmployeeRate</c> — 0..1 aralığında oran. Sayı beyan edilmediyse <c>null</c>.</summary>
    public decimal? WomenEmployeeRate => Ratio(WomenEmployeeCount);

    /// <summary><c>youngEmployeeRate</c> — 0..1 aralığında oran. Sayı beyan edilmediyse <c>null</c>.</summary>
    public decimal? YoungEmployeeRate => Ratio(YoungEmployeeCount);

    public decimal? RAndDEmployeeRate => Ratio(RAndDEmployeeCount);

    /// <summary>
    /// Oran.
    ///
    /// <para>
    /// Pay beyan edilmediyse oran da <b>bilinmiyordur</b>; 0 döndürmek "oran sıfır"
    /// demek olurdu ve kadın istihdam oranı arayan çağrıda firmayı elerdi.
    /// </para>
    /// </summary>
    private decimal? Ratio(int? part) =>
        part is null || EmployeeCount == 0 ? null : Math.Round((decimal)part.Value / EmployeeCount, 4);
}
