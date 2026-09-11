using GovAI.Api.Infrastructure;
using GovAI.Application.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/erp</c> — firmanın ERP bağlantısı ve veri çekme.
///
/// <para>
/// Mevcut <c>/api/company-profile/erp-sync</c> ucu <b>itme</b> yönündedir: ERP tarafındaki
/// bir geliştirici veriyi GOVAI'ye gönderir. Buradaki uçlar <b>çekme</b> yönünü açar:
/// GOVAI, firmanın izin verdiği uçtan veriyi kendisi okur ve profil kendiliğinden güncel
/// kalır.
/// </para>
///
/// <para>
/// Kimlik bilgisi hiçbir yanıtta dönmez; yalnızca tanımlı olup olmadığı bildirilir.
/// </para>
/// </summary>
[ApiController]
[Route("api/erp")]
[Authorize(Policy = Policies.CompanyData)]
public sealed class ErpConnectionsController(
    ErpConnectionService connections,
    ErpPullService pull) : ControllerBase
{
    /// <summary>Firmanın ERP bağlantısı. Kurulmamışsa <c>204</c> döner.</summary>
    [HttpGet("companies/{companyId:guid}/connection")]
    [Produces("application/json")]
    public async Task<ActionResult<ErpConnectionDto>> Get(Guid companyId, CancellationToken cancellationToken)
    {
        var connection = await connections.GetAsync(companyId, cancellationToken);

        return connection is null ? NoContent() : Ok(connection);
    }

    /// <summary>
    /// Bağlantıyı kurar veya günceller.
    ///
    /// <para>
    /// Güncellemede kimlik bilgisi boş bırakılırsa <b>mevcut kimlik korunur</b>; ekranın
    /// kayıtlı sırrı geri göstermesi gerekmez.
    /// </para>
    /// </summary>
    [HttpPut("companies/{companyId:guid}/connection")]
    [Audited("Erp.ConnectionUpserted", "Company", RouteKey = "companyId")]
    public async Task<ActionResult<ErpConnectionDto>> Upsert(
        Guid companyId,
        [FromBody] UpsertErpConnectionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await connections.UpsertAsync(companyId, request, cancellationToken));

    [HttpPost("companies/{companyId:guid}/connection/enabled")]
    [Audited("Erp.ConnectionToggled", "Company", RouteKey = "companyId")]
    public async Task<IActionResult> SetEnabled(
        Guid companyId,
        [FromQuery] bool enabled,
        CancellationToken cancellationToken)
    {
        await connections.SetEnabledAsync(companyId, enabled, cancellationToken);

        return NoContent();
    }

    [HttpDelete("companies/{companyId:guid}/connection")]
    [Audited("Erp.ConnectionDeleted", "Company", RouteKey = "companyId")]
    public async Task<IActionResult> Delete(Guid companyId, CancellationToken cancellationToken)
    {
        await connections.DeleteAsync(companyId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// ERP'den veriyi şimdi çeker.
    ///
    /// <para>
    /// Bağlantı kurulurken de kullanılır: yanıt hangi alanların bulunduğunu ve hangilerinin
    /// <b>bulunamadığını</b> söyler, böylece eşleme gerçek kuruluma göre düzeltilebilir.
    /// </para>
    /// </summary>
    [HttpPost("companies/{companyId:guid}/pull")]
    [Audited("Erp.PullRequested", "Company", RouteKey = "companyId")]
    public async Task<ActionResult<ErpPullResultDto>> Pull(
        Guid companyId,
        CancellationToken cancellationToken) =>
        Ok(await pull.PullCompanyAsync(companyId, cancellationToken));
}

/// <summary>
/// <c>/api/erp/pull-batch</c> — gece turunun çağırdığı toplu çekme.
///
/// <para>
/// Ayrı denetleyicidedir çünkü yetkisi farklıdır: bunu kullanıcı değil zamanlayıcı çağırır
/// ve kiracının tamamına dokunur.
/// </para>
/// </summary>
[ApiController]
[Route("api/erp/pull-batch")]
[Authorize(Policy = Policies.SystemIngest)]
public sealed class ErpPullBatchController(ErpPullService service) : ControllerBase
{
    [HttpPost]
    [Audited("Erp.PullBatch", "Tenant")]
    public async Task<ActionResult<ErpPullBatchResultDto>> Pull(CancellationToken cancellationToken) =>
        Ok(await service.PullTenantAsync(cancellationToken));
}
