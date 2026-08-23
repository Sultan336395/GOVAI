using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Common;
using GovAI.Infrastructure.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GovAI.Infrastructure.Identity;

/// <summary>GOVAI JWT'lerinde kullanılan özel claim adları.</summary>
public static class GovAiClaims
{
    public const string TenantId = "tenant_id";
    public const string Role = "govai_role";

    /// <summary>Kapsamı sınırlanmış kullanıcının erişebileceği firma kimlikleri (virgülle ayrık).</summary>
    public const string ScopedCompanies = "scoped_companies";

    /// <summary>
    /// Firma erişim kapsamının <b>açık</b> bildirimi. Bu claim yoksa erişim reddedilir —
    /// "bilgi yoksa izin ver" davranışı bilinçli olarak kaldırılmıştır (Faz 0 / D1).
    /// </summary>
    public const string CompanyScope = "company_scope";

    /// <summary>Kullanıcı kendi kiracısındaki tüm firmalara erişebilir. Kiracı sınırı ayrıca uygulanır.</summary>
    public const string CompanyScopeTenant = "tenant";

    /// <summary>Kullanıcı yalnızca <see cref="ScopedCompanies"/> listesindeki firmalara erişebilir.</summary>
    public const string CompanyScopeList = "list";
}

/// <summary>
/// PBKDF2-HMAC-SHA256 ile parola özetleme.
/// Format: <c>pbkdf2$&lt;iterations&gt;$&lt;salt-base64&gt;$&lt;hash-base64&gt;</c>
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            // Sabit zamanlı karşılaştırma; zamanlama saldırılarına karşı.
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(
        Guid userId,
        Guid tenantId,
        string email,
        UserRole role,
        IReadOnlyCollection<Guid> scopedCompanyIds)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(GovAiClaims.TenantId, tenantId.ToString()),
            new(GovAiClaims.Role, role.ToString()),
            new(ClaimTypes.Role, role.ToString())
        };

        // Kapsam her zaman açıkça yazılır; alıcı taraf claim'in yokluğunu "sınırsız" saymaz.
        if (scopedCompanyIds.Count > 0)
        {
            claims.Add(new Claim(GovAiClaims.CompanyScope, GovAiClaims.CompanyScopeList));
            claims.Add(new Claim(GovAiClaims.ScopedCompanies, string.Join(',', scopedCompanyIds)));
        }
        else
        {
            claims.Add(new Claim(GovAiClaims.CompanyScope, GovAiClaims.CompanyScopeTenant));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }

    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// HTTP isteğindeki JWT'den aktif kullanıcıyı okur.
///
/// Güvenlik duruşu (Faz 0): erişim <b>yalnızca açıkça doğrulanabildiğinde</b> verilir.
/// Kimlik doğrulanmamış istekler ve kapsamı bildirilmemiş jetonlar firmaya erişemez.
/// Arka plan işleri bu sınıfı kullanmaz; onların yolu <see cref="SystemCurrentUser"/>'dır.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => TryGuid(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                                   ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier));

    public Guid? TenantId => TryGuid(Principal?.FindFirstValue(GovAiClaims.TenantId));

    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email)
                            ?? Principal?.FindFirstValue(ClaimTypes.Email);

    public UserRole? Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(GovAiClaims.Role), out var role) ? role : null;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();

    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;

    public bool CanAccessCompany(Guid companyId)
    {
        // Kimlik yoksa erişim yoktur. Eskiden burada true dönülüyordu; bu, kimliksiz
        // her isteğe tüm firmaları açan sessiz bir açıktı.
        if (!IsAuthenticated)
        {
            return false;
        }

        var scope = Principal?.FindFirstValue(GovAiClaims.CompanyScope);

        // Kiracı genelinde yetki. Kiracı sınırının kendisi ayrıca uygulanır
        // (EF sorgu filtresi + servis katmanındaki CompanyAccessGuard).
        if (string.Equals(scope, GovAiClaims.CompanyScopeTenant, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(scope, GovAiClaims.CompanyScopeList, StringComparison.Ordinal))
        {
            var scoped = Principal?.FindFirstValue(GovAiClaims.ScopedCompanies);
            if (string.IsNullOrWhiteSpace(scoped))
            {
                return false;
            }

            return scoped
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(id => Guid.TryParse(id, out var parsed) && parsed == companyId);
        }

        // Kapsam açıkça bildirilmemiş: eski biçimli veya elle üretilmiş jeton. Reddet.
        return false;
    }

    private static Guid? TryGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;
}

/// <summary>
/// Sistem aktörü: yalnızca uygulama içi, HTTP'siz işlemler (açılış seed'i gibi) için.
/// DI'da <see cref="ICurrentUser"/> olarak kayıtlı <b>değildir</b>; bir HTTP isteğinden
/// erişilemez. Kiracı sınırı <see cref="TenantId"/> açıkça atanarak kurulur.
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public Guid? UserId => null;

    public Guid? TenantId { get; set; }

    public string? Email => "system";

    public UserRole? Role => UserRole.SuperAdmin;

    public string? IpAddress => null;

    public string? UserAgent => "govai-worker";

    public string? CorrelationId => null;

    public bool IsAuthenticated => false;

    public bool CanAccessCompany(Guid companyId) => true;
}
