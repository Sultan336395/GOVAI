using GovAI.Api.Infrastructure;
using GovAI.Application.Regulatory;
using GovAI.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// Mevzuat değişiklikleri (Faz 2 – RegTech).
///
/// Kiracı kullanıcıları resmî ve <b>doğrulanmış</b> kayıtları okuyabilir; değiştiremez.
/// Kayıtlar ortak kataloğa aittir: bir müşteri düzenleme yaparak diğerlerini etkileyemez,
/// çünkü yazma ucu yoktur.
///
/// <b>Bu fazda şirkete etki hesaplanmaz.</b> Etki değerlendirmesi DeepTech analiz
/// motorunun işidir.
/// </summary>
[ApiController]
[Route("api/regulatory-changes")]
[Authorize(Policy = Policies.CompanyData)]
[Produces("application/json")]
public sealed class RegulatoryChangesController(RegulatoryChangeService service) : ControllerBase
{
    /// <summary>Doğrulanmış mevzuat değişiklikleri.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RegulatoryChangeSummaryDto>>> List(
        [FromQuery] RegulationDomain? domain,
        [FromQuery] string? jurisdiction,
        CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(domain, jurisdiction, cancellationToken));

    /// <summary>
    /// Mevzuat detayı: künye, belge sürümü ve kanıt bölümleri.
    /// Kanıt metni resmî belgeye geri gösterilebilir.
    /// </summary>
    [HttpGet("{changeId:guid}")]
    public async Task<ActionResult<RegulatoryChangeDetailDto>> Get(
        Guid changeId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(changeId, cancellationToken));
}
