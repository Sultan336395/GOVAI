using GovAI.Api.Infrastructure;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Eligibility;
using GovAI.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/eligibility</c> — uygunluk analizi, eksik koşullar, belge listesi, gerekçe çıktısı.
/// </summary>
[ApiController]
[Route("api/eligibility")]
[Authorize(Policy = Policies.Read)]
[Produces("application/json")]
public sealed class EligibilityController(EligibilityService service) : ControllerBase
{
    /// <summary>Firmanın önceliklendirilmiş fırsat eşleşmeleri.</summary>
    [HttpGet("companies/{companyId:guid}/matches")]
    public async Task<ActionResult<PagedResult<OpportunityMatchDto>>> ListMatches(
        Guid companyId,
        [FromQuery] decimal? minScore,
        [FromQuery] EligibilityVerdict[]? verdicts,
        [FromQuery] SupportCategory[]? categories,
        [FromQuery] int? deadlineWithinDays,
        [FromQuery] AssessmentSort sort = AssessmentSort.ScoreDescending,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var query = new AssessmentQuery
        {
            CompanyId = companyId,
            MinScore = minScore,
            Verdicts = verdicts,
            Categories = categories,
            DeadlineWithinDays = deadlineWithinDays,
            Sort = sort,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await service.ListMatchesAsync(query, cancellationToken));
    }

    /// <summary>
    /// Tek bir değerlendirmenin tam gerekçesi: boyut kırılımı, sağlanan/sağlanmayan koşullar,
    /// veri boşlukları ve belge kontrol listesi.
    /// </summary>
    [HttpGet("{assessmentId:guid}")]
    public async Task<ActionResult<EligibilityDetailDto>> GetDetail(Guid assessmentId, CancellationToken cancellationToken) =>
        Ok(await service.GetDetailAsync(assessmentId, cancellationToken));

    /// <summary>Belirli bir firma–çağrı çiftini yeniden değerlendirir.</summary>
    [HttpPost("evaluate")]
    [Authorize(Policy = Policies.Operate)]
    [Audited("Eligibility.Evaluated", "EligibilityAssessment")]
    public async Task<ActionResult<EligibilityDetailDto>> Evaluate(
        [FromBody] EvaluateRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.EvaluateAsync(request.CompanyId, request.OpportunityId, request.GenerateSummary, cancellationToken));

    /// <summary>
    /// Firmayı tüm açık çağrılara karşı yeniden skorlar.
    /// Uzun sürebilir; büyük katalogda worker üzerinden tetiklenmesi önerilir.
    /// </summary>
    /// <summary>
    /// Kiracıdaki tüm firmaları yeniden skorlar (worker ve gece toplu turu için).
    ///
    /// Şirket verisi <b>dönmez</b>; yalnızca sayılar döner. Worker'ın müşteri verisi
    /// görmesine gerek yoktur ve görmez.
    /// </summary>
    [HttpPost("rescore-batch")]
    [Authorize(Policy = Policies.Rescore)]
    [Audited("Eligibility.RescoredBatch", "Tenant")]
    public async Task<ActionResult<RescoreBatchResult>> RescoreBatch(
        [FromQuery] SupportCategory[]? categories,
        CancellationToken cancellationToken) =>
        Ok(await service.RescoreTenantAsync(categories, cancellationToken));

    /// <summary>
    /// Bir fırsatın güncel değerlendirmelerini geçersiz kılar.
    /// Kayıtlar silinmez; yalnızca güncelliğini yitirdiği işaretlenir.
    /// </summary>
    [HttpPost("opportunities/{opportunityId:guid}/invalidate")]
    [Authorize(Policy = Policies.Rescore)]
    [Audited("Eligibility.Invalidated", "Opportunity", RouteKey = "opportunityId")]
    public async Task<ActionResult<InvalidateResult>> Invalidate(
        Guid opportunityId,
        CancellationToken cancellationToken) =>
        Ok(await service.InvalidateOpportunityAsync(opportunityId, cancellationToken));

    [HttpPost("companies/{companyId:guid}/rescore")]
    [Authorize(Policy = Policies.Rescore)]
    [Audited("Eligibility.Rescored", "Company", RouteKey = "companyId")]
    public async Task<ActionResult<RescoreResult>> Rescore(
        Guid companyId,
        [FromQuery] SupportCategory[]? categories,
        CancellationToken cancellationToken) =>
        Ok(await service.RescoreCompanyAsync(companyId, categories, cancellationToken));

    /// <summary>Kaydedilmiş bir değerlendirme için AI yönetici özeti üretir.</summary>
    [HttpPost("{assessmentId:guid}/summary")]
    [Authorize(Policy = Policies.Operate)]
    [Audited("Eligibility.SummaryGenerated", "EligibilityAssessment")]
    public async Task<ActionResult<SummaryResponse>> GenerateSummary(Guid assessmentId, CancellationToken cancellationToken)
    {
        var summary = await service.GenerateSummaryAsync(assessmentId, cancellationToken);
        return Ok(new SummaryResponse(assessmentId, summary));
    }

    public sealed record EvaluateRequest(Guid CompanyId, Guid OpportunityId, bool GenerateSummary = false);

    public sealed record SummaryResponse(Guid AssessmentId, string Summary);
}
