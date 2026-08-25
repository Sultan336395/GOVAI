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
    int DocumentCount);

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
    decimal RuleExtractionConfidence,
    bool IsReviewedByConsultant,
    IReadOnlyList<OpportunityRuleDto> Rules,
    IReadOnlyList<DocumentRequirementDto> DocumentChecklist,
    /// <summary>
    /// Hangi alanın neden boş olduğu. Arayüz <c>null</c> yerine buna bakarak
    /// "Resmî kaynakta belirtilmemiş" gösterir.
    /// </summary>
    FieldAvailabilityDto FieldAvailability);

public sealed record BudgetDto(decimal? MinAmount, decimal? MaxAmount, string Currency, decimal? SupportRate);

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
    bool IsManuallyOverridden);

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

public sealed record UpsertRuleDto(
    string Field,
    RuleOperator Operator,
    string Value,
    RuleDimension Dimension,
    RuleSeverity Severity,
    string HumanReadable,
    string? SourceExcerpt,
    decimal Confidence);

/// <summary>Danışmanın tek bir kuralı elle düzeltmesi (istisna yönetimi).</summary>
public sealed record OverrideRuleRequest(
    RuleOperator Operator,
    string Value,
    RuleSeverity Severity,
    string HumanReadable);
