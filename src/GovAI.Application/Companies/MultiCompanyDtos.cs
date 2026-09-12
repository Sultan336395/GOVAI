using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Identity;

namespace GovAI.Application.Companies;

// ══════════════════════════ Şirket ekleme / düzenleme ══════════════════════════

/// <summary>
/// Panelden manuel şirket ekleme/düzenleme isteği (Faz 1).
///
/// Zorunlu alanlar: ticari unvan, vergi numarası, ülke, ana sektör, ana NACE kodu.
/// Kalan her şey sonradan tamamlanabilir — profil doluluğu bunu kullanıcıya gösterir.
/// </summary>
public sealed record CreateCompanyRequest
{
    public required string LegalName { get; init; }
    public required string TaxNumber { get; init; }
    public required string Country { get; init; }
    public required string MainSector { get; init; }
    public required string PrimaryNaceCode { get; init; }

    public string? ShortName { get; init; }
    public string? TaxOffice { get; init; }
    public string? MersisNumber { get; init; }
    public string? TradeRegistryNumber { get; init; }

    public LegalType LegalType { get; init; } = LegalType.LimitedCompany;
    public DateOnly? FoundedOn { get; init; }

    public IReadOnlyList<string> SecondaryNaceCodes { get; init; } = [];
    public IReadOnlyList<string> SubSectors { get; init; } = [];
    public IReadOnlyList<string> TargetCountries { get; init; } = [];

    public int EmployeeCount { get; init; }

    /// <summary>
    /// Personel kırılımı alanları <b>boş bırakılabilir</b> ve boş olmak "sıfır" demek
    /// değildir: <c>null</c> beyan edilmedi, <c>0</c> ise firmanın "yok" beyanıdır.
    ///
    /// <para>
    /// Eskiden bu alanlar <c>int</c>'ti; gönderilmeyen alan 0 olarak kaydediliyor ve
    /// firma o koşulu <b>sağlamıyor</b> sayılıyordu. Sahada 50 kişilik pilot firma
    /// "kadın çalışanı yok" diye kaydedilip kadın istihdamı şartı arayan çağrılardan
    /// elenmişti.
    /// </para>
    /// </summary>
    public int? RAndDEmployeeCount { get; init; }

    public int? WomenEmployeeCount { get; init; }

    /// <summary>
    /// Genç çalışan sayısı. İsteğe bağlıdır; boş bırakılırsa "belirtilmemiş" sayılır.
    /// Kişi bazlı hiçbir bilgi istenmez — yalnızca toplu sayı.
    /// </summary>
    public int? YoungEmployeeCount { get; init; }

    /// <summary>
    /// Firmanın genç çalışanı sayarken kullandığı azami yaş. Sistem bunu VARSAYMAZ:
    /// teşvik programları 25, 29 ve 30 sınırlarını birlikte kullanır ve hangi sınıra
    /// göre sayıldığı bilinmeden koşul doğrulanamaz.
    /// </summary>
    public int? YoungEmployeeMaxAge { get; init; }

    /// <summary>Engelli çalışan sayısı. Kişi bazlı engel bilgisi istenmez ve saklanmaz.</summary>
    public int? DisabledEmployeeCount { get; init; }

    public decimal AnnualRevenue { get; init; }
    public decimal BalanceSize { get; init; }

    public bool IsInTechnopark { get; init; }
    public bool ExportFlag { get; init; }

    public string? Website { get; init; }
    public string? Phone { get; init; }
    public string? CorporateEmail { get; init; }
    public string? City { get; init; }
    public string? Address { get; init; }

    public Guid? GroupId { get; init; }
    public Guid? ParentCompanyId { get; init; }
    public CompanyRelationshipType RelationshipType { get; init; } = CompanyRelationshipType.Independent;
    public bool IsHeadCompany { get; init; }
}

/// <summary>
/// Şirket ekleme sonucu. Mükerrer durumlar hata olarak değil, <b>anlamlı bir sonuç</b>
/// olarak döner: kullanıcıya ne yapabileceğini söyleyebilmek için.
/// </summary>
public enum CreateCompanyOutcome
{
    /// <summary>Şirket oluşturuldu.</summary>
    Created = 1,

    /// <summary>Bu vergi numarası zaten bu çalışma alanında kayıtlı.</summary>
    AlreadyInWorkspace = 2,

    /// <summary>
    /// Vergi numarası başka bir çalışma alanında kayıtlı. Hangi alan olduğu
    /// <b>asla</b> açıklanmaz; doğrulama talebi açılır.
    /// </summary>
    VerificationRequired = 3
}

