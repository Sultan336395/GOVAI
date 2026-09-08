namespace GovAI.Domain.Analysis;

/// <summary>
/// Kriter sonuçlarını açıklanabilir puana çevirir (Faz 3).
///
/// <para>
/// Tek bir sayı üretmek kolaydır; savunulabilir olması için her puanın hangi kriterden
/// geldiğinin görünmesi gerekir. Bu yüzden hesap kırılım başlıklarıyla yapılır ve her
/// başlık kaç kriterin sağlandığını, kaçının eksik kaldığını yanında taşır.
/// </para>
/// </summary>
public static class ScoreCalculator
{
    /// <summary>
    /// Sektör başlığında kural yoksa verilen puan. Tam puan DEĞİLDİR: kural yokluğu
    /// "her sektör kabul" demek değil, "çağrı metninden sektör çıkarılamadı" demektir.
    /// Gerekçe <see cref="Eligibility.EligibilityEngine"/> ve CLAUDE.md §2.2.1.
    /// </summary>
    private const decimal UnverifiedSectorScore = 0.5m;

    public static ExplainableScore Calculate(IReadOnlyList<CriterionResult> criteria, AnalysisRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var components = new List<ScoreComponent>();

        foreach (var group in Enum.GetValues<ScoreGroup>())
        {
            components.Add(group == ScoreGroup.Mandatory
                ? MandatoryComponent(criteria, rules)
                : GroupComponent(group, criteria, rules));
        }

        var hasMandatoryFailure = criteria.Any(c => c.IsMandatoryFailure);
        var raw = components.Sum(c => c.Contribution);
        var value = hasMandatoryFailure ? 0m : Math.Round(Math.Clamp(raw, 0m, 1m) * 100m, 2);

        return new ExplainableScore
        {
            Value = value,
            Components = components,
            HasMandatoryFailure = hasMandatoryFailure,
            MissingDataEffect = hasMandatoryFailure ? 0m : MissingDataEffect(criteria, rules, value),
            RuleSetVersion = rules.Version
        };
    }

    /// <summary>
    /// Zorunlu kriterler başlığı.
    ///
    /// <para>
    /// Zorunlu kriterler kendi konu başlıklarında da sayılır; bu <b>bilinçli bir
    /// tekrardır</b>. Kullanıcının ilk baktığı yer "zorunlu koşulları geçtim mi"
    /// sorusudur ve bu kırılımda ayrı bir satır olarak görünmesi gerekir. Ağırlıklar
    /// tekrarı hesaba katarak belirlenmiştir.
    /// </para>
    /// </summary>
    private static ScoreComponent MandatoryComponent(IReadOnlyList<CriterionResult> criteria, AnalysisRuleSet rules)
    {
        var mandatory = criteria.Where(c => c.IsMandatory).ToList();
        var weight = rules.WeightOf(ScoreGroup.Mandatory);

        if (mandatory.Count == 0)
        {
            return new ScoreComponent
            {
                Group = ScoreGroup.Mandatory,
                Name = "Zorunlu kriterler",
                Value = 1m,
                Weight = weight,
                CriterionCount = 0,
                MetCount = 0,
                NotMetCount = 0,
                UnknownCount = 0,
                ConflictCount = 0,
                Rationale = "Çağrı metninde başvuruyu doğrudan engelleyen bir koşul bulunamadı."
            };
        }

        var met = mandatory.Count(c => c.Outcome == CriterionOutcome.Met);
        var notMet = mandatory.Count(c => c.Outcome == CriterionOutcome.NotMet);
        var unknown = mandatory.Count(c => c.Outcome == CriterionOutcome.Unknown);
        var conflict = mandatory.Count(c => c.Outcome == CriterionOutcome.ConflictingEvidence);

        var value = notMet > 0 ? 0m : mandatory.Average(c => c.ScoreImpact);

        var rationale = notMet > 0
            ? $"{mandatory.Count} zorunlu koşuldan {notMet} tanesi sağlanmıyor; başvuru bu hâliyle yapılamaz."
            : unknown > 0
                ? $"{mandatory.Count} zorunlu koşuldan {met} tanesi sağlanıyor, {unknown} tanesi veri eksikliği nedeniyle karara bağlanamadı."
                : $"{mandatory.Count} zorunlu koşulun tamamı sağlanıyor.";

        return new ScoreComponent
        {
            Group = ScoreGroup.Mandatory,
            Name = "Zorunlu kriterler",
            Value = Math.Round(value, 4),
            Weight = weight,
            CriterionCount = mandatory.Count,
            MetCount = met,
            NotMetCount = notMet,
            UnknownCount = unknown,
            ConflictCount = conflict,
            Rationale = rationale
        };
    }

