using GovAI.Api.Infrastructure;
using GovAI.Application.Tenders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/tenders</c> — ihale başvuru takibi.
///
/// <para>
/// Takip müşteri verisidir ve firma üyeliğinden yetkilenir; platform rolleri
/// (katalog yöneticisi, inceleyici) buraya giremez.
/// </para>
///
/// <para>
/// Takip kaydı skoru <b>değiştirmez</b>. Bu uçlar yalnızca firmanın kendi süreç
/// beyanını saklar; uygunluk kararı her zaman <c>EligibilityEngine</c>'den gelir.
/// </para>
/// </summary>
[ApiController]
[Route("api/tenders")]
[Authorize(Policy = Policies.CompanyData)]
public sealed class TendersController(TenderPursuitService service) : ControllerBase
{
    /// <summary>Firmanın takip tahtası: satırlar ve aşama sayaçları.</summary>
    [HttpGet("companies/{companyId:guid}")]
    [Produces("application/json")]
    public async Task<ActionResult<TenderBoardDto>> Board(
        Guid companyId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetBoardAsync(companyId, cancellationToken));

    /// <summary>
    /// İhaleyi takibe alır. Zaten takipteyse ikinci kayıt açılmaz, mevcut kayıt döner.
    /// </summary>
    [HttpPost("companies/{companyId:guid}")]
    [Audited("TenderPursuit.Started", "Company", RouteKey = "companyId")]
    public async Task<ActionResult<TenderPursuitDto>> Start(
        Guid companyId,
        [FromBody] StartTenderPursuitRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.StartAsync(companyId, request, cancellationToken));

    /// <summary>
    /// Aşamayı değiştirir.
    ///
    /// <para>
    /// Sonuçlanan ihalede sonuç (kazanıldı / kaybedildi / iptal) zorunludur; "sonuçlandı"
    /// tek başına kazanılan ile kaybedilen ihaleyi aynı satırda gösterirdi.
    /// </para>
    /// </summary>
    [HttpPost("{pursuitId:guid}/status")]
    [Audited("TenderPursuit.StatusChanged", "TenderPursuit", RouteKey = "pursuitId")]
    public async Task<ActionResult<TenderPursuitDto>> ChangeStatus(
        Guid pursuitId,
        [FromBody] ChangeTenderPursuitRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.ChangeStatusAsync(pursuitId, request, cancellationToken));
}
