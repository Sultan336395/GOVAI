using GovAI.Api.Infrastructure;
using GovAI.Application.Sources;
using GovAI.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// Bozuk başlıklı belgelerin resmî kaynağından yeniden ayrıştırılması (Faz 2).
///
/// <para>
/// Tamamı <see cref="Policies.PlatformReview"/> altındadır: bu bir bakım işlemidir,
/// ortak kataloğu ilgilendirir ve tek bir müşterinin işi değildir.
/// </para>
///
/// <para>
/// İki adım <b>ayrıdır ve sırası zorunludur</b>: önce <c>plan</c> hangi kayıtların
/// değişeceğini gösterir (ağa çıkmaz, hiçbir şey yazmaz), sonra <c>apply</c>
/// uygular. Kör bir toplu güncelleme yapılmaz.
/// </para>
/// </summary>
[ApiController]
[Route("api/sources/title-repair")]
[Authorize(Policy = Policies.PlatformReview)]
[Produces("application/json")]
public sealed class TitleRepairController(TitleRepairService service) : ControllerBase
{
    /// <summary>
    /// Kuru çalıştırma: hangi kayıtlar değişecek, hangileri neden atlanacak?
    ///
    /// Hiçbir kayda dokunmaz ve resmî kaynaklara <b>istek göndermez</b>.
    /// </summary>
    [HttpGet("plan")]
    public async Task<ActionResult<TitleRepairPlan>> Plan(
        [FromQuery] int limit = TitleRepairService.MaxBatch,
        CancellationToken cancellationToken = default) =>
        Ok(await service.PlanAsync(Math.Clamp(limit, 1, TitleRepairService.MaxBatch), cancellationToken));

    /// <summary>
    /// Planı uygular: her belgeyi resmî adresinden yeniden indirir.
    ///
    /// Başlık indirilen belgeden gelir, tahmin edilmez. Hiçbir kayıt silinmez; yeni
    /// sürüm yalnızca içerik gerçekten değiştiyse açılır.
    /// </summary>
    [HttpPost("apply")]
    [Audited("Source.TitleRepair", "SourceDocument")]
    public async Task<ActionResult<TitleRepairReport>> Apply(
        [FromQuery] int limit = TitleRepairService.MaxBatch,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ApplyAsync(Math.Clamp(limit, 1, TitleRepairService.MaxBatch), cancellationToken));
}
