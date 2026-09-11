using GovAI.Api.Infrastructure;
using GovAI.Application.Integrations;
using GovAI.Infrastructure.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/erp-auth</c> — ERP'nin imzalı beyanını kısa ömürlü jetona çevirir.
///
/// <para>
/// Uç <b>anonimdir</b> ve anonim olmak zorundadır: çağıran tarafın henüz jetonu yoktur.
/// Kimlik doğrulama beyanın <b>imzasıyla</b> yapılır; GOVAI hiçbir sır saklamaz,
/// yalnızca ERP'nin açık anahtarını tutar.
/// </para>
///
/// <para>
/// Hız sınırı vardır: imza doğrulaması ucuz değildir ve anonim bir uç, sınırsız
/// denemeye açık bırakılırsa hem kaynak tüketimi hem de kimlik tarama aracı olur.
/// </para>
/// </summary>
[ApiController]
[Route("api/erp-auth")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class ErpAuthController(
    ErpTokenService tokens,
    IOptions<ErpAuthOptions> options) : ControllerBase
{
    /// <summary>Hız sınırı ilkesinin adı; <c>Program.cs</c> içinde tanımlıdır.</summary>
    public const string RateLimitPolicy = "erp-auth";

    /// <summary>
    /// Beyanı jetona çevirir.
    ///
    /// <para>
    /// Reddedilme sebebi dışarıya <b>ayrıştırılmadan</b> döner: bilinmeyen istemci,
    /// devre dışı kimlik, bozuk imza ve süresi dolmuş beyan aynı cevabı alır. Ayırt
    /// edilseydi saldırgan hangi istemci kimliklerinin var olduğunu ve nerede
    /// takıldığını öğrenirdi. Sebep yalnızca sunucu kaydına yazılır.
    /// </para>
    /// </summary>
    [HttpPost("token")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<ActionResult<ErpTokenResponse>> Token(
        [FromBody] ErpTokenRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await tokens.IssueAsync(
                request, options.Value.AssertionAudience, cancellationToken));
        }
        catch (ErpTokenRejectedException)
        {
            return Unauthorized(new { error = ErpTokenService.GenericFailure });
        }
    }
}
