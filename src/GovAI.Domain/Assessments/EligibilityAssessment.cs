using GovAI.Domain.Common;
using GovAI.Domain.Eligibility;

namespace GovAI.Domain.Assessments;

/// <summary>
/// Bir firma–çağrı çiftinin belirli bir andaki değerlendirme sonucunun kalıcı kaydı.
/// Her yeniden hesaplama yeni bir kayıt üretir; böylece "skorum neden değişti" sorusu
/// geriye dönük olarak cevaplanabilir (denetlenebilirlik gereği).
/// </summary>
public class EligibilityAssessment : AggregateRoot, IAuditable, ITenantScoped
{
    private readonly List<AssessmentDimension> _dimensions = [];

    private EligibilityAssessment()
    {
    }

    public EligibilityAssessment(Guid tenantId, EligibilityOutcome outcome, int companyProfileVersion, string detailJson)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        TenantId = tenantId;
        CompanyId = outcome.CompanyId;
        OpportunityId = outcome.OpportunityId;
        EvaluatedAt = outcome.EvaluatedAt;
        Verdict = outcome.Verdict;
        SectorFit = outcome.SectorFit;
        FinalScore = outcome.Score.FinalScore;
        Confidence = outcome.Score.Confidence;
        HasBlockingFailure = outcome.Score.HasBlockingFailure;
        CompanyProfileVersion = companyProfileVersion;
        DetailJson = detailJson;
        BlockingFailureCount = outcome.BlockingFailures.Count;
        MissingConditionCount = outcome.MissingConditions.Count;
        DataGapCount = outcome.DataGaps.Count;
        MissingMandatoryDocumentCount = outcome.DocumentChecklist.Count(d => d.IsMandatory && d.Status != DocumentStatus.Provided);

        _dimensions.AddRange(outcome.Score.Dimensions.Select(d =>
            new AssessmentDimension(d.Dimension, d.Value, d.Weight, d.Contribution, d.Rationale)));
    }

    public Guid TenantId { get; set; }

    public Guid CompanyId { get; private set; }

    public Guid OpportunityId { get; private set; }

    public DateTimeOffset EvaluatedAt { get; private set; }

    public EligibilityVerdict Verdict { get; private set; }

    /// <summary>
    /// Sektör uyumu. Listeleme sırasının <b>birincil</b> ölçütüdür; skor değil.
    /// Kolon olarak saklanır çünkü sıralama veritabanında yapılır — sayfalama
    /// doğru çalışsın diye sıralama belleğe alınmış sayfada yapılamaz.
    /// </summary>
    public SectorFit SectorFit { get; private set; } = SectorFit.Unverified;

    /// <summary>0..100 nihai fırsat skoru.</summary>
    public decimal FinalScore { get; private set; }

    public decimal Confidence { get; private set; }

    public bool HasBlockingFailure { get; private set; }

    /// <summary>Değerlendirmede kullanılan firma profili sürümü.</summary>
    public int CompanyProfileVersion { get; private set; }

    public int BlockingFailureCount { get; private set; }

    public int MissingConditionCount { get; private set; }

    public int DataGapCount { get; private set; }

    public int MissingMandatoryDocumentCount { get; private set; }

    /// <summary>Kural sonuçları ve belge listesinin tam JSON dökümü (PostgreSQL jsonb).</summary>
    public string DetailJson { get; private set; } = "{}";

    /// <summary>AI Explanation Service'in ürettiği yönetici özeti.</summary>
    public string? ExecutiveSummary { get; private set; }

    public DateTimeOffset? SummaryGeneratedAt { get; private set; }

    /// <summary>Özeti üreten model; çıktı tekrarlanabilirliği için kaydedilir.</summary>
    public string? SummaryModel { get; private set; }

    /// <summary>Bu değerlendirme, kullanıcının takip listesindeki güncel kayıt mı?</summary>
    public bool IsLatest { get; private set; } = true;

    public IReadOnlyCollection<AssessmentDimension> Dimensions => _dimensions.AsReadOnly();

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public void AttachSummary(string summary, string model, DateTimeOffset generatedAt)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(summary), "Yönetici özeti boş olamaz.");
        ExecutiveSummary = summary.Trim();
        SummaryModel = model;
        SummaryGeneratedAt = generatedAt;
    }

    /// <summary>Yeni bir değerlendirme üretildiğinde eski kayıt geçmişe alınır.</summary>
    public void Supersede() => IsLatest = false;
}

/// <summary>Kalıcılaştırılmış boyut puanı; dashboard'daki kırılım grafiklerini besler.</summary>
public class AssessmentDimension : Entity
{
    private AssessmentDimension()
    {
    }

    public AssessmentDimension(RuleDimension dimension, decimal value, decimal weight, decimal contribution, string rationale)
    {
        Dimension = dimension;
        Value = value;
        Weight = weight;
        Contribution = contribution;
        Rationale = rationale;
    }

    public Guid EligibilityAssessmentId { get; private set; }

    public RuleDimension Dimension { get; private set; }

    public decimal Value { get; private set; }

    public decimal Weight { get; private set; }

    public decimal Contribution { get; private set; }

    public string Rationale { get; private set; } = string.Empty;
}
