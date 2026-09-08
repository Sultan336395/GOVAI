using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Analysis;

/// <summary>
/// Şirket–fırsat analizinin kural tabanlı sonucu.
///
/// <para>
/// Yapay zekâ bu nesneyi <b>okur</b>, üretmez. Kural sonuçları burada kesinleşir;
/// model katmanı yalnızca açıklama ekleyebilir ve <see cref="CriterionOutcome.Unknown"/>
/// kalan kriterleri kanıt göstererek çözmeyi önerebilir.
/// </para>
/// </summary>
public sealed record CompanyOpportunityAnalysis
{
    public required Guid CompanyId { get; init; }

    public required Guid OpportunityId { get; init; }

    public required DateTimeOffset EvaluatedAt { get; init; }

    public required EligibilityVerdict Verdict { get; init; }

    public required SectorFit SectorFit { get; init; }

    public required IReadOnlyList<CriterionResult> Criteria { get; init; }

    public required ExplainableScore Score { get; init; }

    public required ConfidenceAssessment Confidence { get; init; }

    /// <summary>Kriterlerin dayandığı ham kural sonuçları; mevcut ekranlar bunu kullanmaya devam eder.</summary>
    public required EligibilityOutcome RuleOutcome { get; init; }

    public IReadOnlyList<CriterionResult> Met =>
        Criteria.Where(c => c.Outcome == CriterionOutcome.Met).ToList();

    public IReadOnlyList<CriterionResult> NotMet =>
        Criteria.Where(c => c.Outcome == CriterionOutcome.NotMet).ToList();

    public IReadOnlyList<CriterionResult> Missing =>
        Criteria.Where(c => c.Outcome == CriterionOutcome.Unknown).ToList();

    public IReadOnlyList<CriterionResult> Conflicting =>
        Criteria.Where(c => c.Outcome == CriterionOutcome.ConflictingEvidence).ToList();

    public bool HasMandatoryFailure => Criteria.Any(c => c.IsMandatoryFailure);
}

