using GovAI.Domain.Common;

namespace GovAI.Domain.Regulatory;

/// <summary>
/// Resmî mevzuat değişikliği (Faz 2 – RegTech).
///
/// <b>Mevzuat bir fırsat değildir.</b> Bir vergi tebliği ya da SGK genelgesi için başvuru
/// yapılmaz, bütçesi ve son başvuru tarihi yoktur; uyulur. Bu yüzden fon/hibe/teşvik/ihale
/// için kullanılan <c>Opportunity</c> yapısından ayrı tutulur — ikisini aynı tabloya
/// koymak, panelde mevzuatı "başvurulabilir fırsat" gibi göstermeye ve skorlamaya yol açardı.
///
/// Bu kayıt <b>kanıta bağlıdır</b>: her mevzuat değişikliği bir kaynak belgesinin belirli
/// bir sürümünden doğar ve o sürüme geri gösterilebilir.
///
/// <b>Bu fazda</b> mevzuatın şirkete etkisi hesaplanmaz. Etki değerlendirmesi DeepTech
/// analiz motorunun işidir; burada yalnızca doğrulanmış veri ve kanıt üretilir.
/// </summary>
public class RegulatoryChange : AggregateRoot, IAuditable
{
    private RegulatoryChange()
    {
    }

    public RegulatoryChange(
        Guid sourceId,
        Guid sourceDocumentId,
        Guid documentVersionId,
        string jurisdiction,
        string authority,
        RegulationDomain domain,
        RegulatoryChangeType changeType,
        string title,
        string officialUrl,
        string contentHash,
        DateTimeOffset detectedAt)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), "Mevzuat başlığı zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(officialUrl), "Resmî kaynak adresi zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(contentHash), "İçerik özeti zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(jurisdiction), "Yargı alanı zorunludur.");

        SourceId = sourceId;
        SourceDocumentId = sourceDocumentId;
        DocumentVersionId = documentVersionId;
        Jurisdiction = jurisdiction.Trim().ToUpperInvariant();
        Authority = authority?.Trim() ?? string.Empty;
        RegulationDomain = domain;
        ChangeType = changeType;
        Title = title.Trim();
        OfficialUrl = officialUrl.Trim();
        ContentHash = contentHash;
        DetectedAt = detectedAt;
        LastVerifiedAt = detectedAt;
        Status = RegulatoryChangeStatus.Detected;
    }

    public Guid SourceId { get; private set; }

    public Guid SourceDocumentId { get; private set; }

    /// <summary>Kaydın dayandığı belge sürümü — kanıt zincirinin bağlantı noktası.</summary>
    public Guid DocumentVersionId { get; private set; }

    /// <summary>ISO ülke kodu ya da "EU".</summary>
    public string Jurisdiction { get; private set; } = string.Empty;

    /// <summary>Yayımlayan kurum.</summary>
    public string Authority { get; private set; } = string.Empty;

    public RegulationDomain RegulationDomain { get; private set; }

    public RegulatoryChangeType ChangeType { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>Resmî Gazete sayısı, tebliğ numarası vb. Yoksa <c>null</c> — uydurulmaz.</summary>
    public string? OfficialNumber { get; private set; }

    public DateTimeOffset? PublicationDate { get; private set; }

    /// <summary>Yürürlük tarihi. Belgede yazmıyorsa <c>null</c> kalır; tahmin edilmez.</summary>
    public DateTimeOffset? EffectiveDate { get; private set; }

    /// <summary>
    /// Resmî özet. <b>Yalnızca belgeden alınan metindir</b>; yapay zekâ tarafından
    /// üretilmiş yorum buraya yazılmaz.
    /// </summary>
    public string? Summary { get; private set; }

    public string OfficialUrl { get; private set; } = string.Empty;

    /// <summary>Kaynak belgenin içerik özeti; aynı metin iki kez kaydedilmesin diye.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>Aynı mevzuatın önceki sürümü; değişiklik zinciri buradan izlenir.</summary>
    public Guid? PreviousVersionId { get; private set; }

    public RegulatoryChangeStatus Status { get; private set; }

    /// <summary>Karantinadaysa nedeni.</summary>
    public QuarantineReason QuarantineReason { get; private set; } = QuarantineReason.None;

    public string? QuarantineNote { get; private set; }

    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>Kaydın resmî kaynağa karşı en son ne zaman doğrulandığı.</summary>
    public DateTimeOffset LastVerifiedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Belgeden çıkarılan künye bilgilerini yazar. Bulunamayan alan <c>null</c> kalır.</summary>
    public void Describe(
        string? officialNumber,
        DateTimeOffset? publicationDate,
        DateTimeOffset? effectiveDate,
        string? summary)
    {
        OfficialNumber = string.IsNullOrWhiteSpace(officialNumber) ? null : officialNumber.Trim();
        PublicationDate = publicationDate;
        EffectiveDate = effectiveDate;
        Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
    }

    /// <summary>Resmî kaynağa karşı doğrulandı; panelde gösterilebilir.</summary>
    public void MarkVerified(DateTimeOffset verifiedAt)
    {
        Status = RegulatoryChangeStatus.Verified;
        QuarantineReason = QuarantineReason.None;
        QuarantineNote = null;
        LastVerifiedAt = verifiedAt;
    }

    /// <summary>Yeni bir sürümle değiştirildi; eski kayıt silinmez, zincirde kalır.</summary>
    public void MarkSuperseded() => Status = RegulatoryChangeStatus.Superseded;

    public void LinkPreviousVersion(Guid previousVersionId) => PreviousVersionId = previousVersionId;

    /// <summary>
    /// Karantinaya alır. Kayıt <b>silinmez</b>: panelde gösterilmez ve skorlanmaz, ama
    /// platform yöneticisi inceleyip yeniden ayrıştırabilir.
    /// </summary>
    public void Quarantine(QuarantineReason reason, string? note = null)
    {
        DomainException.ThrowIf(
            reason == QuarantineReason.None,
            "Karantina nedeni belirtilmelidir.");

        Status = RegulatoryChangeStatus.Quarantined;
        QuarantineReason = reason;
        QuarantineNote = note?[..Math.Min(note.Length, 1000)];
    }

    /// <summary>Karantinadan çıkarır; tekrar doğrulama beklenir.</summary>
    public void ReleaseFromQuarantine()
    {
        Status = RegulatoryChangeStatus.Detected;
        QuarantineReason = QuarantineReason.None;
        QuarantineNote = null;
    }

    /// <summary>Şirketlere gösterilebilir mi? Yalnızca doğrulanmış kayıtlar.</summary>
    public bool IsPublishable => Status == RegulatoryChangeStatus.Verified;
}
