using System.Security.Cryptography;
using System.Text;
using GovAI.Domain.Common;

namespace GovAI.Domain.Sources;

/// <summary>
/// Bir resmî belgenin <b>tek bir yakalanışı</b> (Faz 2).
///
/// <see cref="SourceDocument"/> kanonik adresin kimliğidir ve içerik değiştikçe yerinde
/// güncellenir; eski hâli eskiden kayboluyordu. Bu tip her yakalanışı kalıcı olarak saklar:
/// aynı adresin değişen içeriği <b>yeni bir sürüm</b> olur, öncekinin üstüne yazılmaz.
///
/// DeepTech motorunun ileride "şu cümle şu belgenin şu sürümünde geçiyor" diyebilmesi için
/// kanıt zinciri buradan başlar: sürüm → <see cref="DocumentEvidenceChunk"/> → metin.
///
/// Yapay zekânın ürettiği hiçbir metin buraya yazılmaz; yalnızca kaynaktan indirilen
/// içerik saklanır.
/// </summary>
public class SourceDocumentVersion : Entity, IAuditable
{
    private readonly List<DocumentEvidenceChunk> _chunks = [];

    private SourceDocumentVersion()
    {
    }

    public SourceDocumentVersion(
        Guid sourceDocumentId,
        int versionNumber,
        string sourceUrl,
        string canonicalUrl,
        int httpStatusCode,
        string mediaType,
        string? charset,
        string rawContent,
        DateTimeOffset retrievedAt)
    {
        DomainException.ThrowIf(versionNumber < 1, "Belge sürümü 1'den küçük olamaz.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(sourceUrl), "Kaynak adresi zorunludur.");
        DomainException.ThrowIf(rawContent is null, "Ham içerik null olamaz.");

        SourceDocumentId = sourceDocumentId;
        VersionNumber = versionNumber;
        SourceUrl = sourceUrl.Trim();
        CanonicalUrl = string.IsNullOrWhiteSpace(canonicalUrl) ? sourceUrl.Trim() : canonicalUrl.Trim();
        HttpStatusCode = httpStatusCode;
        MediaType = string.IsNullOrWhiteSpace(mediaType) ? "text/html" : mediaType.Trim();
        Charset = charset?.Trim();
        RawContent = rawContent!;
        RawContentHash = Hash(rawContent!);
        RetrievedAt = retrievedAt;
        ParseStatus = DocumentParseStatus.Pending;
    }

    public Guid SourceDocumentId { get; private set; }

    /// <summary>1'den başlar; aynı adreste içerik her değiştiğinde artar.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>İsteğin yapıldığı adres.</summary>
    public string SourceUrl { get; private set; } = string.Empty;

    /// <summary>Yönlendirmeler sonrası ulaşılan nihai adres — kanıt bunu gösterir.</summary>
    public string CanonicalUrl { get; private set; } = string.Empty;

    public DateTimeOffset RetrievedAt { get; private set; }

    public int HttpStatusCode { get; private set; }

    public string MediaType { get; private set; } = "text/html";

    /// <summary>
    /// Sunucunun bildirdiği karakter kümesi. Kaybedilirse Türkçe karakterler bozulur;
    /// bu yüzden ayrı kolonda saklanır ve ayrıştırma bunu kullanır.
    /// </summary>
    public string? Charset { get; private set; }

    public string RawContent { get; private set; } = string.Empty;

    /// <summary>Ham gövdenin SHA-256'sı; değişiklik tespiti buna bakar.</summary>
    public string RawContentHash { get; private set; } = string.Empty;

    /// <summary>Etiketlerden arındırılmış düz metin.</summary>
    public string? NormalizedText { get; private set; }

    /// <summary>Temizlenmiş metnin SHA-256'sı; biçim değişip metin aynı kalırsa eşitlenir.</summary>
    public string? NormalizedTextHash { get; private set; }

    public string? Title { get; private set; }

    /// <summary>Belgenin kendi üzerinde yazan yayın tarihi.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Kaynağın bildirdiği son güncelleme tarihi.</summary>
    public DateTimeOffset? LastModifiedAt { get; private set; }

    public string? Language { get; private set; }

    /// <summary>Belgenin sayfa sayısı (PDF) ya da bölüm sayısı.</summary>
    public int? PageCount { get; private set; }

    public DocumentParseStatus ParseStatus { get; private set; }

    /// <summary>Taranmış PDF gibi metin katmanı olmayan belgelerde <c>true</c>.</summary>
    public bool RequiresOcr { get; private set; }

    public string? ParseError { get; private set; }

    public IReadOnlyList<DocumentEvidenceChunk> Chunks => _chunks.AsReadOnly();

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Ayrıştırma sonucunu işler.</summary>
    public void RecordParse(
        string normalizedText,
        string? title,
        string? language,
        int? pageCount,
        DateTimeOffset? publishedAt)
    {
        NormalizedText = normalizedText;
        NormalizedTextHash = Hash(normalizedText ?? string.Empty);
        Title = string.IsNullOrWhiteSpace(title) ? Title : title.Trim();
        Language = language ?? Language;
        PageCount = pageCount ?? PageCount;
        PublishedAt = publishedAt ?? PublishedAt;
        ParseStatus = DocumentParseStatus.Parsed;
        ParseError = null;
        RequiresOcr = false;
    }

    /// <summary>
    /// Belge okundu ama kayıt açılmadı. Metin ve kanıt parçaları KORUNUR: bu bir
    /// başarısızlık değil, ayrıştırıcının bilinçli kararıdır ve gerekçesi yazılır.
    /// </summary>
    public void RecordSkipped(string reason)
    {
        ParseStatus = DocumentParseStatus.Skipped;
        RequiresOcr = false;
        ParseError = reason?[..Math.Min(reason.Length, 1000)];
    }

    public void RecordParseFailure(string reason, bool requiresOcr = false)
    {
        ParseStatus = requiresOcr ? DocumentParseStatus.NeedsOcr : DocumentParseStatus.Failed;
        RequiresOcr = requiresOcr;
        ParseError = reason?[..Math.Min(reason.Length, 1000)];
    }

    public void SetLastModified(DateTimeOffset? lastModifiedAt) => LastModifiedAt = lastModifiedAt;

    /// <summary>
    /// Kanıt parçalarını yazar. Parçalar yalnızca ayrıştırılmış metinden üretilir;
    /// üretilmiş (AI) metin kanıt olarak saklanamaz.
    /// </summary>
    public void ReplaceChunks(IEnumerable<DocumentEvidenceChunk> chunks)
    {
        _chunks.Clear();
        _chunks.AddRange(chunks);
    }

    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>
/// Belgeden kesilmiş, kaynağa geri bağlanabilir kanıt parçası (Faz 2).
///
/// DeepTech motoru bir iddiada bulunduğunda hangi belgenin hangi sürümünün hangi
/// paragrafına dayandığını gösterebilmelidir. Konum bilgisi (sayfa, bölüm, karakter
/// aralığı) bu yüzden parçayla birlikte saklanır.
/// </summary>
public class DocumentEvidenceChunk : Entity
{
    private DocumentEvidenceChunk()
    {
    }

    public DocumentEvidenceChunk(
        Guid documentVersionId,
        int sequenceNumber,
        string text,
        int startOffset,
        int endOffset,
        int? pageNumber = null,
        string? sectionTitle = null,
        int? paragraphNumber = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(text), "Kanıt metni boş olamaz.");
        DomainException.ThrowIf(sequenceNumber < 0, "Sıra numarası negatif olamaz.");
        DomainException.ThrowIf(endOffset < startOffset, "Bitiş konumu başlangıçtan küçük olamaz.");

        DocumentVersionId = documentVersionId;
        SequenceNumber = sequenceNumber;
        Text = text;
        TextHash = SourceDocumentVersion.Hash(text);
        StartOffset = startOffset;
        EndOffset = endOffset;
        PageNumber = pageNumber;
        SectionTitle = sectionTitle?.Trim();
        ParagraphNumber = paragraphNumber;
    }

    public Guid DocumentVersionId { get; private set; }

    public int SequenceNumber { get; private set; }

    public int? PageNumber { get; private set; }

    public string? SectionTitle { get; private set; }

    public int? ParagraphNumber { get; private set; }

    public string Text { get; private set; } = string.Empty;

    public string TextHash { get; private set; } = string.Empty;

    /// <summary>Parçanın normalize metindeki başlangıç karakteri.</summary>
    public int StartOffset { get; private set; }

    /// <summary>Parçanın normalize metindeki bitiş karakteri.</summary>
    public int EndOffset { get; private set; }
}
