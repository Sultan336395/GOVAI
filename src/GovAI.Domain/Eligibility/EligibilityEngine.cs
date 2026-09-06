using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Scoring;

namespace GovAI.Domain.Eligibility;

/// <summary>
/// Kural motoru + skorlama servisinin birleşik çıktısı.
/// AI katmanı bu nesneyi okuyup yönetici diline çevirir; kararın kendisini AI vermez.
/// </summary>
public sealed record EligibilityOutcome
{
    public required Guid CompanyId { get; init; }

    public required Guid OpportunityId { get; init; }

    public required DateTimeOffset EvaluatedAt { get; init; }

    public required EligibilityVerdict Verdict { get; init; }

    public required ScoreBreakdown Score { get; init; }

    public required IReadOnlyList<RuleEvaluation> RuleEvaluations { get; init; }

    public required IReadOnlyList<DocumentCheckResult> DocumentChecklist { get; init; }

    /// <summary>
    /// Firmanın sektörünün çağrıyla uyumu. Listeleme bu değere göre birincil sıralanır;
    /// alt ölçütler (personel, ciro, işletme yaşı, personel yapısı) ancak bunun içinde konuşur.
    /// </summary>
    public required SectorFit SectorFit { get; init; }

    /// <summary>Firmayı doğrudan eleyen koşullar.</summary>
    public IReadOnlyList<RuleEvaluation> BlockingFailures =>
        RuleEvaluations.Where(r => r.IsBlockingFailure).ToList();

    /// <summary>Kapatılabilir eksikler — "ne yaparsam uygun olurum" listesi.</summary>
    public IReadOnlyList<RuleEvaluation> MissingConditions =>
        RuleEvaluations.Where(r => r.Outcome == RuleOutcome.NotSatisfied && r.Severity != RuleSeverity.Blocking).ToList();

    /// <summary>Firma profilinde eksik olduğu için karar verilemeyen koşullar.</summary>
    public IReadOnlyList<RuleEvaluation> DataGaps =>
        RuleEvaluations.Where(r => r.NeedsData).ToList();
}

/// <summary>
/// GOVAI'nin karar çekirdeği: firma profilini bir çağrının koşullarıyla karşılaştırır,
/// yedi boyutta puan üretir ve nihai fırsat skorunu hesaplar.
///
/// Tasarım ilkesi: burada rastgelelik, model çağrısı veya dış bağımlılık yoktur.
/// Aynı firma + aynı çağrı her zaman aynı skoru üretir; bu, denetlenebilirliğin ön koşuludur.
/// </summary>
public static class EligibilityEngine
{
    /// <summary>Veri eksikliğinde kurala verilen kısmi kredi — ne tam ödül ne tam ceza.</summary>
    private const decimal UnknownRuleCredit = 0.5m;

    /// <summary>Kuralı olmayan bir boyutta çağrı kısıt koymuyor demektir; bu firma lehinedir.</summary>
    private const decimal UnconstrainedDimensionScore = 1.0m;

    /// <summary>
    /// Sektör boyutunda kural yoksa verilen puan. Diğer boyutların aksine tam puan DEĞİLDİR.
    ///
    /// Belge veya bölge koşulu içermeyen bir çağrı gerçekten "o boyutta kısıt koymuyor"
    /// demektir. Sektörde ise kural yokluğu "her sektör kabul" anlamına gelmez; yalnızca
    /// çağrı metninden sektörün çıkarılamadığını gösterir. Tam puan verilirse sektörsüz
    /// her ilan listenin başına çıkar — bir inşaat firmasına çay nakliye ihalesi
    /// önerilmesinin sebebi buydu. <see cref="UnknownRuleCredit"/> ile aynı mantık:
    /// ne tam ödül ne tam ceza.
    /// </summary>
    private const decimal UnverifiedSectorScore = 0.5m;

    /// <summary>Bu orandan fazla kural veri eksikliğinden değerlendirilemezse karar "belirsiz" olur.</summary>
    private const decimal IndeterminateDataGapThreshold = 0.40m;

