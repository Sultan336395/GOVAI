using GovAI.Domain.Common;

namespace GovAI.Application.Abstractions.Services;

/// <summary>
/// AI Explanation Service'in Application tarafındaki sözleşmesi.
///
/// Önemli tasarım kararı: AI burada karar verici değildir.
/// - <see cref="ExtractRulesAsync"/> serbest formatlı resmî metni yapılandırılmış kural taslağına çevirir;
///   sonuç danışman onayına düşer ve güven değeri (<c>confidence</c>) ile birlikte saklanır.
/// - <see cref="GenerateExecutiveSummaryAsync"/> zaten hesaplanmış skoru yönetici diline çevirir;
///   skoru değiştirmez, yalnızca anlatır.
/// </summary>
public interface IAiExplanationClient
{
    /// <summary>Çağrı metninden makine-değerlendirilebilir koşul taslakları çıkarır.</summary>
    Task<RuleExtractionResult> ExtractRulesAsync(
        RuleExtractionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Hesaplanmış uygunluk sonucunu yöneticiye hitap eden Türkçe özete dönüştürür.</summary>
    Task<AiSummaryResult> GenerateExecutiveSummaryAsync(
        ExecutiveSummaryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Çağrı metnini ve firma profilini <b>bağımsız</b> okuyup kendi kararını verir.
    ///
    /// <para>
    /// Bu karar skoru DEĞİŞTİRMEZ. Amaç, kural motorunun kaçırdığı ya da yanlış okuduğu
    /// vakaları işaret etmektir: iki taraf ayrı yollardan aynı sonuca varıyorsa kayıt
    /// büyük olasılıkla doğrudur, ayrılıyorsa insan bakmalıdır.
    /// </para>
    ///
    /// <para>
    /// Modele <b>hesaplanmış skor verilmez</b>. Verilseydi model onu onaylama eğilimine
    /// girer ve "bağımsız" görüş, sistemin kendi cevabının yankısı olurdu — ayrışma hiç
    /// görünmez, ölçüm işe yaramazdı.
    /// </para>
    ///
    /// <para>
    /// Anahtar yapılandırılmamışsa <c>null</c> döner. Görüş <b>uydurulmaz</b>: sahte bir
    /// ikinci görüş, ölçümü olduğundan iyi ya da kötü gösterir ve kalibrasyonu bozar.
    /// </para>
    /// </summary>
    Task<AiSecondOpinionResult?> ReviewEligibilityAsync(
        SecondOpinionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Bağımsız ikinci görüş için modele verilen bağlam.</summary>
public sealed record SecondOpinionRequest
{
    public required string OpportunityTitle { get; init; }

    /// <summary>Çağrının koşullarını anlatan resmî metin.</summary>
    public required string OpportunityText { get; init; }

    public required string CompanyName { get; init; }

    /// <summary>Firma profilinin okunabilir özeti: sektör, çalışan, mali veriler, belgeler.</summary>
    public required string CompanyProfileSummary { get; init; }

    /// <summary>
    /// Profilde eksik olan alanlar.
    ///
    /// <para>
    /// Modelin eksik veriyi "hayır" sayıp firmayı elememesi için açıkça bildirilir
    /// (bkz. <c>docs/adr/0003</c>). Bilinmeyen bir alan, olumsuz bir cevap değildir.
    /// </para>
    /// </summary>
    public required IReadOnlyList<string> MissingProfileFields { get; init; }
}

/// <summary>Modelin bağımsız kararı.</summary>
public sealed record AiSecondOpinionResult
{
    public required EligibilityVerdict Verdict { get; init; }

    /// <summary>Kararın Türkçe gerekçesi; ayrışma incelenirken okunur.</summary>
    public required string Rationale { get; init; }

    /// <summary>Modelin kendi kararına dair güveni (0..1).</summary>
    public required decimal Confidence { get; init; }

    public required string ModelName { get; init; }
}

public sealed record RuleExtractionRequest
{
    public required string OpportunityTitle { get; init; }

    public required string NormalizedText { get; init; }

    public SupportCategory? ExpectedCategory { get; init; }

    /// <summary>Modelin uydurma alan adı üretmemesi için izin verilen alanların listesi.</summary>
    public required IReadOnlyDictionary<string, string> AllowedFields { get; init; }
}

public sealed record RuleExtractionResult
{
    public required IReadOnlyList<ExtractedRule> Rules { get; init; }

    public required IReadOnlyList<ExtractedDocument> Documents { get; init; }

    /// <summary>Modelin çıkarımın tamamına dair güveni (0..1).</summary>
    public required decimal Confidence { get; init; }

    public string? Summary { get; init; }

    public DateTimeOffset? Deadline { get; init; }

    public SupportCategory? DetectedCategory { get; init; }

    public string? ModelName { get; init; }
}

public sealed record ExtractedRule
{
    public required string Field { get; init; }

    public required string Operator { get; init; }

    public required string Value { get; init; }

    public required string Dimension { get; init; }

    public required string Severity { get; init; }

    public required string HumanReadable { get; init; }

    public string? SourceExcerpt { get; init; }

    public decimal Confidence { get; init; } = 0.5m;
}

public sealed record ExtractedDocument
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public bool IsMandatory { get; init; } = true;

    public string? IssuingAuthority { get; init; }
}

public sealed record ExecutiveSummaryRequest
{
    public required string CompanyName { get; init; }

    public required string OpportunityTitle { get; init; }

    public required string Publisher { get; init; }

    public required EligibilityVerdict Verdict { get; init; }

    public required decimal FinalScore { get; init; }

    public required IReadOnlyList<string> DimensionHighlights { get; init; }

    public required IReadOnlyList<string> BlockingReasons { get; init; }

    public required IReadOnlyList<string> MissingConditions { get; init; }

    public required IReadOnlyList<string> MissingDocuments { get; init; }

    public DateTimeOffset? Deadline { get; init; }
}

public sealed record AiSummaryResult(string Summary, string ModelName);
