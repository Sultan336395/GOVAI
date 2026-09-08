using GovAI.Domain.Analysis;
using GovAI.Domain.Common;

namespace GovAI.Application.Analysis;

/// <summary>Tek bir kriterin ekrana taşınan hâli.</summary>
public sealed record CriterionResultDto(
    string Code,
    string Name,
    bool IsMandatory,
    CriterionOutcome Outcome,
    string OutcomeLabel,
    string Rationale,
    IReadOnlyList<string> CompanyFields,
    IReadOnlyList<CriterionEvidenceDto> Evidence,
    decimal ScoreImpact,
    string? MissingOrConflictExplanation,
    string RuleSetVersion,
    ScoreGroup Group,
    string GroupName);

public sealed record CriterionEvidenceDto(
    Guid? EvidenceChunkId,
    Guid? DocumentVersionId,
    string Excerpt,
    string? Locator);

public sealed record ScoreComponentDto(
    ScoreGroup Group,
    string Name,
    decimal Value,
    decimal Weight,
    decimal Contribution,
    int CriterionCount,
    int MetCount,
    int NotMetCount,
    int UnknownCount,
    int ConflictCount,
    string Rationale);

/// <summary>
/// Açıklanabilir puan. <c>Label</c> alanı bilinçli olarak "uygunluk puanı"dır;
/// hiçbir yerde "kazanma ihtimali" yazmaz.
/// </summary>
public sealed record ExplainableScoreDto(
    decimal Value,
    string Label,
    IReadOnlyList<ScoreComponentDto> Components,
    bool HasMandatoryFailure,
    decimal MissingDataEffect,
    string RuleSetVersion);

public sealed record ConfidenceFactorDto(
    string Code,
    string Name,
    decimal Value,
    decimal Weight,
    string Explanation,
    bool NotMeasured);

/// <summary>
/// Güven göstergesi.
///
/// <para>
/// <c>Title</c> bilinçli olarak ayrı bir alan: yapay zekâ kapalıyken bu sayı
/// <b>kural tabanlı güvendir</b>, hibrit güven değildir. "Hibrit güven: Yüksek"
/// yazmak, model hiç çalışmamışken kullanıcıya modelin de doğruladığı izlenimi verir —
/// ürünün verebileceği en yanıltıcı mesaj budur.
/// </para>
/// </summary>
public sealed record ConfidenceDto(
    decimal Value,
    ConfidenceLevel Level,
    string LevelLabel,
    IReadOnlyList<ConfidenceFactorDto> Factors,
    string RuleSetVersion,
    string Title);

/// <summary>
/// Kural ile yapay zekânın katkısını AYRI gösteren blok.
///
/// <para>
/// Tek bir açıklama metni verilseydi kullanıcı hangi cümlenin deterministik kuraldan,
/// hangisinin modelden geldiğini ayırt edemezdi. Ürünün savunma hattı tam olarak bu
/// ayrımdır: karar kuraldan çıkar, model yalnızca anlatır.
/// </para>
/// </summary>
public sealed record AnalysisContributionDto(
    // Model çalıştı ve en az bir iddiası kanıtla doğrulandı mı?
    bool HasAiContribution,
    AiAnalysisStatus AiStatus,
    string AiStatusLabel,
    // Kanıtla doğrulanmış, kullanıcıya gösterilebilir yapay zekâ açıklamaları.
    IReadOnlyList<string> AiExplanations,
    // Kanıtsız veya yetkisiz oldukları için ELENEN iddia sayısı.
    int RejectedClaimCount,
    // Kural ile modelin çeliştiği noktalar; kural sonucu korunur.
    IReadOnlyList<RuleAiConflictDto> Conflicts,
    // Model yoksa veya güven düşükse ekranda gösterilecek uyarı.
    string? Warning,
    // Analizin türü: "Kural tabanlı analiz" veya "Hibrit analiz".
    string ModeLabel,
    // Yapay zekâ güveni. Model çalışmadıysa "Kullanılamıyor" yazar; sayı gösterilmez.
    string AiConfidenceLabel);

public sealed record RuleAiConflictDto(
    string CriterionCode,
    CriterionOutcome RuleOutcome,
    AiClaimType ClaimType,
    string Note);

/// <summary>Analizin sürüm künyesi; "bu sonuç neden çıktı" sorusunun altyapısı.</summary>
public sealed record AnalysisVersionDto(
    Guid AnalysisRunId,
    int CompanyProfileVersion,
    int FinancialDataVersion,
    string RuleSetVersion,
    string? PromptVersion,
    string? ModelProvider,
    string? ModelName,
    string? OutputSchemaVersion,
    string CorrelationId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    AnalysisRunStatus Status);

