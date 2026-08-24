using System.Diagnostics;
using System.Text.Json;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Assessments;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Notifications;
using GovAI.Domain.Opportunities;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Eligibility;

/// <summary>
/// Uygunluk analizi ve skorlama akışının orkestrasyonu (Modül 5 + 6 + 8).
///
/// Kararın kendisi <see cref="EligibilityEngine"/> içindeki saf domain mantığında verilir.
/// Bu servis yalnızca veriyi toplar, sonucu kalıcılaştırır, bildirim üretir ve
/// isteğe bağlı olarak AI'dan yönetici özeti ister.
/// </summary>
public sealed class EligibilityService(
    ICompanyRepository companies,
    IOpportunityRepository opportunities,
    IAssessmentRepository assessments,
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    IAiExplanationClient ai,
    ILogger<EligibilityService> logger)
{
    private static readonly JsonSerializerOptions DetailJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Tek bir firma–çağrı çiftini değerlendirir ve sonucu kalıcılaştırır.
    /// </summary>
    public async Task<EligibilityDetailDto> EvaluateAsync(
        Guid companyId,
        Guid opportunityId,
        bool generateSummary = false,
        CancellationToken cancellationToken = default)
    {
        var company = await LoadCompanyAsync(companyId, cancellationToken);
        var opportunity = await opportunities.GetWithRulesAsync(opportunityId, cancellationToken)
                          ?? throw new NotFoundException("Fırsat", opportunityId);

        var assessment = await EvaluateAndPersistAsync(company, opportunity, cancellationToken);

        if (generateSummary)
        {
            await AttachSummaryAsync(assessment, company, opportunity, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDetail(assessment, company, opportunity);
    }

    /// <summary>
    /// Firmanın tüm açık fırsatlara karşı yeniden skorlanması.
    /// Profil değiştiğinde veya yeni çağrı geldiğinde worker tarafından tetiklenir.
    /// </summary>
    public async Task<RescoreResult> RescoreCompanyAsync(
        Guid companyId,
        IReadOnlyCollection<SupportCategory>? categories = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var company = await LoadCompanyAsync(companyId, cancellationToken);
        var now = clock.UtcNow;

        var openOpportunities = await opportunities.ListForEvaluationAsync(now, categories, cancellationToken);

        var results = new List<EligibilityAssessment>(openOpportunities.Count);
        foreach (var opportunity in openOpportunities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await EvaluateAndPersistAsync(company, opportunity, cancellationToken));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        stopwatch.Stop();

        var eligible = results.Count(r => r.Verdict == EligibilityVerdict.Eligible);
        var conditional = results.Count(r => r.Verdict == EligibilityVerdict.ConditionallyEligible);
        var notEligible = results.Count(r => r.Verdict == EligibilityVerdict.NotEligible);
        var average = results.Count == 0 ? 0m : Math.Round(results.Average(r => r.FinalScore), 2);

        logger.LogInformation(
            "Yeniden skorlama tamamlandı. CompanyId={CompanyId} Fırsat={Count} Uygun={Eligible} Süre={Duration}ms",
            companyId, results.Count, eligible, stopwatch.ElapsedMilliseconds);

        return new RescoreResult(companyId, results.Count, eligible, conditional, notEligible, average, stopwatch.Elapsed);
    }

    /// <summary>Firmanın önceliklendirilmiş fırsat listesi (dashboard ana tablosu).</summary>
    public async Task<PagedResult<OpportunityMatchDto>> ListMatchesAsync(
        AssessmentQuery query,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAccessAsync(query.CompanyId, cancellationToken);

        var page = await assessments.ListLatestForCompanyAsync(query, cancellationToken);
        var now = clock.UtcNow;

        var items = new List<OpportunityMatchDto>(page.Items.Count);
        foreach (var assessment in page.Items)
        {
            var opportunity = await opportunities.GetWithRulesAsync(assessment.OpportunityId, cancellationToken);
            if (opportunity is null)
            {
                continue;
            }

            items.Add(new OpportunityMatchDto(
                assessment.Id,
                opportunity.Id,
                opportunity.Title,
                opportunity.Publisher,
                opportunity.SupportCategory,
                opportunity.Deadline,
                opportunity.DaysUntilDeadline(now),
                assessment.FinalScore,
                assessment.Confidence,
                assessment.Verdict,
                assessment.MissingConditionCount,
                assessment.MissingMandatoryDocumentCount,
                assessment.DataGapCount,
                opportunity.Budget?.MaxAmount,
                assessment.ExecutiveSummary,
                assessment.EvaluatedAt));
        }

        return new PagedResult<OpportunityMatchDto>(items, page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<EligibilityDetailDto> GetDetailAsync(Guid assessmentId, CancellationToken cancellationToken = default)
    {
        var assessment = await assessments.GetAsync(assessmentId, cancellationToken)
                         ?? throw new NotFoundException("Değerlendirme", assessmentId);

        await EnsureCompanyAccessAsync(assessment.CompanyId, cancellationToken);

        var company = await companies.GetWithDetailsAsync(assessment.CompanyId, cancellationToken)
                      ?? throw new NotFoundException("Firma", assessment.CompanyId);

        var opportunity = await opportunities.GetWithRulesAsync(assessment.OpportunityId, cancellationToken)
                          ?? throw new NotFoundException("Fırsat", assessment.OpportunityId);

        return ToDetail(assessment, company, opportunity);
    }

    /// <summary>Kaydedilmiş bir değerlendirme için AI yönetici özeti üretir.</summary>
    public async Task<string> GenerateSummaryAsync(Guid assessmentId, CancellationToken cancellationToken = default)
    {
        var assessment = await assessments.GetAsync(assessmentId, cancellationToken)
                         ?? throw new NotFoundException("Değerlendirme", assessmentId);

        await EnsureCompanyAccessAsync(assessment.CompanyId, cancellationToken);

        var company = await companies.GetWithDetailsAsync(assessment.CompanyId, cancellationToken)
                      ?? throw new NotFoundException("Firma", assessment.CompanyId);

        var opportunity = await opportunities.GetWithRulesAsync(assessment.OpportunityId, cancellationToken)
                          ?? throw new NotFoundException("Fırsat", assessment.OpportunityId);

        await AttachSummaryAsync(assessment, company, opportunity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return assessment.ExecutiveSummary ?? string.Empty;
    }

    private async Task<EligibilityAssessment> EvaluateAndPersistAsync(
        Company company,
        Opportunity opportunity,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var outcome = EligibilityEngine.Evaluate(company, opportunity, now);

        var previous = await assessments.GetLatestAsync(company.Id, opportunity.Id, cancellationToken);
        await assessments.SupersedePreviousAsync(company.Id, opportunity.Id, cancellationToken);

        var detailJson = JsonSerializer.Serialize(
            new
            {
                outcome.RuleEvaluations,
                outcome.DocumentChecklist,
                Dimensions = outcome.Score.Dimensions,
                outcome.Score.Weights
            },
            DetailJsonOptions);

        var assessment = new EligibilityAssessment(company.TenantId, outcome, company.ProfileVersion, detailJson);
        await assessments.AddAsync(assessment, cancellationToken);

        await RaiseNotificationsAsync(company, opportunity, assessment, previous, now, cancellationToken);

        return assessment;
    }

    /// <summary>
    /// Yeni eşleşme, skor değişimi ve yaklaşan son tarih bildirimlerini üretir (Modül 10).
    /// Tekilleştirme anahtarı sayesinde aynı uyarı tekrar gönderilmez.
    /// </summary>
    private async Task RaiseNotificationsAsync(
        Company company,
        Opportunity opportunity,
        EligibilityAssessment current,
        EligibilityAssessment? previous,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const decimal newMatchScoreThreshold = 60m;
        const decimal significantScoreDelta = 10m;

        if (previous is null && current.FinalScore >= newMatchScoreThreshold && current.Verdict != EligibilityVerdict.NotEligible)
        {
            await AddNotificationAsync(
                company,
                opportunity,
                NotificationKind.NewMatch,
                $"Yeni uygun fırsat: {opportunity.Title}",
                $"{company.LegalName} için {opportunity.Publisher} çağrısı %{current.FinalScore:0} uygunlukla eşleşti.",
                $"match:{opportunity.Id}:{company.Id}",
                now,
                cancellationToken);
        }
        else if (previous is not null && Math.Abs(current.FinalScore - previous.FinalScore) >= significantScoreDelta)
        {
            var direction = current.FinalScore > previous.FinalScore ? "yükseldi" : "düştü";
            await AddNotificationAsync(
                company,
                opportunity,
                NotificationKind.ScoreChanged,
                $"Uygunluk skoru {direction}: {opportunity.Title}",
                $"Skor %{previous.FinalScore:0} → %{current.FinalScore:0} olarak güncellendi.",
                $"score:{opportunity.Id}:{company.Id}:{current.FinalScore:0}",
                now,
                cancellationToken);
        }

        var daysLeft = opportunity.DaysUntilDeadline(now);
        if (daysLeft is > 0 and <= 15 && current.Verdict != EligibilityVerdict.NotEligible)
        {
            await AddNotificationAsync(
                company,
                opportunity,
                NotificationKind.DeadlineApproaching,
                $"Son başvuruya {daysLeft} gün: {opportunity.Title}",
                current.MissingMandatoryDocumentCount > 0
                    ? $"{current.MissingMandatoryDocumentCount} zorunlu belge hâlâ eksik."
                    : "Belge hazırlığı tamamlanmış görünüyor.",
                $"deadline:{opportunity.Id}:{company.Id}:{(daysLeft <= 7 ? "7d" : "15d")}",
                now,
                cancellationToken);
        }
    }

    private async Task AddNotificationAsync(
        Company company,
        Opportunity opportunity,
        NotificationKind kind,
        string title,
        string body,
        string deduplicationKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await notifications.ExistsAsync(deduplicationKey, cancellationToken))
        {
            return;
        }

        var notification = new Notification(company.TenantId, company.Id, kind, title, body, now, deduplicationKey, opportunity.Id);
        await notifications.AddAsync(notification, cancellationToken);
    }

    private async Task AttachSummaryAsync(
        EligibilityAssessment assessment,
        Company company,
        Opportunity opportunity,
        CancellationToken cancellationToken)
    {
        var outcome = EligibilityEngine.Evaluate(company, opportunity, assessment.EvaluatedAt);

        try
        {
            var result = await ai.GenerateExecutiveSummaryAsync(
                new ExecutiveSummaryRequest
                {
                    CompanyName = company.LegalName,
                    OpportunityTitle = opportunity.Title,
                    Publisher = opportunity.Publisher,
                    Verdict = outcome.Verdict,
                    FinalScore = outcome.Score.FinalScore,
                    DimensionHighlights = outcome.Score.Dimensions.Select(DimensionLabels.Describe).ToList(),
                    BlockingReasons = outcome.BlockingFailures.Select(r => r.Requirement).ToList(),
                    MissingConditions = outcome.MissingConditions.Select(r => r.SuggestedAction ?? r.Requirement).ToList(),
                    MissingDocuments = outcome.DocumentChecklist
                        .Where(d => d.Status != DocumentStatus.Provided)
                        .Select(d => d.Name)
                        .ToList(),
                    Deadline = opportunity.Deadline
                },
                cancellationToken);

            assessment.AttachSummary(result.Summary, result.ModelName, clock.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // AI erişilemezse skor ve karar etkilenmez; yalnızca anlatı eksik kalır.
            logger.LogWarning(ex, "Yönetici özeti üretilemedi. AssessmentId={AssessmentId}", assessment.Id);
        }
    }

    /// <summary>
    /// Kiracı ve yetki doğrulaması merkezî kapıya devredilmiştir. Eskiden burada yalnızca
    /// <c>CanAccessCompany</c> çağrılıyor, kiracı hiç karşılaştırılmıyordu (Faz 0 / D1).
    /// </summary>
    private Task<Company> LoadCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        access.LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);

    private async Task EnsureCompanyAccessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        _ = await LoadCompanyAsync(companyId, cancellationToken);
    }

    private static EligibilityDetailDto ToDetail(EligibilityAssessment assessment, Company company, Opportunity opportunity)
    {
        var outcome = EligibilityEngine.Evaluate(company, opportunity, assessment.EvaluatedAt);

        return new EligibilityDetailDto(
            assessment.Id,
            company.Id,
            company.LegalName,
            opportunity.Id,
            opportunity.Title,
            opportunity.Publisher,
            opportunity.SourceUrl,
            opportunity.Deadline,
            outcome.Verdict,
            outcome.Score.FinalScore,
            outcome.Score.Confidence,
            outcome.Score.HasBlockingFailure,
            assessment.EvaluatedAt,
            assessment.CompanyProfileVersion,
            outcome.Score.Dimensions.Select(d => new DimensionScoreDto(
                d.Dimension,
                DimensionLabels.Of(d.Dimension),
                d.Value,
                d.Weight,
                d.Contribution,
                d.EvaluatedRuleCount,
                d.UnknownRuleCount,
                d.Rationale)).ToList(),
            outcome.BlockingFailures.Select(ToDto).ToList(),
            outcome.MissingConditions.Select(ToDto).ToList(),
            outcome.RuleEvaluations.Where(r => r.Outcome == RuleOutcome.Satisfied).Select(ToDto).ToList(),
            outcome.DataGaps.Select(ToDto).ToList(),
            outcome.DocumentChecklist.Select(d => new DocumentCheckDto(
                d.Code, d.Name, d.IsMandatory, d.Status, d.ValidUntil, d.IssuingAuthority, d.Action)).ToList(),
            assessment.ExecutiveSummary);
    }

    private static RuleEvaluationDto ToDto(RuleEvaluation evaluation) => new(
        evaluation.Field,
        evaluation.Dimension,
        evaluation.Severity,
        evaluation.Outcome,
        evaluation.Requirement,
        evaluation.ActualValue,
        evaluation.ExpectedValue,
        evaluation.Strength,
        evaluation.SourceExcerpt,
        evaluation.SuggestedAction);
}
