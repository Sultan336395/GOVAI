using GovAI.Api.Infrastructure;
using GovAI.Application.Evidence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/evidence</c> — kanıtların güvenilirliği ve eskime süresi.
///
/// <para>
/// "Belgeniz var mı?" sorusunun ötesine geçer: elde olan bir kanıtın <b>hâlâ</b> karar
/// dayanağı sayılıp sayılamayacağını söyler. Salt okunur; hiçbir kaydı değiştirmez.
/// </para>
/// </summary>
[ApiController]
[Route("api/evidence")]
[Authorize(Policy = Policies.CompanyData)]
[Produces("application/json")]
public sealed class EvidenceController(EvidenceReliabilityService service) : ControllerBase
{
    /// <summary>Firmanın kanıt portföyü; en acil olanlar başta.</summary>
    [HttpGet("companies/{companyId:guid}")]
    public async Task<ActionResult<EvidencePortfolioDto>> GetPortfolio(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetAsync(companyId, cancellationToken));
}
