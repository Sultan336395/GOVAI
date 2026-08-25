using System.Security.Cryptography;
using System.Text;
using GovAI.Domain.Common;

namespace GovAI.Domain.Sources;

/// <summary>
/// Bir kaynaktan toplanmış ham doküman (Modül 1 → Document Parser Service).
/// <see cref="ContentHash"/> sayesinde aynı ilan tekrar tekrar işlenmez; metin değişirse
/// yeni bir sürüm oluşturulur ve mevzuat değişikliği bildirimi tetiklenir.
/// </summary>
public class SourceDocument : AggregateRoot, IAuditable
{
    private SourceDocument()
    {
    }

    public SourceDocument(Guid sourceId, string url, string title, string rawContent, string mediaType, DateTimeOffset collectedAt)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(url), "Doküman adresi zorunludur.");
        DomainException.ThrowIf(rawContent is null, "Ham içerik null olamaz.");

        SourceId = sourceId;
        Url = url.Trim();
        Title = title?.Trim() ?? string.Empty;
        RawContent = rawContent!;
        MediaType = string.IsNullOrWhiteSpace(mediaType) ? "text/html" : mediaType;
        CollectedAt = collectedAt;
        ContentHash = ComputeHash(rawContent!);
        Status = DocumentProcessingStatus.Raw;
    }

    public Guid SourceId { get; private set; }

    public string Url { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    /// <summary>Kaynaktan indirilen ham metin (HTML/PDF metin katmanı).</summary>
    public string RawContent { get; private set; } = string.Empty;

    /// <summary>Parser'ın ürettiği normalize edilmiş düz metin.</summary>
    public string? NormalizedText { get; private set; }

    public string MediaType { get; private set; } = "text/html";

    /// <summary>SHA-256; tekrar işlemeyi ve değişiklik tespitini sağlar.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    public DateTimeOffset CollectedAt { get; private set; }

    public DocumentProcessingStatus Status { get; private set; }

    public string? ProcessingError { get; private set; }

    /// <summary>Aynı ilanın kaçıncı sürümü olduğu; içerik değiştikçe artar.</summary>
    public int Revision { get; private set; } = 1;

    // ── Faz 2: sürüm zinciri ve karantina ─────────────────────────────────

    private readonly List<SourceDocumentVersion> _versions = [];

    /// <summary>
    /// Belgenin tüm yakalanışları. Bu kayıt kanonik adresin kimliğidir ve içerik
    /// değiştikçe yerinde güncellenir; geçmiş burada saklanır ve <b>üstüne yazılmaz</b>.
    /// </summary>
    public IReadOnlyList<SourceDocumentVersion> Versions => _versions.AsReadOnly();

    /// <summary>Yönlendirmeler sonrası ulaşılan nihai adres.</summary>
    public string? CanonicalUrl { get; private set; }

    /// <summary>Karantinadaysa nedeni; katalog dışında tutulmasının gerekçesi.</summary>
    public QuarantineReason QuarantineReason { get; private set; } = QuarantineReason.None;

    public string? QuarantineNote { get; private set; }

    /// <summary>Karantinadaki belge skorlanmaz ve şirketlere fırsat olarak gösterilmez.</summary>
    public bool IsQuarantined => QuarantineReason != QuarantineReason.None;

    public void SetCanonicalUrl(string? canonicalUrl) =>
        CanonicalUrl = string.IsNullOrWhiteSpace(canonicalUrl) ? CanonicalUrl : canonicalUrl.Trim();

    /// <summary>
    /// Bu yakalanışı sürüm zincirine ekler. Sürüm numarası <see cref="Revision"/> ile
    /// hizalıdır; böylece belge kaydı ile kanıt zinciri aynı sayıyı gösterir.
    /// </summary>
    public SourceDocumentVersion AddVersion(
        string sourceUrl,
        string canonicalUrl,
        int httpStatusCode,
        string mediaType,
        string? charset,
        string rawContent,
        DateTimeOffset retrievedAt)
    {
        var version = new SourceDocumentVersion(
            Id, Revision, sourceUrl, canonicalUrl, httpStatusCode, mediaType, charset, rawContent, retrievedAt);

        _versions.Add(version);
        return version;
    }

    /// <summary>
    /// Karantinaya alır. <b>Silmez</b>: kayıt durur, platform yöneticisi inceleyebilir ve
    /// istenirse yeniden ayrıştırabilir.
    /// </summary>
    public void Quarantine(QuarantineReason reason, string? note = null)
    {
        DomainException.ThrowIf(reason == QuarantineReason.None, "Karantina nedeni belirtilmelidir.");

        QuarantineReason = reason;
        QuarantineNote = note?[..Math.Min(note.Length, 1000)];
        Status = DocumentProcessingStatus.Discarded;
    }

    /// <summary>Karantinadan çıkarır ve yeniden ayrıştırma için sıraya döndürür.</summary>
    public void ReleaseFromQuarantine()
    {
        QuarantineReason = QuarantineReason.None;
        QuarantineNote = null;
        Status = DocumentProcessingStatus.Raw;
    }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public static string ComputeHash(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>İçerik değiştiyse yeni sürüme geçer ve yeniden ayrıştırma kuyruğuna alınır.</summary>
    public bool TryUpdateContent(string rawContent, DateTimeOffset collectedAt)
    {
        var hash = ComputeHash(rawContent);
        if (hash == ContentHash)
        {
            return false;
        }

        RawContent = rawContent;
        ContentHash = hash;
        CollectedAt = collectedAt;
        NormalizedText = null;
        Status = DocumentProcessingStatus.Raw;
        ProcessingError = null;
        Revision++;
        return true;
    }

    public void MarkParsed(string normalizedText)
    {
        NormalizedText = normalizedText;
        Status = DocumentProcessingStatus.Parsed;
        ProcessingError = null;
    }

    public void MarkRulesExtracted() => Status = DocumentProcessingStatus.RulesExtracted;

    public void MarkFailed(string error)
    {
        Status = DocumentProcessingStatus.Failed;
        ProcessingError = error;
    }

    public void Discard(string reason)
    {
        Status = DocumentProcessingStatus.Discarded;
        ProcessingError = reason;
    }
}
