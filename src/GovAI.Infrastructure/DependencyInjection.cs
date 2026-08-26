using GovAI.Application.Abstractions.Services;
using GovAI.Infrastructure.Ai;
using GovAI.Infrastructure.Caching;
using GovAI.Infrastructure.Identity;
using GovAI.Infrastructure.Messaging;
using GovAI.Infrastructure.Options;
using GovAI.Infrastructure.Reporting;
using GovAI.Infrastructure.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GovAI.Infrastructure;

public static class DependencyInjection
{
    /// <param name="environmentName">
    /// Barındırma ortamı adı. JWT anahtar doğrulaması buna göre sıkılaşır: depodaki
    /// geliştirme anahtarı yalnızca Development'ta kabul edilir. Verilmezse en katı
    /// davranış uygulanır (üretim varsayılır).
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? environmentName = null)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Uzunluk kontrolü yetmiyor: örnek anahtar 45 karakter olduğu için onu da geçiyordu.
        services.AddSingleton<IValidateOptions<JwtOptions>>(
            new JwtOptionsValidator(environmentName ?? "Production"));

        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        // Kontrollü manuel içe aktarmanın indiricisi. Yönlendirme takibi KAPALIDIR:
        // her adım SSRF denetiminden yeniden geçmelidir (bkz. SafeDocumentDownloader).
        services.AddHttpClient(SafeDocumentDownloader.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "GOVAI-RegTech/1.0 (+kamu tesvik eslestirme; iletisim: info@talenthubik.com)");
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("tr-TR,tr;q=0.9,en;q=0.8");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
            });

        services.AddScoped<IDocumentDownloader, SafeDocumentDownloader>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IReportRenderer, ReportRenderer>();
        services.TryAddScoped<ICurrentUser, HttpContextCurrentUser>();

        AddAi(services, configuration);
        AddCache(services, configuration);
        AddMessaging(services, configuration);

        return services;
    }

    private static void AddAi(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>() ?? new OpenAiOptions();

        services.AddHttpClient<IAiExplanationClient, OpenAiExplanationClient>(client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

            if (options.IsConfigured)
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
            }
        });
    }

    private static void AddCache(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();

        if (!options.Enabled)
        {
            services.AddSingleton<ICacheService, NullCacheService>();
            return;
        }

        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            config.AbortOnConnectFail = false; // Redis geç açılırsa uygulama ayakta kalsın.
            return ConnectionMultiplexer.Connect(config);
        });

        services.AddSingleton<ICacheService>(provider =>
        {
            try
            {
                return ActivatorUtilities.CreateInstance<RedisCacheService>(provider);
            }
            catch (RedisConnectionException ex)
            {
                provider.GetRequiredService<ILogger<RedisCacheService>>()
                    .LogWarning(ex, "Redis'e bağlanılamadı; önbellek devre dışı bırakıldı.");
                return new NullCacheService();
            }
        });
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();

        if (options.Enabled)
        {
            services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        }
        else
        {
            services.AddSingleton<IEventPublisher, LoggingEventPublisher>();
        }
    }
}
