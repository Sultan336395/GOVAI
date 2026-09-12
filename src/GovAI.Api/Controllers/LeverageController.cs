using GovAI.Api.Infrastructure;
using GovAI.Application.Leverage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/leverage</c> — uyum eksiklerinin fırsat karşılığı.
///
/// <para>
/// "Şu koşulu sağlamıyorsun" cümlesini "şunu kapatırsan şu çağrılara girersin"e
/// çevirir. Salt okunur; hiçbir kaydı değiştirmez.
/// </para>
/// </summary>
[ApiController]
[Route("api/leverage")]
[Authorize(Policy = Policies.CompanyData)]
[Produces("application/json")]
public sealed class LeverageController(ComplianceLeverageService service) : ControllerBase
{
    /// <summary>Firmanın kapatılabilir eksikleri; en çok çağrı açan başta.</summary>
    [HttpGet("companies/{companyId:guid}")]
    public async Task<ActionResult<ComplianceLeverageDto>> GetGaps(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetAsync(companyId, cancellationToken));
}
