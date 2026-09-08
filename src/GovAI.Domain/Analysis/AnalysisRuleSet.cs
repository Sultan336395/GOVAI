using GovAI.Domain.Common;

namespace GovAI.Domain.Analysis;

/// <summary>
/// Analiz motorunun <b>sürümlenmiş</b> kural seti (Faz 3).
///
/// <para>
/// Ağırlıklar ve eşikler tek bir yerde ve sürüm numarasıyla durur. Sebep denetlenebilirlik:
/// "bu firma üç ay önce 72 puandı, şimdi 61" sorusunun cevabı ya veri değişti ya kural seti
/// değişti olabilir. Ağırlıklar koda dağılırsa ikinci ihtimal <b>kanıtlanamaz</b> hâle gelir.
/// Her analiz kaydı hangi sürümle üretildiğini taşır; eski analizler silinmez.
/// </para>
///
/// <para>
/// Sürüm biçimi <c>YYYY.MM.N</c>. Ağırlık, eşik veya kriter zorunluluk varsayılanı
/// değişirse sürüm artırılır — aksi hâlde aynı sürüm iki farklı davranışı anlatır.
/// </para>
/// </summary>
public sealed record AnalysisRuleSet
{
    /// <summary>
    /// Veri eksikliğinde kritere verilen kısmi kredi — ne tam ödül ne tam ceza.
    /// <see cref="Eligibility.EligibilityEngine"/> içindeki <c>UnknownRuleCredit</c> ile
    /// aynı değerdir ve aynı gerekçeye dayanır: eksik veri firmayı elemez.
    /// </summary>
    public const decimal UnknownCredit = 0.5m;

    /// <summary>
    /// Çelişkili kanıtın kredisi. Eksik veriden <b>daha düşüktür</b>: "bilmiyorum"
    /// tamamlanabilir bir boşluktur, "belge kendi içinde çelişiyor" ise doğrulanmadan
    /// güvenilemeyecek bir durumdur.
    /// </summary>
    public const decimal ConflictCredit = 0.25m;

    public static readonly AnalysisRuleSet Current = new()
    {
        Version = "2026.09.1",
        GroupWeights = new Dictionary<ScoreGroup, decimal>
        {
            [ScoreGroup.Mandatory] = 0.20m,
            [ScoreGroup.SectorNace] = 0.22m,
            [ScoreGroup.ScaleAndFinancials] = 0.18m,
            [ScoreGroup.Workforce] = 0.14m,
            [ScoreGroup.Geography] = 0.10m,
            [ScoreGroup.Timing] = 0.06m,
            [ScoreGroup.DocumentsAndConditions] = 0.10m
        },
        ConfidenceWeights = new Dictionary<string, decimal>
        {
            [ConfidenceFactors.EvidenceCoverage] = 0.20m,
            [ConfidenceFactors.ParseQuality] = 0.15m,
            [ConfidenceFactors.ProfileCompleteness] = 0.20m,
            [ConfidenceFactors.FinancialFreshness] = 0.10m,
            [ConfidenceFactors.SourceFreshness] = 0.10m,
            [ConfidenceFactors.EvidenceConsistency] = 0.10m,
            [ConfidenceFactors.RuleCoverage] = 0.10m,
            [ConfidenceFactors.AiEvidenceAgreement] = 0.05m
        },
        HighConfidenceThreshold = 0.75m,
        MediumConfidenceThreshold = 0.50m,
        StaleFinancialYears = 2,
        StaleSourceDays = 180
    };

    public required string Version { get; init; }

    private readonly IReadOnlyDictionary<ScoreGroup, decimal> _groupWeights = new Dictionary<ScoreGroup, decimal>();
    private readonly IReadOnlyDictionary<string, decimal> _confidenceWeights = new Dictionary<string, decimal>();

