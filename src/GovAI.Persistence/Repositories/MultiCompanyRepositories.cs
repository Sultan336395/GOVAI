using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

public sealed class UserCompanyRepository(GovAiDbContext context) : IUserCompanyRepository
{
    public Task<UserCompany?> GetAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default) =>
        context.UserCompanies
            .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.CompanyId == companyId, cancellationToken);

    public Task<UserCompany?> GetByIdAsync(Guid membershipId, CancellationToken cancellationToken = default) =>
        context.UserCompanies.FirstOrDefaultAsync(uc => uc.Id == membershipId, cancellationToken);

    public async Task<IReadOnlyList<UserCompany>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.UserCompanies
            .Where(uc => uc.UserId == userId)
            .OrderByDescending(uc => uc.IsDefault)
            .ThenBy(uc => uc.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Giriş akışına özel, <b>yetkilendirilmiş</b> filtre atlaması (belgelenmiş istisna #3,
    /// bkz. docs/security.md). Kiracı ve yumuşak silme koşulları elle geri konur; bu yüzden
    /// filtre atlanmış olsa da kapsam daralmaz.
    /// </summary>
    public async Task<IReadOnlyList<UserCompany>> ListForUserAtLoginAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await context.UserCompanies
            .IgnoreQueryFilters()
            .Where(uc => uc.UserId == userId
                         && uc.TenantId == tenantId
                         && !uc.IsDeleted)
            .OrderByDescending(uc => uc.IsDefault)
            .ThenBy(uc => uc.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserCompany>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await context.UserCompanies
            .Where(uc => uc.CompanyId == companyId)
            .OrderBy(uc => uc.CompanyRole)
            .ThenBy(uc => uc.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<int> CountActiveOwnersAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        context.UserCompanies
            .CountAsync(
                uc => uc.CompanyId == companyId
                      && uc.IsActive
                      && uc.CompanyRole == CompanyRole.CompanyOwner,
                cancellationToken);

    public async Task AddAsync(UserCompany membership, CancellationToken cancellationToken = default) =>
        await context.UserCompanies.AddAsync(membership, cancellationToken);
}

public sealed class CompanyGroupRepository(GovAiDbContext context) : ICompanyGroupRepository
{
    public Task<CompanyGroup?> GetAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        context.CompanyGroups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

    public async Task<IReadOnlyList<CompanyGroup>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.CompanyGroups
            .Where(g => g.TenantId == tenantId)
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> NameExistsAsync(Guid tenantId, string name, Guid? excludingId, CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim();

        return context.CompanyGroups.AnyAsync(
            g => g.TenantId == tenantId
                 && g.Name == normalized
                 && (excludingId == null || g.Id != excludingId),
            cancellationToken);
    }

    public async Task AddAsync(CompanyGroup group, CancellationToken cancellationToken = default) =>
        await context.CompanyGroups.AddAsync(group, cancellationToken);
}

public sealed class CompanyInvitationRepository(GovAiDbContext context) : ICompanyInvitationRepository
{
    public Task<CompanyInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        context.CompanyInvitations.FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

    public Task<CompanyInvitation?> GetAsync(Guid invitationId, CancellationToken cancellationToken = default) =>
        context.CompanyInvitations.FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);

    public async Task<IReadOnlyList<CompanyInvitation>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await context.CompanyInvitations
            .Where(i => i.CompanyId == companyId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(CompanyInvitation invitation, CancellationToken cancellationToken = default) =>
        await context.CompanyInvitations.AddAsync(invitation, cancellationToken);
}

public sealed class CompanyVerificationRequestRepository(GovAiDbContext context) : ICompanyVerificationRequestRepository
{
    public async Task<IReadOnlyList<CompanyVerificationRequest>> ListForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.CompanyVerificationRequests
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(cancellationToken);

    public Task<CompanyVerificationRequest?> GetPendingAsync(Guid tenantId, string taxNumber, CancellationToken cancellationToken = default)
    {
        var normalized = taxNumber.Trim();

        return context.CompanyVerificationRequests.FirstOrDefaultAsync(
            r => r.TenantId == tenantId
                 && r.TaxNumber == normalized
                 && r.Status == VerificationRequestStatus.Pending,
            cancellationToken);
    }

    public async Task AddAsync(CompanyVerificationRequest request, CancellationToken cancellationToken = default) =>
        await context.CompanyVerificationRequests.AddAsync(request, cancellationToken);
}

/// <summary>
/// Vergi numarasının başka bir kiracıda kullanılıp kullanılmadığını sorar.
///
/// Kod tabanındaki <b>ikinci</b> ve son <c>IgnoreQueryFilters</c> kullanımıdır
/// (ilki: kimlik doğrulama için e-posta araması). Gerekçesi:
/// "bu vergi numarası başka bir çalışma alanında kayıtlı" cevabını verebilmek için
/// kiracı sınırının dışına bakmak zorunludur.
///
/// Sızıntı koruması dönüş tipinde: yalnızca <c>bool</c> döner. Şirketin adı, kimliği
/// veya hangi kiracıya ait olduğu çağırana hiçbir şekilde ulaşmaz.
/// Koruyan test: <c>E. Başka tenanttaki vergi numarası şirket bilgisini sızdırmaz</c>.
/// </summary>
public sealed class CrossTenantCompanyLookup(GovAiDbContext context) : ICrossTenantCompanyLookup
{
    public Task<bool> ExistsInAnotherTenantAsync(
        Guid currentTenantId,
        string taxNumber,
        CancellationToken cancellationToken = default)
    {
        var normalized = taxNumber.Trim();

        return context.Companies
            .IgnoreQueryFilters()
            .AnyAsync(
                c => c.TaxNumber == normalized
                     && c.TenantId != currentTenantId
                     && !c.IsDeleted,
                cancellationToken);
    }
}
