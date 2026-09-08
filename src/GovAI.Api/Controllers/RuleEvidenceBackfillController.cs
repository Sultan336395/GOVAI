using GovAI.Api.Infrastructure;
using GovAI.Application.Opportunities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>Uygulama isteğinin gövdesi: onaylanan planın parmak izi.</summary>
public sealed record RuleEvidenceBackfillApplyBody(string PlanHash);

/// <summary>
/// Mevcut fırsat kurallarına geriye dönük kanıt bağlama (Faz 3).
///
/// <para>
/// Tamamı <see cref="Policies.PlatformReview"/> altındadır. Ortak kataloğu ilgilendiren
/// bir bakım işlemidir; <b>kiracı kullanıcısı başlatamaz</b> — bir müşterinin isteği
/// tüm müşterilerin gördüğü katalog verisini değiştiremez.
/// </para>
///
/// <para>
/// İki adım ayrıdır: <c>plan</c> hiçbir kayda dokunmaz, <c>apply</c> uygular. İkinci
/// <c>apply</c> çağrısı ne yeni sürüm ne mükerrer bağlantı üretir.
/// </para>
///
/// <para>
/// İşlem <b>internete çıkmaz</b>: yalnızca veritabanında saklanan belge sürümlerini ve
/// kanıt parçalarını kullanır. Ham içeriği olmayan kayıtlar değiştirilmez, "yeniden
/// indirme gerekli" olarak raporlanır.
/// </para>
/// </summary>
[ApiController]
[Route("api/opportunities/rule-evidence")]
[Authorize(Policy = Policies.PlatformReview)]
[Produces("application/json")]
public sealed class RuleEvidenceBackfillController(RuleEvidenceBackfillService service) : ControllerBase
{
    /// <summary>
    /// Kuru çalıştırma: hangi kurallar bağlanacak, hangileri neden atlanacak?
    /// </summary>
    /// <param name="after">
    /// İmleç. Önceki turun <c>nextCursor</c> değeri verilirse işlem kaldığı yerden sürer.
    /// </param>
    /// <param name="batchSize">Bir turda incelenecek azami fırsat sayısı.</param>
    [HttpGet("backfill/plan")]
    public async Task<ActionResult<RuleEvidenceBackfillReport>> Plan(
        [FromQuery] Guid? after,
        [FromQuery] int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await service.PlanAsync(new RuleEvidenceBackfillRequest(after, batchSize), cancellationToken));

    /// <summary>
    /// Planı uygular. Yalnızca kanıtı güvenilir biçimde bulunan kurallara bağlantı
    /// ekler; bulunamayan kurala <b>dokunmaz</b>.
    ///
    /// <para>
    /// Gövdedeki <c>planHash</c>, <c>plan</c> ucundan dönen değerle birebir aynı
    /// olmalıdır. Bu kural sunucuda zorunludur: görülmemiş bir plan uygulanamaz.
    /// </para>
    /// </summary>
    [HttpPost("backfill/apply")]
    [Audited("RuleEvidenceBackfill.Applied", "Opportunity")]
    public async Task<ActionResult<RuleEvidenceBackfillReport>> Apply(
        [FromBody] RuleEvidenceBackfillApplyBody body,
        [FromQuery] Guid? after,
        [FromQuery] int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ApplyAsync(
            new RuleEvidenceBackfillRequest(after, batchSize), body.PlanHash, cancellationToken));

    /// <summary>
    /// Bir bağlama çalıştırmasını geri alır: <b>yalnızca o turda kurulan</b> bağlantılar
    /// silinir. Kural, belge, sürüm ve kanıt parçaları yerinde kalır.
    /// </summary>
    [HttpPost("backfill/runs/{id:guid}/undo")]
    [Audited("RuleEvidenceBackfill.Undone", "MaintenanceRun")]
    public async Task<ActionResult<object>> Undo(Guid id, CancellationToken cancellationToken) =>
        Ok(new { removedLinkCount = await service.UndoAsync(id, cancellationToken) });
}
