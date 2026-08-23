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
}

public sealed record IngestDocumentResult(Guid DocumentId, bool IsNew, bool ContentChanged, int Revision);

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
            await documents.AddAsync(document, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await events.PublishAsync(
                QueueNames.DocumentParseRequested,
                ParsePayload(source, document),
                cancellationToken);

            return new IngestDocumentResult(document.Id, IsNew: true, ContentChanged: true, document.Revision);
        }

        var changed = existing.TryUpdateContent(request.RawContent, now);
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

        return new IngestDocumentResult(existing.Id, IsNew: false, changed, existing.Revision);
    }

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
