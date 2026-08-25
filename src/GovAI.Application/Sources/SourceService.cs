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
    string? ConfigurationJson,
    // ── Faz 2 künyesi ──
    // Worker tarama planını buradan okur; kod içinde kaynağa özgü ayar yoktur.
    SourceCategory Category,
    SourceHealth Health,
    bool ConfigurationVerified,
    DateTimeOffset? ConfigurationVerifiedAt,
    DateTimeOffset? LastSuccessfulRunAt,
    bool IsCrawlable,
    string? Authority,
    string? Jurisdiction,
    string? OfficialDomain,
    string? Language,
    string? StartUrl,
    string? ListSelector,
    string? ContentSelector,
    string? UrlPattern,
    int MaxPages,
    string? AllowedDomains,
    string? DocumentTypes);

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
/// Worker'ın canlı doğrulama sonucu (Faz 2).
///
/// Bir kaynak ancak seçicisinin gerçekten bağlantı çıkardığı kanıtlandığında taranabilir
/// hâle gelir. Erişilemeyen ya da seçicisi tutmayan kaynak <b>başarılı gösterilmez</b>;
/// nedeniyle birlikte pasif kalır.
/// </summary>
public sealed record RecordVerificationRequest
{
    public required bool Reachable { get; init; }

    /// <summary>Seçicinin çıkardığı bağlantı sayısı. Sıfırsa yapılandırma çalışmıyordur.</summary>
    public int DiscoveredLinkCount { get; init; }

    public int? HttpStatusCode { get; init; }
    public string? FinalUrl { get; init; }
    public string? Charset { get; init; }

    /// <summary>Başarısızlık nedeni; panelde olduğu gibi gösterilir.</summary>
    public string? FailureReason { get; init; }
}

public sealed record RecordVerificationResult(
    Guid SourceId,
    bool ConfigurationVerified,
    SourceHealth Health,
    bool IsEnabled,
    string Message);

/// <summary>Parser worker'ın ürettiği tek bir kanıt parçası.</summary>
public sealed record EvidenceChunkRequest
{
    public required int SequenceNumber { get; init; }
    public required string Text { get; init; }
    public required int StartOffset { get; init; }
    public required int EndOffset { get; init; }
    public int? PageNumber { get; init; }
    public string? SectionTitle { get; init; }
    public int? ParagraphNumber { get; init; }
}

/// <summary>
/// Parser worker'ın ayrıştırma sonucu.
///
/// Kanıt parçaları <b>yalnızca</b> burada, ayrıştırıcının ürettiği normalize metinden
/// yazılır. Yapay zekânın ürettiği hiçbir metin bu yolla kaydedilmez.
/// </summary>
public sealed record RecordParseResultRequest
{
    /// <summary>Hedef sürüm; verilmezse belgenin en son sürümü kullanılır.</summary>
    public Guid? DocumentVersionId { get; init; }

    public required DocumentParseStatus Status { get; init; }

    public string? NormalizedText { get; init; }
    public string? Title { get; init; }
    public string? Language { get; init; }
    public int? PageCount { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Başarısızlık nedeni; belge silinmez, karantinaya alınır.</summary>
    public string? Error { get; init; }

    public IReadOnlyList<EvidenceChunkRequest> Chunks { get; init; } = [];
}

public sealed record RecordParseResultResult(
    Guid DocumentVersionId,
    DocumentParseStatus Status,
    int ChunkCount,
    QuarantineReason Quarantine);

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

    /// <summary>
    /// Parser sonucunu belge sürümüne yazar ve kanıt parçalarını üretir.
    ///
    /// Üç durum ayrı ele alınır:
    /// <list type="bullet">
    ///   <item><b>Parsed</b> — metin ve kanıt parçaları yazılır.</item>
    ///   <item><b>NeedsOcr</b> — taranmış PDF. Uydurma metin üretilmez; belge insana
    ///         bırakılır ve karantinaya alınır.</item>
    ///   <item><b>Failed</b> — ayrıştırıcı çuvalladı. Belge <b>silinmez</b>, karantinaya
    ///         alınır ve yeniden ayrıştırılabilir.</item>
    /// </list>
    /// </summary>
    public async Task<RecordParseResultResult> RecordParseResultAsync(
        Guid documentId,
        RecordParseResultRequest request,
        CancellationToken cancellationToken = default)
    {
        var document = await documents.GetWithVersionsAsync(documentId, cancellationToken)
            ?? throw new NotFoundException("Doküman", documentId);

        var version = request.DocumentVersionId is { } versionId
            ? document.Versions.FirstOrDefault(v => v.Id == versionId)
              ?? throw new NotFoundException("Belge sürümü", versionId)
            : document.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault()
              ?? throw new NotFoundException("Belge sürümü", documentId);

        if (request.Status == DocumentParseStatus.Parsed && !string.IsNullOrWhiteSpace(request.NormalizedText))
        {
            version.RecordParse(
                request.NormalizedText,
                request.Title,
                request.Language,
                request.PageCount,
                request.PublishedAt);

            // Boş ve anlamsız parçalar kanıt sayılmaz; sessizce elenir.
            var chunks = request.Chunks
                .Where(c => !string.IsNullOrWhiteSpace(c.Text) && c.Text.Trim().Length >= MinimumChunkLength)
                .OrderBy(c => c.SequenceNumber)
                .Select(c => new DocumentEvidenceChunk(
                    version.Id,
                    c.SequenceNumber,
                    c.Text.Trim(),
                    c.StartOffset,
                    c.EndOffset,
                    c.PageNumber,
                    c.SectionTitle,
                    c.ParagraphNumber))
                .ToList();

            version.ReplaceChunks(chunks);
            document.MarkParsed(request.NormalizedText);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Belge ayrıştırıldı. DocumentId={DocumentId} Version={Version} Parça={ChunkCount}",
                document.Id, version.VersionNumber, chunks.Count);

            return new RecordParseResultResult(
                version.Id, DocumentParseStatus.Parsed, chunks.Count, document.QuarantineReason);
        }

