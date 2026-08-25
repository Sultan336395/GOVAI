using GovAI.Api.Infrastructure;
using GovAI.Application.Sources;
using GovAI.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// Karantina inceleme uçları (Faz 2).
///
/// Tamamı <see cref="Policies.PlatformReview"/> altındadır: karantina kararı ortak
/// kataloğu ilgilendirir, tek bir müşterinin işi değildir. Kiracı kullanıcıları bu
/// uçlara erişemez.
/// </summary>
[ApiController]
[Route("api/quarantine")]
[Authorize(Policy = Policies.PlatformReview)]
[Produces("application/json")]
public sealed class QuarantineController(QuarantineService service) : ControllerBase
{
    /// <summary>
    /// Mevcut kayıtları veri kalitesi kurallarından geçirir.
    ///
    /// <c>apply=false</c> (varsayılan) hiçbir şeyi değiştirmez, yalnızca rapor üretir:
    /// önce görülür, sonra uygulanır.
    /// </summary>
    [HttpPost("triage")]
    [Audited("Quarantine.Triage", "SourceDocument")]
    public async Task<ActionResult<TriageReport>> Triage(
        [FromQuery] bool apply = false,
        CancellationToken cancellationToken = default) =>
        Ok(await service.TriageAsync(apply, cancellationToken));

    /// <summary>Karantinadaki kayıtlar.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<QuarantinedDocumentDto>>> List(
        CancellationToken cancellationToken) =>
        Ok(await service.ListQuarantinedAsync(cancellationToken));

    /// <summary>Kaydı reddeder: karantinaya alır. Silmez.</summary>
    [HttpPost("{documentId:guid}/reject")]
    [Audited("Quarantine.Rejected", "SourceDocument", RouteKey = "documentId")]
    public async Task<IActionResult> Reject(
        Guid documentId,
        [FromBody] RejectDocumentRequest request,
        CancellationToken cancellationToken)
    {
        await service.QuarantineAsync(documentId, request.Reason, request.Note, cancellationToken);
        return NoContent();
    }

    /// <summary>Kaydı onaylar: karantinadan çıkarır ve yeniden ayrıştırmaya gönderir.</summary>
    [HttpPost("{documentId:guid}/approve")]
    [Audited("Quarantine.Approved", "SourceDocument", RouteKey = "documentId")]
    public async Task<IActionResult> Approve(Guid documentId, CancellationToken cancellationToken)
    {
        await service.ReleaseAsync(documentId, cancellationToken);
        return NoContent();
    }
}

public sealed record RejectDocumentRequest(QuarantineReason Reason, string? Note);
