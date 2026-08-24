using GovAI.Api.Infrastructure;
using GovAI.Application.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/sources</c> — kaynak tanımı, tarama takvimi, veri çekme logları.
/// Collector worker'ı ham dokümanları bu uçlardan sisteme bırakır.
/// </summary>
[ApiController]
[Route("api/sources")]
[Authorize(Policy = Policies.Read)]
[Produces("application/json")]
public sealed class SourcesController(SourceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SourceDto>>> List(
        [FromQuery] bool onlyEnabled = false,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListAsync(onlyEnabled, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SourceDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.PlatformCatalog)]
    [Audited("Source.Created", "Source")]
    public async Task<ActionResult<SourceDto>> Create(
        [FromBody] UpsertSourceRequest request,
        CancellationToken cancellationToken)
    {
        var created = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.PlatformCatalog)]
    [Audited("Source.Updated", "Source")]
    public async Task<ActionResult<SourceDto>> Update(
        Guid id,
        [FromBody] UpsertSourceRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/enabled")]
    [Authorize(Policy = Policies.PlatformCatalog)]
    [Audited("Source.EnabledChanged", "Source")]
    public async Task<ActionResult<SourceDto>> SetEnabled(
        Guid id,
        [FromQuery] bool enabled,
        CancellationToken cancellationToken) =>
        Ok(await service.SetEnabledAsync(id, enabled, cancellationToken));

    /// <summary>Kaynağı takvim beklemeden hemen taramaya alır.</summary>
    [HttpPost("{id:guid}/crawl")]
    [Authorize(Policy = Policies.SystemIngest)]
    [Audited("Source.CrawlTriggered", "Source")]
    public async Task<IActionResult> TriggerCrawl(Guid id, CancellationToken cancellationToken)
    {
        await service.TriggerCrawlAsync(id, cancellationToken);
        return Accepted();
    }

    /// <summary>
    /// Collector worker'ın topladığı ham dokümanı sisteme bırakması.
    /// İçerik değişmediyse hiçbir iş kuyruğa alınmaz.
    /// </summary>
    [HttpPost("documents")]
    [Authorize(Policy = Policies.SystemIngest)]
    public async Task<ActionResult<IngestDocumentResult>> IngestDocument(
        [FromBody] IngestDocumentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.IngestDocumentAsync(request, cancellationToken));

    /// <summary>Worker'ın tarama sonucunu bildirmesi; üst üste hata alan kaynak otomatik devre dışı kalır.</summary>
    [HttpPost("{id:guid}/runs")]
    [Authorize(Policy = Policies.SystemIngest)]
    public async Task<IActionResult> RecordRun(
        Guid id,
        [FromBody] RecordCrawlRunRequest request,
        CancellationToken cancellationToken)
    {
        await service.RecordRunAsync(id, request, cancellationToken);
        return NoContent();
    }
}
