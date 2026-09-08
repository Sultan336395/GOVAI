using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Regulatory;

namespace GovAI.Domain.Analysis;

/// <summary>
/// Mevzuat etki analizinin güven seviyesi (Faz 3).
///
/// <para>
/// Fırsat analizinden ayrı bir hesap, çünkü girdiler farklı: mevzuatta "kural çıkarım
/// güveni" yoktur, onun yerine kaydın <b>resmî kaynağa karşı doğrulanmış</b> olup
/// olmadığı vardır. Aynı ağırlık listesini paylaşırlar ki iki ekrandaki "Orta güven"
/// aynı şeyi anlatsın.
/// </para>
/// </summary>
public static class RegulationConfidence
{
    public static ConfidenceAssessment Calculate(
        Company company,
        RegulatoryChange change,
        IReadOnlyList<CriterionResult> criteria,
        DateTimeOffset asOf,
        AnalysisRuleSet rules,
        decimal? aiEvidenceAgreement = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(change);

        var kararaBaglanan = criteria.Where(c => c.Outcome != CriterionOutcome.NotApplicable).ToList();

        var factors = new List<ConfidenceFactor>
        {
            EvidenceCoverage(kararaBaglanan, rules),
            RecordQuality(change, rules),
            ProfileCompleteness(kararaBaglanan, company, rules),
            FinancialFreshness(rules),
            SourceFreshness(change, asOf, rules),
            Consistency(kararaBaglanan, rules),
            Coverage(criteria, rules),
            AiAgreement(aiEvidenceAgreement, rules)
        };

        var olculebilen = factors.Where(f => !f.NotMeasured).ToList();
        var toplam = olculebilen.Sum(f => f.Weight);
        var value = toplam == 0m ? 0m : Math.Round(olculebilen.Sum(f => f.Value * f.Weight) / toplam, 4);

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
            return Factor(ConfidenceFactors.EvidenceCoverage, 0m, rules, "Karara bağlanan kriter yok.");
        }

        var kanitli = criteria.Count(c => c.Evidence.Count > 0);

        return Factor(ConfidenceFactors.EvidenceCoverage, (decimal)kanitli / criteria.Count, rules,
            $"{criteria.Count} kriterden {kanitli} tanesi belge metninden alınmış bir alıntıya bağlı.");
    }

    /// <summary>Kaydın kalitesi: doğrulanmamış ya da karantinadaki kayıt güveni düşürür.</summary>
    private static ConfidenceFactor RecordQuality(RegulatoryChange change, AnalysisRuleSet rules) =>
        change.Status switch
        {
            RegulatoryChangeStatus.Verified => Factor(ConfidenceFactors.ParseQuality, 1m, rules,
                "Kayıt resmî kaynağa karşı doğrulandı."),
            RegulatoryChangeStatus.Quarantined => Factor(ConfidenceFactors.ParseQuality, 0m, rules,
                $"Kayıt karantinada ({change.QuarantineReason}); içeriğine güvenilemez."),
            RegulatoryChangeStatus.Superseded => Factor(ConfidenceFactors.ParseQuality, 0.3m, rules,
                "Kaydın yerini daha yeni bir sürüm aldı."),
            _ => Factor(ConfidenceFactors.ParseQuality, 0.5m, rules,
                "Kayıt tespit edildi ancak henüz resmî kaynağa karşı doğrulanmadı.")
        };

    private static ConfidenceFactor ProfileCompleteness(
        IReadOnlyList<CriterionResult> criteria,
        Company company,
        AnalysisRuleSet rules)
    {
        if (criteria.Count == 0)
        {
            return Factor(ConfidenceFactors.ProfileCompleteness, company.ProfileCompletionPercentage / 100m, rules,
                $"Firma profili %{company.ProfileCompletionPercentage} dolu.");
        }

        var eksik = criteria.Count(c => c.Outcome == CriterionOutcome.Unknown);

        return Factor(ConfidenceFactors.ProfileCompleteness, 1m - ((decimal)eksik / criteria.Count), rules,
            eksik == 0
                ? "Etki değerlendirmesi için gereken firma bilgileri dolu."
                : $"{criteria.Count} kriterden {eksik} tanesi eksik bilgi nedeniyle karara bağlanamadı.");
    }

    /// <summary>
    /// Mevzuat etkisi mali veriye dayanmaz; bileşen <b>ölçülmedi</b> işaretlenir.
    /// Sıfır sayılsaydı, mali verisi olmayan bir firma için güven haksız yere düşerdi.
    /// </summary>
    private static ConfidenceFactor FinancialFreshness(AnalysisRuleSet rules) => new()
    {
        Code = ConfidenceFactors.FinancialFreshness,
        Name = ConfidenceFactors.NameOf(ConfidenceFactors.FinancialFreshness),
        Value = 0m,
        Weight = rules.ConfidenceWeightOf(ConfidenceFactors.FinancialFreshness),
        Explanation = "Mevzuat etkisi mali veriye bağlı değil; bileşen hesaba katılmadı.",
        NotMeasured = true
    };

    private static ConfidenceFactor SourceFreshness(RegulatoryChange change, DateTimeOffset asOf, AnalysisRuleSet rules)
    {
        var gun = (asOf - change.LastVerifiedAt).TotalDays;

        var value = gun <= 0 ? 1m
            : gun >= rules.StaleSourceDays ? 0.3m
            : 1m - (0.7m * (decimal)gun / rules.StaleSourceDays);

        return Factor(ConfidenceFactors.SourceFreshness, value, rules,
            $"Kayıt en son {Math.Max(0, Math.Floor(gun)):0} gün önce resmî kaynağa karşı kontrol edildi.");
    }

    private static ConfidenceFactor Consistency(IReadOnlyList<CriterionResult> criteria, AnalysisRuleSet rules)
    {
        if (criteria.Count == 0)
        {
            return Factor(ConfidenceFactors.EvidenceConsistency, 1m, rules, "Çelişki bulunamadı.");
        }

        var celiskili = criteria.Count(c => c.Outcome == CriterionOutcome.ConflictingEvidence);

        return Factor(ConfidenceFactors.EvidenceConsistency, 1m - ((decimal)celiskili / criteria.Count), rules,
            celiskili == 0 ? "Belge kendi içinde tutarlı." : $"{celiskili} kriterde belge çelişiyor.");
    }

    private static ConfidenceFactor Coverage(IReadOnlyList<CriterionResult> criteria, AnalysisRuleSet rules)
    {
        if (criteria.Count == 0)
        {
            return Factor(ConfidenceFactors.RuleCoverage, 0m, rules, "Değerlendirilen kriter yok.");
        }

        var kapsanan = criteria.Count(c => c.Outcome != CriterionOutcome.NotApplicable);

        return Factor(ConfidenceFactors.RuleCoverage, (decimal)kapsanan / criteria.Count, rules,
            $"{criteria.Count} kriterden {kapsanan} tanesi belgede karşılık buldu.");
    }

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
                Explanation = "Bu analizde yapay zekâ katkısı yok; bileşen ölçülmedi.",
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
