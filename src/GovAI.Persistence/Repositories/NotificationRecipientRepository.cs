using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// Bildirim alıcılarının veri erişimi.
///
/// <para>
/// Sorgular kiracı süzgecini <b>aşar</b> (<c>IgnoreQueryFilters</c>) ve kiracıyı kendisi
/// yazar: gönderimi bir kullanıcı değil zamanlayıcı tetikler ve o oturumun kiracısı
/// bildirimin kiracısı olmak zorunda değildir. Sınır gevşetilmez — yalnızca oturumdan
/// değil <b>bildirimin kendisinden</b> alınır ve her sorguda açıkça yazılır.
/// </para>
/// </summary>
public sealed class NotificationRecipientRepository(GovAiDbContext context)
    : INotificationRecipientRepository
{
    public async Task<IReadOnlyList<RecipientAddress>> ListForNotificationAsync(
        Guid tenantId,
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        // Firması olmayan bildirim (kiracı düzeyindeki sistem uyarısı) e-postayla
        // gönderilmez: alıcı listesi şirkete bağlıdır ve kiracının tamamına yazmak,
        // bir firmanın sorumlusuna başka firmanın uyarısını göndermek olurdu.
        if (companyId is null)
        {
            return [];
        }

        return await context.NotificationRecipients.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.CompanyId == companyId && r.IsActive)
            .OrderBy(r => r.Email)
            .Select(r => new RecipientAddress(r.Email, r.FullName, r.Role))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationRecipient>> ListForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        await context.NotificationRecipients
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.Email)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(
        NotificationRecipient recipient,
        CancellationToken cancellationToken = default) =>
        await context.NotificationRecipients.AddAsync(recipient, cancellationToken);
}
