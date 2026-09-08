using GovAI.Api.Infrastructure;
using GovAI.Application.Opportunities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

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
    /// </summary>
    [HttpPost("backfill/apply")]
    public async Task<ActionResult<RuleEvidenceBackfillReport>> Apply(
        [FromQuery] Guid? after,
        [FromQuery] int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ApplyAsync(new RuleEvidenceBackfillRequest(after, batchSize), cancellationToken));
}
