using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;

namespace GovAI.Application.Opportunities;

public sealed record OpportunitySummaryDto(
    Guid Id,
    string Title,
    string Publisher,
    SourceType SourceType,
    SupportCategory SupportCategory,
    DateTimeOffset PublishedAt,
    DateTimeOffset? Deadline,
    int? DaysUntilDeadline,
    decimal? MaxAmount,
    string? Currency,
    bool IsReviewedByConsultant,
    int RuleCount,
    int DocumentCount,

    /// <summary>
    /// Çağrının resmî adresi.
    ///
    /// Listede gerekli: Resmî Gazete ilanları standart başlıklarla yayımlanır ve aynı
    /// gün iki ayrı ihale aynı adı taşır. Bunlar mükerrer kayıt değildir, ama başlıktan
    /// ayırt edilemezler; ekran ayırt edici izi adresten çıkarır.
    /// </summary>
    string? SourceUrl = null);

public sealed record OpportunityDetailDto(
    Guid Id,
    string Title,
    string Publisher,
    string? Summary,
    string? SourceUrl,
    SourceType SourceType,
    SupportCategory SupportCategory,
    DateTimeOffset PublishedAt,
    DateTimeOffset? Deadline,
    int? DaysUntilDeadline,
    BudgetDto? Budget,

    /// <summary>
    /// Türü belirlenmiş tutarlar (Faz 3). Ekran bunları kullanır; hibe ile krediyi
    /// ayırt eden tek kaynak budur.
    /// </summary>
    IReadOnlyList<BudgetItemDto> BudgetItems,

    IReadOnlyList<BudgetRateDto> BudgetRates,

    /// <summary>Çağrının mevzuat dayanağı, metinde geçtiği biçimiyle (Faz 3).</summary>
    string? LegalBasis,

    decimal RuleExtractionConfidence,
    bool IsReviewedByConsultant,
    IReadOnlyList<OpportunityRuleDto> Rules,
    IReadOnlyList<DocumentRequirementDto> DocumentChecklist,
    /// <summary>
    /// Hangi alanın neden boş olduğu. Arayüz <c>null</c> yerine buna bakarak
    /// "Resmî kaynakta belirtilmemiş" gösterir.
    /// </summary>
    FieldAvailabilityDto FieldAvailability,

    /// <summary>
    /// Çağrı bu an itibarıyla açık mı? Son başvuru tarihi geçmiş bir fırsat
    /// <b>açık gibi gösterilemez</b>; ekran bunu ayrıca vurgular.
    /// </summary>
    bool IsOpen = true,

    /// <summary>
    /// Kaydın dayandığı resmî belge ve kanıt parçaları. Elle açılmış ya da kaynak
    /// belgesi olmayan fırsatta <c>null</c>'dır.
    /// </summary>
    OpportunityProvenanceDto? Provenance = null,

    /// <summary>
    /// Kayıt karantinadaysa nedeni; değilse <see cref="QuarantineReason.None"/>.
    ///
    /// İnceleme ekranı için gerekli: karantinadaki bir kayda bakan inceleyici, kaydın
    /// katalogda görünmediğini ekranın kendisinden anlamalıdır. Kiracı ekranları zaten
    /// karantinadaki kaydı hiç listelemez, orada bu alanın bir etkisi olmaz.
    /// </summary>
    QuarantineReason QuarantineReason = QuarantineReason.None,

    string? QuarantineNote = null);

public sealed record BudgetDto(decimal? MinAmount, decimal? MaxAmount, string Currency, decimal? SupportRate);

/// <summary>
/// Türü belirlenmiş tek bir tutar (Faz 3). <c>Label</c> ekranda tutarın yanında
/// yazar: "1.500.000 TL" tek başına hibe mi kredi mi olduğunu söylemez.
/// </summary>
public sealed record BudgetItemDto(
    BudgetItemType Type,
    string Label,
    decimal Amount,
    string Currency,
    string? Excerpt,
    int StartOffset,
    int EndOffset,
    bool NeedsReview);

