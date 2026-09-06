using GovAI.Api.Infrastructure;
using GovAI.Application.Regulatory;
using GovAI.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// Mevzuat değişiklikleri (Faz 2 – RegTech).
///
/// Resmî ve <b>doğrulanmış</b> kayıtlar okunur; değiştirilemez. Kayıtlar ortak kataloğa
/// aittir: bir müşteri düzenleme yaparak diğerlerini etkileyemez, çünkü yazma ucu yoktur.
///
/// <para>
/// Yetki <see cref="Policies.Read"/>'dir, <c>CompanyData</c> DEĞİL — tıpkı kardeşi
/// <see cref="OpportunitiesController"/> gibi. Burada hiçbir kiracı verisi yoktur:
/// dönen alanlar resmî künye, belge sürümü ve kanıt metnidir. <c>CompanyData</c>
/// platform rollerini bilinçli olarak dışarıda bırakır ve bu ekranı platform
/// hesaplarına 403 yapıyordu; oysa sol menü ekranı onlara da gösteriyor ve mevzuatı
/// inceleyecek olan asıl kişi platform inceleyicisidir.
/// </para>
///
/// <b>Bu fazda şirkete etki hesaplanmaz.</b> Etki değerlendirmesi DeepTech analiz
/// motorunun işidir.
/// </summary>
[ApiController]
[Route("api/regulatory-changes")]
[Authorize(Policy = Policies.Read)]
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
