using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Eligibility;

/// <summary>
/// Tek bir kuralın firma verisi karşısındaki sonucu. Açıklanabilirliğin atom birimidir:
/// hangi kural, hangi firma değeriyle, hangi beklentiye karşı, neyle sonuçlandı.
/// </summary>
public sealed record RuleEvaluation
{
    public required Guid RuleId { get; init; }

    public required string Field { get; init; }

    public required RuleDimension Dimension { get; init; }

    public required RuleSeverity Severity { get; init; }

    public required RuleOutcome Outcome { get; init; }

    /// <summary>
    /// Kuralın karşılaştırma operatörü. Kriter düzeyinde <b>çelişki</b> aramak için
    /// gerekir: aynı alanı aynı operatörle sınırlayan iki koşul farklı değer bekliyorsa
    /// belge kendi içinde tutarsızdır. Operatör olmadan "ciro ≥ 1M" ile "ciro ≤ 500K"
    /// ayrımı yapılamaz.
    /// </summary>
    public RuleOperator Operator { get; init; } = RuleOperator.Equals;

    /// <summary>Kuralın Türkçe metni (ör. "Asgari 10 çalışan").</summary>
    public required string Requirement { get; init; }

    /// <summary>Firmanın bu alandaki mevcut değeri (ör. "7").</summary>
    public required string ActualValue { get; init; }

    /// <summary>Kuralın beklediği değer (ör. "&gt;= 10").</summary>
    public required string ExpectedValue { get; init; }

    /// <summary>Kısmi eşleşmelerde (özellikle NACE) 0..1 arası derece; ikili kurallarda 0 veya 1.</summary>
    public required decimal Strength { get; init; }

    /// <summary>Çağrı metnindeki dayanak; rapordan orijinal ifadeye inilebilmesini sağlar.</summary>
    public string? SourceExcerpt { get; init; }

    /// <summary>Eksikliği kapatmak için önerilen aksiyon; kullanıcıya "ne yapmalıyım" cevabını verir.</summary>
    public string? SuggestedAction { get; init; }

    public bool IsBlockingFailure => Severity == RuleSeverity.Blocking && Outcome == RuleOutcome.NotSatisfied;

    public bool NeedsData => Outcome == RuleOutcome.Unknown;
}
