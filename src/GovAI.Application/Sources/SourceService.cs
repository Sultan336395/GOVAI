using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using GovAI.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Sources;

public sealed record SourceDto(
    Guid Id,
    string Name,
    SourceType Type,
    string BaseUrl,
    string CronExpression,
    bool IsEnabled,
    DateTimeOffset? LastRunAt,
    CrawlStatus LastRunStatus,
    string? LastRunMessage,
    int ConsecutiveFailureCount,
    string? ConfigurationJson);

public sealed record UpsertSourceRequest(string Name, SourceType Type, string BaseUrl, string CronExpression, string? ConfigurationJson);

/// <summary>Collector worker'ın topladığı ham dokümanı sisteme bırakması.</summary>
public sealed record IngestDocumentRequest
{
    public required Guid SourceId { get; init; }

    public required string Url { get; init; }

    public required string Title { get; init; }

    public required string RawContent { get; init; }

    public string MediaType { get; init; } = "text/html";

    // ── Faz 2: kanıt alanları ──

    /// <summary>Yönlendirmeler sonrası ulaşılan nihai adres.</summary>
    public string? CanonicalUrl { get; init; }

    /// <summary>Sunucunun bildirdiği karakter kümesi; kaybedilirse Türkçe karakterler bozulur.</summary>
    public string? Charset { get; init; }

    public int HttpStatusCode { get; init; } = 200;

    /// <summary>Kaynağın bildirdiği son güncelleme zamanı.</summary>
    public DateTimeOffset? LastModifiedAt { get; init; }
}

public sealed record IngestDocumentResult(
    Guid DocumentId,
    bool IsNew,
    bool ContentChanged,
    int Revision,
    /// <summary>Bu yakalanış için açılan belge sürümü. İçerik değişmediyse <c>null</c>.</summary>
    Guid? DocumentVersionId = null,
    /// <summary>Kayıt karantinaya alındıysa nedeni.</summary>
    QuarantineReason Quarantine = QuarantineReason.None);

public sealed record RecordCrawlRunRequest(CrawlStatus Status, string? Message, int DocumentCount);

