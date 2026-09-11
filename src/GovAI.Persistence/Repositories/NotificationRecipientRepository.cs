using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// Bildirim alıcılarının veri erişimi.
///
/// <para>
/// Sorgular kiracı süzgecini <b>aşar</b> (<c>IgnoreQueryFilters</c>) ve kiracıyı
/// kendisi yazar: gönderimi bir kullanıcı değil zamanlayıcı tetikler ve o oturumun
/// kiracısı, bildirimin kiracısı olmak zorunda değildir. Kiracı sınırı burada
/// gevşetilmez, yalnızca oturumdan değil <b>bildirimin kendisinden</b> alınır.
/// </para>
/// </summary>
public sealed class NotificationRecipientRepository(GovAiDbContext context)
    : INotificationRecipientRepository
{
    public async Task<IReadOnlyList<NotificationRecipient>> ListForNotificationAsync(
        Guid tenantId,
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        var kullanicilar = context.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.IsActive && !u.IsDeleted);

        if (companyId is null)
        {
            // Kiracı düzeyindeki sistem uyarıları yalnızca kiracı yöneticilerine gider.
            return await kullanicilar
                .Where(u => u.Role == UserRole.SuperAdmin)
                .Select(u => new NotificationRecipient(u.Id, u.Email, u.FullName))
                .ToListAsync(cancellationToken);
        }

        var uyelikler = context.UserCompanies.IgnoreQueryFilters()
            .Where(uc => uc.TenantId == tenantId
                         && uc.CompanyId == companyId
                         && uc.IsActive
                         && !uc.IsDeleted
                         // Görüntüleyici tanım gereği pasif bir izleyicidir.
                         && uc.CompanyRole != CompanyRole.CompanyViewer);

        return await uyelikler
            .Join(kullanicilar, uc => uc.UserId, u => u.Id, (_, u) => u)
            .Distinct()
            .Select(u => new NotificationRecipient(u.Id, u.Email, u.FullName))
            .ToListAsync(cancellationToken);
    }
}
