using GovAI.Api.Infrastructure;
using GovAI.Application.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>Uygulama isteğinin gövdesi: onaylanan planın parmak izi.</summary>
public sealed record CatalogRepairApplyBody(string PlanHash);

/// <summary>
/// Bekleyen katalog onarımları (Faz 3): KOSGEB çöp kayıtları ve SGK taslakları.
///
/// <para>
/// Tamamı <see cref="Policies.PlatformReview"/> altındadır: ortak kataloğu ilgilendiren
/// bir bakım işlemidir, tek bir müşterinin işi değildir. Kiracı kullanıcısı, şirket
/// yöneticisi ve worker kimliği bu uçlara <b>erişemez</b>.
/// </para>
///
/// <para>
/// İki adım <b>ayrıdır ve sırası zorunludur</b>: önce <c>plan</c> ne değişeceğini
/// gösterir ve hiçbir şey yazmaz, sonra <c>apply</c> uygular. Uygulama isteği planın
/// parmak izini taşımak zorundadır — bu kural <b>sunucuda</b> zorunludur, arayüzde
/// değil: görülmemiş bir plan uygulanamaz ve plan gösterildikten sonra veri değiştiyse
/// istek reddedilir.
/// </para>
///
/// <para>
/// Hiçbir kayıt silinmez. Çöp kayıtlar karantinaya alınır; kaynak belge, ham içerik,
/// sürüm ve kanıtlar yerinde kalır. Uygulanan her çalıştırma kaydedilir ve
/// <c>undo</c> ile geri alınabilir.
/// </para>
/// </summary>
[ApiController]
[Route("api/sources/catalog-repair")]
[Authorize(Policy = Policies.PlatformReview)]
[Produces("application/json")]
public sealed class CatalogRepairController(CatalogRepairService service) : ControllerBase
{
    /// <summary>
    /// Kuru çalıştırma: hangi kayıtlar değişecek, hangileri neden atlanacak?
    /// Dönen <c>planHash</c> uygulama isteğinde geri gönderilir.
    /// </summary>
    [HttpGet("plan")]
    public async Task<ActionResult<CatalogRepairPlanReport>> Plan(CancellationToken cancellationToken) =>
        Ok(await service.PlanAsync(cancellationToken));

    /// <summary>
    /// Planı uygular. Yalnızca değişecek kayıtlara dokunur; ikinci çağrı boş geçer.
    /// </summary>
    [HttpPost("apply")]
    [Audited("CatalogRepair.Applied", "Opportunity")]
    public async Task<ActionResult<CatalogRepairReport>> Apply(
        [FromBody] CatalogRepairApplyBody body,
        CancellationToken cancellationToken) =>
        Ok(await service.ApplyAsync(body.PlanHash, cancellationToken));

    /// <summary>
    /// Bir onarım çalıştırmasını geri alır: karantina kalkar, başlık eski hâline döner.
    /// Çalıştırma kaydı silinmez, geri alındı diye damgalanır.
    /// </summary>
    [HttpPost("runs/{id:guid}/undo")]
    [Audited("CatalogRepair.Undone", "MaintenanceRun")]
    public async Task<ActionResult<CatalogRepairReport>> Undo(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await service.UndoAsync(id, cancellationToken));
}
