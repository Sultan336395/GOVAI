using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Calibration;
using GovAI.Domain.Common;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Calibration;

/// <summary>Danışmanın bir değerlendirmeye verdiği karar.</summary>
public sealed record RecordExpertVerdictRequest
{
    public required Guid AssessmentId { get; init; }

    public required EligibilityVerdict ExpertOpinion { get; init; }

    /// <summary>Sistemden farklı karar verildiyse zorunludur.</summary>
    public VerdictDisagreementReason DisagreementReason { get; init; } = VerdictDisagreementReason.None;

    public string? Note { get; init; }
}

public sealed record ExpertVerdictDto(
    Guid Id,
    Guid AssessmentId,
    Guid OpportunityId,
    string OpportunityTitle,
    EligibilityVerdict SystemVerdict,
    decimal SystemScore,
    bool SystemHadDataGap,
    EligibilityVerdict ExpertOpinion,
    VerdictDisagreementReason DisagreementReason,
    string? Note,
    DateTimeOffset RecordedAt,
    string RecordedBy,
    bool Agrees,
    bool IsFalsePositive,
    bool IsFalseNegative);

/// <summary>Kalibrasyon raporu ve kural düzeltme oranı.</summary>
public sealed record CalibrationReportDto(
    CalibrationSummary Summary,
    /// <summary>
    /// Danışmanın elle düzelttiği kural oranı — kural çıkarımının başarısının dolaylı ölçüsü.
    /// </summary>
    RuleExtractionQualityDto RuleExtraction);

public sealed record RuleExtractionQualityDto(
    int TotalRules,
    int ManuallyOverriddenRules,
    decimal OverrideRate);

/// <summary>
/// Uzman değerlendirmesi ve kalibrasyon.
///
/// <para>
/// Bu servis karar üretmez, <b>karar mekanizmasını ölçer</b>. Uzman görüşü skoru
/// değiştirmez; motor deterministik kalır ve uzman kaydı yalnızca ağırlıkların
/// doğruluğunu sınamak için kullanılır (bkz. <see cref="CalibrationReport"/>).
/// </para>
/// </summary>
public sealed class CalibrationService(
    IExpertVerdictRepository verdicts,
    IAssessmentRepository assessments,
    IOpportunityRepository opportunities,
    IUnitOfWork unitOfWork,
    CompanyAccessGuard access,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<CalibrationService> logger)
{
    /// <summary>
    /// Uzman kararını kaydeder. Aynı değerlendirme için ikinci kayıt açılmaz, mevcut
    /// kayıt güncellenir — aksi hâlde tek vaka kalibrasyon sayımına iki kez girerdi.
    /// </summary>
    public async Task<ExpertVerdictDto> RecordAsync(
        RecordExpertVerdictRequest request,
        CancellationToken cancellationToken = default)
    {
        var assessment = await assessments.GetAsync(request.AssessmentId, cancellationToken)
            ?? throw new NotFoundException("Değerlendirme", request.AssessmentId);

        // Yetki firma üyeliğinden geçer; kalibrasyon kaydı da müşteri verisidir.
        await access.LoadAccessibleAsync(assessment.CompanyId, CompanyPermission.Operate, cancellationToken);

        var opportunity = await opportunities.GetWithRulesAsync(assessment.OpportunityId, cancellationToken)
            ?? throw new NotFoundException("Fırsat", assessment.OpportunityId);

        var kim = currentUser.Email ?? currentUser.UserId?.ToString() ?? "bilinmiyor";
        var now = clock.UtcNow;

        var mevcut = await verdicts.GetByAssessmentAsync(request.AssessmentId, cancellationToken);

        if (mevcut is not null)
        {
            mevcut.Update(request.ExpertOpinion, request.DisagreementReason, request.Note, now, kim);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(mevcut, opportunity.Title);
        }

        var verdict = new ExpertVerdict(
            access.RequireTenant(),
            assessment.CompanyId,
            assessment.OpportunityId,
            assessment.Id,
            assessment.Verdict,
            assessment.FinalScore,
            assessment.DataGapCount > 0,
            request.ExpertOpinion,
            request.DisagreementReason,
            request.Note,
            now,
            kim);

        await verdicts.AddAsync(verdict, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Uzman değerlendirmesi kaydedildi. AssessmentId={AssessmentId} Sistem={System} Uzman={Expert}",
            assessment.Id, assessment.Verdict, request.ExpertOpinion);

        return ToDto(verdict, opportunity.Title);
    }

    public async Task<IReadOnlyList<ExpertVerdictDto>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        await access.LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);

        return await verdicts.ListForCompanyAsync(companyId, cancellationToken);
    }

    /// <summary>
    /// Kalibrasyon raporu.
    ///
    /// <para>
    /// Firma verilmezse kiracının tamamı ölçülür: ağırlık kalibrasyonu tek firmanın
    /// verisiyle yapılamaz, örneklem ancak birden çok firmada anlamlı büyüklüğe ulaşır.
    /// </para>
    /// </summary>
    public async Task<CalibrationReportDto> GetReportAsync(
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();

        if (companyId is { } id)
        {
            await access.LoadAccessibleAsync(id, CompanyPermission.Read, cancellationToken);
        }

        var kayitlar = await verdicts.ListForCalibrationAsync(companyId, cancellationToken);
        var kural = await verdicts.GetRuleExtractionQualityAsync(cancellationToken);

        logger.LogInformation(
            "Kalibrasyon raporu üretildi. TenantId={TenantId} CompanyId={CompanyId} Kayıt={Count}",
            tenantId, companyId, kayitlar.Count);

        return new CalibrationReportDto(CalibrationReport.Build(kayitlar), kural);
    }

    private static ExpertVerdictDto ToDto(ExpertVerdict verdict, string opportunityTitle) => new(
        verdict.Id,
        verdict.AssessmentId,
        verdict.OpportunityId,
        opportunityTitle,
        verdict.SystemVerdict,
        verdict.SystemScore,
        verdict.SystemHadDataGap,
        verdict.ExpertOpinion,
        verdict.DisagreementReason,
        verdict.Note,
        verdict.RecordedAt,
        verdict.RecordedBy,
        verdict.Agrees,
        verdict.IsFalsePositive,
        verdict.IsFalseNegative);
}
