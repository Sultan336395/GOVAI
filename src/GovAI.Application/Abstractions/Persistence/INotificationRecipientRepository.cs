namespace GovAI.Application.Abstractions.Persistence;

/// <summary>Bildirimin gideceği kişi.</summary>
public sealed record NotificationRecipient(Guid UserId, string Email, string FullName);

/// <summary>
/// Bildirim alıcılarının çözümü.
///
/// <para>
/// Alıcı listesi bildirimin kendisinde tutulmaz. Tutulsaydı kayıt oluşturulduğu andaki
/// ekiple donardı: aradan geçen sürede işten ayrılan kişiye posta gider, yeni gelen
/// kişi hiçbir hatırlatma almazdı.
/// </para>
/// </summary>
public interface INotificationRecipientRepository
{
    /// <summary>
    /// Firmanın bildirim alacak <b>etkin</b> üyeleri.
    ///
    /// <para>
    /// Görüntüleyici rolü dışarıda kalır: o rol tanım gereği pasif bir izleyicidir ve
    /// başvuru süreciyle ilgili bir işi yoktur. Firma verilmezse (kiracı düzeyindeki
    /// sistem uyarıları) kiracı yöneticileri döner.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<NotificationRecipient>> ListForNotificationAsync(
        Guid tenantId,
        Guid? companyId,
        CancellationToken cancellationToken = default);
}