        var requiresOcr = request.Status == DocumentParseStatus.NeedsOcr;
        var reason = requiresOcr ? "Taranmış PDF; metin katmanı yok." : (request.Error ?? "Ayrıştırma başarısız.");

        version.RecordParseFailure(reason, requiresOcr);

        // Belge SİLİNMEZ: karantinaya alınır, platform yöneticisi yeniden ayrıştırabilir.
        document.Quarantine(
            requiresOcr ? QuarantineReason.NeedsManualReview : QuarantineReason.ParserFailed,
            reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Belge ayrıştırılamadı, karantinaya alındı. DocumentId={DocumentId} Neden={Reason}",
            document.Id, reason);

        return new RecordParseResultResult(
            version.Id, version.ParseStatus, 0, document.QuarantineReason);
    }

    /// <summary>Bir kanıt parçasının anlamlı sayılması için gereken en az uzunluk.</summary>
    private const int MinimumChunkLength = 30;

    /// <summary>
    /// Canlı doğrulama sonucunu kaydeder ve kaynağı taranabilir yapar ya da pasif bırakır.
    ///
    /// Doğrulama <b>üç koşulun üçünü de</b> ister: adrese erişilebilmeli, seçici en az bir
    /// bağlantı çıkarmalı ve tarama planı zaten taranabilir olmalı. Biri eksikse kaynak
    /// pasif kalır — yarım yapılandırmayla tarama, siteyi olduğu gibi toplamak demektir.
    /// </summary>
    public async Task<RecordVerificationResult> RecordVerificationAsync(
        Guid sourceId,
        RecordVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = await sources.GetAsync(sourceId, cancellationToken)
            ?? throw new NotFoundException("Kaynak", sourceId);

        var now = clock.UtcNow;

        if (!request.Reachable)
        {
            var reason = request.FailureReason ?? "Kaynak adresine erişilemedi.";
            source.FailVerification(reason);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Kaynak doğrulanamadı. SourceId={SourceId} Neden={Reason}", sourceId, reason);

            return new RecordVerificationResult(
                sourceId, false, source.Health, source.IsEnabled, reason);
        }

        if (request.DiscoveredLinkCount <= 0)
        {
            const string reason =
                "Adrese erişildi ama liste seçicisi hiç bağlantı çıkarmadı; yapılandırma çalışmıyor.";

            source.FailVerification(reason);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new RecordVerificationResult(
                sourceId, false, source.Health, source.IsEnabled, reason);
        }

        if (!source.CrawlPlan.IsCrawlable)
        {
            const string reason = "Liste seçicisi veya URL kalıbı tanımlı değil.";
            source.FailVerification(reason);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new RecordVerificationResult(
                sourceId, false, source.Health, source.IsEnabled, reason);
        }

        source.MarkVerified(now);
        source.Enable();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var message = $"Doğrulandı: {request.DiscoveredLinkCount} bağlantı bulundu.";

        logger.LogInformation(
            "Kaynak doğrulandı. SourceId={SourceId} Bağlantı={LinkCount}",
            sourceId, request.DiscoveredLinkCount);

        return new RecordVerificationResult(sourceId, true, source.Health, source.IsEnabled, message);
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
        source.ConfigurationJson,
        source.Category,
        source.Health,
        source.ConfigurationVerified,
        source.ConfigurationVerifiedAt,
        source.LastSuccessfulRunAt,
        source.IsCrawlable,
        source.Profile.Authority,
        source.Profile.Jurisdiction,
        source.Profile.OfficialDomain,
        source.Profile.Language,
        source.CrawlPlan.StartUrl,
        source.CrawlPlan.ListSelector,
        source.CrawlPlan.ContentSelector,
        source.CrawlPlan.UrlPattern,
        source.CrawlPlan.MaxPages,
        source.CrawlPlan.AllowedDomains,
        source.CrawlPlan.DocumentTypes);
}
