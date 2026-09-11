using GovAI.Api.Infrastructure;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Notifications;
using GovAI.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/notifications</c> — son tarih uyarıları, yeni fırsat eşleşmeleri, durum güncellemeleri.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize(Policy = Policies.Read)]
[Produces("application/json")]
public sealed class NotificationsController(NotificationService service, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> List(
        [FromQuery] Guid? companyId,
        [FromQuery] bool? onlyUnread,
        [FromQuery] NotificationKind[]? kinds,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var query = new NotificationQuery
        {
            // Servis bu değeri oturumdan yeniden yazar; burada verilmesinin tek sebebi
            // alanın zorunlu olması ve hiçbir çağrı yerinin kiracıyı atlayamamasıdır.
            TenantId = currentUser.TenantId ?? Guid.Empty,
            CompanyId = companyId,
            OnlyUnread = onlyUnread,
            Kinds = kinds,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await service.ListAsync(query, cancellationToken));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<NotificationDto>> MarkRead(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.MarkReadAsync(id, cancellationToken));

    /// <summary>
    /// Gönderilmemiş bildirimleri kanallarına aktarır. Zamanlanmış worker çağırır.
    ///
    /// <para>
    /// Cevap işlenen sayıyı değil <b>ne olduğunu</b> döner: kaçı gerçekten gönderildi,
    /// kaçı başarısız oldu, kaçı kanal yapılandırılmadığı için beklemede kaldı. Tek bir
    /// "işlendi" sayısı, hiç ulaşmayan bildirimleri başarı gibi gösteriyordu.
    /// </para>
    /// </summary>
    [HttpPost("dispatch")]
    [Authorize(Policy = Policies.SystemIngest)]
    [Audited("Notification.Dispatched", "Notification")]
    public async Task<ActionResult<DispatchResponse>> Dispatch(
        [FromQuery] int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var sonuc = await service.DispatchPendingAsync(batchSize, cancellationToken);

        return Ok(new DispatchResponse(
            sonuc.Processed, sonuc.Sent, sonuc.Failed, sonuc.Skipped, sonuc.RecipientMissing));
    }

    public sealed record DispatchResponse(
        int ProcessedCount,
        int SentCount,
        int FailedCount,
        int SkippedCount,
        /// <summary>Şirkette tanımlı bildirim alıcısı olmadığı için gönderilemeyenler.</summary>
        int RecipientMissingCount);
}