public sealed record CreateCompanyResult(
    CreateCompanyOutcome Outcome,
    string Message,
    Guid? CompanyId,
    /// <summary>Mükerrer kayıtta, kullanıcının o şirkete erişimi varsa gitmesi için.</summary>
    bool CanNavigateToExisting,
    Guid? VerificationRequestId);

// ══════════════════════════ Şirketlerim ══════════════════════════

/// <summary>Kullanıcının erişebildiği tek bir şirketin özeti (Şirketlerim ekranı).</summary>
public sealed record MyCompanyDto(
    Guid Id,
    string LegalName,
    string? ShortName,
    string TaxNumber,
    LegalType LegalType,
    EnterpriseSize Size,
    string? PrimaryNaceCode,
    string? MainSector,
    string? City,
    int EmployeeCount,

    /// <summary>
    /// Personel kırılımı. Düzenleme ekranı bunları <b>geri yazmak zorunda</b> olduğu
    /// için özet DTO'da taşınır: taşınmasaydı form onları bilmeden gönderir ve her
    /// düzenleme firmanın beyanını silerdi. <c>null</c> "beyan edilmedi" demektir.
    /// </summary>
    int? WomenEmployeeCount,

    int? YoungEmployeeCount,

    int? YoungEmployeeMaxAge,

    int? RAndDEmployeeCount,

    int? DisabledEmployeeCount,

    decimal AnnualRevenue,
    int ProfileCompletionPercentage,
    bool IsActive,
    Guid? GroupId,
    string? GroupName,
    Guid? ParentCompanyId,
    string? ParentCompanyName,
    CompanyRelationshipType RelationshipType,
    bool IsHeadCompany,
    CompanyRole CompanyRole,
    bool IsDefault);

// ══════════════════════════ Şirket grubu ══════════════════════════

public sealed record CompanyGroupDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int CompanyCount);

public sealed record UpsertCompanyGroupRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}

/// <summary>Şirketin grup ve ana–bağlı şirket bağını değiştirme isteği.</summary>
public sealed record UpdateCompanyHierarchyRequest
{
    public Guid? GroupId { get; init; }
    public Guid? ParentCompanyId { get; init; }
    public CompanyRelationshipType RelationshipType { get; init; } = CompanyRelationshipType.Independent;
    public bool IsHeadCompany { get; init; }
}

// ══════════════════════════ Üyelik ══════════════════════════

public sealed record CompanyMemberDto(
    Guid MembershipId,
    Guid UserId,
    string Email,
    string FullName,
    CompanyRole CompanyRole,
    bool IsActive,
    bool IsDefault,
    DateTimeOffset CreatedAt);

public sealed record AddCompanyMemberRequest
{
    public required Guid UserId { get; init; }
    public required CompanyRole CompanyRole { get; init; }
}

public sealed record ChangeCompanyRoleRequest
{
    public required CompanyRole CompanyRole { get; init; }
}

/// <summary>Davet oluşturma sonucu. Jetonun açık metni <b>yalnızca burada, bir kez</b> döner.</summary>
public sealed record CompanyInvitationResult(
    Guid InvitationId,
    string Email,
    CompanyRole CompanyRole,
    DateTimeOffset ExpiresAt,
    string Token);

public sealed record CreateInvitationRequest
{
    public required string Email { get; init; }
    public required CompanyRole CompanyRole { get; init; }
    public int ValidForDays { get; init; } = 7;
}

public sealed record CompanyInvitationDto(
    Guid Id,
    string Email,
    CompanyRole CompanyRole,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt,
    bool IsRedeemable);

// ══════════════════════════ Aktif şirket ══════════════════════════

public sealed record SetActiveCompanyRequest
{
    public required Guid CompanyId { get; init; }
}

/// <summary>
/// Aktif şirket değiştirme sonucu. Yeni bir jeton döner: aktif şirket sunucuda
/// doğrulanır ve jetona yazılır, böylece istemcinin gönderdiği değere güvenilmez.
/// </summary>
public sealed record ActiveCompanyResult(
    Guid CompanyId,
    string CompanyName,
    CompanyRole CompanyRole,
    string AccessToken,
    DateTimeOffset ExpiresAt);

// ══════════════════════════ Doğrulama talebi ══════════════════════════

public sealed record VerificationRequestDto(
    Guid Id,
    string TaxNumber,
    string RequestedLegalName,
    VerificationRequestStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote);
