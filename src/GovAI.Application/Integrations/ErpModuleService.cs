using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Common;

namespace GovAI.Application.Integrations;

/// <summary>ERP modülünde gösterilen bildirim satırı.</summary>
public sealed record ErpModuleNotificationDto(
    Guid Id,
    NotificationKind Kind,
    string KindLabel,
    string Title,
    string Body,
    Guid? OpportunityId,
    DateTimeOffset CreatedAt,
    bool IsRead,
    /// <summary>Dış kanala çıktı mı; çıkmadıysa neden. ERP bunu gösterebilir.</summary>
    string DeliveryStatus);

public sealed record ErpModuleNotificationsDto(
    Guid CompanyId,
    IReadOnlyList<ErpModuleNotificationDto> Items,
    int TotalCount,
    int UnreadCount);

/// <summary>
/// ERP modülünün okuduğu veri.
///
/// <para>
/// Servis <b>şirket kimliğini parametre olarak alır ve doğrulamaz</b>; doğrulama
/// çağıran uçta, jetonun claim'inden yapılır. Bu bilinçli bir ayrımdır: ERP modülü
/// jetonu tek bir şirkete bağlıdır ve <c>CompanyAccessGuard</c>'ın dayandığı kullanıcı
/// üyeliğine sahip değildir. Üyelik hattına sokulsaydı, GOVAI hesabı olmayan ERP
/// kullanıcıları için sahte üyelik kayıtları açmak gerekirdi.
/// </para>
///
/// <para>
/// Yalnızca <b>okuma</b> vardır. ERP modülü GOVAI'de hiçbir şey değiştirmez.
/// </para>
/// </summary>
public sealed class ErpModuleService(INotificationRepository notifications)
{
    /// <summary>Tek seferde dönen azami satır. ERP ekranı sayfalar.</summary>
    public const int MaximumPageSize = 100;

    public async Task<ErpModuleNotificationsDto> GetNotificationsAsync(
        Guid tenantId,
        Guid companyId,
        bool onlyUnread,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var sorgu = new NotificationQuery
        {
            TenantId = tenantId,
            CompanyId = companyId,
            OnlyUnread = onlyUnread ? true : null,
            Page = page < 1 ? 1 : page,
            PageSize = Math.Clamp(pageSize, 1, MaximumPageSize),
        };

        var sayfa = await notifications.ListAsync(sorgu, cancellationToken);

        return new ErpModuleNotificationsDto(
            companyId,
            sayfa.Items.Select(ToDto).ToList(),
            sayfa.TotalCount,
            sayfa.Items.Count(n => !n.IsRead));
    }

    private static ErpModuleNotificationDto ToDto(Domain.Notifications.Notification n) =>
        new(
            n.Id,
            n.Kind,
            KindLabel(n.Kind),
            n.Title,
            n.Body,
            n.OpportunityId,
            n.CreatedAt,
            n.IsRead,
            n.DeliveryStatus.ToString());

    /// <summary>Ham enum adı ERP ekranında gösterilmez.</summary>
    private static string KindLabel(NotificationKind kind) => kind switch
    {
        NotificationKind.DeadlineApproaching => "Son başvuru yaklaşıyor",
        NotificationKind.NewMatch => "Yeni fırsat",
        NotificationKind.ScoreChanged => "Skor güncellendi",
        NotificationKind.RegulationChanged => "Mevzuat değişikliği",
        NotificationKind.DocumentMissing => "Eksik belge",
        _ => "Sistem uyarısı"
    };
}
