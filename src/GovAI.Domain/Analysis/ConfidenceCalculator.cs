using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Analysis;

/// <summary>
/// Analizin güven seviyesi — <b>puandan bağımsız</b> hesaplanır (Faz 3).
///
/// <para>
/// Güven "sonucun ne kadar sağlam bilgiye dayandığı"dır; "firmanın ne kadar uygun
/// olduğu" değil. Tam uygun bir firma, belgesi kötü ayrıştırılmış bir çağrıda düşük
/// güvenle çıkabilir; bu doğru davranıştır ve kullanıcıyı doğrulamaya yönlendirir.
/// </para>
///
/// <para>
/// Ölçülemeyen bileşen sıfır sayılmaz, <b>ağırlığıyla birlikte dışarıda bırakılır</b>.
/// Yapay zekâ kapalıyken "yapay zekâ–kanıt tutarlılığı" ölçülemez; sıfır sayılsaydı
/// model bağlanmadığı için güven haksız yere düşerdi.
/// </para>
/// </summary>
public static class ConfidenceCalculator
{
    public static ConfidenceAssessment Calculate(
        Company company,
        Opportunity opportunity,
        IReadOnlyList<CriterionResult> criteria,
        DateTimeOffset asOf,
        AnalysisRuleSet rules,
        decimal? aiEvidenceAgreement = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(opportunity);

        var karara_baglanan = criteria
            .Where(c => c.Outcome != CriterionOutcome.NotApplicable)
            .ToList();

        var factors = new List<ConfidenceFactor>
        {
            EvidenceCoverage(karara_baglanan, rules),
            ParseQuality(opportunity, rules),
            ProfileCompleteness(company, karara_baglanan, rules),
            FinancialFreshness(company, asOf, rules),
            SourceFreshness(opportunity, asOf, rules),
            EvidenceConsistency(karara_baglanan, rules),
            RuleCoverage(criteria, rules),
            AiAgreement(aiEvidenceAgreement, rules)
        };

        var olculebilen = factors.Where(f => !f.NotMeasured).ToList();
        var toplamAgirlik = olculebilen.Sum(f => f.Weight);

        var value = toplamAgirlik == 0m
            ? 0m
            : Math.Round(olculebilen.Sum(f => f.Value * f.Weight) / toplamAgirlik, 4);

        return new ConfidenceAssessment
        {
            Value = value,
            Level = rules.LevelOf(value),
            Factors = factors,
            RuleSetVersion = rules.Version
        };
    }

    private static ConfidenceFactor EvidenceCoverage(IReadOnlyList<CriterionResult> criteria, AnalysisRuleSet rules)
    {
        if (criteria.Count == 0)
        {
            return Factor(ConfidenceFactors.EvidenceCoverage, 0m, rules,
                "Karara bağlanan kriter yok; kanıt kapsamı ölçülemiyor.");
        }

        var kanitli = criteria.Count(c => c.Evidence.Count > 0);
        var oran = (decimal)kanitli / criteria.Count;

        return Factor(ConfidenceFactors.EvidenceCoverage, oran, rules,
            $"{criteria.Count} kriterden {kanitli} tanesi resmî belgeden alınmış bir alıntıya bağlı.");
    }

    private static ConfidenceFactor ParseQuality(Opportunity opportunity, AnalysisRuleSet rules)
    {
        if (opportunity.IsReviewedByConsultant)
        {
            return Factor(ConfidenceFactors.ParseQuality, 1m, rules,
                "Çağrının koşulları danışman tarafından incelenip onaylandı.");
        }

        // Sıfır güven, çıkarımın hiç yapılmadığını değil değerin yazılmadığını gösterir.
        var value = opportunity.RuleExtractionConfidence == 0m ? 0.5m : opportunity.RuleExtractionConfidence;

        return Factor(ConfidenceFactors.ParseQuality, value, rules,
            $"Koşullar belgeden otomatik çıkarıldı; çıkarım güveni {value:0.00}.");
    }

    private static ConfidenceFactor ProfileCompleteness(
        Company company,
        IReadOnlyList<CriterionResult> criteria,
        AnalysisRuleSet rules)
    {
        if (criteria.Count == 0)
        {
            return Factor(ConfidenceFactors.ProfileCompleteness, company.ProfileCompletionPercentage / 100m, rules,
                $"Firma profili %{company.ProfileCompletionPercentage} dolu.");
        }

        var eksik = criteria.Count(c => c.Outcome == CriterionOutcome.Unknown);
        var oran = 1m - ((decimal)eksik / criteria.Count);

        return Factor(ConfidenceFactors.ProfileCompleteness, oran, rules,
            eksik == 0
                ? "Bu çağrının sorduğu tüm alanlar firma profilinde dolu."
                : $"{criteria.Count} kriterden {eksik} tanesi firma verisi eksik olduğu için karara bağlanamadı.");
    }

