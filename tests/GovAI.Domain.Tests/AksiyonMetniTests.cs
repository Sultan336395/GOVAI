using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Tests;

/// <summary>
/// Önerilen aksiyon metni. Rapora ve yapılacaklar listesine birebir girer.
/// </summary>
public class AksiyonMetniTests
{
    /// <summary>İhracat yapmayan bir firma: koşul sağlanmaz, aksiyon metni üretilir.</summary>
    private static RuleEvaluation Degerlendir(string metin)
    {
        var firma = new Company(
            Guid.CreateVersion7(), "Örnek Üretim A.Ş.", "1112223334", LegalType.JointStockCompany);

        firma.UpdateIdentity("Örnek Üretim A.Ş.", LegalType.JointStockCompany, new DateOnly(2015, 1, 1));

        var kural = new OpportunityRule(
            "Company.ExportFlag", RuleOperator.Equals, "true", RuleDimension.TechnicalQualification,
            RuleSeverity.Blocking, metin);

        return RuleEvaluator.Evaluate(kural, firma, new DateOnly(2026, 9, 10));
    }

    [Fact(DisplayName = "AM1. Kural metni kendi noktasıyla geliyorsa ÇİFT nokta olmaz")]
    public void Cift_nokta_olmaz()
    {
        // Raporda "Firma ihracat yapıyor olmalıdır.." yazıyordu.
        var sonuc = Degerlendir("Firma ihracat yapıyor olmalıdır.");

        Assert.NotNull(sonuc.SuggestedAction);
        Assert.DoesNotContain("..", sonuc.SuggestedAction, StringComparison.Ordinal);
        Assert.EndsWith("olmalıdır.", sonuc.SuggestedAction, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "AM2. Noktası olmayan metne nokta EKLENİR")]
    public void Noktasiz_metne_nokta_eklenir()
    {
        var sonuc = Degerlendir("Firma ihracat yapıyor olmalıdır");

        Assert.NotNull(sonuc.SuggestedAction);
        Assert.EndsWith("olmalıdır.", sonuc.SuggestedAction, StringComparison.Ordinal);
        Assert.DoesNotContain("..", sonuc.SuggestedAction, StringComparison.Ordinal);
    }
}