    public static EligibilityOutcome Evaluate(
        Company company,
        Opportunity opportunity,
        DateTimeOffset asOf,
        ScoreWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(opportunity);

        var effectiveWeights = weights ?? ScoreWeights.For(opportunity.SupportCategory);
        var asOfDate = DateOnly.FromDateTime(asOf.UtcDateTime);

        var evaluations = opportunity.Rules
            .Select(rule => RuleEvaluator.Evaluate(rule, company, asOfDate))
            .ToList();

        var documentResults = DocumentReadinessCalculator.Check(opportunity, company, asOfDate);

        var dimensions = BuildDimensionScores(evaluations, documentResults, opportunity, asOf, effectiveWeights);

        var hasBlockingFailure = evaluations.Any(e => e.IsBlockingFailure);

        var rawScore = dimensions.Sum(d => d.Contribution);
        var finalScore = hasBlockingFailure ? 0m : Math.Round(Math.Clamp(rawScore, 0m, 1m) * 100m, 2);

        var confidence = CalculateConfidence(evaluations, opportunity);
        var verdict = DecideVerdict(evaluations, documentResults, hasBlockingFailure);

        return new EligibilityOutcome
        {
            CompanyId = company.Id,
            OpportunityId = opportunity.Id,
            EvaluatedAt = asOf,
            Verdict = verdict,
            SectorFit = DecideSectorFit(evaluations),
            RuleEvaluations = evaluations,
            DocumentChecklist = documentResults,
            Score = new ScoreBreakdown
            {
                Dimensions = dimensions,
                Weights = effectiveWeights,
                FinalScore = finalScore,
                HasBlockingFailure = hasBlockingFailure,
                Confidence = confidence
            }
        };
    }

    private static List<DimensionScore> BuildDimensionScores(
        IReadOnlyList<RuleEvaluation> evaluations,
        IReadOnlyList<DocumentCheckResult> documentResults,
        Opportunity opportunity,
        DateTimeOffset asOf,
        ScoreWeights weights)
    {
        var dimensions = new List<DimensionScore>();

        foreach (var dimension in Enum.GetValues<RuleDimension>())
        {
            var relevant = evaluations.Where(e => e.Dimension == dimension).ToList();

            var (value, rationale) = dimension switch
            {
                RuleDimension.Timing => CombineTiming(relevant, opportunity, asOf),
                RuleDimension.Documentation => CombineDocumentation(relevant, documentResults),
                RuleDimension.Sector => ScoreSector(relevant),
                _ => ScoreFromRules(relevant)
            };

            dimensions.Add(new DimensionScore
            {
                Dimension = dimension,
                Value = Math.Round(Math.Clamp(value, 0m, 1m), 4),
                Weight = weights.WeightOf(dimension),
                EvaluatedRuleCount = relevant.Count,
                UnknownRuleCount = relevant.Count(r => r.NeedsData),
                Rationale = rationale
            });
        }

        return dimensions;
    }

    /// <summary>
    /// Sektör boyutunun puanı — kural yokluğunda tam puan vermemesiyle diğerlerinden ayrılır.
    /// Gerekçe <see cref="UnverifiedSectorScore"/> üzerinde yazılıdır.
    /// </summary>
    private static (decimal Value, string Rationale) ScoreSector(IReadOnlyList<RuleEvaluation> evaluations)
    {
        var applicable = evaluations.Where(e => e.Outcome != RuleOutcome.NotApplicable).ToList();
        var scored = applicable.Where(e => e.Severity != RuleSeverity.Bonus).ToList();

        if (scored.Count == 0)
        {
            return (UnverifiedSectorScore, "Çağrı metninden sektör koşulu çıkarılamadı; sektör uyumu doğrulanamadı.");
        }

        return ScoreFromRules(evaluations);
    }