    /// <summary>
    /// Mali verinin güncelliği. Veri <b>hiç yoksa</b> güven düşer ama puan düşmez:
    /// eksik mali veri kriteri <see cref="CriterionOutcome.Unknown"/> bırakır.
    /// </summary>
    private static ConfidenceFactor FinancialFreshness(Company company, DateTimeOffset asOf, AnalysisRuleSet rules)
    {
        var kayit = company.LatestAnnualFinancials();
        var yil = kayit?.FiscalYear ?? company.Financials.FiscalYear;

        if (yil is null)
        {
            return Factor(ConfidenceFactors.FinancialFreshness, 0m, rules,
                "Firmanın mali verisi girilmemiş; mali kriterler doğrulanamıyor.");
        }

        var yas = asOf.Year - yil.Value;

        var value = yas <= 0 ? 1m
            : yas <= rules.StaleFinancialYears ? 1m - (0.25m * yas)
            : 0.25m;

        var dogrulama = kayit?.VerificationStatus switch
        {
            FinancialVerificationStatus.Verified => " Veri mali müşavir/denetim onaylı.",
            FinancialVerificationStatus.DocumentChecked => " Veri belgeyle karşılaştırıldı.",
            _ => " Veri beyana dayanıyor, doğrulanmadı."
        };

        return Factor(ConfidenceFactors.FinancialFreshness, value, rules,
            $"En güncel mali veri {yil} yılına ait ({yas} yıl eski).{dogrulama}");
    }

    private static ConfidenceFactor SourceFreshness(Opportunity opportunity, DateTimeOffset asOf, AnalysisRuleSet rules)
    {
        var gun = (asOf - opportunity.PublishedAt).TotalDays;

        var value = gun <= 0 ? 1m
            : gun >= rules.StaleSourceDays ? 0.3m
            : 1m - (0.7m * (decimal)gun / rules.StaleSourceDays);

        return Factor(ConfidenceFactors.SourceFreshness, value, rules,
            $"Çağrı belgesi {Math.Max(0, Math.Floor(gun)):0} gün önce yayımlandı.");
    }

    private static ConfidenceFactor EvidenceConsistency(IReadOnlyList<CriterionResult> criteria, AnalysisRuleSet rules)
    {
        if (criteria.Count == 0)
        {
            return Factor(ConfidenceFactors.EvidenceConsistency, 1m, rules, "Çelişki bulunamadı.");
        }

        var celiskili = criteria.Count(c => c.Outcome == CriterionOutcome.ConflictingEvidence);
        var oran = 1m - ((decimal)celiskili / criteria.Count);

        return Factor(ConfidenceFactors.EvidenceConsistency, oran, rules,
            celiskili == 0
                ? "Kanıtlar arasında çelişki bulunamadı."
                : $"{celiskili} kriterde belge kendi içinde çelişiyor.");
    }

    private static ConfidenceFactor RuleCoverage(IReadOnlyList<CriterionResult> criteria, AnalysisRuleSet rules)
    {
        if (criteria.Count == 0)
        {
            return Factor(ConfidenceFactors.RuleCoverage, 0m, rules, "Değerlendirilen kriter yok.");
        }

        var kapsanan = criteria.Count(c => c.Outcome != CriterionOutcome.NotApplicable);
        var oran = (decimal)kapsanan / criteria.Count;

        return Factor(ConfidenceFactors.RuleCoverage, oran, rules,
            $"{criteria.Count} kriterden {kapsanan} tanesi için çağrı belgesinde koşul bulundu.");
    }

    /// <summary>
    /// Yapay zekâ–kanıt tutarlılığı. Model bağlı değilse <b>ölçülmedi</b> işaretlenir;
    /// sistem bu durumda "hibrit çalışıyor" iddiasında bulunmaz.
    /// </summary>
    private static ConfidenceFactor AiAgreement(decimal? agreement, AnalysisRuleSet rules)
    {
        if (agreement is null)
        {
            return new ConfidenceFactor
            {
                Code = ConfidenceFactors.AiEvidenceAgreement,
                Name = ConfidenceFactors.NameOf(ConfidenceFactors.AiEvidenceAgreement),
                Value = 0m,
                Weight = rules.ConfidenceWeightOf(ConfidenceFactors.AiEvidenceAgreement),
                Explanation = "Bu analizde yapay zekâ katkısı yok; bileşen ölçülmedi ve hesaba katılmadı.",
                NotMeasured = true
            };
        }

        return Factor(ConfidenceFactors.AiEvidenceAgreement, agreement.Value, rules,
            $"Yapay zekâ iddialarının {agreement.Value:P0} oranı kanıtla doğrulandı.");
    }

    private static ConfidenceFactor Factor(string code, decimal value, AnalysisRuleSet rules, string explanation) =>
        new()
        {
            Code = code,
            Name = ConfidenceFactors.NameOf(code),
            Value = Math.Round(Math.Clamp(value, 0m, 1m), 4),
            Weight = rules.ConfidenceWeightOf(code),
            Explanation = explanation
        };
}
