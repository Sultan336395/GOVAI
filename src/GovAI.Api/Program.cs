using System.Text;
using System.Text.Json.Serialization;
using GovAI.Api.Infrastructure;
using GovAI.Application;
using GovAI.Infrastructure;
using GovAI.Infrastructure.Options;
using GovAI.Persistence;
using GovAI.Persistence.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Loglama: merkezî yapı, hata/uyarı/performans ayrımı (Teknik doküman 5.5) ----
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "GovAI.Api"));

// ---- Katmanlar ----
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres tanımlı değil.");

builder.Services.AddPersistence(connectionString);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddApplication();
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<SimulasyonSeeder>();

// ---- Kimlik doğrulama ve yetkilendirme ----
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt bölümü tanımlı değil.");

var erpAuthOptions = builder.Configuration
    .GetSection(GovAI.Infrastructure.Options.ErpAuthOptions.SectionName)
    .Get<GovAI.Infrastructure.Options.ErpAuthOptions>()
    ?? new GovAI.Infrastructure.Options.ErpAuthOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    })
    // ERP modülü jetonu AYRI bir şemadır ve ayrı bir ALICI (audience) doğrular.
    // Tek şema kullanılsaydı, ERP için verilen kısa ömürlü jeton panelin bütün
    // uçlarında da geçerli olurdu; oysa o jeton ne rol ne kiracı kapsamı taşır.
    .AddJwtBearer(ErpModuleDefaults.Scheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = erpAuthOptions.TokenAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Rol hiyerarşisi tek yerde tanımlanır; controller'lar yalnızca politika adı kullanır.
    options.AddPolicy(Policies.SuperAdmin, policy => policy.RequireRole(nameof(GovAI.Domain.Common.UserRole.SuperAdmin)));

    options.AddPolicy(Policies.ManageCompany, policy => policy.RequireRole(
        nameof(GovAI.Domain.Common.UserRole.SuperAdmin),
        nameof(GovAI.Domain.Common.UserRole.CompanyManager)));

    options.AddPolicy(Policies.Operate, policy => policy.RequireRole(
        nameof(GovAI.Domain.Common.UserRole.SuperAdmin),
        nameof(GovAI.Domain.Common.UserRole.CompanyManager),
        nameof(GovAI.Domain.Common.UserRole.OperationUser),
        nameof(GovAI.Domain.Common.UserRole.Consultant)));

    // ── Platform işletim rolleri ──
    // Kiracı SuperAdmin'i bu politikaların HİÇBİRİNDE yoktur: ortak katalog tüm
    // müşterilerce paylaşılır, bir müşterinin yöneticisi orayı değiştiremez.

    options.AddPolicy(Policies.PlatformCatalog, policy => policy.RequireRole(
        nameof(GovAI.Domain.Common.UserRole.PlatformCatalogManager)));

    options.AddPolicy(Policies.PlatformReview, policy => policy.RequireRole(
        nameof(GovAI.Domain.Common.UserRole.PlatformCatalogManager),
        nameof(GovAI.Domain.Common.UserRole.PlatformReviewer)));

    // Worker kimliği. PlatformCatalogManager da elle müdahale edebilsin diye dahildir.
    options.AddPolicy(Policies.SystemIngest, policy => policy.RequireRole(
        nameof(GovAI.Domain.Common.UserRole.SystemIngest),
        nameof(GovAI.Domain.Common.UserRole.PlatformCatalogManager)));

    // Yeniden skorlama: kiracı operasyon rolleri + worker.
    options.AddPolicy(Policies.Rescore, policy => policy.RequireRole(
        nameof(GovAI.Domain.Common.UserRole.SuperAdmin),
        nameof(GovAI.Domain.Common.UserRole.CompanyManager),
        nameof(GovAI.Domain.Common.UserRole.OperationUser),
        nameof(GovAI.Domain.Common.UserRole.Consultant),
        nameof(GovAI.Domain.Common.UserRole.SystemIngest)));

    // Kiracı şirket verisi: platform rolleri dışarıda bırakılır.
    options.AddPolicy(Policies.CompanyData, policy => policy.RequireRole(
        nameof(GovAI.Domain.Common.UserRole.SuperAdmin),
        nameof(GovAI.Domain.Common.UserRole.CompanyManager),
        nameof(GovAI.Domain.Common.UserRole.OperationUser),
        nameof(GovAI.Domain.Common.UserRole.Consultant),
        nameof(GovAI.Domain.Common.UserRole.ReadOnly)));

    // Yeni şirket kaydetme: kiracı rolleri içeri alınır, platform rolleri ve veri
    // toplama kimliği dışarıda bırakılır. Hangi kiracı kullanıcısının gerçekten
    // ekleyebileceğine servis katmanı üyelikten karar verir.
    options.AddPolicy(Policies.ManageTenantCompanies, policy => policy.RequireRole(
        nameof(GovAI.Domain.Common.UserRole.SuperAdmin),
        nameof(GovAI.Domain.Common.UserRole.CompanyManager),
        nameof(GovAI.Domain.Common.UserRole.OperationUser),
        nameof(GovAI.Domain.Common.UserRole.Consultant),
        nameof(GovAI.Domain.Common.UserRole.ReadOnly)));

    // ERP modülü: YALNIZCA kendi şeması kabul edilir ve kapsam claim'i aranır.
    // Şema kısıtlanmasaydı, panel kullanıcısının jetonu da bu uçları açardı.
    options.AddPolicy(ErpModuleDefaults.Policy, policy => policy
        .AddAuthenticationSchemes(ErpModuleDefaults.Scheme)
        .RequireAuthenticatedUser()
        .RequireClaim(
            GovAI.Infrastructure.Integrations.ErpModuleClaims.Scope,
            GovAI.Application.Integrations.ErpTokenService.Scope)
        .RequireClaim(GovAI.Infrastructure.Integrations.ErpModuleClaims.CompanyId)
        .RequireClaim(GovAI.Infrastructure.Integrations.ErpModuleClaims.TenantId));

    options.AddPolicy(Policies.Read, policy => policy.RequireAuthenticatedUser());
});