    /// <summary>
    /// Sektör uyumunun üç değerli kararı. "Bilmiyorum" ile "hayır" ayrı tutulur:
    /// doğrulanamayan kayıt elenmez, yalnızca listenin sonuna iner.
    /// </summary>
    private static SectorFit DecideSectorFit(IReadOnlyList<RuleEvaluation> evaluations)
    {
        var applicable = evaluations
            .Where(e => e.Dimension == RuleDimension.Sector && e.Outcome != RuleOutcome.NotApplicable)
            .ToList();

        if (applicable.Count == 0)
        {
            return SectorFit.Unverified;
        }

        var decisive = applicable.Where(e => e.Severity != RuleSeverity.Bonus).ToList();
        if (decisive.Count == 0)
        {
            // Yalnızca avantaj kuralı var: sağlanıyorsa uyum sayılır, sağlanmıyorsa
            // bu tek başına "sektör tutmuyor" demek değildir.
            return applicable.Any(e => e.Outcome == RuleOutcome.Satisfied)
                ? SectorFit.Matched
                : SectorFit.Unverified;
        }

        // Karşılanmayan bir engelleyici/ana sektör koşulu, sağlanan bir başkasıyla
        // telafi edilemez: sektör uyuşmazlığı ciro veya personel sayısıyla kapanmaz.
        if (decisive.Any(e => e.Outcome == RuleOutcome.NotSatisfied && e.Severity != RuleSeverity.Minor))
        {
            return SectorFit.NotMatched;
        }

        if (decisive.Any(e => e.Outcome == RuleOutcome.Satisfied))
        {
            return SectorFit.Matched;
        }

        if (decisive.Any(e => e.Outcome == RuleOutcome.NotSatisfied))
        {
            return SectorFit.NotMatched;
        }

        // Geriye yalnızca firma verisi eksik olduğu için karar verilemeyen kurallar kalır.
        return SectorFit.Unverified;
    }

    /// <summary>
    /// Bir boyutun kural tabanlı puanı. Kurallar ciddiyetlerine göre ağırlıklandırılır:
    /// engelleyici kurallar bilgilendirici kurallardan daha fazla söz sahibidir.
    /// </summary>
    private static (decimal Value, string Rationale) ScoreFromRules(IReadOnlyList<RuleEvaluation> evaluations)
    {
        var applicable = evaluations.Where(e => e.Outcome != RuleOutcome.NotApplicable).ToList();
        var scored = applicable.Where(e => e.Severity != RuleSeverity.Bonus).ToList();

        if (scored.Count == 0)
        {
            var bonusOnly = applicable.Count(e => e.Severity == RuleSeverity.Bonus && e.Outcome == RuleOutcome.Satisfied);
            return applicable.Count == 0
                ? (UnconstrainedDimensionScore, "Çağrı bu boyutta koşul içermiyor.")
                : (UnconstrainedDimensionScore, $"Yalnızca avantaj kuralı var; {bonusOnly} tanesi sağlanıyor.");
        }

        decimal weightedSum = 0m, weightTotal = 0m;
        foreach (var evaluation in scored)
        {
            var weight = SeverityWeight(evaluation.Severity);
            var value = evaluation.Outcome switch
            {
                // NACE gibi dereceli kurallarda Strength 0..1 gelir; ikili kurallarda 1'dir.
                RuleOutcome.Satisfied => evaluation.Strength,
                RuleOutcome.NotSatisfied => evaluation.Strength,   // kısmi eşleşmede kısmi kredi
                RuleOutcome.Unknown => UnknownRuleCredit,
                _ => 0m
            };

            weightedSum += weight * value;
            weightTotal += weight;
        }

        var baseScore = weightTotal == 0m ? UnconstrainedDimensionScore : weightedSum / weightTotal;

        // Avantaj (Bonus) kuralları puanı yukarı çeker ama tek başına belirleyici olamaz.
        var satisfiedBonuses = applicable.Count(e => e.Severity == RuleSeverity.Bonus && e.Outcome == RuleOutcome.Satisfied);
        var bonusBoost = Math.Min(0.10m, 0.05m * satisfiedBonuses);

        var satisfied = scored.Count(e => e.Outcome == RuleOutcome.Satisfied);
        var unknown = scored.Count(e => e.NeedsData);
        var rationale = $"{scored.Count} koşuldan {satisfied} tanesi sağlanıyor"
                        + (unknown > 0 ? $", {unknown} tanesi veri eksikliği nedeniyle değerlendirilemedi" : string.Empty)
                        + (satisfiedBonuses > 0 ? $", {satisfiedBonuses} avantaj koşulu ek puan getirdi" : string.Empty)
                        + ".";

        return (baseScore + bonusBoost, rationale);
    }

