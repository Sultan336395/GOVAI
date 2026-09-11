using System.Security.Claims;
using System.Text;
using GovAI.Application.Integrations;
using GovAI.Domain.Integrations;
using GovAI.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GovAI.Infrastructure.Integrations;

/// <summary>ERP modülü jetonunda kullanılan claim adları.</summary>
public static class ErpModuleClaims
{
    /// <summary>Jetonun konuşabileceği tek firma. Yetki kararı BU claim'den verilir.</summary>
    public const string CompanyId = "erp_company";

    /// <summary>Kiracı. Şirketle birlikte yazılır ki sorgular ikisiyle birden sınırlansın.</summary>
    public const string TenantId = "erp_tenant";

    /// <summary>Beyanı sunan servis kimliği.</summary>
    public const string ClientId = "erp_client";

    /// <summary>ERP'deki kullanıcının kimliği. Servis jetonunda istemci kimliğine eşittir.</summary>
    public const string Subject = "erp_subject";

    /// <summary>ERP kullanıcısının görünen adı; yalnızca denetim izinde kullanılır.</summary>
    public const string SubjectName = "erp_subject_name";

    /// <summary>Servis mi kullanıcı mı.</summary>
    public const string PrincipalKind = "erp_kind";

    public const string Scope = "scope";
}

/// <summary>
/// ERP modülü için kısa ömürlü jeton üretir.
///
/// <para>
/// Bu jeton bir <b>GOVAI kullanıcı jetonu değildir</b> ve olmamalıdır: kullanıcı
/// jetonunun taşıdığı rol, kiracı kapsamı ve firma listesi claim'lerinin hiçbirini
/// taşımaz. Taşısaydı, ERP modülü için verilen bir jeton panelin tamamında geçerli
/// olurdu.
/// </para>
///
/// <para>
/// Ömür <b>kısa</b>dır. Uzun olsaydı model düz makine anahtarına dönerdi: bir kez alınan
/// jeton aylarca kullanılır ve iptal etmenin yolu kalmazdı. Kısa ömür, kimliği devre
/// dışı bırakmanın dakikalar içinde etkili olmasını sağlar.
/// </para>
/// </summary>
public sealed class ErpTokenIssuer(IOptions<JwtOptions> jwt, IOptions<ErpAuthOptions> erp)
    : IErpTokenIssuer
{
    private readonly JwtOptions _jwt = jwt.Value;
    private readonly ErpAuthOptions _erp = erp.Value;

    public (string Token, DateTimeOffset ExpiresAt) Issue(
        Guid tenantId,
        Guid companyId,
        string clientId,
        string subject,
        string? subjectName,
        ErpPrincipalKind kind,
        DateTimeOffset now)
    {
        var expiresAt = now.AddSeconds(_erp.TokenLifetimeSeconds);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(ErpModuleClaims.TenantId, tenantId.ToString()),
            new(ErpModuleClaims.CompanyId, companyId.ToString()),
            new(ErpModuleClaims.ClientId, clientId),
            new(ErpModuleClaims.Subject, subject),
            new(ErpModuleClaims.PrincipalKind, kind.ToString()),
            new(ErpModuleClaims.Scope, ErpTokenService.Scope),
        };

        if (!string.IsNullOrWhiteSpace(subjectName))
        {
            claims.Add(new Claim(ErpModuleClaims.SubjectName, subjectName));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,

            // Alıcı kullanıcı jetonundan AYRI. Aynı olsaydı ERP modülü jetonu panelin
            // uçlarında da kabul edilirdi.
            Audience = _erp.TokenAudience,
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
