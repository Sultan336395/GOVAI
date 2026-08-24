using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Identity;

namespace GovAI.Application.Common;

/// <summary>Bir şirket işlemi için gereken en düşük yetki seviyesi.</summary>
public enum CompanyPermission
{
    /// <summary>Görüntüleme. Tüm şirket rolleri karşılar.</summary>
    Read = 0,

    /// <summary>Analiz, skorlama, simülasyon, rapor üretme. Viewer karşılamaz.</summary>
    Operate = 1,

    /// <summary>Firma profilini değiştirme. Owner ve Manager karşılar.</summary>
    ManageProfile = 2,

    /// <summary>Üyelik ve rol yönetimi. Yalnızca Owner karşılar.</summary>
    ManageMembers = 3
}

/// <summary>
/// Şirket kimliğiyle çalışan her use-case'in geçmek zorunda olduğu tek kapı.
///
/// Faz 1'de erişimin kaynağı değişti: yetki artık JWT'deki kapsam claim'inden değil,
/// <see cref="UserCompany"/> üyeliğinden okunur. Sebebi bir güvenlik gereğidir —
/// bir kullanıcının şirket erişimi kaldırıldığında elindeki jeton hâlâ geçerli olur;
/// karar veritabanından verilmezse o jeton erişmeye devam ederdi.
/// Koruyan test: <c>O. Eski JWT, kaldırılan üyelikle erişim sağlayamaz</c>.
///
/// Her erişimde beş soru sırayla cevaplanır:
/// <list type="number">
///   <item>İstek bir kiracıya bağlı mı?</item>
///   <item>Şirket gerçekten var mı?</item>
///   <item>Şirket bu kiracıya mı ait?</item>
///   <item>Kullanıcının bu şirkette <b>aktif</b> üyeliği var mı? (veritabanından)</item>
///   <item>Üyeliğin rolü, istenen işlem için yeterli mi?</item>
/// </list>
///
/// Erişim reddinde <see cref="NotFoundException"/> atılır, <see cref="ForbiddenException"/>
/// değil: "yasak" cevabı o kimliğin var olduğunu doğrular ve kimlik sayımına izin verirdi.
/// Tek istisna, üyeliği olan ama <b>rolü yetmeyen</b> kullanıcıdır: orada şirketin varlığı
/// zaten bilindiği için 403 daha doğru ve daha anlaşılır bir cevaptır.
/// </summary>
public sealed class CompanyAccessGuard(
    ICompanyRepository companies,
    IUserCompanyRepository memberships,
    ICurrentUser currentUser)
{
    public Guid RequireTenant() =>
        currentUser.TenantId ?? throw new ForbiddenException("İstek bir kiracıya bağlı değil.");

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new ForbiddenException("İstek bir kullanıcıya bağlı değil.");

    /// <summary>
    /// Şirketi yükler ve erişim hakkını doğrular. Doğrulama geçilmeden çağırana veri dönmez.
    /// </summary>
    public async Task<Company> LoadAccessibleAsync(
        Guid companyId,
        CompanyPermission required = CompanyPermission.Read,
        CancellationToken cancellationToken = default)
    {
        var company = await LoadInTenantAsync(companyId, cancellationToken);
        _ = await RequireMembershipAsync(companyId, required, cancellationToken);

        return company;
    }

    /// <summary>Şirket nesnesine ihtiyaç duyulmayan yollar için yalnızca doğrulama.</summary>
    public async Task EnsureAccessAsync(
        Guid companyId,
        CompanyPermission required = CompanyPermission.Read,
        CancellationToken cancellationToken = default) =>
        _ = await LoadAccessibleAsync(companyId, required, cancellationToken);

    /// <summary>Kullanıcının bu şirketteki üyeliği; yoksa <c>null</c>.</summary>
    public async Task<UserCompany?> FindMembershipAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return null;
        }

        var membership = await memberships.GetAsync(userId.Value, companyId, cancellationToken);
        return membership is not null && membership.GrantsAccess ? membership : null;
    }

    /// <summary>Şirketi kiracı sınırı içinde yükler; üyelik kontrolü yapmaz.</summary>
    private async Task<Company> LoadInTenantAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();

        var company = await companies.GetWithDetailsAsync(companyId, cancellationToken)
                      ?? throw new NotFoundException("Firma", companyId);

        // Başka kiracının şirketi ile hiç var olmayan şirket aynı cevabı verir.
        if (company.TenantId != tenantId)
        {
            throw new NotFoundException("Firma", companyId);
        }

        return company;
    }

    /// <summary>
    /// Veri toplama servisinin (<see cref="UserRole.SystemIngest"/>) üyelik zorunluluğundan
    /// muaf olduğu <b>tek</b> nokta.
    ///
    /// Gerekçe: gece toplu skorlama işi kiracıdaki tüm şirketleri işler; bu şirketlerin
    /// hiçbirinde insan üyeliği yoktur ve olmamalıdır. Servise sahte üyelikler açmak,
    /// üyelik tablosunu gerçek erişim kaydı olmaktan çıkarırdı.
    ///
    /// Muafiyetin sınırları dardır ve iki katmanla korunur:
    /// <list type="bullet">
    ///   <item>Kiracı sınırı burada da uygulanır — muafiyet kiracıyı aşmaz.</item>
    ///   <item>Uç seviyesinde <c>Policies.CompanyData</c> platform rollerini dışarıda
    ///         bıraktığı için rapor, skor ve simülasyon okuma yolları zaten kapalıdır.
    ///         Koruyan test: <c>T3. SystemIngest şirket raporlarını okuyamaz</c>.</item>
    /// </list>
    /// </summary>
    private bool IsSystemIngestActor => currentUser.Role == UserRole.SystemIngest;

    private async Task<UserCompany?> RequireMembershipAsync(
        Guid companyId,
        CompanyPermission required,
        CancellationToken cancellationToken)
    {
        if (IsSystemIngestActor)
        {
            return null;
        }

        var userId = RequireUser();

        var membership = await memberships.GetAsync(userId, companyId, cancellationToken);

        // Üyelik yoksa veya pasifse şirket "yok" sayılır.
        if (membership is null || !membership.GrantsAccess)
        {
            throw new NotFoundException("Firma", companyId);
        }

        if (!Satisfies(membership.CompanyRole, required))
        {
            throw new ForbiddenException(
                $"Bu işlem için yetkiniz yok. Şirketteki rolünüz: {membership.CompanyRole}.");
        }

        return membership;
    }

    /// <summary>Rol–yetki matrisi. Tek yerde tanımlıdır; controller'lar tekrar etmez.</summary>
    public static bool Satisfies(CompanyRole role, CompanyPermission required) => required switch
    {
        CompanyPermission.Read => true,

        CompanyPermission.Operate =>
            role is CompanyRole.CompanyOwner or CompanyRole.CompanyManager or CompanyRole.CompanyExpert,

        CompanyPermission.ManageProfile =>
            role is CompanyRole.CompanyOwner or CompanyRole.CompanyManager,

        CompanyPermission.ManageMembers =>
            role is CompanyRole.CompanyOwner,

        _ => false
    };
}
