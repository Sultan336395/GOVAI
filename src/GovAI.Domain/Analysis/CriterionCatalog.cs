namespace GovAI.Domain.Analysis;

/// <summary>Bir kriterin sabit tanımı: kodu, adı, kırılım başlığı ve zorunluluk varsayılanı.</summary>
public sealed record CriterionDefinition
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required ScoreGroup Group { get; init; }

    /// <summary>
    /// Kriter, çağrı metninde engelleyici bir koşul yazmasa bile sağlanmadığında
    /// başvuruyu imkânsız kılıyor mu?
    ///
    /// <para>
    /// Bugün yalnızca <see cref="CriterionCatalog.ApplicationWindow"/> böyledir: son
    /// başvuru tarihi geçmiş bir çağrıya belge ne derse desin başvurulamaz. Diğer
    /// kriterlerin zorunluluğu <b>belgeden</b> gelir (kuralın
    /// <see cref="Common.RuleSeverity.Blocking"/> olması). Sektör uyumsuzluğunu burada
    /// zorunlu işaretlemek eleme yaratırdı; oysa uyumsuz kayıt elenmez, listenin sonuna
    /// iner (CLAUDE.md §2.2.1).
    /// </para>
    /// </summary>
    public required bool BlockingByNature { get; init; }

    /// <summary>Kriterin ne sorduğunun kısa Türkçe açıklaması.</summary>
    public required string Question { get; init; }
}

/// <summary>
/// Kriter kataloğu — analiz motorunun ortak sözlüğü (Faz 3).
///
/// <para>
/// Kriter kodları <b>sabittir</b>: altın veri seti, ekran metinleri ve yapay zekâ
/// iddiaları bu kodlarla konuşur. Kural kimlikleri her yeniden ayrıştırmada değişir,
/// kriter kodu değişmez; aksi hâlde "aynı senaryo" iki hafta sonra farklı bir şeyi
/// ölçüyor olurdu.
/// </para>
/// </summary>
public static class CriterionCatalog
{
    // ───────────── Şirket–fırsat kriterleri ─────────────

    public const string SectorNace = "SECTOR_NACE";
    public const string CompanyType = "COMPANY_TYPE";
    public const string SmeScale = "SME_SCALE";
    public const string CompanyAge = "COMPANY_AGE";
    public const string EmployeeCount = "EMPLOYEE_COUNT";
    public const string YoungEmployees = "YOUNG_EMPLOYEES";
    public const string WomenEmployees = "WOMEN_EMPLOYEES";
    public const string DisabledEmployees = "DISABLED_EMPLOYEES";
    public const string Geography = "GEOGRAPHY";
    public const string ApplicationWindow = "APPLICATION_WINDOW";
    public const string SupportType = "SUPPORT_TYPE";
    public const string MandatoryDocuments = "MANDATORY_DOCUMENTS";
    public const string SpecialConditions = "SPECIAL_CONDITIONS";
    public const string RevenueAndFinancials = "REVENUE_FINANCIALS";

    // ───────────── Şirket–mevzuat etki kriterleri ─────────────

    public const string RegulationDomainMatch = "REG_DOMAIN";
    public const string RegulationSector = "REG_SECTOR_NACE";
    public const string RegulationCompanySize = "REG_COMPANY_SIZE";
    public const string RegulationEmployerStatus = "REG_EMPLOYER_STATUS";
    public const string RegulationTaxAndSocialSecurity = "REG_TAX_SGK";
    public const string RegulationJurisdiction = "REG_JURISDICTION";
    public const string RegulationEffectiveDate = "REG_EFFECTIVE_DATE";
    public const string RegulationTransitionPeriod = "REG_TRANSITION";
    public const string RegulationObligations = "REG_OBLIGATIONS";

