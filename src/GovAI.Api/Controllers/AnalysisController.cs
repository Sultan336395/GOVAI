using GovAI.Api.Infrastructure;
using GovAI.Application.Analysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/analysis</c> — DeepTech hibrit analiz (Faz 3).
///
/// <para>
/// Uygunluk analizinden farkı: sonuç <b>kriter</b> düzeyinde açıklanır, puan kırılımlı
/// gelir, güven seviyesi ayrı taşınır ve yapay zekâ katkısı kural katkısından ayrı
/// gösterilir. Mevcut <c>/api/eligibility</c> uçları olduğu gibi çalışmaya devam eder.
/// </para>
///
/// <para>
/// Yetki <see cref="Policies.Read"/>: platform inceleyicisi de analiz sonucunu
/// görebilmelidir. Firma verisine erişim ayrıca <c>CompanyAccessGuard</c> ile
/// üyelik üzerinden doğrulanır — kiracı sınırı burada değil, serviste korunur.
/// </para>
/// </summary>
[ApiController]
[Route("api/analysis")]
[Authorize(Policy = Policies.Read)]
[Produces("application/json")]
public sealed class AnalysisController(HybridAnalysisService service) : ControllerBase
{
    /// <summary>Bir firmanın belirli bir çağrıya uygunluğunu kriter kriter analiz eder.</summary>
    [HttpPost("companies/{companyId:guid}/opportunities/{opportunityId:guid}")]
    public async Task<ActionResult<OpportunityAnalysisDto>> AnalyzeOpportunity(
        Guid companyId,
        Guid opportunityId,
        CancellationToken cancellationToken) =>
        Ok(await service.AnalyzeOpportunityAsync(
            companyId, opportunityId, HttpContext.TraceIdentifier, cancellationToken));

    /// <summary>Bir mevzuat değişikliğinin firmaya olası etkisini analiz eder.</summary>
    [HttpPost("companies/{companyId:guid}/regulations/{regulatoryChangeId:guid}")]
    public async Task<ActionResult<RegulationImpactDto>> AnalyzeRegulation(
        Guid companyId,
        Guid regulatoryChangeId,
        CancellationToken cancellationToken) =>
        Ok(await service.AnalyzeRegulationAsync(
            companyId, regulatoryChangeId, HttpContext.TraceIdentifier, cancellationToken));
}
