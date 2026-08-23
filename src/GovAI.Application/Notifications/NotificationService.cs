using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    NotificationKind Kind,
    string Title,
    string Body,
    Guid? CompanyId,
    Guid? OpportunityId,
    NotificationChannel Channel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    bool IsRead);

/// <summary>
/// Bildirim ve Hatırlatma Modülü (Modül 10) use-case servisi.
/// Bildirimlerin üretimi değerlendirme akışında yapılır; burada okuma ve gönderim yönetilir.
/// </summary>
public sealed class NotificationService(
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    IEventPublisher events,
    ILogger<NotificationService> logger)
{
    public async Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery query, CancellationToken cancellationToken = default)
    {
        // Kiracı her zaman oturumdan yeniden yazılır; çağıranın gönderdiği değere güvenilmez.
        var scoped = query with { TenantId = access.RequireTenant() };

        // Firma verildiyse ayrıca o firmanın bu kiracıya ait olduğu ve kullanıcının
        // erişebildiği doğrulanır. Firma verilmediyse kiracı sınırı tek başına yeterlidir.
        if (scoped.CompanyId is not null)
        {
            await access.EnsureAccessAsync(scoped.CompanyId.Value, cancellationToken);
        }

        var page = await notifications.ListAsync(scoped, cancellationToken);

        return new PagedResult<NotificationDto>(
            page.Items.Select(ToDto).ToList(),
            page.TotalCount,
            page.Page,
            page.PageSize);
    }

    public async Task<NotificationDto> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();

        var notification = await notifications.GetAsync(notificationId, cancellationToken)
                           ?? throw new NotFoundException("Bildirim", notificationId);

        // Başka kiracının bildirimi "yok" sayılır; varlığı doğrulanmaz.
        if (notification.TenantId != tenantId)
        {
            throw new NotFoundException("Bildirim", notificationId);
        }

        if (notification.CompanyId is not null)
        {
            await access.EnsureAccessAsync(notification.CompanyId.Value, cancellationToken);
        }

        notification.MarkRead(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(notification);
    }

    /// <summary>
    /// Gönderilmemiş bildirimleri dış kanallara (e-posta, webhook) aktarılmak üzere kuyruğa bırakır.
    /// Zamanlanmış bir worker tarafından çağrılır.
    /// </summary>
    public async Task<int> DispatchPendingAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var pending = await notifications.ListUnsentAsync(batchSize, cancellationToken);

        foreach (var notification in pending)
        {
            if (notification.Channel == NotificationChannel.InApp)
            {
                notification.MarkSent(clock.UtcNow);
                continue;
            }

            await events.PublishAsync(
                QueueNames.NotificationDispatchRequested,
                new
                {
                    NotificationId = notification.Id,
                    notification.Channel,
                    notification.Title,
                    notification.Body,
                    notification.TenantId
                },
                cancellationToken);

            notification.MarkSent(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (pending.Count > 0)
        {
            logger.LogInformation("{Count} bildirim gönderim için işlendi.", pending.Count);
        }

        return pending.Count;
    }

    private static NotificationDto ToDto(Domain.Notifications.Notification notification) => new(
        notification.Id,
        notification.Kind,
        notification.Title,
        notification.Body,
        notification.CompanyId,
        notification.OpportunityId,
        notification.Channel,
        notification.CreatedAt,
        notification.SentAt,
        notification.IsRead);
}