    private static readonly IReadOnlyList<CriterionDefinition> All =
    [
        new()
        {
            Code = SectorNace,
            Name = "Sektör ve NACE uyumu",
            Group = ScoreGroup.SectorNace,
            BlockingByNature = false,
            Question = "Firmanın faaliyet alanı çağrının aradığı sektörle örtüşüyor mu?"
        },
        new()
        {
            Code = CompanyType,
            Name = "Şirket türü",
            Group = ScoreGroup.ScaleAndFinancials,
            BlockingByNature = false,
            Question = "Firmanın hukuki yapısı başvuru yapabilecek türler arasında mı?"
        },
        new()
        {
            Code = SmeScale,
            Name = "KOBİ ve ölçek durumu",
            Group = ScoreGroup.ScaleAndFinancials,
            BlockingByNature = false,
            Question = "Firmanın ölçeği çağrının hedef ölçeğine uyuyor mu?"
        },
        new()
        {
            Code = CompanyAge,
            Name = "Kuruluş tarihi ve şirket yaşı",
            Group = ScoreGroup.ScaleAndFinancials,
            BlockingByNature = false,
            Question = "Firmanın faaliyet süresi çağrının aradığı aralıkta mı?"
        },
        new()
        {
            Code = EmployeeCount,
            Name = "Çalışan sayısı",
            Group = ScoreGroup.Workforce,
            BlockingByNature = false,
            Question = "Firmanın çalışan sayısı çağrının koşulunu karşılıyor mu?"
        },
        new()
        {
            Code = YoungEmployees,
            Name = "Genç çalışan sayısı ve yaş tanımı",
            Group = ScoreGroup.Workforce,
            BlockingByNature = false,
            Question = "Firmanın genç çalışan sayısı, çağrının kullandığı yaş tanımıyla karşılaştırılabilir mi?"
        },
        new()
        {
            Code = WomenEmployees,
            Name = "Kadın çalışan sayısı",
            Group = ScoreGroup.Workforce,
            BlockingByNature = false,
            Question = "Firmanın kadın çalışan sayısı çağrının koşulunu karşılıyor mu?"
        },
        new()
        {
            Code = DisabledEmployees,
            Name = "Engelli çalışan sayısı",
            Group = ScoreGroup.Workforce,
            BlockingByNature = false,
            Question = "Firmanın engelli çalışan sayısı çağrının koşulunu karşılıyor mu?"
        },
        new()
        {
            Code = Geography,
            Name = "Coğrafi kapsam",
            Group = ScoreGroup.Geography,
            BlockingByNature = false,
            Question = "Firmanın faaliyet gösterdiği il/bölge çağrının kapsamında mı?"
        },
        new()
        {
            Code = ApplicationWindow,
            Name = "Başvuru açılış ve son başvuru tarihi",
            Group = ScoreGroup.Timing,
            BlockingByNature = true,
            Question = "Çağrı bugün başvuruya açık mı?"
        },
        new()
        {
            Code = SupportType,
            Name = "Destek türü (hibe / kredi / geri ödemeli)",
            Group = ScoreGroup.DocumentsAndConditions,
            BlockingByNature = false,
            Question = "Çağrının sunduğu destek türü belgeden kesin olarak anlaşılıyor mu?"
        },
        new()
        {
            Code = MandatoryDocuments,
            Name = "Zorunlu belge ve sertifikalar",
            Group = ScoreGroup.DocumentsAndConditions,
            BlockingByNature = false,
            Question = "Başvuru için istenen belgeler firmada mevcut mu?"
        },
        new()
        {
            Code = SpecialConditions,
            Name = "Özel başvuru koşulları",
            Group = ScoreGroup.DocumentsAndConditions,
            BlockingByNature = false,
            Question = "Çağrının kendine özgü koşulları sağlanıyor mu?"
        },
        new()
        {
            Code = RevenueAndFinancials,
            Name = "Ciro ve mali kriterler",
            Group = ScoreGroup.ScaleAndFinancials,
            BlockingByNature = false,
            Question = "Firmanın mali göstergeleri çağrının eşiklerini karşılıyor mu?"
        },

        new()
        {
            Code = RegulationDomainMatch,
            Name = "Düzenleme alanı",
            Group = ScoreGroup.DocumentsAndConditions,
            BlockingByNature = false,
            Question = "Düzenlemenin alanı firmanın faaliyetiyle ilgili mi?"
        },
        new()
        {
            Code = RegulationSector,
            Name = "Sektör ve NACE kapsamı",
            Group = ScoreGroup.SectorNace,
            BlockingByNature = false,
            Question = "Düzenleme firmanın sektörünü kapsıyor mu?"
        },
        new()
        {
            Code = RegulationCompanySize,
            Name = "Şirket büyüklüğü kapsamı",
            Group = ScoreGroup.ScaleAndFinancials,
            BlockingByNature = false,
            Question = "Düzenleme firmanın ölçeğindeki işletmeleri kapsıyor mu?"
        },
        new()
        {
            Code = RegulationEmployerStatus,
            Name = "Çalışan ve işverenlik durumu",
            Group = ScoreGroup.Workforce,
            BlockingByNature = false,
            Question = "Firma bu düzenleme bakımından işveren sıfatı taşıyor mu?"
        },
        new()
        {
            Code = RegulationTaxAndSocialSecurity,
            Name = "Vergi ve SGK niteliği",
            Group = ScoreGroup.ScaleAndFinancials,
            BlockingByNature = false,
            Question = "Firmanın vergi ve SGK niteliği düzenlemenin muhatabı mı?"
        },
        new()
        {
            Code = RegulationJurisdiction,
            Name = "Coğrafi ve yargısal kapsam",
            Group = ScoreGroup.Geography,
            BlockingByNature = false,
            Question = "Düzenleme firmanın tabi olduğu yargı alanında mı?"
        },
        new()
        {
            Code = RegulationEffectiveDate,
            Name = "Yayın ve yürürlük tarihi",
            Group = ScoreGroup.Timing,
            BlockingByNature = false,
            Question = "Düzenleme yürürlüğe girdi mi?"
        },
        new()
        {
            Code = RegulationTransitionPeriod,
            Name = "Geçiş süresi",
            Group = ScoreGroup.Timing,
            BlockingByNature = false,
            Question = "Uyum için tanınmış bir geçiş süresi var mı?"
        },
        new()
        {
            Code = RegulationObligations,
            Name = "Resmî belgede yer alan yükümlülükler",
            Group = ScoreGroup.DocumentsAndConditions,
            BlockingByNature = false,
            Question = "Belgede firmayı bağlayan açık bir yükümlülük yazıyor mu?"
        }
    ];

