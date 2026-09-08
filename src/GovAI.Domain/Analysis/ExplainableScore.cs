namespace GovAI.Domain.Analysis;

/// <summary>Puan kırılımının tek bir başlığı.</summary>
public sealed record ScoreComponent
{
    public required ScoreGroup Group { get; init; }

    public required string Name { get; init; }

    /// <summary>0..1 aralığında başlık puanı.</summary>
    public required decimal Value { get; init; }

    public required decimal Weight { get; init; }

    /// <summary>Nihai puana katkı (0..1 ölçeğinde).</summary>
    public decimal Contribution => Math.Round(Value * Weight, 4);

    public required int CriterionCount { get; init; }

    public required int MetCount { get; init; }

    public required int NotMetCount { get; init; }

    public required int UnknownCount { get; init; }

    public required int ConflictCount { get; init; }

    public required string Rationale { get; init; }
}

/// <summary>
/// Açıklanabilir uygunluk puanı (0–100).
///
/// <para>
/// <b>Bu bir kazanma ihtimali değildir</b> ve hiçbir ekranda öyle adlandırılmaz.
/// Puan, firmanın profilinin çağrının yazılı koşullarıyla ne ölçüde örtüştüğünü
/// gösterir. Başvurunun kabul edilip edilmeyeceği değerlendirme komisyonunun kararıdır;
/// sistemin elinde o kararı tahmin edecek geçmiş sonuç verisi yoktur. "Kazanma
/// ihtimali %72" cümlesi kullanıcıyı yanıltır ve savunulamaz.
/// </para>
/// </summary>
public sealed record ExplainableScore
{
    /// <summary>0–100 arası uygunluk puanı.</summary>
    public required decimal Value { get; init; }

    public required IReadOnlyList<ScoreComponent> Components { get; init; }

    /// <summary>Zorunlu bir kriter sağlanmadığı için puan sıfırlandı mı?</summary>
    public required bool HasMandatoryFailure { get; init; }

    /// <summary>
    /// Eksik veri yüzünden kaybedilen puan. Kullanıcıya "profilini tamamlarsan puanın
    /// en fazla şu kadar artabilir" bilgisini verir; kırılımın sekizinci başlığıdır.
    /// </summary>
    public required decimal MissingDataEffect { get; init; }

    public required string RuleSetVersion { get; init; }

    public decimal ContributionOf(ScoreGroup group) =>
        Components.FirstOrDefault(c => c.Group == group)?.Contribution ?? 0m;
}

/// <summary>Güven seviyesinin tek bir bileşeni.</summary>
public sealed record ConfidenceFactor
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    /// <summary>0..1 aralığında bileşen değeri.</summary>
    public required decimal Value { get; init; }

    public required decimal Weight { get; init; }

    public required string Explanation { get; init; }

    /// <summary>
    /// Bu bileşen bu analizde ölçülemedi mi? Ölçülemeyen bileşen ağırlığıyla birlikte
    /// dışarıda bırakılır; 0 sayılırsa güven haksız yere düşerdi. Yapay zekâ kapalıyken
    /// "yapay zekâ–kanıt tutarlılığı" böyledir.
    /// </summary>
    public bool NotMeasured { get; init; }
}

/// <summary>
/// Analizin güven seviyesi — <b>puandan ayrı</b> hesaplanır.
///
/// <para>
/// İkisini tek sayıya indirmek en sık yapılan hatadır: "%40 puan" ile "%40 güven"
/// bambaşka şeylerdir. Birincisi firmanın koşulları karşılamadığını, ikincisi sistemin
/// yeterince bilgisi olmadığını söyler. Kullanıcı ilkinde başvurudan vazgeçer,
/// ikincisinde veri tamamlar.
/// </para>
/// </summary>
public sealed record ConfidenceAssessment
{
    /// <summary>0..1 aralığında sayısal güven.</summary>
    public required decimal Value { get; init; }

    public required ConfidenceLevel Level { get; init; }

    public required IReadOnlyList<ConfidenceFactor> Factors { get; init; }

    public required string RuleSetVersion { get; init; }

    /// <summary>Kullanıcıya gösterilen Türkçe karşılık.</summary>
    public string LevelLabel => Level switch
    {
        ConfidenceLevel.High => "Yüksek",
        ConfidenceLevel.Medium => "Orta",
        _ => "Düşük"
    };
}
