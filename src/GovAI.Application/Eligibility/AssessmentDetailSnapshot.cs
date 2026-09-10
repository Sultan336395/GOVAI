using System.Text.Json;
using System.Text.Json.Serialization;
using GovAI.Domain.Common;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Scoring;

namespace GovAI.Application.Eligibility;

/// <summary>
/// <see cref="Domain.Assessments.EligibilityAssessment.DetailJson"/> içinde saklanan gövde.
///
/// <para>
/// <b>Bu tip bir sözleşmedir ve yazan ile okuyan taraf AYNI tipi kullanmak zorundadır.</b>
/// Gövde daha önce isimsiz (anonymous) bir nesneyle yazılıyordu; okuyan taraf ise onu
/// <c>EligibilityOutcome</c> sanıyordu. İki şekil hiç tutmadı: haftalık raporun risk ve
/// eksik listeleri sessizce BOŞ üretildi ve rapor "başvuruyu engelleyen bir eksik
/// görünmüyor" diye yazdı — oysa her değerlendirmede dört eksik zorunlu belge vardı.
/// Derleyici bunu yakalayamazdı, çünkü iki taraf da birbirini tanımıyordu. Artık tanıyor.
/// </para>
///
/// <para>
/// Alan adları <b>değiştirilemez</b>: eskiden yazılmış kayıtlar bu adlarla duruyor ve
/// geçmiş raporların dayandığı veri onlardır. Alan eklemek serbesttir.
/// </para>
/// </summary>
public sealed record AssessmentDetailSnapshot
{
    public required IReadOnlyList<RuleEvaluation> RuleEvaluations { get; init; }

    public required IReadOnlyList<DocumentCheckResult> DocumentChecklist { get; init; }

    public IReadOnlyList<DimensionScore> Dimensions { get; init; } = [];

    public ScoreWeights? Weights { get; init; }

    /// <summary>Firmayı doğrudan eleyen koşullar.</summary>
    [JsonIgnore]
    public IReadOnlyList<RuleEvaluation> BlockingFailures =>
        RuleEvaluations.Where(r => r.IsBlockingFailure).ToList();

    /// <summary>Kapatılabilir eksikler — "ne yaparsam uygun olurum" listesi.</summary>
    [JsonIgnore]
    public IReadOnlyList<RuleEvaluation> MissingConditions =>
        RuleEvaluations
            .Where(r => r.Outcome == RuleOutcome.NotSatisfied && r.Severity != RuleSeverity.Blocking)
            .ToList();

    /// <summary>Firma profilinde eksik olduğu için karar verilemeyen koşullar.</summary>
    [JsonIgnore]
    public IReadOnlyList<RuleEvaluation> DataGaps =>
        RuleEvaluations.Where(r => r.NeedsData).ToList();

    /// <summary>Eksik ya da süresi geçmiş zorunlu belgeler.</summary>
    [JsonIgnore]
    public IReadOnlyList<DocumentCheckResult> MissingMandatoryDocuments =>
        DocumentChecklist.Where(d => d.IsMandatory && d.Status != DocumentStatus.Provided).ToList();

    /// <summary>Değerlendirme sonucundan gövdeyi kurar.</summary>
    public static AssessmentDetailSnapshot From(EligibilityOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new AssessmentDetailSnapshot
        {
            RuleEvaluations = outcome.RuleEvaluations,
            DocumentChecklist = outcome.DocumentChecklist,
            Dimensions = outcome.Score.Dimensions,
            Weights = outcome.Score.Weights,
        };
    }

    /// <summary>
    /// Yazma ve okuma ayarları <b>tek yerde</b> durur.
    ///
    /// <para>
    /// Numaralandırmalar sayı olarak yazılır ve sayı olarak okunur; kayıtlı milyonlarca
    /// satırın biçimi budur. Okurken büyük–küçük harf gözetilmez, çünkü gövde geçmişte
    /// PascalCase yazıldı ve öyle kalmalıdır.
    /// </para>
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
