using GovAI.Domain.Common;

namespace GovAI.Domain.Sources;

/// <summary>
/// İzlenen resmî veri kaynağı (Modül 1). Tarama takvimi ve son durum bilgisini taşır.
/// Python collector worker'ı bu kayıtları okuyup tarama yapar.
/// </summary>
public class Source : AggregateRoot, IAuditable
{
    private Source()
    {
    }

    public Source(string name, SourceType type, string baseUrl, string cronExpression)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "Kaynak adı zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(baseUrl), "Kaynak adresi zorunludur.");
        DomainException.ThrowIf(
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps),
            "Kaynak adresi geçerli bir http/https adresi olmalıdır.");

        Name = name.Trim();
        Type = type;
        BaseUrl = baseUrl.Trim();
        CronExpression = cronExpression;
        IsEnabled = true;
    }

    public string Name { get; private set; } = string.Empty;

    public SourceType Type { get; private set; }

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>Tarama takvimi (ör. <c>0 6 * * *</c> — her gün 06:00).</summary>
    public string CronExpression { get; private set; } = "0 6 * * *";

    /// <summary>Kaynağa özgü ayarlar (seçiciler, sayfalama, kimlik doğrulama) — jsonb olarak saklanır.</summary>
    public string? ConfigurationJson { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset? LastRunAt { get; private set; }

    public CrawlStatus LastRunStatus { get; private set; } = CrawlStatus.Pending;

    public string? LastRunMessage { get; private set; }

    /// <summary>Üst üste başarısız çalışma sayısı; eşiği aşarsa kaynak otomatik devre dışı bırakılır.</summary>
    public int ConsecutiveFailureCount { get; private set; }

    // ── Faz 2: RegTech kaynak künyesi ──────────────────────────────────────

    /// <summary>Kaynağın beslediği konu (mevzuat, vergi, hibe, ihale…).</summary>
    public SourceCategory Category { get; private set; } = SourceCategory.Regulation;

    /// <summary>Yayımlayan kurum, yargı alanı, resmî alan adı, dil.</summary>
    public SourceProfile Profile { get; private set; } = SourceProfile.Empty;

    /// <summary>Tarama planı: başlangıç adresi, seçiciler, URL kalıbı, sayfa sınırı.</summary>
    public SourceCrawlPlan CrawlPlan { get; private set; } = SourceCrawlPlan.Empty;

    /// <summary>İşletim sağlığı. Tarama kararı buna bakar.</summary>
    public SourceHealth Health { get; private set; } = SourceHealth.Unverified;

    /// <summary>
    /// Yapılandırma canlı olarak doğrulandı mı? Doğrulanmamış kaynak <b>taranmaz</b>:
    /// seçicisi çalışmayan bir kaynak siteyi olduğu gibi toplar.
    /// </summary>
    public bool ConfigurationVerified { get; private set; }

    /// <summary>Doğrulamanın ne zaman yapıldığı; seçiciler kırılırsa geriye dönük bakılır.</summary>
    public DateTimeOffset? ConfigurationVerifiedAt { get; private set; }

    /// <summary>Son <b>başarılı</b> tarama. <see cref="LastRunAt"/> başarısızı da içerir.</summary>
    public DateTimeOffset? LastSuccessfulRunAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    private const int MaxConsecutiveFailures = 5;

    public void Configure(string cronExpression, string? configurationJson)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(cronExpression), "Tarama takvimi zorunludur.");
        CronExpression = cronExpression.Trim();
        ConfigurationJson = configurationJson;
    }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;

    /// <summary>Kurumsal künyeyi günceller.</summary>
    public void Describe(SourceCategory category, SourceProfile profile)
    {
        Category = category;
        Profile = profile;
    }

    /// <summary>
    /// Tarama planını günceller. Plan değiştiğinde doğrulama <b>düşer</b>: yeni seçicilerin
    /// çalıştığı kanıtlanmadan kaynak yeniden taranmamalıdır.
    /// </summary>
    public void PlanCrawl(SourceCrawlPlan plan)
    {
        CrawlPlan = plan;
        ConfigurationVerified = false;
        ConfigurationVerifiedAt = null;

        if (Health == SourceHealth.Healthy)
        {
            Health = SourceHealth.Unverified;
        }
    }

    /// <summary>
    /// Yapılandırmanın canlı doğrulandığını işaretler. Yalnızca gerçekten bağlantı
    /// çıkarılabildiğinde çağrılır; aksi hâlde <see cref="FailVerification"/> kullanılır.
    /// </summary>
    public void MarkVerified(DateTimeOffset verifiedAt)
    {
        DomainException.ThrowIf(
            !CrawlPlan.IsCrawlable,
            "Liste seçicisi veya URL kalıbı olmayan kaynak doğrulanmış sayılamaz.");

        ConfigurationVerified = true;
        ConfigurationVerifiedAt = verifiedAt;
        Health = SourceHealth.Healthy;
    }

    /// <summary>Doğrulama başarısız: kaynak taranabilir listesinden çıkar.</summary>
    public void FailVerification(string reason)
    {
        ConfigurationVerified = false;
        ConfigurationVerifiedAt = null;
        Health = SourceHealth.Failing;
        LastRunMessage = reason;
    }

    /// <summary>
    /// Tarayıcının bu kaynağı işleyip işlemeyeceği. Üç koşul da gerekir: açık olmalı,
    /// yapılandırması doğrulanmış olmalı ve planı gerçekten taranabilir olmalı.
    /// </summary>
    public bool IsCrawlable => IsEnabled && ConfigurationVerified && CrawlPlan.IsCrawlable;

    public void RecordRun(DateTimeOffset runAt, CrawlStatus status, string? message)
    {
        LastRunAt = runAt;
        LastRunStatus = status;
        LastRunMessage = message;

        if (status == CrawlStatus.Failed)
        {
            ConsecutiveFailureCount++;
            if (ConsecutiveFailureCount >= MaxConsecutiveFailures)
            {
                IsEnabled = false;
            }
        }
        else if (status == CrawlStatus.Succeeded)
        {
            ConsecutiveFailureCount = 0;
            LastSuccessfulRunAt = runAt;
        }

        Health = status switch
        {
            // Tek hata kaynağı "bozuk" yapmaz; üst üste hata sağlığı düşürür.
            CrawlStatus.Failed when ConsecutiveFailureCount >= MaxConsecutiveFailures => SourceHealth.Failing,
            CrawlStatus.Failed => SourceHealth.Degraded,
            CrawlStatus.Succeeded when ConfigurationVerified => SourceHealth.Healthy,
            _ => Health
        };
    }
}
