using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Analysis;

namespace GovAI.Application.Analysis;

/// <summary>
/// Domain analiz sonuçlarını ekran sözleşmesine çevirir (Faz 3).
///
/// <para>
/// Tek yerde tutulur çünkü iki analiz türü aynı kriter, güven ve katkı bloklarını
/// paylaşıyor. İki ayrı eşleyici yazılsaydı biri güncellenir, diğeri unutulur ve
/// mevzuat ekranı fırsat ekranından farklı bir "Orta güven" gösterirdi.
/// </para>
/// </summary>
public static class AnalysisMapper
{
    public static OpportunityAnalysisDto ToDto(
        CompanyOpportunityAnalysis analysis,
        string title,
        MergedAnalysis merged,
        AnalysisRun run) =>
        new(
            analysis.CompanyId,
            analysis.OpportunityId,
            title,
            analysis.EvaluatedAt,
            analysis.Verdict,
            AnalysisLabels.Of(analysis.Verdict),
            analysis.SectorFit,
            new ExplainableScoreDto(
                analysis.Score.Value,
                AnalysisLabels.ScoreLabel,
                analysis.Score.Components
                    .Select(c => new ScoreComponentDto(
                        c.Group, c.Name, c.Value, c.Weight, c.Contribution,
                        c.CriterionCount, c.MetCount, c.NotMetCount, c.UnknownCount, c.ConflictCount, c.Rationale))
                    .ToList(),
                analysis.Score.HasMandatoryFailure,
                analysis.Score.MissingDataEffect,
                analysis.Score.RuleSetVersion),
            ToDto(analysis.Confidence, merged.HasAiContribution),
            analysis.Criteria.Select(ToDto).ToList(),
            analysis.Met.Select(ToDto).ToList(),
            analysis.NotMet.Select(ToDto).ToList(),
            analysis.Missing.Select(ToDto).ToList(),
            analysis.Conflicting.Select(ToDto).ToList(),
            analysis.Score.RuleSetVersion,
            ToDto(merged, analysis.Confidence.Level),
            ToDto(run));

    public static RegulationImpactDto ToDto(
        CompanyRegulationImpact impact,
        string title,
        MergedAnalysis merged,
        AnalysisRun run) =>
        new(
            impact.CompanyId,
            impact.RegulatoryChangeId,
            title,
            impact.EvaluatedAt,
            impact.Impact,
            AnalysisLabels.Of(impact.Impact),
            ToDto(impact.Confidence, merged.HasAiContribution),
            impact.Criteria.Select(ToDto).ToList(),
            impact.OpenQuestions,
            CompanyRegulationImpact.LegalDisclaimer,
            AnalysisRuleSet.Current.Version,
            ToDto(merged, impact.Confidence.Level),
            ToDto(run));

    /// <summary>
    /// Katkı bloğu.
    ///
    /// <para>
    /// Uyarı metni iki durumda doluyor: model çalışmadıysa ("kural tabanlı sonuç") ve
    /// güven düşükse. Düşük güvenli bir analiz kesin sonuç gibi görünmemeli — sayının
    /// yanında bunu söyleyen bir cümle olmadan kullanıcı farkı anlamaz.
    /// </para>
    /// </summary>
    public static AnalysisContributionDto ToDto(MergedAnalysis merged, ConfidenceLevel level)
    {
        var uyarilar = new List<string>();

        if (!merged.HasAiContribution)
        {
            uyarilar.Add(AnalysisAiStatusDescriptions.RulesOnlyWarning);
        }

        if (level == ConfidenceLevel.Low)
        {
            uyarilar.Add("Güven seviyesi düşük: eksik bilgi veya doğrulanmamış kaynak nedeniyle "
                         + "bu sonuç kesin kabul edilmemelidir.");
        }

        return new AnalysisContributionDto(
            merged.HasAiContribution,
            merged.AiStatus,
            AnalysisLabels.Of(merged.AiStatus),
            merged.AiExplanations,
            merged.RejectedClaims.Count,
            merged.Conflicts
                .Select(c => new RuleAiConflictDto(c.CriterionCode, c.RuleOutcome, c.ClaimType, c.Note))
                .ToList(),
            uyarilar.Count == 0 ? null : string.Join(" ", uyarilar),
            merged.HasAiContribution ? AnalysisLabels.HybridModeLabel : AnalysisLabels.RulesOnlyModeLabel,
            // Model çalışmadıysa sayı DEĞİL, "Kullanılamıyor" yazar.
            merged.HasAiContribution && merged.AiEvidenceAgreement is { } uyum
                ? $"{uyum:P0}"
                : AnalysisLabels.AiConfidenceUnavailable);
    }

    public static AnalysisVersionDto ToDto(AnalysisRun run) =>
        new(
            run.Id,
            run.CompanyProfileVersion,
            run.FinancialDataVersion,
            run.RuleSetVersion,
            run.PromptVersion,
            run.ModelProvider,
            run.ModelName,
            run.OutputSchemaVersion,
            run.CorrelationId,
            run.StartedAt,
            run.CompletedAt,
            run.Status);

    /// <summary>
    /// Güven göstergesi. Başlık, modelin gerçekten katkı verip vermediğine göre
    /// değişir: model çalışmadıysa bu sayı <b>kural tabanlı güvendir</b>.
    /// </summary>
    public static ConfidenceDto ToDto(ConfidenceAssessment confidence, bool hasAiContribution = false) =>
        new(
            confidence.Value,
            confidence.Level,
            confidence.LevelLabel,
            confidence.Factors
                .Select(f => new ConfidenceFactorDto(f.Code, f.Name, f.Value, f.Weight, f.Explanation, f.NotMeasured))
                .ToList(),
            confidence.RuleSetVersion,
            hasAiContribution
                ? AnalysisLabels.HybridConfidenceTitle
                : AnalysisLabels.RulesOnlyConfidenceTitle);

    public static CriterionResultDto ToDto(CriterionResult criterion) =>
        new(
            criterion.Code,
            criterion.Name,
            criterion.IsMandatory,
            criterion.Outcome,
            AnalysisLabels.Of(criterion.Outcome),
            criterion.Rationale,
            criterion.CompanyFields,
            criterion.Evidence
                .Select(e => new CriterionEvidenceDto(e.EvidenceChunkId, e.DocumentVersionId, e.Excerpt, e.Locator))
                .ToList(),
            criterion.ScoreImpact,
            criterion.MissingOrConflictExplanation,
            criterion.RuleSetVersion,
            criterion.Group,
            ScoreCalculator.NameOf(criterion.Group));
}
