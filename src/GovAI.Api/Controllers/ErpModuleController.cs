using GovAI.Api.Infrastructure;
using GovAI.Application.Integrations;
using GovAI.Infrastructure.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/erp-module</c> — müşterinin ERP'sindeki GOVAI modülünün okuduğu uçlar.
///
/// <para>
/// Kiracı ve şirket <b>yalnızca jetondan</b> okunur; istekten alınmaz. Alınsaydı, bir
/// ERP kendi jetonuyla başka şirketin kimliğini yazıp o şirketin verisini isteyebilirdi.
/// Bu yüzden uçlarda şirket kimliği parametresi <b>yoktur</b> — olmayan bir parametre
/// kötüye kullanılamaz.
/// </para>
///
/// <para>
/// Yüzey bilerek dardır ve yalnızca okumadır: ERP modülü GOVAI'de hiçbir şey değiştirmez.
/// </para>
/// </summary>
[ApiController]
[Route("api/erp-module")]
[Authorize(AuthenticationSchemes = ErpModuleDefaults.Scheme, Policy = ErpModuleDefaults.Policy)]
[Produces("application/json")]
public sealed class ErpModuleController(ErpModuleService module) : ControllerBase
{
    /// <summary>Şirketin bildirimleri. Sayfa ve okunmamış süzgeci desteklenir.</summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<ErpModuleNotificationsDto>> Notifications(
        [FromQuery] bool onlyUnread = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (!Kimlik(out var tenantId, out var companyId))
        {
            return Unauthorized();
        }

        return Ok(await module.GetNotificationsAsync(
            tenantId, companyId, onlyUnread, page, pageSize, cancellationToken));
    }

    /// <summary>
    /// Jetonun kime ait olduğunu söyler. ERP tarafı kurulumu buradan doğrular.
    ///
    /// <para>
    /// Hiçbir müşteri verisi taşımaz; yalnızca bağlantının doğru şirkete gittiğini
    /// teyit eder.
    /// </para>
    /// </summary>
    [HttpGet("whoami")]
    public ActionResult<object> WhoAmI()
    {
        if (!Kimlik(out var tenantId, out var companyId))
        {
            return Unauthorized();
        }

        return Ok(new
        {
            tenantId,
            companyId,
            clientId = User.FindFirst(ErpModuleClaims.ClientId)?.Value,
            subject = User.FindFirst(ErpModuleClaims.Subject)?.Value,
            subjectName = User.FindFirst(ErpModuleClaims.SubjectName)?.Value,
            kind = User.FindFirst(ErpModuleClaims.PrincipalKind)?.Value,
        });
    }

    /// <summary>
    /// Jetondaki kiracı ve şirketi okur.
    ///
    /// <para>
    /// İkisi de <b>zorunludur</b>: biri eksikse istek reddedilir. Eksik claim'i
    /// "sınırsız" saymak, bütün kiracıya açılan bir jeton demek olurdu.
    /// </para>
    /// </summary>
    private bool Kimlik(out Guid tenantId, out Guid companyId)
    {
        tenantId = default;
        companyId = default;

        return Guid.TryParse(User.FindFirst(ErpModuleClaims.TenantId)?.Value, out tenantId)
               && Guid.TryParse(User.FindFirst(ErpModuleClaims.CompanyId)?.Value, out companyId);
    }
}
