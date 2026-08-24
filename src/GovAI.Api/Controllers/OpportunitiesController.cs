using GovAI.Api.Infrastructure;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Opportunities;
using GovAI.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/opportunities</c> — çağrı listesi, filtreleme, detay, son tarih, kaynak bilgisi.
/// </summary>
[ApiController]
[Route("api/opportunities")]
[Authorize(Policy = Policies.Read)]
[Produces("application/json")]
public sealed class OpportunitiesController(OpportunityService service) : ControllerBase
{
    /// <summary>Fırsat kataloğunda filtreli arama.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<OpportunitySummaryDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] SupportCategory[]? categories,
        [FromQuery] SourceType[]? sourceTypes,
        [FromQuery] bool onlyOpen = true,
        [FromQuery] bool? onlyReviewed = null,
        [FromQuery] DateTimeOffset? publishedAfter = null,
        [FromQuery] DateTimeOffset? deadlineBefore = null,
        [FromQuery] OpportunitySort sort = OpportunitySort.DeadlineAscending,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var query = new OpportunityQuery
        {
            SearchTerm = search,
            Categories = categories,
            SourceTypes = sourceTypes,
            OnlyOpen = onlyOpen,
            OnlyReviewed = onlyReviewed,
            PublishedAfter = publishedAfter,
            DeadlineBefore = deadlineBefore,
            Sort = sort,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await service.SearchAsync(query, cancellationToken));
    }

    /// <summary>Çağrının tam detayı: çıkarılmış koşullar, belge listesi, kaynak bağlantısı.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OpportunityDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    /// <summary>
    /// Çağrıyı oluşturur veya kaynak dokümanına göre günceller.
    /// Parser worker'ı ve danışman elle giriş ekranı aynı uçtan geçer.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Policies.CatalogWrite)]
    [Audited("Opportunity.Upserted", "Opportunity")]
    public async Task<ActionResult<OpportunityDetailDto>> Upsert(
        [FromBody] UpsertOpportunityRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.UpsertAsync(request, cancellationToken));

    /// <summary>Danışmanın otomatik çıkarılan bir kuralı düzeltmesi (istisna yönetimi).</summary>
    [HttpPut("{id:guid}/rules/{ruleId:guid}")]
    [Authorize(Policy = Policies.CatalogWrite)]
    [Audited("Opportunity.RuleOverridden", "OpportunityRule")]
    public async Task<ActionResult<OpportunityDetailDto>> OverrideRule(
        Guid id,
        Guid ruleId,
        [FromBody] OverrideRuleRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.OverrideRuleAsync(id, ruleId, request, cancellationToken));

    /// <summary>Danışman onayı; skor güvenini tavana taşır.</summary>
    [HttpPost("{id:guid}/review")]
    [Authorize(Policy = Policies.CatalogWrite)]
    [Audited("Opportunity.Reviewed", "Opportunity")]
    public async Task<ActionResult<OpportunityDetailDto>> MarkReviewed(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.MarkReviewedAsync(id, cancellationToken));
}