    private static ScoreComponent GroupComponent(
        ScoreGroup group,
        IReadOnlyList<CriterionResult> criteria,
        AnalysisRuleSet rules)
    {
        var weight = rules.WeightOf(group);
        var all = criteria.Where(c => c.Group == group).ToList();
        var scored = all.Where(c => c.Outcome != CriterionOutcome.NotApplicable).ToList();

        if (scored.Count == 0)
        {
            var bos = group == ScoreGroup.SectorNace ? UnverifiedSectorScore : 1m;

            return new ScoreComponent
            {
                Group = group,
                Name = NameOf(group),
                Value = bos,
                Weight = weight,
                CriterionCount = 0,
                MetCount = 0,
                NotMetCount = 0,
                UnknownCount = 0,
                ConflictCount = 0,
                Rationale = group == ScoreGroup.SectorNace
                    ? "Çağrı metninden sektör koşulu çıkarılamadı; sektör uyumu doğrulanamadı."
                    : "Çağrı bu başlıkta koşul içermiyor."
            };
        }

        var met = scored.Count(c => c.Outcome == CriterionOutcome.Met);
        var notMet = scored.Count(c => c.Outcome == CriterionOutcome.NotMet);
        var unknown = scored.Count(c => c.Outcome == CriterionOutcome.Unknown);
        var conflict = scored.Count(c => c.Outcome == CriterionOutcome.ConflictingEvidence);

        var rationale = $"{scored.Count} kriterden {met} tanesi sağlanıyor"
                        + (notMet > 0 ? $", {notMet} tanesi sağlanmıyor" : string.Empty)
                        + (unknown > 0 ? $", {unknown} tanesi veri eksikliği nedeniyle değerlendirilemedi" : string.Empty)
                        + (conflict > 0 ? $", {conflict} tanesinde belge kendi içinde çelişiyor" : string.Empty)
                        + ".";

        return new ScoreComponent
        {
            Group = group,
            Name = NameOf(group),
            Value = Math.Round(scored.Average(c => c.ScoreImpact), 4),
            Weight = weight,
            CriterionCount = scored.Count,
            MetCount = met,
            NotMetCount = notMet,
            UnknownCount = unknown,
            ConflictCount = conflict,
            Rationale = rationale
        };
    }

    /// <summary>
    /// Eksik verinin puana etkisi: "profilini tamamlarsan puanın en fazla ne kadar
    /// artabilir". Aynı kriterler <see cref="CriterionOutcome.Met"/> kabul edilip puan
    /// yeniden hesaplanır ve fark alınır. Bu bir vaat değil, <b>tavan</b> ölçüsüdür.
    /// </summary>
    private static decimal MissingDataEffect(
        IReadOnlyList<CriterionResult> criteria,
        AnalysisRuleSet rules,
        decimal actual)
    {
        var belirsiz = criteria
            .Where(c => c.Outcome is CriterionOutcome.Unknown or CriterionOutcome.ConflictingEvidence)
            .ToList();

        if (belirsiz.Count == 0)
        {
            return 0m;
        }

        var tamamlanmis = criteria
            .Select(c => c.Outcome is CriterionOutcome.Unknown or CriterionOutcome.ConflictingEvidence
                ? c with { Outcome = CriterionOutcome.Met, ScoreImpact = 1m }
                : c)
            .ToList();

        var tavan = Calculate(tamamlanmis, rules).Value;
        return Math.Round(Math.Max(0m, tavan - actual), 2);
    }

    public static string NameOf(ScoreGroup group) => group switch
    {
        ScoreGroup.Mandatory => "Zorunlu kriterler",
        ScoreGroup.SectorNace => "Sektör ve NACE",
        ScoreGroup.ScaleAndFinancials => "Ölçek ve mali yapı",
        ScoreGroup.Geography => "Coğrafya",
        ScoreGroup.Workforce => "İş gücü",
        ScoreGroup.Timing => "Tarih",
        ScoreGroup.DocumentsAndConditions => "Belge ve özel koşullar",
        _ => group.ToString()
    };
}
