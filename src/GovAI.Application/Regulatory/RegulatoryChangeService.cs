using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Common;
using GovAI.Domain.Common;

namespace GovAI.Application.Regulatory;

/// <summary>Mevzuat listesi satırı.</summary>
public sealed record RegulatoryChangeSummaryDto(
    Guid Id,
    string Title,
    RegulationDomain RegulationDomain,
    string Authority,
    string Jurisdiction,
    RegulatoryChangeType ChangeType,
    DateTimeOffset? PublicationDate,
    DateTimeOffset? EffectiveDate,
    RegulatoryChangeStatus Status,
    string OfficialUrl,
    DateTimeOffset DetectedAt);

/// <summary>Kanıt parçası; metin kaynağa geri gösterilebilir.</summary>
public sealed record EvidenceChunkDto(
    int SequenceNumber,
    int? PageNumber,
    string? SectionTitle,
    int? ParagraphNumber,
    string Text,
    int StartOffset,
    int EndOffset);

/// <summary>Mevzuat detayı: künye, belge sürümü ve kanıt bölümleri.</summary>
public sealed record RegulatoryChangeDetailDto(
    Guid Id,
    string Title,
    RegulationDomain RegulationDomain,
    string Authority,
    string Jurisdiction,
    RegulatoryChangeType ChangeType,
    string? OfficialNumber,
    DateTimeOffset? PublicationDate,
    DateTimeOffset? EffectiveDate,
    string? Summary,
    string OfficialUrl,
    RegulatoryChangeStatus Status,
    DateTimeOffset DetectedAt,
    DateTimeOffset LastVerifiedAt,
    Guid? PreviousVersionId,
    string SourceName,
    // ── Belge sürümü ──
    int DocumentVersion,
    string CanonicalUrl,
    string? Charset,
    string MediaType,
    int HttpStatusCode,
    DateTimeOffset RetrievedAt,
    DocumentParseStatus ParseStatus,
    bool RequiresOcr,
    int? PageCount,
    IReadOnlyList<EvidenceChunkDto> Evidence);

/// <summary>
/// Mevzuat okuma servisi (Faz 2).
///
/// Kiracı kullanıcıları yalnızca <b>doğrulanmış</b> kayıtları görür: karantinadaki ya da
/// henüz doğrulanmamış kayıt panelde çıkmaz.
///
/// <b>Bu fazda şirkete etki hesaplanmaz.</b> Panel bunun yerine etkinin DeepTech analiz
/// motoru tarafından değerlendirileceğini bildirir.
/// </summary>
public sealed class RegulatoryChangeService(IRegulatoryChangeRepository repository)
{
    public Task<IReadOnlyList<RegulatoryChangeSummaryDto>> ListAsync(
        RegulationDomain? domain = null,
        string? jurisdiction = null,
        CancellationToken cancellationToken = default) =>
        repository.ListPublishableAsync(domain, jurisdiction, cancellationToken);

    public async Task<RegulatoryChangeDetailDto> GetAsync(
        Guid changeId,
        CancellationToken cancellationToken = default) =>
        await repository.GetDetailAsync(changeId, cancellationToken)
        ?? throw new NotFoundException("Mevzuat değişikliği", changeId);
}
