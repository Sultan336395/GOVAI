using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Identity;

namespace GovAI.Application.Abstractions.Persistence;

/// <summary>
/// Kullanıcı–şirket üyelikleri. Faz 1'den itibaren şirket erişiminin tek kaynağıdır;
/// JWT'deki kapsam claim'i tek başına yetki vermez.
/// </summary>
public interface IUserCompanyRepository
{
    /// <summary>Belirli bir kullanıcının belirli bir şirketteki üyeliği (pasif olsa da döner).</summary>
    Task<UserCompany?> GetAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default);

    Task<UserCompany?> GetByIdAsync(Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary>Kullanıcının erişebildiği tüm şirket üyelikleri.</summary>
    Task<IReadOnlyList<UserCompany>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Yalnızca giriş akışı için.</b> Kullanıcının üyelikleri, kiracı sorgu filtresi
    /// devrede olmadan okunur.
    ///
    /// Gerekçesi: giriş anında istek henüz bir kiracıya bağlı değildir
    /// (<c>ICurrentUser.TenantId</c> boştur), bu yüzden global filtre tüm üyelikleri eler
    /// ve kullanıcının varsayılan şirketi bulunamaz. Kiracı, ancak kullanıcı bulunduktan
    /// sonra bilinir ve buraya <b>parametre olarak</b> verilir.
    ///
    /// Filtre sınırsız kaldırılmaz: kiracı ve yumuşak silme koşulları elle yeniden uygulanır.
    /// </summary>
    Task<IReadOnlyList<UserCompany>> ListForUserAtLoginAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Bir şirketteki tüm üyelikler (yönetim ekranı).</summary>
    Task<IReadOnlyList<UserCompany>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>Şirketteki aktif sahip sayısı; son sahibin kaldırılmasını engellemek için.</summary>
    Task<int> CountActiveOwnersAsync(Guid companyId, CancellationToken cancellationToken = default);

    Task AddAsync(UserCompany membership, CancellationToken cancellationToken = default);
}

public interface ICompanyGroupRepository
{
    Task<CompanyGroup?> GetAsync(Guid groupId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyGroup>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(Guid tenantId, string name, Guid? excludingId, CancellationToken cancellationToken = default);

    Task AddAsync(CompanyGroup group, CancellationToken cancellationToken = default);
}

public interface ICompanyInvitationRepository
{
    Task<CompanyInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<CompanyInvitation?> GetAsync(Guid invitationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyInvitation>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);

    Task AddAsync(CompanyInvitation invitation, CancellationToken cancellationToken = default);
}

public interface ICompanyVerificationRequestRepository
{
    Task<IReadOnlyList<CompanyVerificationRequest>> ListForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<CompanyVerificationRequest?> GetPendingAsync(Guid tenantId, string taxNumber, CancellationToken cancellationToken = default);

    Task AddAsync(CompanyVerificationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Vergi numarasının <b>başka bir kiracıda</b> kayıtlı olup olmadığını sorar.
///
/// Bilinçli olarak yalnızca <c>bool</c> döner: çağıran taraf hangi kiracı, hangi şirket,
/// hangi unvan olduğunu asla öğrenemez. Bu yüzden kiracı sorgu filtresini aşan tek
/// yer burasıdır ve dönüş tipi bilgi taşımayacak şekilde daraltılmıştır.
/// </summary>
public interface ICrossTenantCompanyLookup
{
    Task<bool> ExistsInAnotherTenantAsync(Guid currentTenantId, string taxNumber, CancellationToken cancellationToken = default);
}
