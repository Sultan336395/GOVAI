using GovAI.Application.Regulatory;
using GovAI.Domain.Common;

namespace GovAI.Application.Opportunities;

/// <summary>
/// Fırsatın nereden geldiğinin kanıtı (Faz 2).
///
/// <para>
/// Detay ekranındaki her kritik bilgi resmî bir belgeye dayanmak zorundadır. Bu kayıt
/// o belgeyi künyesiyle taşır: hangi kaynak, hangi sürüm, hangi adres, hangi hash ve
/// metnin hangi bölümünden alındığı.
/// </para>
///
/// <para>
/// <b>Resmî bağlantı doğrulanmadan gösterilmez.</b> <see cref="OfficialUrl"/> yalnızca
/// adres kaynağın resmî alan adında ise doldurulur; aksi hâlde <c>null</c> kalır ve
/// arayüz "Resmî kaynağa git" düğmesini <b>göstermez</b>. Kullanıcının girdiği ya da
/// üçüncü bir siteye ait bir adres resmî kaynak gibi sunulamaz.
/// </para>
/// </summary>
public sealed record OpportunityProvenanceDto(
    // ── Kaynak ──
    Guid SourceId,
    string SourceName,
    SourceCategory SourceCategory,

    /// <summary>Kaynağın yapılandırması canlı olarak doğrulandı mı?</summary>
    bool SourceVerified,

    DateTimeOffset? SourceVerifiedAt,
    SourceHealth SourceHealth,

    /// <summary>Kaynağın resmî alan adı; bağlantı doğrulaması buna göre yapılır.</summary>
    string? OfficialDomain,

    // ── Resmî bağlantı ──
    /// <summary>
    /// Doğrulanmış resmî adres. Resmî alan adında değilse <c>null</c>.
    /// </summary>
    string? OfficialUrl,

    /// <summary>
    /// Bağlantı neden gösterilemiyor? Doğrulandıysa <c>null</c>. Operatöre ve
    /// kullanıcıya sebep açıkça söylenir; sessizce gizlenmez.
    /// </summary>
    string? OfficialUrlRejectionReason,

    // ── Belge sürümü ──
    Guid? DocumentId,
    int? DocumentVersion,
    string? CanonicalUrl,
    string? ContentHash,
    string? NormalizedTextHash,
    string? Charset,
    string? MediaType,
    DateTimeOffset? RetrievedAt,
    DocumentParseStatus? ParseStatus,
    bool RequiresOcr,
    int? PageCount,

    /// <summary>Belge ayrıştırılamadıysa sebebi; OCR gerekiyorsa hangi ölçüyle.</summary>
    string? ParseError,

    // ── Kanıt ──
    IReadOnlyList<EvidenceChunkDto> Evidence);
