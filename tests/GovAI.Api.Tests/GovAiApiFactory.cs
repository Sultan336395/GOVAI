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
using Microsoft.EntityFrameworkCore.Diagnostics;
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
/// Varsayılan olarak bellek içi sağlayıcı kullanılır (veritabanı olmayan makinelerde de
/// <c>dotnet test</c> çalışsın diye). <c>GOVAI_TEST_POSTGRES</c> ortam değişkeni
/// ayarlanırsa aynı testler gerçek PostgreSQL 17'ye karşı koşar.
///
/// Bilinen sınır (yalnızca bellek içi modda): <c>EF.Functions.ILike</c> Npgsql'e özgüdür,
/// bu yüzden fırsat aramasında <c>search</c> parametresi bu testlerde kullanılmaz.
/// </summary>
public sealed class GovAiApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Testlerde kullanılan ortak parola; kullanıcılar gerçek giriş akışından geçer.</summary>
    public const string Password = "TestParolasi!2026";

    /// <summary>En az 32 karakter, yer tutucu iz taşımayan geçerli bir imza anahtarı.</summary>
    public const string ValidSigningKey = "govai-test-imza-anahtari-yeterince-uzun-2026";

    /// <summary>Platform hesabı açma ucunu koruyan bootstrap sırrı (yalnızca testte).</summary>
    public const string BootstrapSecret = "test-bootstrap-sirri-2026";

    /// <summary>Ortak katalog tanımını yönetebilen platform hesabı.</summary>
    public const string PlatformCatalogEmail = "platform-katalog@govai.test";

    /// <summary>Yalnızca kural düzeltme ve danışman onayı yetkisi olan platform hesabı.</summary>
    public const string PlatformReviewerEmail = "platform-inceleyici@govai.test";

    /// <summary>Worker'ın kullandığı sınırlı veri toplama kimliği.</summary>
    public const string SystemIngestEmail = "worker@govai.test";

    private readonly string _databaseName = $"govai-tests-{Guid.CreateVersion7()}";

    public TenantFixture TenantA { get; } = new("Kiracı A", "kiraci-a", "a@govai.test", "1111111111");
    public TenantFixture TenantB { get; } = new("Kiracı B", "kiraci-b", "b@govai.test", "2222222222");

    /// <summary>
    /// Hiç şirketi olmayan kiracı. "İlk şirketi yalnızca kiracı yöneticisi açabilir"
    /// kuralı yalnızca burada sınanabilir.
    /// </summary>
    public TenantFixture TenantEmpty { get; } = new("Kiracı C", "kiraci-c", "c@govai.test", "3333333333");

    /// <summary>
    /// İkinci boş kiracı. "İlk şirketi yönetici açabilir" testi <see cref="TenantEmpty"/>
    /// içine şirket eklediği için, "sıradan kullanıcı açamaz" testi ayrı ve gerçekten boş
    /// kalan bir kiracıya ihtiyaç duyar; aksi hâlde iki test birbirinin sırasına bağlanırdı.
    /// </summary>
    public TenantFixture TenantEmptyDenied { get; } = new("Kiracı D", "kiraci-d", "d@govai.test", "4444444444");

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
            ["OpenAI:ApiKey"] = string.Empty,
            ["GOVAI_PLATFORM_BOOTSTRAP_SECRET"] = BootstrapSecret
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

            // Manuel içe aktarma testleri ağa çıkmaz: indirici sahtelenir. Böylece
            // CI resmî kurum sitelerine bağımlı olmaz ve o siteler gereksiz yüklenmez.
            services.RemoveAll<IDocumentDownloader>();
            services.AddSingleton<SahteIndirici>();
            services.AddScoped<IDocumentDownloader>(sp => sp.GetRequiredService<SahteIndirici>());

            if (PostgresHost is null)
            {
                // Bellek içi sağlayıcı işlem (transaction) desteklemez ve BeginTransaction
                // çağrısında uyarıyı istisnaya çevirir. Üretimde işlem gerçekten açılır;
                // burada yok sayılması yalnızca test altyapısına ait bir tavizdir.
                // Sıra ve kısıt davranışının asıl sınandığı yer gerçek PostgreSQL koşusudur.
                services.AddDbContext<GovAiDbContext>(options => options
                    .UseInMemoryDatabase(_databaseName)
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            }
            else
            {
                // Gerçek PostgreSQL: jsonb sütunları, snake_case adlandırma ve sorgu
                // filtrelerinin SQL'e nasıl çevrildiği bellek içi sağlayıcıda sınanamaz.
                services.AddDbContext<GovAiDbContext>(options =>
                {
                    options.UseNpgsql($"{PostgresHost};Database={_databaseName}");
                    options.UseSnakeCaseNamingConvention();
                });
            }
        });
    }

    /// <summary>
    /// Ayarlanmışsa testler gerçek PostgreSQL'e karşı koşar. Her fabrika örneği kendi
    /// veritabanını oluşturur ve sonunda düşürür; paylaşılan bir şemaya dokunulmaz.
    /// Ayarlanmamışsa bellek içi sağlayıcı kullanılır, böylece veritabanı olmayan
    /// makinelerde de <c>dotnet test</c> çalışmaya devam eder.
    /// </summary>
    private static string? PostgresHost =>
        Environment.GetEnvironmentVariable("GOVAI_TEST_POSTGRES") is { Length: > 0 } value ? value : null;

    /// <summary>Gerçek PostgreSQL kullanılıyorsa şemayı kurar.</summary>
    private void EnsureSchema()
    {
        if (PostgresHost is null)
        {
            return;
        }

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();

        // Migration çalıştırılmaz; şema doğrudan modelden kurulur ve test sonunda düşürülür.
        context.Database.EnsureCreated();
    }

    private bool _databaseDropped;

    protected override void Dispose(bool disposing)
    {
        // base.Dispose zincirini yeniden girişli çağırır; test veritabanı yalnızca bir kez
        // ve servis sağlayıcısı hâlâ ayaktayken düşürülmelidir.
        if (disposing && PostgresHost is not null && !_databaseDropped)
        {
            _databaseDropped = true;

            try
            {
                using var scope = Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<GovAiDbContext>().Database.EnsureDeleted();
            }
            catch (ObjectDisposedException)
            {
                // Sağlayıcı zaten kapanmışsa temizlik test kabının silinmesiyle tamamlanır.
            }
        }

        base.Dispose(disposing);
    }

    private bool _seeded;

    /// <summary>
    /// İki kiracıyı ve her birinin tam veri setini yükler.
    ///
    /// Test sınıflarının her testte çağırması normaldir; bu yüzden <b>bir kez</b> çalışır.
    /// Aksi hâlde her çağrı aynı e-postalarla ikinci bir kiracı kümesi eklerdi: bellek içi
    /// sağlayıcı tekil dizini uygulamadığı için mükerrer kullanıcılar oluşur, giriş bir
    /// kümenin kullanıcısını döndürürken fixture kimlikleri diğerini gösterir ve testler
    /// yalnızca birlikte koştuklarında "kayıt bulunamadı" ile düşerdi.
    /// </summary>
    public async Task SeedAsync()
    {
        EnsureSchema();

        if (_seeded)
        {
            return;
        }

        _seeded = true;

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

        SeedTenant(context, hasher, TenantA, opportunity, source);
        SeedTenant(context, hasher, TenantB, opportunity, source);
        SeedEmptyTenant(context, hasher, TenantEmpty);
        SeedEmptyTenant(context, hasher, TenantEmptyDenied);

        // Platform işletim hesapları. Kiracıya bağlıdırlar (AppUser kiracı ister) ama
        // rolleri kiracı rolü değildir; ortak kataloğa ve worker uçlarına erişirler.
        SeedPlatformUser(context, hasher, TenantA.TenantId, PlatformCatalogEmail,
            "Platform Katalog Yöneticisi", UserRole.PlatformCatalogManager);
        SeedPlatformUser(context, hasher, TenantA.TenantId, PlatformReviewerEmail,
            "Platform İnceleyici", UserRole.PlatformReviewer);
        SeedPlatformUser(context, hasher, TenantA.TenantId, SystemIngestEmail,
            "Veri Toplama Servisi", UserRole.SystemIngest);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Şirketi olmayan kiracı: bir yönetici ve bir salt okuyucu, hiç şirket ve hiç üyelik yok.
    /// </summary>
    private static void SeedEmptyTenant(GovAiDbContext context, IPasswordHasher hasher, TenantFixture fixture)
    {
        var tenant = new Tenant(fixture.Name, fixture.Slug);
        tenant.SetPlan("Professional", maxCompanies: 25);

        var admin = new AppUser(tenant.Id, fixture.Email, $"{fixture.Name} Yöneticisi", UserRole.SuperAdmin);
        admin.SetPasswordHash(hasher.Hash(Password));

        var reader = new AppUser(tenant.Id, fixture.ViewerEmail, $"{fixture.Name} Görüntüleyici", UserRole.ReadOnly);
        reader.SetPasswordHash(hasher.Hash(Password));

        context.Tenants.Add(tenant);
        context.Users.Add(admin);
        context.Users.Add(reader);

        fixture.TenantId = tenant.Id;
        fixture.UserId = admin.Id;
        fixture.ViewerUserId = reader.Id;
    }

    /// <summary>Platform işletim hesabı; şirket üyeliği almaz.</summary>
    private static void SeedPlatformUser(
        GovAiDbContext context,
        IPasswordHasher hasher,
        Guid tenantId,
        string email,
        string fullName,
        UserRole role)
    {
        var user = new AppUser(tenantId, email, fullName, role);
        user.SetPasswordHash(hasher.Hash(Password));
        context.Users.Add(user);
    }

    private static void SeedTenant(
        GovAiDbContext context,
        IPasswordHasher hasher,
        TenantFixture fixture,
        Opportunity opportunity,
        Source source)
    {
        var tenant = new Tenant(fixture.Name, fixture.Slug);
        tenant.SetPlan("Professional", maxCompanies: 25);

        var admin = new AppUser(tenant.Id, fixture.Email, $"{fixture.Name} Yöneticisi", UserRole.SuperAdmin);
        admin.SetPasswordHash(hasher.Hash(Password));

        // Operate yetkisine sahip, SuperAdmin OLMAYAN sıradan kiracı kullanıcısı.
        // Ortak katalog yazma yetkisinin bu kullanıcıya kapalı olduğu sınanır.
        var operatorUser = new AppUser(tenant.Id, fixture.OperatorEmail, $"{fixture.Name} Operatörü", UserRole.OperationUser);
        operatorUser.SetPasswordHash(hasher.Hash(Password));

        // SuperAdmin olmayan şirket sahibi: "şirket ekleme yetkisi üyelikten gelir"
        // kuralının kiracı yöneticiliğinden bağımsız çalıştığı bu hesapla sınanır.
        var ownerUser = new AppUser(tenant.Id, fixture.OwnerEmail, $"{fixture.Name} Şirket Sahibi", UserRole.OperationUser);
        ownerUser.SetPasswordHash(hasher.Hash(Password));

        // Şirket rolü bazlı testler için dört ayrı hesap.
        var expertUser = new AppUser(tenant.Id, fixture.ExpertEmail, $"{fixture.Name} Uzmanı", UserRole.OperationUser);
        expertUser.SetPasswordHash(hasher.Hash(Password));

        var viewerUser = new AppUser(tenant.Id, fixture.ViewerEmail, $"{fixture.Name} Görüntüleyici", UserRole.ReadOnly);
        viewerUser.SetPasswordHash(hasher.Hash(Password));

        var company = new Company(tenant.Id, $"{fixture.Name} Sanayi A.Ş.", fixture.TaxNumber, LegalType.JointStockCompany);
        company.UpdateWorkforce(new Workforce(40, 15, 10, 5, 1));
        company.UpdateFinancials(new Financials(50_000_000m, 30_000_000m, 12_000_000m, 5_000_000m, "TRY", 2026));

        // Aynı kiracıda ikinci tüzel şirket: şirketler arası geçiş testleri için.
        var secondCompany = new Company(
            tenant.Id, $"{fixture.Name} Lojistik A.Ş.", fixture.SecondTaxNumber, LegalType.LimitedCompany);
        secondCompany.UpdateWorkforce(new Workforce(12, 4, 3, 1, 0));

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
        context.Users.Add(operatorUser);
        context.Users.Add(ownerUser);
        context.Users.Add(expertUser);
        context.Users.Add(viewerUser);
        context.Companies.Add(company);
        context.Companies.Add(secondCompany);

        // Faz 1: şirket erişimi artık üyelikten doğrulanır. Yönetici iki şirkete de
        // sahip olarak bağlanır; diğerleri yalnızca birinci şirkete ve farklı rollerle.
        context.UserCompanies.AddRange(
            new UserCompany(tenant.Id, admin.Id, company.Id, CompanyRole.CompanyOwner, isDefault: true),
            new UserCompany(tenant.Id, admin.Id, secondCompany.Id, CompanyRole.CompanyOwner),
            new UserCompany(tenant.Id, operatorUser.Id, company.Id, CompanyRole.CompanyManager, isDefault: true),
            // İKİNCİ şirkete bağlanır: birinci şirkette tek sahip (admin) kalmalı ki
            // "son sahip kaldırılamaz" kuralı orada sınanabilsin.
            new UserCompany(tenant.Id, ownerUser.Id, secondCompany.Id, CompanyRole.CompanyOwner, isDefault: true),
            new UserCompany(tenant.Id, expertUser.Id, company.Id, CompanyRole.CompanyExpert, isDefault: true),
            new UserCompany(tenant.Id, viewerUser.Id, company.Id, CompanyRole.CompanyViewer, isDefault: true));
        context.Assessments.Add(assessment);
        context.ScenarioSimulations.Add(simulation);
        context.Notifications.Add(notification);

        fixture.TenantId = tenant.Id;
        fixture.UserId = admin.Id;
        fixture.OperatorUserId = operatorUser.Id;
        fixture.OwnerUserId = ownerUser.Id;
        fixture.ExpertUserId = expertUser.Id;
        fixture.ViewerUserId = viewerUser.Id;
        fixture.SourceId = source.Id;
        fixture.CompanyId = company.Id;
        fixture.SecondCompanyId = secondCompany.Id;
        fixture.AssessmentId = assessment.Id;
        fixture.SimulationId = simulation.Id;
        fixture.NotificationId = notification.Id;
        fixture.OpportunityId = opportunity.Id;
    }

    /// <summary>Gerçek giriş akışından geçerek jeton alınmış HTTP istemcisi döndürür.</summary>
    public Task<HttpClient> CreateAuthenticatedClientAsync(TenantFixture fixture) =>
        CreateAuthenticatedClientAsync(fixture.Email);

    /// <summary>Belirli bir kullanıcı hesabıyla giriş yapar (rol bazlı yetki testleri için).</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string? password = null)
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email, password = password ?? Password });
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

    /// <summary>Şirkette CompanyManager rolündeki hesap.</summary>
    public string OperatorEmail { get; } = $"operator-{email}";

    /// <summary>Şirkette CompanyExpert rolündeki hesap.</summary>
    /// <summary>SuperAdmin <b>olmayan</b>, şirkette CompanyOwner rolündeki hesap.</summary>
    public string OwnerEmail { get; } = $"sahip-{email}";

    public string ExpertEmail { get; } = $"uzman-{email}";

    /// <summary>Şirkette CompanyViewer rolündeki hesap.</summary>
    public string ViewerEmail { get; } = $"okuyucu-{email}";

    /// <summary>Aynı kiracıdaki ikinci tüzel şirketin vergi numarası.</summary>
    public string SecondTaxNumber { get; } = taxNumber[..^1] + "9";

    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid OperatorUserId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid ExpertUserId { get; set; }
    public Guid ViewerUserId { get; set; }
    public Guid SourceId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SecondCompanyId { get; set; }
    public Guid AssessmentId { get; set; }
    public Guid SimulationId { get; set; }
    public Guid NotificationId { get; set; }
    public Guid OpportunityId { get; set; }
}

/// <summary>
/// Manuel içe aktarma testleri için sahte indirici.
///
/// Gerçek indirici (<c>SafeDocumentDownloader</c>) ağa çıkar ve SSRF denetimi yapar;
/// testte adres denetimi <c>ManualImportService</c> katmanında sınanır, ağ trafiği
/// gerekmez.
/// </summary>
public sealed class SahteIndirici : IDocumentDownloader
{
    private readonly Dictionary<string, DownloadedDocument> _icerikler = new(StringComparer.Ordinal);

    /// <summary>Kaç kez indirme denendi? Adres reddedilmişse hiç artmamalı.</summary>
    public int CagriSayisi { get; private set; }

    public void Ayarla(string url, DownloadedDocument belge) => _icerikler[url] = belge;

    public void Temizle()
    {
        _icerikler.Clear();
        CagriSayisi = 0;
    }

    public Task<DownloadedDocument?> DownloadAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        CagriSayisi++;
        return Task.FromResult(_icerikler.GetValueOrDefault(url));
    }
}