/// <summary>
/// Şirket profilini bir çağrının kriterleriyle karşılaştırır (Faz 3).
///
/// <para>
/// <b>Paralel bir motor değildir.</b> Kararı yine <see cref="EligibilityEngine"/> verir;
/// bu sınıf onun kural sonuçlarını <i>kriter</i> düzeyine toplar, çelişkiyi görünür kılar
/// ve kırılımlı puan ile güven seviyesini ayrı ayrı üretir. Ayrı bir motor yazılsaydı iki
/// sonuç zamanla ayrışır ve hangisinin doğru olduğu bilinemezdi.
/// </para>
///
/// <para>Rastgelelik, saat okuma veya dış çağrı yoktur; aynı girdi aynı çıktıyı verir.</para>
/// </summary>
public static class OpportunityCriteriaEvaluator
{
    public static CompanyOpportunityAnalysis Evaluate(
        Company company,
        Opportunity opportunity,
        DateTimeOffset asOf,
        AnalysisRuleSet? ruleSet = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(opportunity);

        var rules = ruleSet ?? AnalysisRuleSet.Current;
        var outcome = EligibilityEngine.Evaluate(company, opportunity, asOf);

        var byCriterion = outcome.RuleEvaluations
            .GroupBy(e => CriterionCatalog.CriterionOfField(e.Field), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RuleEvaluation>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        var criteria = new List<CriterionResult>();

        foreach (var code in CriterionCatalog.OpportunityCriteria)
        {
            var group = byCriterion.TryGetValue(code, out var found) ? found : [];

            criteria.Add(code switch
            {
                CriterionCatalog.ApplicationWindow => ApplicationWindowCriterion(opportunity, asOf, rules),
                CriterionCatalog.SupportType => SupportTypeCriterion(opportunity, rules),
                CriterionCatalog.MandatoryDocuments => DocumentCriterion(group, outcome.DocumentChecklist, rules),
                CriterionCatalog.YoungEmployees => YoungEmployeeCriterion(company, opportunity, group, rules),
                _ => FromRules(code, group, rules)
            });
        }

        var score = ScoreCalculator.Calculate(criteria, rules);
        var confidence = ConfidenceCalculator.Calculate(company, opportunity, criteria, asOf, rules);

        return new CompanyOpportunityAnalysis
        {
            CompanyId = company.Id,
            OpportunityId = opportunity.Id,
            EvaluatedAt = asOf,
            Verdict = DecideVerdict(criteria, outcome),
            SectorFit = outcome.SectorFit,
            Criteria = criteria,
            Score = score,
            Confidence = confidence,
            RuleOutcome = outcome
        };
    }

    /// <summary>
    /// Kriterin kararı, kendisini besleyen kurallardan çıkar.
    ///
    /// <para>
    /// Sıra bilinçlidir: önce çelişki, sonra karşılanmayan koşul, sonra eksik veri.
    /// Eksik veri en sona bırakılırsa "bir koşulu bilmiyoruz" durumu, karşılanmayan
    /// başka bir koşulu örterdi.
    /// </para>
    /// </summary>
    private static CriterionResult FromRules(
        string code,
        IReadOnlyList<RuleEvaluation> evaluations,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(code);
        var decisive = evaluations
            .Where(e => e.Severity != RuleSeverity.Bonus && e.Outcome != RuleOutcome.NotApplicable)
            .ToList();

        var mandatory = decisive.Any(e => e.Severity == RuleSeverity.Blocking);

        if (decisive.Count == 0)
        {
            var bonusMet = evaluations.Any(e => e.Severity == RuleSeverity.Bonus && e.Outcome == RuleOutcome.Satisfied);

            return Build(
                definition,
                mandatory: false,
                outcome: bonusMet ? CriterionOutcome.Met : CriterionOutcome.NotApplicable,
                rationale: bonusMet
                    ? "Çağrı bu başlıkta yalnızca avantaj koşulu içeriyor ve firma bunu sağlıyor."
                    : "Çağrı metninde bu başlıkta bir koşul bulunamadı.",
                impact: bonusMet ? 1m : 0m,
                evaluations: evaluations,
                rules: rules);
        }

        if (TryFindConflict(decisive, out var conflictNote))
        {
            return Build(
                definition,
                mandatory,
                CriterionOutcome.ConflictingEvidence,
                $"Resmî belge bu başlıkta birbiriyle çelişen koşullar içeriyor. {conflictNote}",
                AnalysisRuleSet.ConflictCredit,
                evaluations,
                rules,
                explanation: $"{conflictNote} Sonuç doğrulanmadan kesin kabul edilmemelidir.");
        }

        var unmet = decisive.Where(e => e.Outcome == RuleOutcome.NotSatisfied).ToList();
        if (unmet.Count > 0)
        {
            var impact = unmet.Average(e => e.Strength);

            return Build(
                definition,
                mandatory,
                CriterionOutcome.NotMet,
                $"{decisive.Count} koşuldan {unmet.Count} tanesi firmanın verisiyle karşılanmıyor: "
                + string.Join("; ", unmet.Select(e => $"{e.Requirement} (firma: {e.ActualValue}, beklenen: {e.ExpectedValue})")),
                impact,
                evaluations,
                rules);
        }

        var unknown = decisive.Where(e => e.Outcome == RuleOutcome.Unknown).ToList();
        if (unknown.Count > 0)
        {
            var fields = unknown.Select(e => e.Field).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            return Build(
                definition,
                mandatory,
                CriterionOutcome.Unknown,
                $"{decisive.Count} koşuldan {unknown.Count} tanesi firma verisi eksik olduğu için değerlendirilemedi.",
                AnalysisRuleSet.UnknownCredit,
                evaluations,
                rules,
                explanation: "Şu alanlar doldurulmadan bu kriter karara bağlanamaz: " + string.Join(", ", fields) + ".");
        }

        var satisfiedStrength = decisive.Average(e => e.Strength);

        return Build(
            definition,
            mandatory,
            CriterionOutcome.Met,
            $"{decisive.Count} koşulun tamamı firmanın verisiyle karşılanıyor.",
            satisfiedStrength,
            evaluations,
            rules);
    }

    /// <summary>
    /// Aynı kriterde çelişki: <b>aynı alan ve aynı operatörle</b> yazılmış iki koşul
    /// farklı değer bekliyor ve sonuçları zıt çıkıyor.
    ///
    /// <para>
    /// Dar tanım bilinçli. Farklı alanlarda birden çok koşulun bir kısmının sağlanmaması
    /// çelişki değildir, olağan durumdur ("ciro ≥ 1M ve bilanço ≥ 2M" — biri tutmayabilir).
    /// Çelişki, belgenin <i>aynı şeyi</i> iki farklı biçimde söylemesidir: bir paragrafta
    /// "tüm sektörler", başka bir paragrafta "yalnızca imalat". Böyle bir belgede birini
    /// seçmek, diğerini sessizce yok saymaktır.
    /// </para>
    /// </summary>
    private static bool TryFindConflict(IReadOnlyList<RuleEvaluation> decisive, out string note)
    {
        foreach (var group in decisive.GroupBy(e => (e.Field, e.Operator)))
        {
            var distinctExpectations = group
                .Select(e => e.ExpectedValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            var hasMet = group.Any(e => e.Outcome == RuleOutcome.Satisfied);
            var hasUnmet = group.Any(e => e.Outcome == RuleOutcome.NotSatisfied);

            if (distinctExpectations > 1 && hasMet && hasUnmet)
            {
                var beklentiler = group
                    .Select(e => $"\"{e.Requirement}\"")
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                note = $"Aynı alan ({group.Key.Field}) için farklı koşullar yazılmış: {string.Join(" ve ", beklentiler)}.";
                return true;
            }
        }

        note = string.Empty;
        return false;
    }

    /// <summary>
    /// Başvuru dönemi. Sistemin kesin bildiği tek kriter budur: tarih belgeden gelir,
    /// firma verisine bağlı değildir. Bu yüzden süresi geçmiş çağrı zorunlu başarısızlıktır.
    /// </summary>
    private static CriterionResult ApplicationWindowCriterion(
        Opportunity opportunity,
        DateTimeOffset asOf,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.ApplicationWindow);

        if (opportunity.Deadline is null)
        {
            return Build(
                definition,
                mandatory: false,
                CriterionOutcome.Unknown,
                "Çağrı belgesinde son başvuru tarihi yazmıyor.",
                AnalysisRuleSet.UnknownCredit,
                [],
                rules,
                explanation: "Son başvuru tarihi resmî kaynakta belirtilmemiş; tarih tahmin edilmez, "
                    + "başvurudan önce kurumdan doğrulanmalıdır.");
        }

        if (!opportunity.IsOpenOn(asOf))
        {
            return Build(
                definition,
                mandatory: true,
                CriterionOutcome.NotMet,
                $"Son başvuru tarihi {opportunity.Deadline:dd.MM.yyyy} olarak geçmiş; çağrı kapalı.",
                0m,
                [],
                rules);
        }

        var kalan = (opportunity.Deadline.Value - asOf).TotalDays;

        return Build(
            definition,
            mandatory: true,
            CriterionOutcome.Met,
            $"Çağrı başvuruya açık; son başvuru tarihine {Math.Floor(kalan)} gün var.",
            1m,
            [],
            rules);
    }

    /// <summary>
    /// Destek türü. Puan değil <b>anlatım</b> kriteridir: hibe ile kredi karışırsa
    /// kullanıcı borcu destek sanır. Türü belirlenememiş tutar "hibe" sayılmaz.
    /// </summary>
    private static CriterionResult SupportTypeCriterion(Opportunity opportunity, AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.SupportType);

        var kalemler = opportunity.BudgetItems.ToList();
        if (kalemler.Count == 0)
        {
            return Build(
                definition,
                mandatory: false,
                CriterionOutcome.NotApplicable,
                "Çağrı belgesinde tutar bulunamadı; destek türü değerlendirilmiyor.",
                0m,
                [],
                rules);
        }

        var belirli = kalemler.Where(k => !k.NeedsReview).ToList();
        var kanit = kalemler
            .Where(k => !string.IsNullOrWhiteSpace(k.Excerpt))
            .Select(k => new CriterionEvidence { Excerpt = k.Excerpt!, Locator = $"Bütçe kalemi ({k.Type})" })
            .ToList();

        if (belirli.Count == 0)
        {
            return Build(
                definition,
                mandatory: false,
                CriterionOutcome.Unknown,
                $"Belgede {kalemler.Count} tutar var ancak hiçbirinin türü kesin olarak anlaşılamadı.",
                AnalysisRuleSet.UnknownCredit,
                [],
                rules,
                explanation: "Tutarların hibe mi kredi mi olduğu belgeden çıkarılamadı; "
                    + "kesin gösterilmez, resmî kaynaktan doğrulanmalıdır.",
                evidence: kanit);
        }

        var turler = belirli
            .Select(k => k.Type)
            .Distinct()
            .Select(BudgetTypeName)
            .ToList();

        var belirsizNot = kalemler.Count > belirli.Count
            ? $" Ayrıca türü belirlenemeyen {kalemler.Count - belirli.Count} tutar var."
            : string.Empty;

        return Build(
            definition,
            mandatory: false,
            CriterionOutcome.Met,
            $"Belgede tanımlı destek türleri: {string.Join(", ", turler)}.{belirsizNot}",
            1m,
            [],
            rules,
            evidence: kanit);
    }

