using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Sources;

/// <summary>
/// Kontrollü manuel içe aktarma (Faz 2).
///
/// <para>
/// <b>Neden var:</b> bazı resmî kaynaklar otomatik taramaya uygun değildir. EKAP'ın
/// güncel arayüzü tek sayfa uygulamasıdır, eski sayfaları ASP.NET postback'iyle
/// çalışır ve kurumun yayımlanmış bir API/RSS/açık veri ucu yoktur. Böyle bir kaynağı
/// "taranıyor" gibi göstermek yerine, inceleyicinin <b>resmî ilan adresini vererek</b>
/// tek bir kaydı sisteme alması sağlanır.
/// </para>
///
/// <para>
/// <b>Bu bir tarama değildir ve öyle gösterilmez.</b> Kaynak doğrulanmamış kalır,
/// <see cref="SourceHealth"/> değişmez, tarama takvimi çalışmaz. Kayıt
/// <see cref="DocumentOrigin.ManualImport"/> olarak işaretlenir ve ekranda "elle
/// aktarıldı" diye görünür.
/// </para>
///
/// Güvenlik kuralları:
/// <list type="bullet">
///   <item>Adres <b>yalnızca</b> kaynağın resmî alan adından olabilir. Başka bir alan
///         adı reddedilir — kimse bu uçtan keyfî bir siteyi sisteme sokamaz.</item>
///   <item>İçerik <b>yapıştırılmaz, indirilir</b>: metni kullanıcı yazsaydı belge
///         resmî sayılamazdı. İndirme normal SSRF korumasından geçer.</item>
///   <item>Kaynak adres, belge özeti ve kanıt parçaları normal hattaki gibi üretilir.</item>
/// </list>
/// </summary>
public sealed class ManualImportService(
    ISourceRepository sources,
    SourceService documents,
    IDocumentDownloader downloader,
    ICurrentUser currentUser,
    ILogger<ManualImportService> logger)
{
    public async Task<ManualImportResult> ImportAsync(
        ManualImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = await sources.GetAsync(request.SourceId, cancellationToken)
            ?? throw new NotFoundException("Kaynak", request.SourceId);

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var adres)
            || adres.Scheme is not ("http" or "https"))
        {
            throw new ValidationException(
                nameof(request.Url), "Geçerli bir http/https adresi verilmelidir.");
        }

        var izinli = IsOfficialHost(adres.Host, source.Profile.OfficialDomain, source.BaseUrl);

        if (!izinli)
        {
            // Kayıt resmî kaynaktan gelmiyorsa resmî belge sayılamaz.
            throw new ValidationException(
                nameof(request.Url),
                $"Adres '{source.Name}' kaynağının resmî alan adına ait değil. "
                + $"Beklenen: {source.Profile.OfficialDomain ?? source.BaseUrl}");
        }

        var indirilen = await downloader.DownloadAsync(request.Url, cancellationToken)
            ?? throw new ValidationException(
                nameof(request.Url),
                "Adres indirilemedi. Sayfa erişilebilir değil ya da içerik türü desteklenmiyor.");

        var sonuc = await documents.IngestDocumentAsync(
            new IngestDocumentRequest
            {
                SourceId = request.SourceId,
                Url = request.Url,
                Title = string.IsNullOrWhiteSpace(request.Title) ? indirilen.Title : request.Title,
                RawContent = indirilen.Content,
                MediaType = indirilen.MediaType,
                CanonicalUrl = indirilen.FinalUrl,
                Charset = indirilen.Charset,
                HttpStatusCode = indirilen.HttpStatusCode,
                Origin = DocumentOrigin.ManualImport,
            },
            cancellationToken);

        logger.LogInformation(
            "Belge elle içe aktarıldı. Kaynak={Source} Adres={Url} Belge={DocumentId} Kullanıcı={UserId}",
            source.Name, request.Url, sonuc.DocumentId, currentUser.UserId);

        return new ManualImportResult(
            sonuc.DocumentId,
            sonuc.IsNew,
            sonuc.Revision,
            indirilen.FinalUrl,
            indirilen.HttpStatusCode,
            indirilen.Content.Length,
            SourceName: source.Name,
            Note: "Bu kayıt elle içe aktarıldı; kaynağın otomatik taraması yapılmadı.");
    }

    /// <summary>
    /// Adres kaynağın resmî alan adına mı ait? Alt alan adları kabul edilir, fakat
    /// yalnızca gerçek bir nokta sınırında: <c>sahte-ekap.kik.gov.tr.example</c> eşleşmez.
    /// </summary>
    private static bool IsOfficialHost(string host, string? officialDomain, string baseUrl)
    {
        var beklenen = officialDomain;

        if (string.IsNullOrWhiteSpace(beklenen) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var b))
        {
            beklenen = b.Host;
        }

        if (string.IsNullOrWhiteSpace(beklenen))
        {
            return false;
        }

        beklenen = beklenen.Trim().TrimEnd('.').ToLowerInvariant();
        host = host.Trim().TrimEnd('.').ToLowerInvariant();

        return host == beklenen || host.EndsWith("." + beklenen, StringComparison.Ordinal);
    }
}

public sealed record ManualImportRequest
{
    public required Guid SourceId { get; init; }

    /// <summary>Resmî ilan/belge adresi. Kaynağın resmî alan adında olmak zorundadır.</summary>
    public required string Url { get; init; }

    /// <summary>Boş bırakılırsa belgenin kendi başlığı kullanılır.</summary>
    public string? Title { get; init; }
}

public sealed record ManualImportResult(
    Guid DocumentId,
    bool IsNew,
    int Revision,
    string FinalUrl,
    int HttpStatusCode,
    int ContentLength,
    string SourceName,
    string Note);