// ---- API ----
builder.Services
    .AddControllers(options => options.Filters.Add<AuditActionFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "GOVAI API",
        Version = "v1",
        Description = "Kurumsal teşvik, hibe ve ihale uygunluk analizi platformu — REST API"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT erişim jetonu. Örnek: Bearer eyJhbGciOi..."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<GovAiDbContext>("postgres");

// Yönetim paneli ayrı origin'de çalışır.
const string CorsPolicy = "govai-web";
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// ---- Hız sınırı ----
// Yalnızca ERP jeton ucu için. Uç anonimdir ve imza doğrulaması ucuz değildir;
// sınırsız bırakılırsa hem kaynak tüketim aracı hem de istemci kimliği tarama aracı
// olur. Anahtar istemci kimliğine değil ADRESE göredir: istemci kimliği saldırganın
// kontrolündedir ve her istekte değiştirilebilirdi.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(GovAI.Api.Controllers.ErpAuthController.RateLimitPolicy, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "GOVAI API v1");
        options.DocumentTitle = "GOVAI API";
    });
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

await ApplyStartupTasksAsync(app);

app.Run();

/// <summary>
/// Uygulama açılışında migration ve (yalnızca yapılandırıldıysa) başlangıç verisini uygular.
/// Üretimde migration'ın otomatik uygulanması <c>Database:AutoMigrate</c> ile kontrol edilir.
/// </summary>
static async Task ApplyStartupTasksAsync(WebApplication app)
{
    if (app.Configuration.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment()))
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();

        try
        {
            await context.Database.MigrateAsync();
            app.Logger.LogInformation("Veritabanı şeması güncel.");
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Migration uygulanamadı. Veritabanı erişilebilir mi?");
            throw;
        }
    }

    // Tanıtım simülasyonu. Başlangıç verisinden AYRI bir bayrakla çalışır: bu iş
    // bilerek yıkıcıdır (mevcut firma verisini siler) ve "demo verisi yükle" niyetiyle
    // açılan bir bayrağın yanına konulamaz. Bayrak açık unutulursa ikinci açılışta
    // hiçbir şey silinmez; seeder grubun varlığına bakıp kendini durdurur.
    if (app.Configuration.GetValue("Simulation:Enabled", false))
    {
        using var simScope = app.Services.CreateScope();
        var simulasyon = simScope.ServiceProvider.GetRequiredService<SimulasyonSeeder>();
        var sonuc = await simulasyon.KurAsync();

        app.Logger.LogInformation("Tanıtım simülasyonu sonucu: {Sonuc}", sonuc);
    }

    if (!app.Configuration.GetValue("Seed:Enabled", false))
    {
        return;
    }

    using var seedScope = app.Services.CreateScope();
    var seeder = seedScope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    var email = app.Configuration["Seed:AdminEmail"] ?? "admin@govai.local";
    var password = app.Configuration["Seed:AdminPassword"]
        ?? throw new InvalidOperationException("Seed:AdminPassword tanımlanmadan başlangıç verisi yüklenemez.");

    await seeder.SeedAsync(email, password);

    // Worker kimliği ayrı yapılandırmadan gelir; parola koda veya depoya yazılmaz.
    var workerEmail = app.Configuration["Seed:WorkerEmail"];
    var workerPassword = app.Configuration["Seed:WorkerPassword"];

    if (!string.IsNullOrWhiteSpace(workerEmail) && !string.IsNullOrWhiteSpace(workerPassword))
    {
        await seeder.SeedWorkerIdentityAsync(workerEmail, workerPassword);
        // Faz 2: resmî kaynak kataloğu. Yalnızca eksikleri ekler; var olanlara dokunmaz.
        await seeder.SeedOfficialSourceCatalogAsync();
    }
    else
    {
        app.Logger.LogWarning(
            "Seed:WorkerEmail / Seed:WorkerPassword verilmedi; worker kimliği oluşturulmadı.");
    }
}

/// <summary>Entegrasyon testlerinin <c>WebApplicationFactory&lt;Program&gt;</c> kullanabilmesi için.</summary>
public partial class Program;