    /// <summary>
    /// Puan kırılımı başlıklarının ağırlıkları; toplamı 1.0 olmak zorundadır.
    ///
    /// <para>
    /// Doğrulama kurucu içinde yapılır: toplamı 1.0 olmayan bir ağırlık seti puanı
    /// sessizce 100'ün altında veya üstünde tutar ve hiçbir test bunu fark etmez.
    /// </para>
    /// </summary>
    public required IReadOnlyDictionary<ScoreGroup, decimal> GroupWeights
    {
        get => _groupWeights;
        init
        {
            var total = value.Values.Sum();
            DomainException.ThrowIf(
                Math.Abs(total - 1m) > 0.0001m,
                $"Kırılım ağırlıklarının toplamı 1.0 olmalıdır; gelen toplam {total}.");

            _groupWeights = value;
        }
    }

    /// <summary>Güven seviyesi bileşenlerinin ağırlıkları; toplamı 1.0 olmak zorundadır.</summary>
    public required IReadOnlyDictionary<string, decimal> ConfidenceWeights
    {
        get => _confidenceWeights;
        init
        {
            var total = value.Values.Sum();
            DomainException.ThrowIf(
                Math.Abs(total - 1m) > 0.0001m,
                $"Güven bileşeni ağırlıklarının toplamı 1.0 olmalıdır; gelen toplam {total}.");

            _confidenceWeights = value;
        }
    }

    /// <summary>Bu değerin üstü "Yüksek" güven.</summary>
    public required decimal HighConfidenceThreshold { get; init; }

    /// <summary>Bu değerin üstü "Orta" güven; altı "Düşük".</summary>
    public required decimal MediumConfidenceThreshold { get; init; }

    /// <summary>Mali verinin kaç yıl sonra bayat sayılacağı.</summary>
    public required int StaleFinancialYears { get; init; }

    /// <summary>Kaynak belgenin kaç gün sonra bayat sayılacağı.</summary>
    public required int StaleSourceDays { get; init; }

    public decimal WeightOf(ScoreGroup group) =>
        GroupWeights.TryGetValue(group, out var weight) ? weight : 0m;

    public decimal ConfidenceWeightOf(string factor) =>
        ConfidenceWeights.TryGetValue(factor, out var weight) ? weight : 0m;

    public ConfidenceLevel LevelOf(decimal value) =>
        value >= HighConfidenceThreshold ? ConfidenceLevel.High
        : value >= MediumConfidenceThreshold ? ConfidenceLevel.Medium
        : ConfidenceLevel.Low;
}

/// <summary>Güven bileşenlerinin sabit kodları.</summary>
public static class ConfidenceFactors
{
    public const string EvidenceCoverage = "EVIDENCE_COVERAGE";
    public const string ParseQuality = "PARSE_QUALITY";
    public const string ProfileCompleteness = "PROFILE_COMPLETENESS";
    public const string FinancialFreshness = "FINANCIAL_FRESHNESS";
    public const string SourceFreshness = "SOURCE_FRESHNESS";
    public const string EvidenceConsistency = "EVIDENCE_CONSISTENCY";
    public const string RuleCoverage = "RULE_COVERAGE";
    public const string AiEvidenceAgreement = "AI_EVIDENCE_AGREEMENT";

    public static string NameOf(string code) => code switch
    {
        EvidenceCoverage => "Resmî kanıt kapsamı",
        ParseQuality => "Belge ayrıştırma kalitesi",
        ProfileCompleteness => "Şirket profili doluluğu",
        FinancialFreshness => "Mali veri güncelliği",
        SourceFreshness => "Kaynak güncelliği",
        EvidenceConsistency => "Kanıtlar arası tutarlılık",
        RuleCoverage => "Kural kapsamı",
        AiEvidenceAgreement => "Yapay zekâ–kanıt tutarlılığı",
        _ => code
    };
}

/// <summary>Güven seviyesinin kullanıcıya gösterilen üç kademesi.</summary>
public enum ConfidenceLevel
{
    Low = 1,
    Medium = 2,
    High = 3
}