    private static string BudgetTypeName(BudgetItemType type) => type switch
    {
        BudgetItemType.TotalProgrammeBudget => "toplam program bütçesi",
        BudgetItemType.GrantCeiling => "hibe (geri ödemesiz)",
        BudgetItemType.CreditCeiling => "kredi (geri ödemeli borç)",
        BudgetItemType.RepayableSupport => "geri ödemeli destek",
        BudgetItemType.EligibleExpenditure => "uygun harcama tutarı",
        _ => "türü belirlenemedi"
    };

    /// <summary>
    /// Zorunlu belgeler. Sertifika listesi bilinçli olarak "boş olabilir" alandır
    /// (CLAUDE.md §2.2): "belgemiz yok" geçerli bir cevaptır ve <c>NotMet</c> üretir,
    /// <c>Unknown</c> değil.
    /// </summary>
    private static CriterionResult DocumentCriterion(
        IReadOnlyList<RuleEvaluation> evaluations,
        IReadOnlyList<DocumentCheckResult> checklist,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.MandatoryDocuments);

        if (checklist.Count == 0)
        {
            return FromRules(CriterionCatalog.MandatoryDocuments, evaluations, rules);
        }

        var zorunlu = checklist.Where(d => d.IsMandatory).ToList();
        var eksik = zorunlu.Where(d => d.Status != DocumentStatus.Provided).ToList();
        var mandatory = evaluations.Any(e => e.Severity == RuleSeverity.Blocking);

