using GovAI.Domain.Notifications;

namespace GovAI.Application.Abstractions.Persistence;

/// <summary>
/// Gönderim anında çözülmüş bir alıcı adresi.
///
/// <para>
/// Varlığın kendisinden (<see cref="NotificationRecipient"/>) ayrı bir tür: gönderim
/// hattının ihtiyacı yalnızca adres ve gösterim adıdır, kaydın tamamı değil.
/// </para>
/// </summary>
public sealed record RecipientAddress(string Email, string? FullName, string? Role);

/// <summary>
/// Bildirim alıcılarının veri erişimi.
///
/// <para>
/// Alıcılar <b>kullanıcı tablosundan türetilmez</b>. Müşteri çalışanlarının GOVAI
/// hesabı yoktur; kendi ERP'lerindeki GOVAI modülünü kullanırlar. Liste şirkete ait
/// ayrı bir tablodadır ve asıl kaynağı şirketin ERP'sidir.
/// </para>
///
/// <para>
/// Silme yöntemi yoktur: bırakılan alıcı pasifleştirilir. Silinebilseydi "bu uyarı
/// kime gitti" sorusunun cevabı da kaybolurdu.
/// </para>
/// </summary>
public interface INotificationRecipientRepository
{
    /// <summary>
    /// Bildirimin gideceği <b>etkin</b> alıcılar.
    ///
    /// <para>
    /// Kiracı kimliği bildirimin kendisinden gelir, oturumdan değil: gönderimi bir
    /// kullanıcı değil zamanlayıcı tetikler.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<RecipientAddress>> ListForNotificationAsync(
        Guid tenantId,
        Guid? companyId,
        CancellationToken cancellationToken = default);

    /// <summary>Şirketin tüm alıcı kayıtları (pasifler dahil); yönetim ekranı için.</summary>
    Task<IReadOnlyList<NotificationRecipient>> ListForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task AddAsync(NotificationRecipient recipient, CancellationToken cancellationToken = default);
}