/// <summary>
/// Veri Toplama ve Kaynak İzleme Modülü'nün (Modül 1) use-case servisi.
/// Kaynak tanımlarını yönetir ve worker'lardan gelen ham dokümanları sisteme alır.
/// </summary>
public sealed class SourceService(
    ISourceRepository sources,
    ISourceDocumentRepository documents,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    IEventPublisher events,
    ILogger<SourceService> logger)
{
    public async Task<IReadOnlyList<SourceDto>> ListAsync(bool onlyEnabled = false, CancellationToken cancellationToken = default)
    {
        var items = await sources.ListAsync(onlyEnabled, cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<SourceDto> GetAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var source = await sources.GetAsync(sourceId, cancellationToken)
                     ?? throw new NotFoundException("Kaynak", sourceId);

        return ToDto(source);
    }

    public async Task<SourceDto> CreateAsync(UpsertSourceRequest request, CancellationToken cancellationToken = default)
    {
        var source = new Source(request.Name, request.Type, request.BaseUrl, request.CronExpression);
        source.Configure(request.CronExpression, request.ConfigurationJson);

        await sources.AddAsync(source, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(source);
    }

    public async Task<SourceDto> UpdateAsync(Guid sourceId, UpsertSourceRequest request, CancellationToken cancellationToken = default)
    {
        var source = await sources.GetAsync(sourceId, cancellationToken)
                     ?? throw new NotFoundException("Kaynak", sourceId);

        source.Configure(request.CronExpression, request.ConfigurationJson);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(source);
    }

    public async Task<SourceDto> SetEnabledAsync(Guid sourceId, bool enabled, CancellationToken cancellationToken = default)
    {
        var source = await sources.GetAsync(sourceId, cancellationToken)
                     ?? throw new NotFoundException("Kaynak", sourceId);

        if (enabled)
        {
            source.Enable();
        }
        else
        {
            source.Disable();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(source);
    }

    /// <summary>Kaynağı takvim beklemeden hemen taramaya alır.</summary>
    public async Task TriggerCrawlAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var source = await sources.GetAsync(sourceId, cancellationToken)
                     ?? throw new NotFoundException("Kaynak", sourceId);

        await events.PublishAsync(
            QueueNames.SourceCrawlRequested,
            new { SourceId = source.Id, source.BaseUrl, source.ConfigurationJson, RequestedAt = clock.UtcNow },
            cancellationToken);

        logger.LogInformation("Manuel tarama tetiklendi. SourceId={SourceId}", sourceId);
    }

    /// <summary>
    /// Worker'ın topladığı dokümanı kaydeder. Aynı URL daha önce alınmışsa yalnızca içerik
    /// değiştiyse yeni sürüm oluşturulur; değişmediyse hiçbir iş kuyruğa bırakılmaz.
    /// </summary>
    public async Task<IngestDocumentResult> IngestDocumentAsync(IngestDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var source = await sources.GetAsync(request.SourceId, cancellationToken)
            ?? throw new NotFoundException("Kaynak", request.SourceId);

        var existing = await documents.GetByUrlAsync(request.SourceId, request.Url, cancellationToken);
        var now = clock.UtcNow;

        if (existing is null)
        {
            var document = new SourceDocument(request.SourceId, request.Url, request.Title, request.RawContent, request.MediaType, now);
            document.SetCanonicalUrl(request.CanonicalUrl);

            // Her yakalanış kalıcı bir sürüm bırakır; belge kaydı yerinde güncellense de
            // geçmiş kaybolmaz (Faz 2 kanıt zinciri).
            var firstVersion = document.AddVersion(
                request.Url,
                request.CanonicalUrl ?? request.Url,
                request.HttpStatusCode,
                request.MediaType,
                request.Charset,
                request.RawContent,
                now);

            firstVersion.SetLastModified(request.LastModifiedAt);

            var quarantine = Screen(request);
            if (quarantine != QuarantineReason.None)
            {
                document.Quarantine(quarantine, "Otomatik ön eleme.");
            }

            await documents.AddAsync(document, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (quarantine != QuarantineReason.None)
            {
                logger.LogInformation(
                    "Doküman karantinaya alındı. DocumentId={DocumentId} Neden={Reason}",
                    document.Id, quarantine);

                return new IngestDocumentResult(
                    document.Id, IsNew: true, ContentChanged: true, document.Revision,
                    firstVersion.Id, quarantine);
            }

            await events.PublishAsync(
                QueueNames.DocumentParseRequested,
                ParsePayload(source, document),
                cancellationToken);

            return new IngestDocumentResult(
                document.Id, IsNew: true, ContentChanged: true, document.Revision, firstVersion.Id);
        }

        var changed = existing.TryUpdateContent(request.RawContent, now);
        existing.SetCanonicalUrl(request.CanonicalUrl);

        SourceDocumentVersion? newVersion = null;
        if (changed)
        {
            // Aynı adresin değişen içeriği ÜSTÜNE YAZILMAZ: yeni sürüm açılır.
            newVersion = existing.AddVersion(
                request.Url,
                request.CanonicalUrl ?? request.Url,
                request.HttpStatusCode,
                request.MediaType,
                request.Charset,
                request.RawContent,
                now);

            newVersion.SetLastModified(request.LastModifiedAt);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (changed)
        {
            await events.PublishAsync(
                QueueNames.DocumentParseRequested,
                ParsePayload(source, existing),
                cancellationToken);

            logger.LogInformation(
                "Doküman içeriği değişti, yeniden ayrıştırılacak. DocumentId={DocumentId} Revision={Revision}",
                existing.Id, existing.Revision);
        }

        return new IngestDocumentResult(
            existing.Id, IsNew: false, changed, existing.Revision, newVersion?.Id, existing.QuarantineReason);
    }

    /// <summary>
    /// Katalog öncesi ön eleme (Faz 2).
    ///
    /// Menü, iletişim, hakkımızda gibi sayfalar ilan değildir; skorlanmamalı ve şirketlere
    /// fırsat olarak gösterilmemelidir. Bunlar <b>silinmez</b>, karantinaya alınır: yanlış
    /// elenen bir kayıt platform yöneticisi tarafından geri alınabilir.
    /// </summary>
    private static QuarantineReason Screen(IngestDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.RawContent))
        {
            return QuarantineReason.MissingRequiredFields;
        }

        var haystack = $"{request.Url} {request.Title}".ToLowerInvariant();

        foreach (var marker in NonOpportunityMarkers)
        {
            if (haystack.Contains(marker, StringComparison.Ordinal))
            {
                return QuarantineReason.InvalidSourcePage;
            }
        }

        // Gövdesi bir ilanı taşıyamayacak kadar kısa olan sayfalar insana bırakılır.
        return request.RawContent.Trim().Length < MinimumContentLength
            ? QuarantineReason.NeedsManualReview
            : QuarantineReason.None;
    }

    /// <summary>
    /// Kurumsal sitelerde ilan olmayan sayfaların adres ve başlıklarında geçen kalıplar.
    /// Liste bilinçli olarak dar tutulur: şüphede kalan kayıt elenmez, incelemeye gider.
    /// </summary>
    private static readonly string[] NonOpportunityMarkers =
    [
        "/iletisim", "/contact", "iletişim",
        "/hakkimizda", "/hakkinda", "/about", "hakkımızda",
        "/tarihce", "tarihçe", "/history",
        "/misyon", "/vizyon", "/mission", "/vision",
        "/sitemap", "site haritası",
        "/gizlilik", "/privacy", "/kvkk",
        "/cerez", "çerez", "/cookie",
        "/personel", "/yonetim-kurulu", "/organizasyon",
        "/basin", "/foto-galeri", "/video-galeri",
        "javascript:", "/login", "/giris"
    ];

    private const int MinimumContentLength = 200;

    public async Task RecordRunAsync(Guid sourceId, RecordCrawlRunRequest request, CancellationToken cancellationToken = default)
    {
        var source = await sources.GetAsync(sourceId, cancellationToken)
                     ?? throw new NotFoundException("Kaynak", sourceId);

        source.RecordRun(clock.UtcNow, request.Status, request.Message);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!source.IsEnabled && source.ConsecutiveFailureCount > 0)
        {
            logger.LogError(
                "Kaynak üst üste başarısız olduğu için devre dışı bırakıldı. SourceId={SourceId} Hata={Message}",
                sourceId, request.Message);
        }
    }

    /// <summary>
    /// Parser worker'ının ihtiyaç duyduğu alanların tamamı mesajda taşınır: worker'ın
    /// veritabanına erişimi yoktur, eksik alan sessizce "Bilinmiyor" kaydına dönüşür.
    /// </summary>
    private static object ParsePayload(Source source, SourceDocument document) => new
    {
        DocumentId = document.Id,
        SourceId = source.Id,
        SourceName = source.Name,
        SourceType = source.Type.ToString(),
        document.Url,
        document.Title,
        document.MediaType,
        document.CollectedAt,
    };

    private static SourceDto ToDto(Source source) => new(
        source.Id,
        source.Name,
        source.Type,
        source.BaseUrl,
        source.CronExpression,
        source.IsEnabled,
        source.LastRunAt,
        source.LastRunStatus,
        source.LastRunMessage,
        source.ConsecutiveFailureCount,
        source.ConfigurationJson);
}
