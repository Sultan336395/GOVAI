using GovAI.Domain.Common;

namespace GovAI.Domain.Identity;

/// <summary>
/// Kullanıcı ↔ şirket üyeliği. Faz 1'den itibaren şirket erişiminin <b>tek kaynağıdır</b>;
/// <c>AppUser.ScopedCompanyIdsJson</c> yalnızca geçici geri dönüş uyumluluğu için durur.
///
/// Neden ayrı tablo: eski jsonb listesi yabancı anahtarla korunamıyor, indekslenemiyor
/// ve şirket başına rol tutamıyordu. Aynı kullanıcı bir şirkette sahip, başkasında
/// yalnızca görüntüleyici olabilmelidir.
///
/// Değişmez kurallar (servis katmanında uygulanır):
/// <list type="bullet">
///   <item>Bir kullanıcı aynı şirkete iki kez bağlanamaz — <c>(user_id, company_id)</c> benzersiz.</item>
///   <item>Bir kullanıcının en fazla bir varsayılan şirketi olabilir.</item>
///   <item>Her şirkette en az bir aktif <see cref="CompanyRole.CompanyOwner"/> bulunmalıdır.</item>
/// </list>
/// </summary>
public class UserCompany : AggregateRoot, IAuditable, ISoftDeletable, ITenantScoped
{
    private UserCompany()
    {
    }

    public UserCompany(Guid tenantId, Guid userId, Guid companyId, CompanyRole companyRole, bool isDefault = false)
    {
        DomainException.ThrowIf(userId == Guid.Empty, "Kullanıcı zorunludur.");
        DomainException.ThrowIf(companyId == Guid.Empty, "Şirket zorunludur.");

        TenantId = tenantId;
        UserId = userId;
        CompanyId = companyId;
        CompanyRole = companyRole;
        IsDefault = isDefault;
        IsActive = true;
    }

    public Guid TenantId { get; set; }

    public Guid UserId { get; private set; }

    public Guid CompanyId { get; private set; }

    public CompanyRole CompanyRole { get; private set; }

    /// <summary>Kullanıcı giriş yaptığında öntanımlı açılacak şirket.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Pasif üyelik erişim vermez; kayıt izlenebilirlik için silinmez.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public void ChangeRole(CompanyRole role) => CompanyRole = role;

    public void MarkDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;

    public void Deactivate()
    {
        IsActive = false;
        IsDefault = false;
    }

    public void Activate() => IsActive = true;

    /// <summary>Üyeliğin şu anda erişim verip vermediği.</summary>
    public bool GrantsAccess => IsActive && !IsDeleted;

    /// <summary>Şirket ve üyelik yönetimi yetkisi.</summary>
    public bool CanManageMembers => CompanyRole is CompanyRole.CompanyOwner;

    /// <summary>Firma profilini ve operasyonel ayarları değiştirme yetkisi.</summary>
    public bool CanManageProfile => CompanyRole is CompanyRole.CompanyOwner or CompanyRole.CompanyManager;

    /// <summary>Skorlama, simülasyon ve rapor üretme gibi analiz işlemleri.</summary>
    public bool CanOperate =>
        CompanyRole is CompanyRole.CompanyOwner or CompanyRole.CompanyManager or CompanyRole.CompanyExpert;
}
