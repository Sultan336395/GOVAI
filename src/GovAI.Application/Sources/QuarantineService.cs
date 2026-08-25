using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Sources;

/// <summary>Karantina triyaj raporunun tek satırı.</summary>
public sealed record TriageRow(
    Guid DocumentId,
    string Title,
    string Url,
    string SourceName,
    QuarantineReason ProposedReason,
    string Evidence,
    string? MissingField,
    bool IsScored,
    int LinkedAssessmentCount,
    string RecommendedAction);

public sealed record TriageReport(
    int Reviewed,
    int Clean,
    int Flagged,
    int Applied,
    int AffectedAssessments,
    IReadOnlyList<TriageRow> Rows);

/// <summary>Karantinadaki tek bir kaydın inceleme görünümü.</summary>
public sealed record QuarantinedDocumentDto(
    Guid DocumentId,
    string Title,
    string Url,
    string SourceName,
    QuarantineReason Reason,
    string? Note,
    DateTimeOffset CollectedAt,
    int VersionCount);

/// <summary>
/// Karantina yönetimi (Faz 2).
///
/// Karantina <b>silme değildir</b>. Kayıt durur, incelenebilir ve yeniden ayrıştırılabilir;
/// yalnızca katalog dışında tutulur: şirketlere fırsat olarak gösterilmez, skorlanmaz,
/// bildirim üretmez ve haftalık rapora girmez.
///
/// Triyaj <b>kanıta göre</b> sınıflandırır. Belirli bir sayıya ulaşmak için kayıt
/// işaretlenmez; kural eşleşmiyorsa kayıt temiz kalır.
/// </summary>
public sealed class QuarantineService(
    ISourceDocumentRepository documents,
    ISourceRepository sources,
    IQuarantineQueryRepository query,
    IUnitOfWork unitOfWork,
    ILogger<QuarantineService> logger)
{
    /// <summary>
    /// Kurumsal sayfaları ilan sayfalarından ayıran adres/başlık kalıpları.
    ///
    /// Liste bilinçli olarak dar tutulur: şüphede kalan kayıt elenmez, incelemeye gider.
    /// Yanlış eleme, gerçek bir çağrının şirketlere hiç ulaşmaması demektir.
    /// </summary>
    private static readonly (string Marker, string Explanation)[] InstitutionalPages =
    [
        ("hakkimizda", "kurumsal tanıtım sayfası"),
        ("hakkında", "kurumsal tanıtım sayfası"),
        ("about", "kurumsal tanıtım sayfası"),
        ("iletisim", "iletişim sayfası"),
        ("iletişim", "iletişim sayfası"),
        ("contact", "iletişim sayfası"),
        ("tarihce", "kurum tarihçesi"),
        ("tarihçe", "kurum tarihçesi"),
        ("organizasyon", "organizasyon şeması"),
        ("insan-kaynaklari", "insan kaynakları sayfası"),
        ("insan kaynakları", "insan kaynakları sayfası"),
        ("etik", "etik komisyonu sayfası"),
        ("kurumsal-kimlik", "kurumsal kimlik sayfası"),
        ("kurumsal kimlik", "kurumsal kimlik sayfası"),
        ("sikca-sorulan", "sıkça sorulan sorular"),
        ("sıkça sorulan", "sıkça sorulan sorular"),
        ("kvkk", "aydınlatma metni"),
        ("gizlilik", "gizlilik politikası"),
        ("cerez", "çerez politikası"),
        ("çerez", "çerez politikası"),
        ("site-haritasi", "site haritası"),
        ("yonetim-kurulu", "yönetim kurulu sayfası"),
        ("personel", "personel sayfası"),
        ("foto-galeri", "görsel galeri"),
        ("video-galeri", "görsel galeri"),
    ];

    /// <summary>Bir ilan sayfasının taşıyabileceği en az içerik uzunluğu.</summary>
    private const int MinimumContentLength = 200;

    /// <summary>
    /// Mevcut kayıtları veri kalitesi kurallarından geçirir.
    ///
    /// <paramref name="apply"/> <c>false</c> iken hiçbir şey değiştirilmez; yalnızca rapor
    /// üretilir. Böylece önce görülür, sonra uygulanır.
    /// </summary>
    public async Task<TriageReport> TriageAsync(
        bool apply,
        CancellationToken cancellationToken = default)
    {
        var candidates = await query.ListTriageCandidatesAsync(cancellationToken);
        var sourceNames = (await sources.ListAsync(false, cancellationToken))
            .ToDictionary(s => s.Id, s => s.Name);

        var rows = new List<TriageRow>();
        var seenHashes = new Dictionary<string, Guid>();
        var applied = 0;
        var affectedAssessments = 0;

        foreach (var candidate in candidates.OrderBy(c => c.CollectedAt))
        {
            var reason = Classify(candidate, seenHashes, out var evidence, out var missingField);

            if (reason == QuarantineReason.None)
            {
                seenHashes.TryAdd(candidate.ContentHash, candidate.DocumentId);
                continue;
            }

            var assessmentCount = candidate.LinkedAssessmentCount;

            rows.Add(new TriageRow(
                candidate.DocumentId,
                candidate.Title,
                candidate.Url,
                sourceNames.GetValueOrDefault(candidate.SourceId, "(bilinmiyor)"),
                reason,
                evidence,
                missingField,
                IsScored: assessmentCount > 0,
                LinkedAssessmentCount: assessmentCount,
                RecommendedAction: Recommend(reason)));

            if (!apply)
            {
                continue;
            }

            var document = await documents.GetWithVersionsAsync(candidate.DocumentId, cancellationToken);
            if (document is null)
            {
                continue;
            }

            document.Quarantine(reason, evidence);
            applied++;

            // Bu kayda dayanan değerlendirmeler SESSİZCE SİLİNMEZ; yeniden
            // değerlendirilmesi gerektiği işaretlenir ve sayısı raporlanır.
            affectedAssessments += await query.MarkAssessmentsForReevaluationAsync(
                candidate.DocumentId, cancellationToken);
        }

        if (apply && applied > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Karantina triyajı uygulandı. Kayıt={Applied} EtkilenenDeğerlendirme={Assessments}",
                applied, affectedAssessments);
        }

        return new TriageReport(
            Reviewed: candidates.Count,
            Clean: candidates.Count - rows.Count,
            Flagged: rows.Count,
            Applied: applied,
            AffectedAssessments: affectedAssessments,
            Rows: rows);
    }

    /// <summary>Karantinadaki kayıtlar (PlatformReviewer inceleme ekranı).</summary>
    public Task<IReadOnlyList<QuarantinedDocumentDto>> ListQuarantinedAsync(
        CancellationToken cancellationToken = default) =>
        query.ListQuarantinedAsync(cancellationToken);

    /// <summary>Kaydı karantinadan çıkarır; yeniden ayrıştırma için sıraya döner.</summary>
    public async Task ReleaseAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await documents.GetWithVersionsAsync(documentId, cancellationToken)
            ?? throw new NotFoundException("Doküman", documentId);

        document.ReleaseFromQuarantine();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Kayıt karantinadan çıkarıldı. DocumentId={DocumentId}", documentId);
    }

    /// <summary>Kaydı elle karantinaya alır (inceleme sonucu).</summary>
    public async Task QuarantineAsync(
        Guid documentId,
        QuarantineReason reason,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var document = await documents.GetWithVersionsAsync(documentId, cancellationToken)
            ?? throw new NotFoundException("Doküman", documentId);

        document.Quarantine(reason, note);
        var affected = await query.MarkAssessmentsForReevaluationAsync(documentId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Kayıt karantinaya alındı. DocumentId={DocumentId} Neden={Reason} Değerlendirme={Affected}",
            documentId, reason, affected);
    }

    /// <summary>
    /// Kanıta dayalı sınıflandırma. Kural eşleşmiyorsa kayıt <b>temiz</b> sayılır;
    /// sayı tutturmak için işaretleme yapılmaz.
    /// </summary>
    private static QuarantineReason Classify(
        TriageCandidate candidate,
        Dictionary<string, Guid> seenHashes,
        out string evidence,
        out string? missingField)
    {
        missingField = null;

        if (string.IsNullOrWhiteSpace(candidate.Title))
        {
            evidence = "Başlık boş.";
            missingField = "Başlık";
            return QuarantineReason.MissingRequiredFields;
        }

        if (string.IsNullOrWhiteSpace(candidate.Url))
        {
            evidence = "Kaynak adresi boş.";
            missingField = "Kaynak URL";
            return QuarantineReason.MissingRequiredFields;
        }

        var haystack = $"{candidate.Url} {candidate.Title}".ToLowerInvariant();

        foreach (var (marker, explanation) in InstitutionalPages)
        {
            if (haystack.Contains(marker, StringComparison.Ordinal))
            {
                evidence = $"Adres/başlıkta '{marker}' geçiyor — {explanation}.";
                return QuarantineReason.InvalidSourcePage;
            }
        }

        // Kurumun kök sayfası bir ilan değildir.
        if (IsRootPage(candidate.Url))
        {
            evidence = "Kurumun kök/dil ana sayfası; tekil ilan sayfası değil.";
            return QuarantineReason.InvalidSourcePage;
        }

        if (seenHashes.TryGetValue(candidate.ContentHash, out var original))
        {
            evidence = $"İçeriği birebir aynı olan başka kayıt var ({original}).";
            return QuarantineReason.Duplicate;
        }

        if (candidate.ContentLength < MinimumContentLength)
        {
            evidence = $"İçerik {candidate.ContentLength} karakter; bir ilanı taşıyamayacak kadar kısa.";
            missingField = "İçerik";
            return QuarantineReason.NeedsManualReview;
        }

        evidence = string.Empty;
        return QuarantineReason.None;
    }

    private static bool IsRootPage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath.Trim('/');

        // "", "tr", "en" gibi yalnızca dil kökü olan yollar.
        return path.Length == 0 || (path.Length <= 5 && !path.Contains('/'));
    }

    private static string Recommend(QuarantineReason reason) => reason switch
    {
        QuarantineReason.InvalidSourcePage =>
            "Karantinaya al; kaynağın URL kalıbını daraltarak bu sayfaların hiç toplanmamasını sağla.",
        QuarantineReason.Duplicate =>
            "Karantinaya al; aynı içeriğin ilk kaydı katalogda kalsın.",
        QuarantineReason.MissingRequiredFields =>
            "Karantinaya al; kaynak seçicisi doğru alanı yakalamıyor olabilir.",
        QuarantineReason.NeedsManualReview =>
            "İncelemeye al; içerik seçicisi gövdeyi kaçırmış olabilir, silme.",
        _ => "İşlem gerekmiyor."
    };
}