public sealed record BudgetRateDto(
    BudgetRateType Type,
    string Label,
    decimal Rate,
    string? Excerpt,
    int StartOffset,
    int EndOffset,
    bool NeedsReview);

/// <summary>Türlü bütçe kalemlerinin Türkçe etiketleri.</summary>
public static class BudgetLabels
{
    public static string Of(BudgetItemType type) => type switch
    {
        BudgetItemType.TotalProgrammeBudget => "Toplam program bütçesi",
        BudgetItemType.GrantCeiling => "Hibe üst limiti",
        BudgetItemType.CreditCeiling => "Kredi üst limiti",
        BudgetItemType.RepayableSupport => "Geri ödemeli destek",
        BudgetItemType.EligibleExpenditure => "Uygun harcama tutarı",
        _ => "Türü belirlenemedi"
    };

    public static string Of(BudgetRateType type) => type switch
    {
        BudgetRateType.SupportRate => "Destek oranı",
        BudgetRateType.OwnContributionRate => "Öz kaynak / ortaklık payı",
        _ => "Türü belirlenemedi"
    };
}

/// <summary>
/// Kanıt parçasının gösterim bilgisi. Kanıt satırında saklanmaz, belge sürümünden
/// okunur: metni kopyalamak iki ayrı doğruluk kaynağı yaratırdı.
/// </summary>
public sealed record RuleEvidenceContext(
    string Text,
    string TextHash,
    int DocumentVersionNumber,
    string? OfficialUrl);

/// <summary>Kuralın dayandığı tek bir resmî kanıt parçası (Faz 3).</summary>
public sealed record RuleEvidenceDto(
    Guid EvidenceChunkId,
    Guid DocumentVersionId,
    RuleEvidenceRole Role,
    string RoleLabel,
    int StartOffset,
    int EndOffset,
    int? PageNumber,
    string? SectionTitle,
    string Text,
    string TextHash,
    string? OfficialUrl,
    int? DocumentVersionNumber);

public sealed record OpportunityRuleDto(
    Guid Id,
    string Field,
    RuleOperator Operator,
    string Value,
    RuleDimension Dimension,
    RuleSeverity Severity,
    string HumanReadable,
    string? SourceExcerpt,
    decimal Confidence,
    bool IsManuallyOverridden,
    IReadOnlyList<RuleEvidenceDto> Evidence,
    // Bu kural hakkında yapay zekâ resmî kaynağa dayalı iddia üretebilir mi?
    bool SupportsAiClaims);

/// <summary>Kanıt rolünün Türkçe karşılığı; ham enum adı ekrana çıkmaz.</summary>
public static class RuleEvidenceLabels
{
    public static string Of(RuleEvidenceRole role) => role switch
    {
        RuleEvidenceRole.ValueSource => "Değerin geçtiği bölüm",
        RuleEvidenceRole.ConditionText => "Koşulun geçtiği bölüm",
        _ => "Bağlam"
    };
}

public sealed record DocumentRequirementDto(string Code, string Name, bool IsMandatory, string? IssuingAuthority, string? Notes);

/// <summary>Fırsatın elle veya worker tarafından oluşturulması/güncellenmesi.</summary>
/// <summary>
/// Bir alanın neden boş olduğu. Arayüz <c>null</c> yerine bu duruma bakarak
/// "Resmî kaynakta belirtilmemiş" gösterir.
/// </summary>
public sealed record FieldAvailabilityDto(
    string Deadline,
    string Budget,
    string Currency,
    string EligibleApplicant,
    string Geography,
    string Sector,
    string ProgrammeType,
    string OfficialDocumentUrl);

public sealed record UpsertOpportunityRequest
{
    public required Guid SourceId { get; init; }

    public Guid? SourceDocumentId { get; init; }

    public required SourceType SourceType { get; init; }

    public required SupportCategory SupportCategory { get; init; }

    public required string Title { get; init; }

