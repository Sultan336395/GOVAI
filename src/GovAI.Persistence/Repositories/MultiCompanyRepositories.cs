using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Sources;
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


/// <summary>
/// Karantina okuma sorguları.
///
/// <b>Kiracı filtresi bilinçli olarak devre dışı bırakılmaz</b>: belgeler ve fırsatlar
/// ortak kataloğa aittir ve zaten kiracıya bağlı değildir. Değerlendirme sayımı ise
/// kiracılar arasıdır çünkü karantina kararı bütün müşterileri ilgilendirir; bu yüzden
/// yalnızca <b>sayı</b> döner, hiçbir kiracının verisi çağırana açılmaz.
/// </summary>
public sealed class QuarantineQueryRepository(GovAiDbContext context) : IQuarantineQueryRepository
{
    public async Task<IReadOnlyList<TriageCandidate>> ListTriageCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        var documents = await context.SourceDocuments
            .Where(d => d.QuarantineReason == QuarantineReason.None)
            .Select(d => new
            {
                d.Id,
                d.SourceId,
                d.Title,
                d.Url,
                d.ContentHash,
                Length = d.RawContent.Length,
                d.CollectedAt,
            })
            .ToListAsync(cancellationToken);

        var documentIds = documents.Select(d => d.Id).ToList();

        // Bir belgeden doğan fırsatlara bağlı değerlendirme sayısı.
        var assessmentCounts = await context.Opportunities
            .IgnoreQueryFilters()
            .Where(o => o.SourceDocumentId != null && documentIds.Contains(o.SourceDocumentId!.Value))
            .Select(o => new
            {
                DocumentId = o.SourceDocumentId!.Value,
                Count = context.Assessments.IgnoreQueryFilters().Count(a => a.OpportunityId == o.Id),
            })
            .ToListAsync(cancellationToken);

        var counts = assessmentCounts
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        return documents
            .Select(d => new TriageCandidate(
                d.Id, d.SourceId, d.Title, d.Url, d.ContentHash, d.Length, d.CollectedAt,
                counts.GetValueOrDefault(d.Id, 0)))
            .ToList();
    }

    public async Task<IReadOnlyList<QuarantinedDocumentDto>> ListQuarantinedAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await context.SourceDocuments
            .Where(d => d.QuarantineReason != QuarantineReason.None)
            .Join(context.Sources, d => d.SourceId, s => s.Id, (d, s) => new
            {
                d.Id, d.Title, d.Url, SourceName = s.Name, d.QuarantineReason,
                d.QuarantineNote, d.CollectedAt, VersionCount = d.Versions.Count,
            })
            .OrderByDescending(x => x.CollectedAt)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new QuarantinedDocumentDto(
                x.Id, x.Title, x.Url, x.SourceName, x.QuarantineReason,
                x.QuarantineNote, x.CollectedAt, x.VersionCount))
            .ToList();
    }

    public async Task<int> MarkAssessmentsForReevaluationAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var opportunityIds = await context.Opportunities
            .IgnoreQueryFilters()
            .Where(o => o.SourceDocumentId == documentId)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        if (opportunityIds.Count == 0)
        {
            return 0;
        }

        var assessments = await context.Assessments
            .IgnoreQueryFilters()
            .Where(a => opportunityIds.Contains(a.OpportunityId) && a.IsLatest)
            .ToListAsync(cancellationToken);

        foreach (var assessment in assessments)
        {
            // Kayıt SİLİNMEZ: yalnızca "en güncel" işareti kalkar, böylece panelde
            // geçerli sonuç gibi görünmez ve yeniden skorlama beklenir.
            assessment.Supersede();
        }

        return assessments.Count;
    }
}
