using System.Text.Json;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using GovAI.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Identity;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    UserDto User);

public sealed record UserDto(
    Guid Id,
    Guid TenantId,
    string Email,
    string FullName,
    UserRole Role,
    bool IsActive,
    DateTimeOffset? LastLoginAt);

public sealed record CreateUserRequest(string Email, string FullName, UserRole Role, string Password, IReadOnlyList<Guid>? ScopedCompanyIds);

/// <summary>
/// JWT tabanlı kimlik doğrulama. Kurumsal SSO devreye alındığında bu servis
/// yalnızca yerel kullanıcılar için kullanılmaya devam eder.
/// </summary>
public sealed class AuthenticationService(
    IUserRepository users,
    ITenantRepository tenants,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<AuthenticationService> logger)
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var user = await users.GetByEmailAsync(request.Email, cancellationToken);

        // Kullanıcı yoksa da aynı hata mesajı döner; hesap varlığı sızdırılmaz.
        if (user is null || !user.IsActive)
        {
            logger.LogWarning("Başarısız giriş denemesi. Email={Email}", request.Email);
            throw new AuthenticationFailedException("E-posta veya parola hatalı.");
        }

        if (user.IsLockedOut(now))
        {
            throw new AuthenticationFailedException("Hesap geçici olarak kilitlendi, lütfen daha sonra tekrar deneyin.");
        }

        if (user.PasswordHash is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.RecordFailedLogin(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new AuthenticationFailedException("E-posta veya parola hatalı.");
        }

        var tenant = await tenants.GetAsync(user.TenantId, cancellationToken);
        if (tenant is null || !tenant.IsActive)
        {
            throw new AuthenticationFailedException("Kurum hesabı aktif değil.");
        }

        user.RecordSuccessfulLogin(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var scopedCompanyIds = ParseScopedCompanies(user);
        var (token, expiresAt) = tokenService.CreateAccessToken(user.Id, user.TenantId, user.Email, user.Role, scopedCompanyIds);

        logger.LogInformation("Giriş başarılı. UserId={UserId} TenantId={TenantId}", user.Id, user.TenantId);

        return new LoginResponse(token, expiresAt, tokenService.CreateRefreshToken(), ToDto(user));
    }

    /// <summary>
    /// Platform işletim rolleri (10 ve üzeri) kiracı yöneticileri tarafından atanamaz.
    /// Bu roller ortak kataloğa ve worker uçlarına erişir; bir müşterinin yöneticisinin
    /// kendine veya başkasına platform yetkisi verebilmesi, kiracı sınırını anlamsız kılardı.
    /// Bu hesaplar yalnızca dağıtım yapılandırmasından (seed) oluşturulur.
    /// </summary>
    private static void EnsureAssignableByTenantAdmin(UserRole role)
    {
        if ((int)role >= 10)
        {
            throw new ForbiddenException(
                "Platform rolleri kiracı yöneticisi tarafından atanamaz. " +
                "Bu hesaplar yalnızca platform yapılandırmasıyla oluşturulur.");
        }
    }

    public async Task<UserDto> CreateUserAsync(Guid tenantId, CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAssignableByTenantAdmin(request.Role);

        var existing = await users.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
        {
            throw new ValidationException(nameof(request.Email), "Bu e-posta ile kayıtlı bir kullanıcı zaten var.");
        }

        if (request.Password.Length < 10)
        {
            throw new ValidationException(nameof(request.Password), "Parola en az 10 karakter olmalıdır.");
        }

        var user = new AppUser(tenantId, request.Email, request.FullName, request.Role);
        user.SetPasswordHash(passwordHasher.Hash(request.Password));

        if (request.ScopedCompanyIds is { Count: > 0 })
        {
            user.RestrictToCompanies(JsonSerializer.Serialize(request.ScopedCompanyIds));
        }

        await users.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var items = await users.ListAsync(tenantId, cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<UserDto> ChangeRoleAsync(Guid userId, UserRole role, CancellationToken cancellationToken = default)
    {
        EnsureAssignableByTenantAdmin(role);

        var user = await LoadUserInTenantAsync(userId, cancellationToken);

        // Mevcut platform hesabının rolü de kiracı yöneticisi tarafından düşürülemez.
        EnsureAssignableByTenantAdmin(user.Role);

        user.ChangeRole(role);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    public async Task<UserDto> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var user = await LoadUserInTenantAsync(userId, cancellationToken);

        if (isActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    private static IReadOnlyCollection<Guid> ParseScopedCompanies(AppUser user)
    {
        if (string.IsNullOrWhiteSpace(user.ScopedCompanyIdsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(user.ScopedCompanyIdsJson) ?? [];
        }
        catch (JsonException)
        {
            // Bozuk kapsam verisi yetkiyi genişletmemeli; boş liste "kısıt yok" anlamına geldiği için
            // burada güvenli taraf, kullanıcıyı hiçbir firmaya erişemez saymaktır.
            return [Guid.Empty];
        }
    }

    /// <summary>
    /// Yönetim işlemleri için kullanıcıyı yükler ve <b>çağıranın kiracısına ait olduğunu</b>
    /// doğrular. Eskiden bu kontrol yoktu: bir kiracının yöneticisi başka kiracının
    /// kullanıcısının rolünü değiştirebiliyordu (Faz 0 / D3).
    ///
    /// Başka kiracının kullanıcısı "bulunamadı" sayılır; varlığı doğrulanmaz.
    /// </summary>
    private async Task<AppUser> LoadUserInTenantAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId
                       ?? throw new ForbiddenException("İstek bir kiracıya bağlı değil.");

        var user = await users.GetAsync(userId, cancellationToken)
                   ?? throw new NotFoundException("Kullanıcı", userId);

        if (user.TenantId != tenantId)
        {
            throw new NotFoundException("Kullanıcı", userId);
        }

        return user;
    }

    private static UserDto ToDto(AppUser user) => new(
        user.Id,
        user.TenantId,
        user.Email,
        user.FullName,
        user.Role,
        user.IsActive,
        user.LastLoginAt);
}
