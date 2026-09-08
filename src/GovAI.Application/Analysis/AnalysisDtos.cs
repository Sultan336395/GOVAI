using GovAI.Domain.Analysis;
using GovAI.Domain.Common;

namespace GovAI.Application.Analysis;

/// <summary>Tek bir kriterin ekrana taşınan hâli.</summary>
public sealed record CriterionResultDto(
    string Code,
    string Name,
    bool IsMandatory,
    CriterionOutcome Outcome,
    string OutcomeLabel,
    string Rationale,
    IReadOnlyList<string> CompanyFields,
    IReadOnlyList<CriterionEvidenceDto> Evidence,
    decimal ScoreImpact,
    string? MissingOrConflictExplanation,
    string RuleSetVersion,
    ScoreGroup Group,
    string GroupName);

public sealed record CriterionEvidenceDto(
    Guid? EvidenceChunkId,
    Guid? DocumentVersionId,
    string Excerpt,
    string? Locator);

public sealed record ScoreComponentDto(
    ScoreGroup Group,
    string Name,
    decimal Value,
    decimal Weight,
    decimal Contribution,
    int CriterionCount,
    int MetCount,
    int NotMetCount,
    int UnknownCount,
    int ConflictCount,
    string Rationale);

/// <summary>
/// Açıklanabilir puan. <c>Label</c> alanı bilinçli olarak "uygunluk puanı"dır;
/// hiçbir yerde "kazanma ihtimali" yazmaz.
/// </summary>
public sealed record ExplainableScoreDto(
    decimal Value,
    string Label,
    IReadOnlyList<ScoreComponentDto> Components,
    bool HasMandatoryFailure,
    decimal MissingDataEffect,
    string RuleSetVersion);

public sealed record ConfidenceFactorDto(
    string Code,
    string Name,
    decimal Value,
    decimal Weight,
    string Explanation,
    bool NotMeasured);

public sealed record ConfidenceDto(
    decimal Value,
    ConfidenceLevel Level,
    string LevelLabel,
    IReadOnlyList<ConfidenceFactorDto> Factors,
    string RuleSetVersion);

/// <summary>Şirket–fırsat analizinin ekran sözleşmesi.</summary>
public sealed record OpportunityAnalysisDto(
    Guid CompanyId,
    Guid OpportunityId,
    string OpportunityTitle,
    DateTimeOffset EvaluatedAt,
    EligibilityVerdict Verdict,
    string VerdictLabel,
    SectorFit SectorFit,
    ExplainableScoreDto Score,
    ConfidenceDto Confidence,
    IReadOnlyList<CriterionResultDto> Criteria,
    IReadOnlyList<CriterionResultDto> Met,
    IReadOnlyList<CriterionResultDto> NotMet,
    IReadOnlyList<CriterionResultDto> Missing,
    IReadOnlyList<CriterionResultDto> Conflicting,
    string RuleSetVersion,
    // Yapay zekâ katkısı var mı? Yoksa arayüz "kural tabanlı sonuç" uyarısı gösterir.
    bool HasAiContribution);

/// <summary>Şirket–mevzuat etki analizinin ekran sözleşmesi.</summary>
public sealed record RegulationImpactDto(
    Guid CompanyId,
    Guid RegulatoryChangeId,
    string RegulationTitle,
    DateTimeOffset EvaluatedAt,
    RegulationImpact Impact,
    string ImpactLabel,
    ConfidenceDto Confidence,
    IReadOnlyList<CriterionResultDto> Criteria,
    IReadOnlyList<string> OpenQuestions,
    string LegalDisclaimer,
    string RuleSetVersion,
    bool HasAiContribution);

/// <summary>Kriter ve etki sonuçlarının Türkçe karşılıkları.</summary>
public static class AnalysisLabels
{
    public static string Of(CriterionOutcome outcome) => outcome switch
    {
        CriterionOutcome.Met => "Sağlanıyor",
        CriterionOutcome.NotMet => "Sağlanmıyor",
        CriterionOutcome.Unknown => "Bilgi eksik",
        CriterionOutcome.NotApplicable => "Bu çağrı için geçerli değil",
        _ => "Belgede çelişki var"
    };

    public static string Of(RegulationImpact impact) => impact switch
    {
        RegulationImpact.Applicable => "Firmayı kapsıyor",
        RegulationImpact.NotApplicable => "Firmayı kapsamıyor",
        RegulationImpact.PotentiallyApplicable => "Kapsaması muhtemel",
        _ => "Kapsam belirlenemedi"
    };

    public static string Of(EligibilityVerdict verdict) => verdict switch
    {
        EligibilityVerdict.Eligible => "Uygun",
        EligibilityVerdict.ConditionallyEligible => "Şartlı uygun",
        EligibilityVerdict.NotEligible => "Uygun değil",
        _ => "Belirsiz"
    };

    /// <summary>
    /// Puanın ekrandaki adı. Sabit tutulur ki hiçbir ekran bunu "kazanma ihtimali"
    /// diye yazamasın; sistemde o tahmini yapacak geçmiş başvuru sonucu verisi yoktur.
    /// </summary>
    public const string ScoreLabel = "Uygunluk puanı";
}