    private static (decimal Value, string Rationale) CombineTiming(
        IReadOnlyList<RuleEvaluation> timingRules,
        Opportunity opportunity,
        DateTimeOffset asOf)
    {
        var (deadlineScore, deadlineRationale) = TimingScoreCalculator.Calculate(opportunity.Deadline, asOf);

        if (timingRules.Count == 0)
        {
            return (deadlineScore, deadlineRationale);
        }

        var (ruleScore, ruleRationale) = ScoreFromRules(timingRules);
        return ((deadlineScore + ruleScore) / 2m, $"{deadlineRationale} {ruleRationale}");
    }

    private static (decimal Value, string Rationale) CombineDocumentation(
        IReadOnlyList<RuleEvaluation> documentRules,
        IReadOnlyList<DocumentCheckResult> documentResults)
    {
        var (checklistScore, checklistRationale) = DocumentReadinessCalculator.Score(documentResults);

        if (documentRules.Count == 0)
        {
            return (checklistScore, checklistRationale);
        }

        var (ruleScore, ruleRationale) = ScoreFromRules(documentRules);

        if (documentResults.Count == 0)
        {
            return (ruleScore, ruleRationale);
        }

        return ((checklistScore + ruleScore) / 2m, $"{checklistRationale} {ruleRationale}");
    }

    private static decimal SeverityWeight(RuleSeverity severity) => severity switch
    {
        RuleSeverity.Blocking => 3m,
        RuleSeverity.Major => 2m,
        RuleSeverity.Minor => 1m,
        _ => 0m
    };

    /// <summary>
    /// Skora ne kadar güvenilebileceği. İki bileşen: firma verisinin doluluğu ve
    /// çağrı metninden kural çıkarımının güvenilirliği. Danışman onayı güveni tavana taşır.
    /// </summary>
    private static decimal CalculateConfidence(IReadOnlyList<RuleEvaluation> evaluations, Opportunity opportunity)
    {
        var dataCompleteness = evaluations.Count == 0
            ? 0.5m
            : 1m - (decimal)evaluations.Count(e => e.NeedsData) / evaluations.Count;

        var extractionConfidence = opportunity.IsReviewedByConsultant
            ? 1m
            : opportunity.RuleExtractionConfidence == 0m ? 0.5m : opportunity.RuleExtractionConfidence;

        return Math.Round(Math.Clamp((dataCompleteness * 0.5m) + (extractionConfidence * 0.5m), 0m, 1m), 4);
    }

    private static EligibilityVerdict DecideVerdict(
        IReadOnlyList<RuleEvaluation> evaluations,
        IReadOnlyList<DocumentCheckResult> documentResults,
        bool hasBlockingFailure)
    {
        if (hasBlockingFailure)
        {
            return EligibilityVerdict.NotEligible;
        }

        if (evaluations.Count > 0)
        {
            var gapRatio = (decimal)evaluations.Count(e => e.NeedsData) / evaluations.Count;
            if (gapRatio > IndeterminateDataGapThreshold)
            {
                return EligibilityVerdict.Indeterminate;
            }
        }

        var hasUnmetCondition = evaluations.Any(e => e.Outcome == RuleOutcome.NotSatisfied);
        var hasMissingMandatoryDocument = documentResults.Any(d => d.IsMandatory && d.Status != DocumentStatus.Provided);

        return hasUnmetCondition || hasMissingMandatoryDocument
            ? EligibilityVerdict.ConditionallyEligible
            : EligibilityVerdict.Eligible;
    }
}
