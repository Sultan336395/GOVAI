using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Analysis;
using GovAI.Domain.Common;

namespace GovAI.Application.Analysis;

/// <summary>
/// Kural tabanlı DeepTech analizinin orkestrasyonu (Faz 3 – Aşama 1).
///
/// <para>
/// Kararı domain verir; bu servis yalnızca veriyi toplar, kiracı sınırını uygular ve
/// sonucu ekran sözleşmesine çevirir. Yapay zekâ katkısı bu aşamada <b>yoktur</b> ve
/// çıktıda <c>HasAiContribution=false</c> olarak açıkça bildirilir — sistem model
/// bağlanmadan "hibrit çalışıyor" demez.
/// </para>
///
/// <para>
/// Karantinadaki kayıtlar analiz edilmez: içeriğine güvenilmeyen bir belgeden çıkarılan
/// sonuç kullanıcıyı yanlış yönlendirir.
/// </para>
/// </summary>
public sealed class CompanyAnalysisService(
    ICompanyRepository companies,
    IOpportunityRepository opportunities,
    IRegulatoryChangeRepository regulatoryChanges,
    CompanyAccessGuard access,
    IDateTimeProvider clock)
{
    public async Task<OpportunityAnalysisDto> AnalyzeOpportunityAsync(
        Guid companyId,
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureAccessAsync(companyId, cancellationToken: cancellationToken);

        var company = await companies.GetWithDetailsAsync(companyId, cancellationToken)
                      ?? throw new NotFoundException("Firma", companyId);

        var opportunity = await opportunities.GetWithRulesAsync(opportunityId, cancellationToken)
                          ?? throw new NotFoundException("Fırsat", opportunityId);

        if (!opportunity.IsPublishable)
        {
            throw new ValidationException(
                "opportunityId",
                "Karantinadaki bir çağrı analiz edilemez. Kayıt önce incelenip karantinadan çıkarılmalıdır.");
        }

        var analysis = OpportunityCriteriaEvaluator.Evaluate(company, opportunity, clock.UtcNow);

        return Map(analysis, opportunity.Title);
    }

    public async Task<RegulationImpactDto> AnalyzeRegulationAsync(
        Guid companyId,
        Guid regulatoryChangeId,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureAccessAsync(companyId, cancellationToken: cancellationToken);

        var company = await companies.GetWithDetailsAsync(companyId, cancellationToken)
                      ?? throw new NotFoundException("Firma", companyId);

        var change = await regulatoryChanges.GetAsync(regulatoryChangeId, cancellationToken)
                     ?? throw new NotFoundException("Mevzuat değişikliği", regulatoryChangeId);

        if (change.Status == RegulatoryChangeStatus.Quarantined)
        {
            throw new ValidationException(
                "regulatoryChangeId",
                "Karantinadaki bir mevzuat kaydı için etki analizi üretilmez. "
                + "Kayıt önce resmî kaynağa karşı doğrulanmalıdır.");
        }

        var chunks = await regulatoryChanges.ListEvidenceAsync(change.DocumentVersionId, cancellationToken);

        var evidence = chunks
            .Select(c => new RegulationEvidence
            {
                EvidenceChunkId = c.Id,
                DocumentVersionId = c.DocumentVersionId,
                Excerpt = c.Text,
                Locator = c.SectionTitle ?? $"Paragraf {c.SequenceNumber}"
            })
            .ToList();

        var impact = RegulationImpactEvaluator.Evaluate(company, change, evidence, clock.UtcNow);

        return new RegulationImpactDto(
            impact.CompanyId,
            impact.RegulatoryChangeId,
            change.Title,
            impact.EvaluatedAt,
            impact.Impact,
            AnalysisLabels.Of(impact.Impact),
            Map(impact.Confidence),
            impact.Criteria.Select(Map).ToList(),
            impact.OpenQuestions,
            CompanyRegulationImpact.LegalDisclaimer,
            AnalysisRuleSet.Current.Version,
            HasAiContribution: false);
    }

    private static OpportunityAnalysisDto Map(CompanyOpportunityAnalysis analysis, string title) =>
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
            Map(analysis.Confidence),
            analysis.Criteria.Select(Map).ToList(),
            analysis.Met.Select(Map).ToList(),
            analysis.NotMet.Select(Map).ToList(),
            analysis.Missing.Select(Map).ToList(),
            analysis.Conflicting.Select(Map).ToList(),
            analysis.Score.RuleSetVersion,
            HasAiContribution: false);

    private static ConfidenceDto Map(ConfidenceAssessment confidence) =>
        new(
            confidence.Value,
            confidence.Level,
            confidence.LevelLabel,
            confidence.Factors
                .Select(f => new ConfidenceFactorDto(f.Code, f.Name, f.Value, f.Weight, f.Explanation, f.NotMeasured))
                .ToList(),
            confidence.RuleSetVersion);

    private static CriterionResultDto Map(CriterionResult criterion) =>
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