        if (eksik.Count == 0)
        {
            return Build(
                definition,
                mandatory,
                CriterionOutcome.Met,
                zorunlu.Count == 0
                    ? "Çağrı zorunlu belge istemiyor."
                    : $"İstenen {zorunlu.Count} zorunlu belgenin tamamı firmada mevcut.",
                1m,
                evaluations,
                rules);
        }

        var oran = zorunlu.Count == 0 ? 0m : (decimal)(zorunlu.Count - eksik.Count) / zorunlu.Count;

        return Build(
            definition,
            mandatory,
            CriterionOutcome.NotMet,
            $"{zorunlu.Count} zorunlu belgeden {eksik.Count} tanesi eksik: "
            + string.Join(", ", eksik.Select(d => d.Name)) + ".",
            oran,
            evaluations,
            rules);
    }

    /// <summary>
    /// Genç çalışan kriteri — yaş tanımı uyuşmazlığı burada yakalanır.
    ///
    /// <para>
    /// Teşvik programları 25, 29 ve 30 yaş sınırlarını birlikte kullanır. Firma 30 yaş
    /// altını sayıyorsa, 25 yaş altı arayan bir çağrıda o sayı <b>fazla</b> gelir ve
    /// karşılaştırılamaz. Sistem bunu "sağlanıyor" sayarsa firma uygun olmadığı bir
    /// çağrıya başvurur ve reddedilir; "sağlanmıyor" sayarsa gerçekten uygun olabilecek
    /// firma elenir. Doğru cevap: <see cref="CriterionOutcome.Unknown"/> ve tanımın
    /// düzeltilmesi isteği.
    /// </para>
    /// </summary>
    private static CriterionResult YoungEmployeeCriterion(
        Company company,
        Opportunity opportunity,
        IReadOnlyList<RuleEvaluation> evaluations,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.YoungEmployees);

        var yasKurali = opportunity.Rules.FirstOrDefault(r =>
            string.Equals(r.Field, "Workforce.YoungEmployeeMaxAge", StringComparison.OrdinalIgnoreCase));

        var sayimKurali = evaluations.Any(e =>
            e.Field.StartsWith("Workforce.Young", StringComparison.OrdinalIgnoreCase)
            && !e.Field.EndsWith("MaxAge", StringComparison.OrdinalIgnoreCase));

        if (yasKurali is not null
            && sayimKurali
            && decimal.TryParse(yasKurali.Value, out var istenenYas)
            && company.Workforce.YoungDefinitionSatisfies((int)istenenYas) is false)
        {
            return Build(
                definition,
                mandatory: evaluations.Any(e => e.Severity == RuleSeverity.Blocking),
                CriterionOutcome.Unknown,
                $"Çağrı genç çalışanı {istenenYas:0} yaş altı olarak tanımlıyor; firma ise "
                + $"{company.Workforce.YoungEmployeeMaxAge} yaş altını sayıyor. İki sayı karşılaştırılamaz.",
                AnalysisRuleSet.UnknownCredit,
                evaluations,
                rules,
                explanation: $"Firmanın genç çalışan sayısı {company.Workforce.YoungEmployeeMaxAge} yaş "
                    + $"tanımıyla girilmiş. Çağrının {istenenYas:0} yaş tanımına göre yeniden sayılması gerekir; "
                    + "mevcut sayı bu çağrı için ne yeterli ne yetersiz sayılabilir.");
        }

        return FromRules(CriterionCatalog.YoungEmployees, evaluations, rules);
    }

    private static CriterionResult Build(
        CriterionDefinition definition,
        bool mandatory,
        CriterionOutcome outcome,
        string rationale,
        decimal impact,
        IReadOnlyList<RuleEvaluation> evaluations,
        AnalysisRuleSet rules,
        string? explanation = null,
        IReadOnlyList<CriterionEvidence>? evidence = null)
    {
        var kanit = evidence ?? evaluations
            .Where(e => !string.IsNullOrWhiteSpace(e.SourceExcerpt))
            .Select(e => new CriterionEvidence { Excerpt = e.SourceExcerpt!, Locator = e.Requirement })
            .ToList();

        return new CriterionResult
        {
            Code = definition.Code,
            Name = definition.Name,
            IsMandatory = mandatory || (definition.BlockingByNature && outcome == CriterionOutcome.NotMet),
            Outcome = outcome,
            Rationale = rationale,
            CompanyFields = evaluations.Select(e => e.Field).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Evidence = kanit,
            ScoreImpact = Math.Round(Math.Clamp(impact, 0m, 1m), 4),
            MissingOrConflictExplanation = explanation,
            RuleSetVersion = rules.Version,
            Group = definition.Group,
            RuleIds = evaluations.Select(e => e.RuleId).Distinct().ToList()
        };
    }

    /// <summary>
    /// Nihai uygunluk kararı. Mevcut motorun kararıyla uyumlu kalır; yalnızca kriter
    /// düzeyinde ortaya çıkan çelişki ve zorunlu başarısızlık bilgisini ekler.
    /// </summary>
    private static EligibilityVerdict DecideVerdict(
        IReadOnlyList<CriterionResult> criteria,
        EligibilityOutcome outcome)
    {
        if (criteria.Any(c => c.IsMandatoryFailure))
        {
            return EligibilityVerdict.NotEligible;
        }

        // Çelişkili kanıt kesin karar vermeyi engeller: belge kendi içinde tutarsızsa
        // "uygun" demek de "uygun değil" demek de dayanaksızdır.
        if (criteria.Any(c => c.Outcome == CriterionOutcome.ConflictingEvidence))
        {
            return EligibilityVerdict.Indeterminate;
        }

        return outcome.Verdict;
    }
}

