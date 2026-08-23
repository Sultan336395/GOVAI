using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using GovAI.Domain.Common;
using GovAI.Infrastructure.Identity;
using GovAI.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GovAI.Api.Tests;

/// <summary>
/// Kapsam claim'i eksik jetonların reddedildiğini (senaryo K) ve JWT imza anahtarı
/// kurallarının açılışta uygulandığını (senaryo N/O) doğrular.
/// </summary>
public sealed class ClaimAndJwtSecurityTests : IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = new();

    public Task InitializeAsync() => _factory.SeedAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ─────────────────────────── K ───────────────────────────

    [Fact(DisplayName = "K. Kapsam claim'i olmayan jeton hiçbir firmaya erişemez")]
    public async Task Kapsam_claimi_olmayan_jeton_firmaya_erisemez()
    {
        // Eski biçimli jeton: imzası geçerli, kiracısı doğru, ama company_scope taşımıyor.
        // Düzeltmeden önce bu jeton kiracının tüm firmalarına erişebiliyordu.
        var token = CreateTokenWithoutCompanyScope(_factory.TenantA);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Jeton kimlik doğrulamasından geçer...
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        // ...ama hiçbir firma verisine ulaşamaz.
        var ownCompany = await client.GetAsync($"/api/company-profile/{_factory.TenantA.CompanyId}");
        var ownDashboard = await client.GetAsync($"/api/reports/companies/{_factory.TenantA.CompanyId}/dashboard");

        Assert.Equal(HttpStatusCode.NotFound, ownCompany.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ownDashboard.StatusCode);
    }

    [Fact(DisplayName = "K2. Kimlik doğrulanmamış istek firmaya erişemez")]
    public async Task Kimliksiz_istek_firmaya_erisemez()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/company-profile/{_factory.TenantA.CompanyId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "K3. Kimliksiz CanAccessCompany artık true dönmez")]
    public void Kimliksiz_CanAccessCompany_true_donmez()
    {
        var currentUser = new HttpContextCurrentUser(new NoHttpContextAccessor());

        Assert.False(currentUser.IsAuthenticated);
        Assert.False(currentUser.CanAccessCompany(Guid.CreateVersion7()));
    }

    private static string CreateTokenWithoutCompanyScope(TenantFixture fixture)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, fixture.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, fixture.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(GovAiClaims.TenantId, fixture.TenantId.ToString()),
            new(GovAiClaims.Role, nameof(UserRole.SuperAdmin)),
            new(ClaimTypes.Role, nameof(UserRole.SuperAdmin))
            // company_scope bilinçli olarak yok.
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "govai",
            Audience = "govai-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GovAiApiFactory.ValidSigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed class NoHttpContextAccessor : Microsoft.AspNetCore.Http.IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }
    }
}

/// <summary>
/// JWT imza anahtarı doğrulayıcısının kuralları (senaryo N/O).
/// Doğrulayıcı doğrudan sınanır: uygulamanın açılışta reddetmesi bu kurala bağlıdır
/// (<c>ValidateOnStart</c> ile bağlanmıştır, bkz. Infrastructure/DependencyInjection.cs).
/// </summary>
public sealed class JwtOptionsValidatorTests
{
    private const string ProductionEnvironment = "Production";
    private const string DevelopmentEnvironment = "Development";

    [Theory(DisplayName = "N. Güvensiz imza anahtarı reddedilir")]
    [InlineData("", "boş anahtar")]
    [InlineData("   ", "yalnızca boşluk")]
    [InlineData("kisa-anahtar", "32 karakterden kısa")]
    [InlineData("CHANGE_ME_AT_LEAST_32_CHARACTERS_LONG_SECRET", "depodaki örnek anahtar (45 karakter)")]
    [InlineData("prefix-CHANGE_ME-suffix-yeterince-uzun-anahtar", "içinde CHANGE_ME geçen")]
    public void Guvensiz_anahtar_reddedilir(string key, string reason)
    {
        var result = Validate(key, ProductionEnvironment);

        Assert.True(result.Failed, $"Reddedilmesi bekleniyordu: {reason}");
        Assert.NotNull(result.FailureMessage);
    }

    [Fact(DisplayName = "N2. Geliştirme anahtarı üretim ortamında reddedilir")]
    public void Gelistirme_anahtari_uretimde_reddedilir()
    {
        const string devKey = "LOCAL-DEV-ONLY-herkese-acik-depoda-durur-uretimde-asla-kullanma";

        var inProduction = Validate(devKey, ProductionEnvironment);
        var inDevelopment = Validate(devKey, DevelopmentEnvironment);

        Assert.True(inProduction.Failed);
        Assert.True(inDevelopment.Succeeded, "Aynı anahtar Development ortamında kabul edilmelidir.");
    }

    [Fact(DisplayName = "O. Geçerli ve güvenli anahtar kabul edilir")]
    public void Gecerli_anahtar_kabul_edilir()
    {
        var result = Validate(GovAiApiFactory.ValidSigningKey, ProductionEnvironment);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact(DisplayName = "O2. Geçerli anahtarla uygulama gerçekten ayağa kalkar")]
    public async Task Gecerli_anahtarla_uygulama_ayaga_kalkar()
    {
        using var factory = new GovAiApiFactory();
        await factory.SeedAsync();

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = factory.TenantA.Email,
            password = GovAiApiFactory.Password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static ValidateOptionsResult Validate(string key, string environmentName) =>
        new JwtOptionsValidator(environmentName).Validate(
            name: null,
            new JwtOptions { SigningKey = key, Issuer = "govai", Audience = "govai-api" });
}