    public required string Publisher { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public string? Summary { get; init; }

    public string? SourceUrl { get; init; }

    public DateTimeOffset? Deadline { get; init; }

    public BudgetDto? Budget { get; init; }

    /// <summary>
    /// Çağrının mevzuat dayanağı, metinde geçtiği biçimiyle (Faz 3).
    /// Boş gönderilirse mevcut dayanak korunur; yeniden ayrıştırmada kalıp tutmazsa
    /// daha önce bulunmuş bilgi kaybolmamalıdır.
    /// </summary>
    public string? LegalBasis { get; init; }

    /// <summary>
    /// Türü belirlenmiş tutarlar. Boş gelirse mevcut kalemler korunur; yeniden
    /// ayrıştırmada kalıp tutmazsa daha önce çıkarılmış doğru bilgi kaybolmamalıdır.
    /// </summary>
    public IReadOnlyList<UpsertBudgetItemDto> BudgetItems { get; init; } = [];

    public IReadOnlyList<UpsertBudgetRateDto> BudgetRates { get; init; } = [];

    public decimal RuleExtractionConfidence { get; init; } = 1m;

    public IReadOnlyList<UpsertRuleDto> Rules { get; init; } = [];

    public IReadOnlyList<DocumentRequirementDto> DocumentChecklist { get; init; } = [];

    // ── Faz 2: veri kalitesi ──
    // Bulunamayan alan TAHMİN EDİLMEZ; null bırakılır ve durumu NotProvided olur.

    /// <summary>Uygun başvuru sahibi (ör. "KOBİ", "Üniversite-sanayi iş birliği").</summary>
    public string? EligibleApplicant { get; init; }

    /// <summary>Coğrafi kapsam (ör. "TR62", "Tüm Türkiye", "EU27").</summary>
    public string? Geography { get; init; }

    /// <summary>Sektör kapsamı.</summary>
    public string? Sector { get; init; }

    /// <summary>Program türü (ör. "Horizon Europe", "KOBİGEL").</summary>
    public string? ProgrammeType { get; init; }

    /// <summary>Resmî çağrı belgesinin adresi.</summary>
    public string? OfficialDocumentUrl { get; init; }

    /// <summary>
    /// Çağrı sürekli açıksa son başvuru tarihi <b>eksik değil, geçersizdir</b>;
    /// worker bunu bildirirse alan NotApplicable olarak işaretlenir.
    /// </summary>
    public bool IsContinuouslyOpen { get; init; }
}

/// <summary>
/// Ayrıştırıcıdan gelen tek bir koşul.
///
/// <para>
/// <c>StartOffset</c>/<c>EndOffset</c> koşulun belge metnindeki yeridir. Worker kanıt
/// parçalarının kimliklerini bilmez — parçalar API tarafında oluşturulur — bu yüzden
/// bağlama sunucuda, karakter aralığı çakışmasıyla yapılır. Alanlar <c>null</c> ise
/// kural kanıtsız kalır; deterministik motor onu kullanmaya devam eder ama yapay zekâ
/// o kural hakkında resmî kaynağa dayalı iddia üretemez.
/// </para>
/// </summary>
public sealed record UpsertRuleDto(
    string Field,
    RuleOperator Operator,
    string Value,
    RuleDimension Dimension,
    RuleSeverity Severity,
    string HumanReadable,
    string? SourceExcerpt,
    decimal Confidence,
    int? StartOffset = null,
    int? EndOffset = null);

/// <summary>Danışmanın tek bir kuralı elle düzeltmesi (istisna yönetimi).</summary>
public sealed record OverrideRuleRequest(
    RuleOperator Operator,
    string Value,
    RuleSeverity Severity,
    string HumanReadable);

public sealed record UpsertBudgetItemDto(
    BudgetItemType Type,
    decimal Amount,
    string Currency,
    string? Excerpt,
    int StartOffset,
    int EndOffset);

public sealed record UpsertBudgetRateDto(
    BudgetRateType Type,
    decimal Rate,
    string? Excerpt,
    int StartOffset,
    int EndOffset);
