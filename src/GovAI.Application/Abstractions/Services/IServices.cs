using GovAI.Domain.Common;

namespace GovAI.Application.Abstractions.Services;

/// <summary>Test edilebilirlik için zamanı soyutlar; domain testleri sabit tarihle çalışır.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }

    DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

/// <summary>Aktif isteği yapan kullanıcı. API katmanı JWT'den doldurur.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid? TenantId { get; }

    string? Email { get; }

    UserRole? Role { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    string? CorrelationId { get; }

    bool IsAuthenticated { get; }

    /// <summary>Danışman rolü belirli firmalarla sınırlandırılmışsa erişim kontrolü burada yapılır.</summary>
    bool CanAccessCompany(Guid companyId);

    /// <summary>
    /// Jetondaki aktif şirket. Yalnızca bilgilendirme amaçlıdır; erişim kararı
    /// her zaman veritabanındaki üyelikten verilir.
    /// </summary>
    Guid? ActiveCompanyId { get; }
}

/// <summary>Redis üzerinden okuma yoğun sorguların önbelleklenmesi.</summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}

/// <summary>RabbitMQ'ya iş bırakır; Python worker'ları bu kuyrukları dinler.</summary>
public interface IEventPublisher
{
    Task PublishAsync<T>(string routingKey, T payload, CancellationToken cancellationToken = default) where T : class;
}

/// <summary>Uygulama genelinde kullanılan kuyruk adları.</summary>
public static class QueueNames
{
    /// <summary>Kaynak tarama isteği → collector worker.</summary>
    public const string SourceCrawlRequested = "govai.source.crawl.requested";

    /// <summary>Yeni ham doküman → parser worker.</summary>
    public const string DocumentParseRequested = "govai.document.parse.requested";

    /// <summary>Ayrıştırılmış metin → kural çıkarım worker'ı (AI).</summary>
    public const string RuleExtractionRequested = "govai.rules.extraction.requested";

    /// <summary>Firma profili değişti → skorların yeniden hesaplanması.</summary>
    public const string ScoringRequested = "govai.scoring.requested";

    /// <summary>Bildirim gönderim isteği.</summary>
    public const string NotificationDispatchRequested = "govai.notification.dispatch.requested";
}

/// <summary>Parola özetleme; Infrastructure katmanında PBKDF2 ile uygulanır.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}

/// <summary>JWT üretimi.</summary>
public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(
        Guid userId,
        Guid tenantId,
        string email,
        UserRole role,
        IReadOnlyCollection<Guid> scopedCompanyIds,
        Guid? activeCompanyId = null);

    string CreateRefreshToken();
}

/// <summary>PDF ve Excel çıktıları (Modül 9).</summary>
public interface IReportRenderer
{
    Task<byte[]> RenderPdfAsync(string title, string htmlBody, CancellationToken cancellationToken = default);

    Task<byte[]> RenderExcelAsync(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, CancellationToken cancellationToken = default);
}