    private static readonly IReadOnlyDictionary<string, CriterionDefinition> ByCode =
        All.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CriterionDefinition> Definitions => All;

    /// <summary>Şirket–fırsat analizinde değerlendirilen kriterler, ekran sırasıyla.</summary>
    public static IReadOnlyList<string> OpportunityCriteria =>
    [
        SectorNace, CompanyType, SmeScale, CompanyAge, RevenueAndFinancials,
        EmployeeCount, YoungEmployees, WomenEmployees, DisabledEmployees,
        Geography, ApplicationWindow, SupportType, MandatoryDocuments, SpecialConditions
    ];

    /// <summary>Şirket–mevzuat etki analizinde değerlendirilen kriterler.</summary>
    public static IReadOnlyList<string> RegulationCriteria =>
    [
        RegulationDomainMatch, RegulationSector, RegulationCompanySize,
        RegulationEmployerStatus, RegulationTaxAndSocialSecurity, RegulationJurisdiction,
        RegulationEffectiveDate, RegulationTransitionPeriod, RegulationObligations
    ];

    public static CriterionDefinition Get(string code) =>
        ByCode.TryGetValue(code, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Tanımsız kriter kodu: {code}");

    public static bool IsKnown(string code) => ByCode.ContainsKey(code);

    /// <summary>
    /// Kural motorunun alan adını kritere bağlar. Beyaz liste dışında bir alan gelirse
    /// kriter <see cref="SpecialConditions"/> altında toplanır — sessizce düşürülmez.
    /// </summary>
    public static string CriterionOfField(string field) => field.Trim() switch
    {
        var f when Eq(f, "Company.NaceCodes") => SectorNace,
        var f when Eq(f, "Company.LegalType") => CompanyType,
        var f when Eq(f, "Company.Size") => SmeScale,
        var f when Eq(f, "Company.AgeInYears") => CompanyAge,
        var f when Eq(f, "Company.Cities") || Eq(f, "Company.Nuts2Codes") => Geography,
        var f when Eq(f, "Company.Certificates") => MandatoryDocuments,
        var f when Eq(f, "Workforce.EmployeeCount") => EmployeeCount,
        var f when Eq(f, "Workforce.WomenEmployeeCount") || Eq(f, "Workforce.WomenEmployeeRate") => WomenEmployees,
        var f when Eq(f, "Workforce.DisabledEmployeeCount") => DisabledEmployees,
        var f when f.StartsWith("Workforce.Young", StringComparison.OrdinalIgnoreCase) => YoungEmployees,
        var f when f.StartsWith("Financials.", StringComparison.OrdinalIgnoreCase) => RevenueAndFinancials,
        _ => SpecialConditions
    };

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
