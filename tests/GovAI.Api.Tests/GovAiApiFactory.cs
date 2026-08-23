using System.Net.Http.Json;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Assessments;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Identity;
using GovAI.Domain.Notifications;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;
using GovAI.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GovAI.Api.Tests;

/// <summary>
/// İki kiracılı, gerçek HTTP hattı üzerinden çalışan test ortamı.
///
/// Kimlik doğrulama, yetkilendirme politikaları, controller'lar, servisler ve
/// EF sorgu filtreleri <b>gerçek uygulamadaki gibi</b> devrededir; yalnızca
/// veritabanı sağlayıcısı PostgreSQL yerine bellek içi sağlayıcıyla değiştirilir.
/// Böylece izolasyonun sahte nesnelerle değil, üretimdeki kod yoluyla
/// doğrulandığından emin oluruz.
///
/// Bilinen sınır: <c>EF.Functions.ILike</c> yalnızca Npgsql'de çalışır, bu yüzden
/// fırsat aramasında <c>search</c> parametresi bu testlerde kullanılmaz.
/// </summary>
public sealed class GovAiApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Testlerde kullanılan ortak parola; kullanıcılar gerçek giriş akışından geçer.</summary>
    public const string Password = "TestParolasi!2026";

    /// <summary>En az 32 karakter, yer tutucu iz taşımayan geçerli bir imza anahtarı.</summary>
    public const string ValidSigningKey = "govai-test-imza-anahtari-yeterince-uzun-2026";

    private readonly string _databaseName = $"govai-tests-{Guid.CreateVersion7()}";

    public TenantFixture TenantA { get; } = new("Kiracı A", "kiraci-a", "a@govai.test", "1111111111");
    public TenantFixture TenantB { get; } = new("Kiracı B", "kiraci-b", "b@govai.test", "2222222222");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development: HTTPS yönlendirmesi devreye girmez ve test istekleri düz HTTP kalır.
        // JWT anahtar kuralları ayrıca JwtStartupTests içinde üretim ortamıyla sınanır.
        builder.UseEnvironment(Environments.Development);

        // UseSetting kullanılır, ConfigureAppConfiguration değil: Program.cs jeton doğrulama
        // parametrelerini builder.Configuration'dan ÇOK ERKEN okur. Geç uygulanan bir
        // yapılandırma kaynağında jeton bir anahtarla imzalanıp başka anahtarla doğrulanır
        // ve her istek 401 döner.
        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings:Postgres"] = "Host=test;Database=test;Username=test;Password=test",
            ["Database:AutoMigrate"] = "false",
            ["Seed:Enabled"] = "false",
            ["Jwt:SigningKey"] = ValidSigningKey,
            ["Jwt:Issuer"] = "govai",
            ["Jwt:Audience"] = "govai-api",
            ["Jwt:AccessTokenMinutes"] = "60",
            ["Redis:Enabled"] = "false",
            ["RabbitMq:Enabled"] = "false",
            ["OpenAI:ApiKey"] = string.Empty
        };

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            // EF Core 10'da AddDbContext, seçenek yapılandırmasını ayrı bir kayıt olarak tutar.
            // Yalnızca DbContextOptions'ı silmek yetmez: Npgsql yapılandırması da kaldırılmazsa
            // iki sağlayıcı aynı seçenek nesnesine uygulanır ve EF hata verir.
            var obsolete = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(DbContextOptions<GovAiDbContext>) ||
                    descriptor.ServiceType == typeof(DbContextOptions) ||
                    descriptor.ServiceType == typeof(GovAiDbContext) ||
                    (descriptor.ServiceType.IsGenericType &&
                     descriptor.ServiceType.GetGenericTypeDefinition().Name
                         .StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)))
                .ToList();

            foreach (var descriptor in obsolete)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<GovAiDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }

    /// <summary>İki kiracıyı ve her birinin tam veri setini yükler.</summary>
    public async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // Ortak katalog: her iki kiracının da göreceği tek fırsat.
        var source = new Source("Test Kaynağı", SourceType.Ministry, "https://test.govai.local", "0 9 * * *");
        var opportunity = new Opportunity(
            source.Id,
            SourceType.Ministry,
            SupportCategory.Grant,
            "Ortak Katalog Çağrısı",
            "Test Bakanlığı",
            DateTimeOffset.UtcNow.AddDays(-5));
        opportunity.SetSchedule(DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(60));

        context.Sources.Add(source);
        context.Opportunities.Add(opportunity);

        SeedTenant(context, hasher, TenantA, opportunity);
        SeedTenant(context, hasher, TenantB, opportunity);

        await context.SaveChangesAsync();
    }

    private static void SeedTenant(
        GovAiDbContext context,
        IPasswordHasher hasher,
        TenantFixture fixture,
        Opportunity opportunity)
    {
        var tenant = new Tenant(fixture.Name, fixture.Slug);
        tenant.SetPlan("Professional", maxCompanies: 25);

        var admin = new AppUser(tenant.Id, fixture.Email, $"{fixture.Name} Yöneticisi", UserRole.SuperAdmin);
        admin.SetPasswordHash(hasher.Hash(Password));

        var company = new Company(tenant.Id, $"{fixture.Name} Sanayi A.Ş.", fixture.TaxNumber, LegalType.JointStockCompany);
        company.UpdateWorkforce(new Workforce(40, 15, 10, 5, 1));
        company.UpdateFinancials(new Financials(50_000_000m, 30_000_000m, 12_000_000m, 5_000_000m, "TRY", 2026));

        var outcome = EligibilityEngine.Evaluate(company, opportunity, DateTimeOffset.UtcNow);
        var assessment = new EligibilityAssessment(tenant.Id, outcome, company.ProfileVersion, "{}");

        var simulation = new ScenarioSimulation(
            tenant.Id,
            company.Id,
            $"{fixture.Name} senaryosu",
            "{}");

        var notification = new Notification(
            tenant.Id,
            company.Id,
            NotificationKind.NewMatch,
            $"{fixture.Name} için yeni fırsat",
            $"{fixture.Name} gizli bildirim gövdesi",
            DateTimeOffset.UtcNow,
            $"test:{tenant.Id}",
            opportunity.Id);

        context.Tenants.Add(tenant);
        context.Users.Add(admin);
        context.Companies.Add(company);
        context.Assessments.Add(assessment);
        context.ScenarioSimulations.Add(simulation);
        context.Notifications.Add(notification);

        fixture.TenantId = tenant.Id;
        fixture.UserId = admin.Id;
        fixture.CompanyId = company.Id;
        fixture.AssessmentId = assessment.Id;
        fixture.SimulationId = simulation.Id;
        fixture.NotificationId = notification.Id;
        fixture.OpportunityId = opportunity.Id;
    }

    /// <summary>Gerçek giriş akışından geçerek jeton alınmış HTTP istemcisi döndürür.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(TenantFixture fixture)
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = fixture.Email, password = Password });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LoginPayload>()
                      ?? throw new InvalidOperationException("Giriş yanıtı çözümlenemedi.");

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", payload.AccessToken);

        return client;
    }

    private sealed record LoginPayload(string AccessToken);
}

/// <summary>Tek bir kiracının test verisi.</summary>
public sealed class TenantFixture(string name, string slug, string email, string taxNumber)
{
    public string Name { get; } = name;
    public string Slug { get; } = slug;
    public string Email { get; } = email;
    public string TaxNumber { get; } = taxNumber;

    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AssessmentId { get; set; }
    public Guid SimulationId { get; set; }
    public Guid NotificationId { get; set; }
    public Guid OpportunityId { get; set; }
}