/// <summary>Şirket–fırsat analizinin ekran sözleşmesi.</summary>
public sealed record OpportunityAnalysisDto(
    Guid CompanyId,
    Guid OpportunityId,
    string OpportunityTitle,
    DateTimeOffset EvaluatedAt,
    EligibilityVerdict Verdict,
    string VerdictLabel,
    SectorFit SectorFit,
    ExplainableScoreDto Score,
    ConfidenceDto Confidence,
    IReadOnlyList<CriterionResultDto> Criteria,
    IReadOnlyList<CriterionResultDto> Met,
    IReadOnlyList<CriterionResultDto> NotMet,
    IReadOnlyList<CriterionResultDto> Missing,
    IReadOnlyList<CriterionResultDto> Conflicting,
    string RuleSetVersion,
    AnalysisContributionDto Contribution,
    AnalysisVersionDto Version)
{
    // Ekranların doğrudan okuduğu kısayol; sözleşme tek yerde tanımlı kalsın.
    public bool HasAiContribution => Contribution.HasAiContribution;
}

/// <summary>Şirket–mevzuat etki analizinin ekran sözleşmesi.</summary>
public sealed record RegulationImpactDto(
    Guid CompanyId,
    Guid RegulatoryChangeId,
    string RegulationTitle,
    DateTimeOffset EvaluatedAt,
    RegulationImpact Impact,
    string ImpactLabel,
    ConfidenceDto Confidence,
    IReadOnlyList<CriterionResultDto> Criteria,
    IReadOnlyList<string> OpenQuestions,
    string LegalDisclaimer,
    string RuleSetVersion,
    AnalysisContributionDto Contribution,
    AnalysisVersionDto Version)
{
    public bool HasAiContribution => Contribution.HasAiContribution;
}

/// <summary>Kriter ve etki sonuçlarının Türkçe karşılıkları.</summary>
public static class AnalysisLabels
{
    public static string Of(CriterionOutcome outcome) => outcome switch
    {
        CriterionOutcome.Met => "Sağlanıyor",
        CriterionOutcome.NotMet => "Sağlanmıyor",
        CriterionOutcome.Unknown => "Bilgi eksik",
        CriterionOutcome.NotApplicable => "Bu çağrı için geçerli değil",
        _ => "Belgede çelişki var"
    };

    public static string Of(RegulationImpact impact) => impact switch
    {
        RegulationImpact.Applicable => "Firmayı kapsıyor",
        RegulationImpact.NotApplicable => "Firmayı kapsamıyor",
        RegulationImpact.PotentiallyApplicable => "Kapsaması muhtemel",
        _ => "Kapsam belirlenemedi"
    };

    public static string Of(AiAnalysisStatus status) => status switch
    {
        AiAnalysisStatus.Succeeded => "Yapay zekâ açıklaması eklendi",
        AiAnalysisStatus.InvalidOutput => "Yapay zekâ çıktısı geçersiz; kullanılmadı",
        AiAnalysisStatus.Error => "Yapay zekâ hatası; kural sonuçları korundu",
        _ => "Yapay zekâ kullanılamadı"
    };

    public static string Of(EligibilityVerdict verdict) => verdict switch
    {
        EligibilityVerdict.Eligible => "Uygun",
        EligibilityVerdict.ConditionallyEligible => "Şartlı uygun",
        EligibilityVerdict.NotEligible => "Uygun değil",
        // Analiz ekranında "Belirsiz" yerine "Doğrulanamadı": kullanıcı ne yapması
        // gerektiğini anlamalı. Belirsizlik bir durum değil, kapatılacak bir eksiktir.
        _ => "Doğrulanamadı"
    };

    /// <summary>
    /// Puanın ekrandaki adı. Sabit tutulur ki hiçbir ekran bunu "kazanma ihtimali"
    /// diye yazamasın; sistemde o tahmini yapacak geçmiş başvuru sonucu verisi yoktur.
    /// </summary>
    public const string ScoreLabel = "Uygunluk puanı";

    /// <summary>Model çalışmadığında analiz türünün adı.</summary>
    public const string RulesOnlyModeLabel = "Kural tabanlı analiz";

    /// <summary>Model çalıştığında analiz türünün adı.</summary>
    public const string HybridModeLabel = "Hibrit analiz";

    /// <summary>Model çalışmadığında güven göstergesinin başlığı.</summary>
    public const string RulesOnlyConfidenceTitle = "Kural tabanlı güven";

    /// <summary>Model çalıştığında güven göstergesinin başlığı.</summary>
    public const string HybridConfidenceTitle = "Hibrit güven";

    /// <summary>
    /// Model çalışmadığında yapay zekâ güveninin karşılığı.
    ///
    /// <para>
    /// Sayı gösterilmez. Sıfır yazmak "model baktı ve güvenmedi" demektir; oysa model
    /// hiç çalışmamıştır. İki durum kullanıcıyı zıt yönlere iter.
    /// </para>
    /// </summary>
    public const string AiConfidenceUnavailable = "Kullanılamıyor";
}
