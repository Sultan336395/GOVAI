using GovAI.Domain.Companies;

namespace GovAI.Domain.Eligibility;

/// <summary>
/// Kural metinlerinden çıkarılan alan yollarını (<c>Workforce.EmployeeCount</c> gibi) firma profilindeki
/// gerçek değerlere çevirir. Desteklenen alanların listesi <see cref="SupportedFields"/> ile dışa açılır;
/// AI kural çıkarım prompt'u da bu listeyi kullanır, böylece model uydurma alan adı üretemez.
/// </summary>
public static class CompanyFieldResolver
{
    /// <summary>Kural çıkarımında kullanılabilecek alan adları ve açıklamaları.</summary>
    public static readonly IReadOnlyDictionary<string, string> SupportedFields =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.LegalType"] = "Hukuki yapı (LimitedCompany, JointStockCompany, SoleProprietorship, Cooperative ...)",
            ["Company.Size"] = "KOBİ ölçeği (Micro, Small, Medium, Large)",
            ["Company.AgeInYears"] = "Kuruluşundan bu yana geçen yıl",
            ["Company.ExportFlag"] = "İhracat yapıyor mu (true/false)",
            ["Company.TechnologyFlag"] = "Teknoloji/Ar-Ge merkezi statüsü var mı (true/false)",
            ["Company.IsInTechnopark"] = "Teknoparkta yerleşik bir tesisi var mı (true/false)",
            ["Company.PreviousSuccessfulApplications"] = "Geçmişte kabul almış başvuru sayısı",
            ["Company.NaceCodes"] = "Firmanın NACE kodları (küme)",
            ["Company.Certificates"] = "Geçerli sertifika kodları (küme)",
            ["Company.Cities"] = "Faaliyet gösterilen iller (küme)",
            ["Company.Nuts2Codes"] = "İstatistiki bölge kodları, ör. TR62 (küme)",
            ["Workforce.EmployeeCount"] = "Toplam çalışan sayısı",
            ["Workforce.WomenEmployeeCount"] = "Kadın çalışan sayısı",
            ["Workforce.WomenEmployeeRate"] = "Kadın çalışan oranı (0..1)",
            ["Workforce.YoungEmployeeCount"] = "Genç çalışan sayısı (yaş sınırı firmanın beyanına göre)",
            ["Workforce.YoungEmployeeRate"] = "Genç çalışan oranı (0..1)",
            ["Workforce.YoungEmployeeMaxAge"] = "Firmanın genç çalışan sayarken kullandığı azami yaş",
            ["Workforce.RAndDEmployeeCount"] = "Ar-Ge personeli sayısı",
            ["Workforce.RAndDEmployeeRate"] = "Ar-Ge personeli oranı (0..1)",
            ["Workforce.DisabledEmployeeCount"] = "Engelli çalışan sayısı",
            ["Financials.AnnualRevenue"] = "Yıllık ciro",
            ["Financials.BalanceSize"] = "Bilanço (aktif) büyüklüğü",
            ["Financials.Equity"] = "Özkaynak",
            ["Financials.ExportRevenue"] = "İhracat cirosu",
            ["Financials.ExportRatio"] = "İhracatın ciroya oranı (0..1)",
            ["Financials.FiscalYear"] = "Mali verinin ait olduğu yıl",
            ["Financials.AnnualIncome"] = "Yıllık gelir (yıllık toplu mali veriden)",
            ["Financials.AnnualExpense"] = "Yıllık gider (yıllık toplu mali veriden)",
            ["Financials.NetProfitOrLoss"] = "Net kâr/zarar (yıllık toplu mali veriden)"
        };

    public static FieldValue Resolve(Company company, string field, DateOnly asOf)
    {
        return field.Trim() switch
        {
            var f when Is(f, "Company.LegalType") => FieldValue.FromText(company.LegalType.ToString()),
            var f when Is(f, "Company.Size") => FieldValue.FromText(company.Size.ToString()),
            var f when Is(f, "Company.AgeInYears") => FieldValue.FromNumber(AgeInYears(company, asOf)),
            var f when Is(f, "Company.ExportFlag") => FieldValue.FromBoolean(company.ExportFlag),
            var f when Is(f, "Company.TechnologyFlag") => FieldValue.FromBoolean(company.TechnologyFlag),
            var f when Is(f, "Company.IsInTechnopark") => FieldValue.FromBoolean(company.Locations.Any(l => l.IsInTechnopark)),
            var f when Is(f, "Company.PreviousSuccessfulApplications") => FieldValue.FromNumber(company.PreviousSuccessfulApplications),

            // Her gerçek firmanın NACE kodu ve adresi vardır; boş koleksiyon "yok" değil "girilmemiş" demektir.
            var f when Is(f, "Company.NaceCodes") => RequiredSet(company.NaceCodes.Select(n => n.Code)),
            var f when Is(f, "Company.Cities") => RequiredSet(company.Locations.Select(l => l.City)),
            var f when Is(f, "Company.Nuts2Codes") => RequiredSet(company.Locations.Select(l => l.Nuts2Code ?? string.Empty)),

            // Sertifika listesi bilinçli olarak boş olabilir ("belgemiz yok"); bu geçerli bir cevaptır.
            var f when Is(f, "Company.Certificates") => FieldValue.FromSet(company.ValidCertificateCodes(asOf)),

            var f when Is(f, "Workforce.EmployeeCount") => Headcount(company, company.Workforce.EmployeeCount),
            var f when Is(f, "Workforce.WomenEmployeeCount") => Headcount(company, company.Workforce.WomenEmployeeCount),
            var f when Is(f, "Workforce.WomenEmployeeRate") => Headcount(company, company.Workforce.WomenEmployeeRate),
            // Genç çalışan sayısı, tanımı bilinmeden ANLAMSIZDIR: teşvik programları
            // 25, 29 ve 30 yaş sınırlarını birlikte kullanır. Firma hangi sınıra göre
            // saydığını beyan etmediyse sayı "bilinmiyor"dur — sıfır değil, uygun da
            // değil. Aksi hâlde 30 yaş altını sayan bir firma, 25 yaş altı arayan
            // çağrıda sessizce uygun görünürdü.
            var f when Is(f, "Workforce.YoungEmployeeCount") => Young(company, company.Workforce.YoungEmployeeCount),
            var f when Is(f, "Workforce.YoungEmployeeRate") => Young(company, company.Workforce.YoungEmployeeRate),
            var f when Is(f, "Workforce.YoungEmployeeMaxAge") => company.Workforce.YoungEmployeeMaxAge is { } yas
                ? FieldValue.FromNumber(yas)
                : FieldValue.Unknown(),
            var f when Is(f, "Workforce.RAndDEmployeeCount") => Headcount(company, company.Workforce.RAndDEmployeeCount),
            var f when Is(f, "Workforce.RAndDEmployeeRate") => Headcount(company, company.Workforce.RAndDEmployeeRate),
            var f when Is(f, "Workforce.DisabledEmployeeCount") => Headcount(company, company.Workforce.DisabledEmployeeCount),

            // Yıllık toplu mali kayıt varsa önceliklidir: hangi yıla ait olduğu bellidir
            // ve doğrulama durumu taşır. Yoksa tek dönemlik eski alanlara düşülür —
            // Faz 3 öncesi girilmiş profiller bozulmadan çalışmaya devam eder.
            var f when Is(f, "Financials.AnnualRevenue") =>
                Annual(company, a => a.AnnualRevenue) is { IsKnown: true } yillik
                    ? yillik
                    : Money(company.Financials.AnnualRevenue),
            var f when Is(f, "Financials.BalanceSize") =>
                Annual(company, a => a.BalanceTotal) is { IsKnown: true } bilanco
                    ? bilanco
                    : Money(company.Financials.BalanceSize),
            var f when Is(f, "Financials.AnnualIncome") => Annual(company, a => a.AnnualIncome),
            var f when Is(f, "Financials.AnnualExpense") => Annual(company, a => a.AnnualExpense),
            var f when Is(f, "Financials.NetProfitOrLoss") => Annual(company, a => a.NetProfitOrLoss),
            var f when Is(f, "Financials.Equity") => FieldValue.FromNumber(company.Financials.Equity),
            var f when Is(f, "Financials.ExportRevenue") => Money(company.Financials.ExportRevenue),
            var f when Is(f, "Financials.ExportRatio") => FieldValue.FromNumber(company.Financials.ExportRatio),
            var f when Is(f, "Financials.FiscalYear") =>
                FieldValue.FromNumber(company.LatestAnnualFinancials()?.FiscalYear ?? company.Financials.FiscalYear),

            _ => FieldValue.Unknown()
        };
    }

    private static bool Is(string candidate, string known) =>
        string.Equals(candidate, known, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Eksik veri ile "sıfır" ayrımı, motorun en kritik davranışıdır.
    /// Doldurulmamış bir profil firmayı ELEMEZ; "belirsiz" sonucu üretir ve kullanıcıdan veri ister.
    /// Aksi hâlde sistem, sadece profili eksik olduğu için firmayı uygun fırsatlardan mahrum bırakırdı.
    /// </summary>
    /// <summary>
    /// Genç çalışan alanları, yaş tanımı beyan edilmediği sürece <b>bilinmiyor</b>dur.
    /// Gerekçe <see cref="Companies.Workforce.YoungEmployeeMaxAge"/> üzerinde yazılıdır.
    /// </summary>
    private static FieldValue Young(Company company, decimal value) =>
        company.Workforce.YoungEmployeeMaxAge is null
            ? FieldValue.Unknown()
            : Headcount(company, value);

    /// <summary>
    /// En güncel yıllık mali kayıttan bir alanı okur.
    ///
    /// <para>
    /// <c>null</c> "girilmedi" demektir ve <see cref="FieldValue.Unknown"/> döner —
    /// sıfır sayılırsa cirosu girilmemiş bir firma "cirosu 0" diye elenirdi. Net
    /// kâr/zarar alanında sıfır <b>geçerli bir değerdir</b>; bu yüzden burada sıfır
    /// filtrelenmez, yalnızca <c>null</c> bilinmeyen sayılır.
    /// </para>
    /// </summary>
    private static FieldValue Annual(Company company, Func<AnnualFinancialRecord, decimal?> selector)
    {
        var kayit = company.LatestAnnualFinancials();
        return kayit is null ? FieldValue.Unknown() : FieldValue.FromNumber(selector(kayit));
    }

    private static FieldValue Money(decimal value) =>
        value == 0m ? FieldValue.Unknown() : FieldValue.FromNumber(value);

    /// <summary>Toplam çalışan sayısı girilmemişse personel kırılımlarının hiçbiri değerlendirilemez.</summary>
    private static FieldValue Headcount(Company company, decimal value) =>
        company.Workforce.EmployeeCount == 0 ? FieldValue.Unknown() : FieldValue.FromNumber(value);

    /// <summary>Zorunlu koleksiyon alanları: boşsa "girilmemiş" kabul edilir.</summary>
    private static FieldValue RequiredSet(IEnumerable<string> values)
    {
        var set = FieldValue.FromSet(values);
        return set.Set!.Count == 0 ? FieldValue.Unknown() : set;
    }

    private static decimal? AgeInYears(Company company, DateOnly asOf)
    {
        if (company.FoundedOn is null)
        {
            return null;
        }

        var days = asOf.DayNumber - company.FoundedOn.Value.DayNumber;
        return days < 0 ? 0m : Math.Round(days / 365.25m, 2);
    }
}
