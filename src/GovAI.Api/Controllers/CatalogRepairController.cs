using GovAI.Api.Infrastructure;
using GovAI.Application.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// Bekleyen katalog onarımları (Faz 3): KOSGEB çöp kayıtları ve SGK taslakları.
///
/// <para>
/// Tamamı <see cref="Policies.PlatformReview"/> altındadır: ortak kataloğu ilgilendiren
/// bir bakım işlemidir, tek bir müşterinin işi değildir.
/// </para>
///
/// <para>
/// İki adım <b>ayrıdır ve sırası zorunludur</b>: önce <c>plan</c> ne değişeceğini
/// gösterir ve hiçbir şey yazmaz, sonra <c>apply</c> uygular. Uygulama
/// <b>idempotenttir</b>: ikinci çağrı ilave değişiklik üretmez.
/// </para>
///
/// <para>
/// Hiçbir kayıt silinmez. Çöp kayıtlar karantinaya alınır; kaynak belge, ham içerik,
/// sürüm ve kanıtlar yerinde kalır ve PlatformReviewer işlemi geri alabilir.
/// </para>
/// </summary>
[ApiController]
[Route("api/sources/catalog-repair")]
[Authorize(Policy = Policies.PlatformReview)]
[Produces("application/json")]
public sealed class CatalogRepairController(CatalogRepairService service) : ControllerBase
{
    /// <summary>Kuru çalıştırma: hangi kayıtlar değişecek, hangileri neden atlanacak?</summary>
    [HttpGet("plan")]
    public async Task<ActionResult<CatalogRepairPlanReport>> Plan(CancellationToken cancellationToken) =>
        Ok(await service.PlanAsync(cancellationToken));

    /// <summary>
    /// Planı uygular. Yalnızca değişecek kayıtlara dokunur; ikinci çağrı boş geçer.
    /// </summary>
    [HttpPost("apply")]
    public async Task<ActionResult<CatalogRepairReport>> Apply(CancellationToken cancellationToken) =>
        Ok(await service.ApplyAsync(cancellationToken));
}
