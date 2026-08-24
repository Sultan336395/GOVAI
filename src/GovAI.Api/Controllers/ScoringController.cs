using GovAI.Api.Infrastructure;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Eligibility;
using GovAI.Application.Simulation;
using GovAI.Domain.Common;
using GovAI.Domain.Scoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/scoring</c> — puanlama detayları, karşılaştırma, öncelik sıralaması, simülasyon sonucu.
/// </summary>
[ApiController]
[Route("api/scoring")]
[Authorize(Policy = Policies.CompanyData)]
[Produces("application/json")]
public sealed class ScoringController(
    EligibilityService eligibility,
    ScenarioSimulationService simulation) : ControllerBase
{
    /// <summary>
    /// Skorlama ağırlıklarını döner. Ağırlıklar destek türüne göre değişir; arayüz bu bilgiyi
    /// "skorunuz nasıl hesaplandı" açıklamasında kullanır.
    /// </summary>
    [HttpGet("weights")]
    public ActionResult<IReadOnlyList<WeightsDto>> GetWeights()
    {
        var categories = Enum.GetValues<SupportCategory>();

        var result = categories
            .Select(category =>
            {
                var weights = ScoreWeights.For(category);
                return new WeightsDto(
                    category,
                    weights.SectorMatch,
                    weights.FinancialFit,
                    weights.EmployeeFit,
                    weights.DocumentReadiness,
                    weights.RegionalCompliance,
                    weights.TechnicalQualification,
                    weights.Timing);
            })
            .ToList();

        return Ok(result);
    }

    /// <summary>Öncelik sıralaması — en yüksek skorlu N fırsat.</summary>
    [HttpGet("companies/{companyId:guid}/ranking")]
    public async Task<ActionResult<PagedResult<OpportunityMatchDto>>> GetRanking(
        Guid companyId,
        [FromQuery] int top = 20,
        [FromQuery] decimal? minScore = null,
        CancellationToken cancellationToken = default)
    {
        var query = new AssessmentQuery
        {
            CompanyId = companyId,
            MinScore = minScore,
            PageSize = Math.Clamp(top, 1, 200),
            Sort = AssessmentSort.ScoreDescending
        };

        return Ok(await eligibility.ListMatchesAsync(query, cancellationToken));
    }

    /// <summary>
    /// "What-if" senaryosu çalıştırır (Modül 7). Firma kaydına dokunulmaz;
    /// profilin bellekteki değiştirilmiş kopyası üzerinde aynı kural motoru çalışır.
    /// </summary>
    [HttpPost("companies/{companyId:guid}/simulate")]
    [Authorize(Policy = Policies.Operate)]
    [Audited("Scoring.Simulated", "Company", RouteKey = "companyId")]
    public async Task<ActionResult<ScenarioResultDto>> Simulate(
        Guid companyId,
        [FromBody] ScenarioRequest request,
        [FromQuery] bool persist = true,
        CancellationToken cancellationToken = default) =>
        Ok(await simulation.RunAsync(companyId, request, persist, cancellationToken));

    /// <summary>Firmanın kayıtlı senaryoları.</summary>
    [HttpGet("companies/{companyId:guid}/simulations")]
    public async Task<ActionResult<IReadOnlyList<ScenarioSummaryDto>>> ListSimulations(
        Guid companyId,
        CancellationToken cancellationToken) =>
        Ok(await simulation.ListAsync(companyId, cancellationToken));

    public sealed record WeightsDto(
        SupportCategory Category,
        decimal SectorMatch,
        decimal FinancialFit,
        decimal EmployeeFit,
        decimal DocumentReadiness,
        decimal RegionalCompliance,
        decimal TechnicalQualification,
        decimal Timing);
}
