using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Companies;

namespace GovAI.Application.Common;

/// <summary>
/// Şirket kimliğiyle çalışan her use-case'in geçmek zorunda olduğu tek kapı.
///
/// Kullanıcıdan gelen <c>companyId</c> asla güvenilir kabul edilmez. Her erişimde
/// dört soru sırayla cevaplanır:
/// <list type="number">
///   <item>İstek bir kiracıya bağlı mı?</item>
///   <item>Şirket gerçekten var mı?</item>
///   <item>Şirket bu kiracıya mı ait?</item>
///   <item>Kullanıcının bu şirket için açık yetkisi var mı?</item>
/// </list>
///
/// Bu sınıf savunmanın <b>ikinci</b> katmanıdır; birincisi
/// <c>GovAiDbContext.ApplyTenantFilters</c> içindeki veritabanı sorgu filtresidir.
/// Sorgu filtresi var diye buradaki kontrol kaldırılamaz: repository'ler ileride
/// filtresiz bir yol açarsa (ör. saklı yordam, ham SQL) tek koruma bu katman kalır.
///
/// Başka kiracıya ait bir şirket kimliği verildiğinde <see cref="NotFoundException"/>
/// atılır — <see cref="ForbiddenException"/> değil. Sebep: "yasak" cevabı, o kimliğin
/// var olduğunu doğrular ve kimlik sayımı (enumeration) yapılmasına izin verir.
/// </summary>
public sealed class CompanyAccessGuard(ICompanyRepository companies, ICurrentUser currentUser)
{
    /// <summary>İsteğin bağlı olduğu kiracı; yoksa istek reddedilir.</summary>
    public Guid RequireTenant() =>
        currentUser.TenantId ?? throw new ForbiddenException("İstek bir kiracıya bağlı değil.");

    /// <summary>
    /// Şirketi tüm alt koleksiyonlarıyla yükler ve erişim hakkını doğrular.
    /// Doğrulama geçilmeden çağırana hiçbir veri dönmez.
    /// </summary>
    public async Task<Company> LoadAccessibleAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var company = await companies.GetWithDetailsAsync(companyId, cancellationToken)
                      ?? throw new NotFoundException("Firma", companyId);

        if (company.TenantId != tenantId || !currentUser.CanAccessCompany(companyId))
        {
            // Başka kiracının şirketi ile hiç var olmayan şirket aynı cevabı verir.
            throw new NotFoundException("Firma", companyId);
        }

        return company;
    }

    /// <summary>Yalnızca erişim hakkını doğrular; şirket nesnesine ihtiyaç duyulmayan yollar için.</summary>
    public async Task EnsureAccessAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _ = await LoadAccessibleAsync(companyId, cancellationToken);
}
