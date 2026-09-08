using GovAI.Domain.Common;

namespace GovAI.Domain.Opportunities;

/// <summary>
/// Bir kanıt parçasının kuralla ilişkisinin türü.
///
/// <para>
/// Tür gerekli çünkü aynı kural birden çok parçaya dayanabilir ve hepsi aynı ağırlıkta
/// değildir. "Asgari 10 çalışan" koşulunda sayının yazdığı cümle ile o bölümün başlığı
/// aynı şey değildir; yapay zekâ iddiası birincisine dayanabilir, ikincisine dayanan
/// bir iddia "belgede yazıyor" sayılamaz.
/// </para>
/// </summary>
public enum RuleEvidenceRole
{
    /// <summary>Kuralın <b>değeri</b> bu parçada birebir yazıyor (asıl kanıt).</summary>
    ValueSource = 1,

    /// <summary>Koşul cümlesi bu parçada geçiyor ama değer başka parçada.</summary>
    ConditionText = 2,

    /// <summary>Bağlam: bölüm başlığı, tanım, açıklama. Tek başına kanıt sayılmaz.</summary>
    Supporting = 3
}

/// <summary>
/// Bir çağrı kuralının resmî belgedeki kanıt parçasına bağlantısı (Faz 3).
///
/// <para>
/// Neden ayrı tablo: bir kriter birden çok kanıta dayanabiliyor. Son başvuru tarihi bir
/// paragrafta, bütçe başka bir paragrafta, sektör kısıtı üçüncü bir yerde yazıyor.
/// Kurala tek bir nullable kanıt kolonu eklenseydi ikinci kanıt kaydedilemez, kaydedilen
/// tek kanıt da hangi bilginin dayanağı olduğunu söyleyemezdi.
/// </para>
///
/// <para>
/// Bağlantı belge <b>sürümüne</b> bağlıdır. Belge güncellenip yeniden ayrıştırıldığında
/// yeni kurallar yeni sürümün parçalarına bağlanır; eski analiz kaydı kendi sürümünü ve
/// o sürümün alıntılarını taşımaya devam eder.
/// </para>
/// </summary>
public class OpportunityRuleEvidence : Entity
{
    private OpportunityRuleEvidence()
    {
    }

    public OpportunityRuleEvidence(
        Guid evidenceChunkId,
        Guid documentVersionId,
        RuleEvidenceRole role,
        int startOffset,
        int endOffset,
        DateTimeOffset createdAt,
        int? pageNumber = null,
        string? sectionTitle = null)
    {
        DomainException.ThrowIf(evidenceChunkId == Guid.Empty, "Kanıt parçası kimliği zorunludur.");
        DomainException.ThrowIf(documentVersionId == Guid.Empty, "Belge sürümü kimliği zorunludur.");
        DomainException.ThrowIf(startOffset < 0 || endOffset < startOffset, "Kanıt aralığı geçersiz.");

        EvidenceChunkId = evidenceChunkId;
        DocumentVersionId = documentVersionId;
        Role = role;
        StartOffset = startOffset;
        EndOffset = endOffset;
        PageNumber = pageNumber;
        SectionTitle = string.IsNullOrWhiteSpace(sectionTitle) ? null : sectionTitle.Trim();
        CreatedAt = createdAt;
    }

    public Guid OpportunityRuleId { get; private set; }

    public Guid EvidenceChunkId { get; private set; }

    /// <summary>Kanıtın alındığı belge sürümü; sürüm değişince yeni bağlantı kurulur.</summary>
    public Guid DocumentVersionId { get; private set; }

    public RuleEvidenceRole Role { get; private set; }

    /// <summary>Değerin belgedeki karakter aralığı; kullanıcı tam yerini görebilir.</summary>
    public int StartOffset { get; private set; }

    public int EndOffset { get; private set; }

    public int? PageNumber { get; private set; }

    public string? SectionTitle { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Yapay zekâ iddiasına dayanak olabilir mi? Bağlam parçası tek başına yetmez:
    /// bölüm başlığına dayanan "belgede yazıyor" iddiası doğrulanamaz.
    /// </summary>
    public bool IsCitable => Role is RuleEvidenceRole.ValueSource or RuleEvidenceRole.ConditionText;
}
