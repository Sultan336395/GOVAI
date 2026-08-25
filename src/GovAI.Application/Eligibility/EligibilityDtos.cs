using GovAI.Domain.Common;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Scoring;

namespace GovAI.Application.Eligibility;

/// <summary>Öncelik listesindeki tek satır — dashboard'ın ana veri birimi.</summary>
public sealed record OpportunityMatchDto(
    Guid AssessmentId,
    Guid OpportunityId,
    string OpportunityTitle,
    string Publisher,
    SupportCategory SupportCategory,
    DateTimeOffset? Deadline,
    int? DaysUntilDeadline,
    decimal FinalScore,
    decimal Confidence,
    EligibilityVerdict Verdict,
    int MissingConditionCount,
    int MissingMandatoryDocumentCount,
    int DataGapCount,
    decimal? MaxAmount,
    string? ExecutiveSummary,
    DateTimeOffset EvaluatedAt);

/// <summary>Tek bir değerlendirmenin tam açıklaması — "neden uygun / neden değil" ekranı.</summary>
public sealed record EligibilityDetailDto(
    Guid AssessmentId,
    Guid CompanyId,
    string CompanyName,
    Guid OpportunityId,
    string OpportunityTitle,
    string Publisher,
    string? SourceUrl,
    DateTimeOffset? Deadline,
    EligibilityVerdict Verdict,
    decimal FinalScore,
    decimal Confidence,
    bool HasBlockingFailure,
    DateTimeOffset EvaluatedAt,
    int CompanyProfileVersion,
    IReadOnlyList<DimensionScoreDto> Dimensions,
    IReadOnlyList<RuleEvaluationDto> BlockingFailures,
    IReadOnlyList<RuleEvaluationDto> MissingConditions,
    IReadOnlyList<RuleEvaluationDto> SatisfiedConditions,
    IReadOnlyList<RuleEvaluationDto> DataGaps,
    IReadOnlyList<DocumentCheckDto> DocumentChecklist,
    string? ExecutiveSummary);

public sealed record DimensionScoreDto(
    RuleDimension Dimension,
    string DimensionLabel,
    decimal Value,
    decimal Weight,
    decimal Contribution,
    int EvaluatedRuleCount,
    int UnknownRuleCount,
    string Rationale);

public sealed record RuleEvaluationDto(
    string Field,
    RuleDimension Dimension,
    RuleSeverity Severity,
    RuleOutcome Outcome,
    string Requirement,
    string ActualValue,
    string ExpectedValue,
    decimal Strength,
    string? SourceExcerpt,
    string? SuggestedAction);

public sealed record DocumentCheckDto(
    string Code,
    string Name,
    bool IsMandatory,
    DocumentStatus Status,
    DateOnly? ValidUntil,
    string? IssuingAuthority,
    string? Action);

/// <summary>Toplu yeniden hesaplama sonucu.</summary>
/// <summary>Toplu yeniden skorlama sonucu. Şirket verisi DÖNMEZ; yalnızca sayılar.</summary>
public sealed record RescoreBatchResult(
    int CompanyCount,
    int EvaluatedOpportunityCount,
    int EligibleCount,
    int FailedCompanyCount);

/// <summary>Bir fırsatın değerlendirmelerinin geçersiz kılınma sonucu.</summary>
public sealed record InvalidateResult(Guid OpportunityId, int SupersededAssessmentCount);

public sealed record RescoreResult(
    Guid CompanyId,
    int EvaluatedOpportunityCount,
    int EligibleCount,
    int ConditionallyEligibleCount,
    int NotEligibleCount,
    decimal AverageScore,
    TimeSpan Duration);

/// <summary>Boyut adlarının Türkçe karşılıkları; rapor ve arayüzde tek yerden yönetilir.</summary>
public static class DimensionLabels
{
    private static readonly Dictionary<RuleDimension, string> Labels = new()
    {
        [RuleDimension.Sector] = "Sektörel eşleşme",
        [RuleDimension.Financial] = "Mali yeterlilik",
        [RuleDimension.Employment] = "Personel yapısı",
        [RuleDimension.Documentation] = "Belge hazır olma",
        [RuleDimension.Region] = "Bölgesel uygunluk",
        [RuleDimension.TechnicalQualification] = "Teknik yeterlilik",
        [RuleDimension.Timing] = "Başvuru takvimi"
    };

    public static string Of(RuleDimension dimension) =>
        Labels.TryGetValue(dimension, out var label) ? label : dimension.ToString();

    public static string Describe(DimensionScore score) =>
        $"{Of(score.Dimension)}: {score.Value:P0} (ağırlık {score.Weight:P0}, katkı {score.Contribution:P1}) — {score.Rationale}";
}
