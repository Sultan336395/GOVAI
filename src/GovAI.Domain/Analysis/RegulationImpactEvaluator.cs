using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Regulatory;

namespace GovAI.Domain.Analysis;

/// <summary>
/// Bir mevzuat değişikliğinin firmaya olası etkisi.
///
/// <para>
/// Dört değerlidir çünkü hukukta "hayır" ile "belli değil" farklıdır ve sistem
/// <b>kesin hukuki tavsiye vermez</b>. En sık çıkan sonuç
/// <see cref="PotentiallyApplicable"/> olacaktır; bu bir eksiklik değil, dürüstlüktür:
/// resmî belge çoğu zaman kapsamı tek cümlede yazmaz.
/// </para>
/// </summary>
public enum RegulationImpact
{
    /// <summary>Kapsam belgeden çıkarılamadı.</summary>
    Unknown = 0,

    /// <summary>Belgedeki açık ifadelere göre firma kapsamda.</summary>
    Applicable = 1,

    /// <summary>Belgedeki açık ifadelere göre firma kapsam dışında.</summary>
    NotApplicable = 2,

    /// <summary>Kapsamda olması muhtemel; doğrulanması gereken noktalar var.</summary>
    PotentiallyApplicable = 3
}

/// <summary>Şirket–mevzuat etki analizinin kural tabanlı sonucu.</summary>
public sealed record CompanyRegulationImpact
{
    public required Guid CompanyId { get; init; }

    public required Guid RegulatoryChangeId { get; init; }

    public required DateTimeOffset EvaluatedAt { get; init; }

    public required RegulationImpact Impact { get; init; }

    public required IReadOnlyList<CriterionResult> Criteria { get; init; }

    public required ConfidenceAssessment Confidence { get; init; }

    /// <summary>Kullanıcıya gösterilecek "doğrulanması gereken eksikler" listesi.</summary>
    public IReadOnlyList<string> OpenQuestions =>
        Criteria
            .Where(c => c.Outcome is CriterionOutcome.Unknown or CriterionOutcome.ConflictingEvidence)
            .Select(c => c.MissingOrConflictExplanation ?? c.Rationale)
            .ToList();

    /// <summary>
    /// Ekranda sonucun yanında her zaman görünen uyarı. Sistem hukuk müşaviri değildir;
    /// çıktı bir ön değerlendirmedir.
    /// </summary>
    public const string LegalDisclaimer =
        "Bu değerlendirme resmî belgedeki ifadelere dayanan bir ön incelemedir, hukuki görüş değildir. "
        + "Yükümlülüğün firmanıza uygulanıp uygulanmadığı mali müşavir veya hukuk danışmanınızla doğrulanmalıdır.";
}

/// <summary>
/// Mevzuat değişikliğinin firmaya etkisini deterministik olarak değerlendirir (Faz 3).
///
/// <para>
/// Belge metninde yalnızca <b>açık ifadeler</b> aranır ve bulunan her sonuç kanıt
/// alıntısına bağlanır. Metinde yazmayan bir yükümlülük üretilmez; çıkarım yapılmaz.
/// Model katmanı bu sonucu değiştiremez, yalnızca açıklayabilir.
/// </para>
/// </summary>
public static class RegulationImpactEvaluator
{
    /// <summary>İşverenlik ifadeleri.</summary>
    private static readonly string[] EmployerMarkers =
        ["isveren", "isyeri", "sigortali calistiran", "calisan calistiran", "muhtasar ve prim hizmet"];

    /// <summary>KOBİ/ölçek ifadeleri.</summary>
    private static readonly string[] SmeMarkers =
        ["kucuk ve orta buyuklukteki", "kucuk ve orta olcekli", "kobi", "mikro isletme"];

    /// <summary>Büyük ölçek ifadeleri.</summary>
    private static readonly string[] LargeScaleMarkers =
        ["buyuk olcekli isletme", "bagimsiz denetime tabi"];

    /// <summary>Vergi ve SGK niteliği ifadeleri.</summary>
    private static readonly string[] TaxMarkers =
        ["kurumlar vergisi", "gelir vergisi", "katma deger vergisi", "kdv", "beyanname", "vergi mukellefi"];

    private static readonly string[] SocialSecurityMarkers =
        ["sigorta primi", "prim borcu", "sgk", "sosyal guvenlik", "emeklilik"];

    /// <summary>
    /// Yükümlülük ifadeleri. "Zorunludur" ile "tavsiye edilir" ayrı şeylerdir.
    ///
    /// <para>
    /// <c>zorundadir</c> ayrı bir işaret: resmî metinlerin çoğu yükümlülüğü fiile
    /// bağlıyor ("vermek zorundadır", "bildirim yapmak zorundadır"). Yalnızca
    /// "zorunludur" aransaydı bu cümlelerin hiçbiri yakalanmazdı.
    /// </para>
    /// </summary>
    private static readonly string[] ObligationMarkers =
        ["yukumludur", "zorunludur", "zorundadir", "yukumlulugu", "mecburdur", "yapmakla yukumlu"];

    /// <summary>Geçiş süresi ifadeleri.</summary>
    private static readonly string[] TransitionMarkers =
        ["gecis suresi", "gecici madde", "tarihine kadar uygulanmaz", "sureli olarak", "uyum sureci"];

    /// <summary>Sektör ifadeleri; hangi sektörün adı geçiyorsa kanıt olarak bağlanır.</summary>
    private static readonly string[] SectorMarkers =
        ["imalat", "insaat", "tarim", "madencilik", "turizm", "saglik", "egitim", "bilisim", "lojistik", "tekstil"];

    public static CompanyRegulationImpact Evaluate(
        Company company,
        RegulatoryChange change,
        IReadOnlyList<RegulationEvidence> evidence,
        DateTimeOffset asOf,
        AnalysisRuleSet? ruleSet = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(evidence);

        var rules = ruleSet ?? AnalysisRuleSet.Current;

        var criteria = new List<CriterionResult>
        {
            DomainCriterion(company, change, rules),
            SectorCriterion(company, evidence, rules),
            SizeCriterion(company, evidence, rules),
            EmployerCriterion(company, evidence, rules),
            TaxAndSocialSecurityCriterion(change, evidence, rules),
            JurisdictionCriterion(company, change, rules),
            EffectiveDateCriterion(change, asOf, rules),
            TransitionCriterion(evidence, rules),
            ObligationCriterion(evidence, rules)
        };

        return new CompanyRegulationImpact
        {
            CompanyId = company.Id,
            RegulatoryChangeId = change.Id,
            EvaluatedAt = asOf,
            Impact = Decide(criteria),
            Criteria = criteria,
            Confidence = RegulationConfidence.Calculate(company, change, criteria, asOf, rules)
        };
    }

    /// <summary>
    /// Nihai etki kararı.
    ///
    /// <para>
    /// Kapsam dışı kalmak için <b>açık</b> bir gerekçe gerekir (yargı alanı tutmuyor ya da
    /// düzenleme firmanın hiç taşımadığı bir sıfata bağlı). "Bilmiyorum" kapsam dışı
    /// sayılmaz: firma haberi olmadığı bir yükümlülüğe düşerse zarar sistemin sessizliğinden
    /// doğar.
    /// </para>
    /// </summary>
    private static RegulationImpact Decide(IReadOnlyList<CriterionResult> criteria)
    {
        var kapsamDisi = criteria.Any(c =>
            c.Outcome == CriterionOutcome.NotMet
            && c.Code is CriterionCatalog.RegulationJurisdiction or CriterionCatalog.RegulationDomainMatch);

        if (kapsamDisi)
        {
            return RegulationImpact.NotApplicable;
        }

        var yururlukte = criteria.First(c => c.Code == CriterionCatalog.RegulationEffectiveDate);
        var alan = criteria.First(c => c.Code == CriterionCatalog.RegulationDomainMatch);
        var yukumluluk = criteria.First(c => c.Code == CriterionCatalog.RegulationObligations);

        if (alan.Outcome == CriterionOutcome.Met
            && yukumluluk.Outcome == CriterionOutcome.Met
            && yururlukte.Outcome == CriterionOutcome.Met)
        {
            return RegulationImpact.Applicable;
        }

        if (alan.Outcome == CriterionOutcome.Unknown && yukumluluk.Outcome != CriterionOutcome.Met)
        {
            return RegulationImpact.Unknown;
        }

        return RegulationImpact.PotentiallyApplicable;
    }

    /// <summary>Düzenleme alanının firmanın faaliyetiyle ilgisi.</summary>
    private static CriterionResult DomainCriterion(Company company, RegulatoryChange change, AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.RegulationDomainMatch);
        var calisanVar = company.Workforce.EmployeeCount > 0;

        return change.RegulationDomain switch
        {
            RegulationDomain.Tax or RegulationDomain.CommercialLaw => Build(
                definition, CriterionOutcome.Met, rules,
                $"{Ad(change.RegulationDomain)} alanındaki düzenlemeler faaliyet gösteren her işletmeyi ilgilendirir."),

            RegulationDomain.SocialSecurity or RegulationDomain.LabourLaw when calisanVar => Build(
                definition, CriterionOutcome.Met, rules,
                $"{Ad(change.RegulationDomain)} alanı işveren sıfatına bağlıdır; firmanın "
                + $"{company.Workforce.EmployeeCount} çalışanı var."),

            RegulationDomain.SocialSecurity or RegulationDomain.LabourLaw => Build(
                definition, CriterionOutcome.Unknown, rules,
                $"{Ad(change.RegulationDomain)} alanı işveren sıfatına bağlı; firmanın çalışan sayısı girilmemiş.",
                "Çalışan sayısı profile girildiğinde bu düzenlemenin firmayı kapsayıp kapsamadığı belirlenebilir."),

            RegulationDomain.CorporateGovernance when company.LegalType == LegalType.JointStockCompany => Build(
                definition, CriterionOutcome.Met, rules,
                "Kurumsal yönetim düzenlemeleri anonim şirketleri doğrudan ilgilendirir."),

            RegulationDomain.CorporateGovernance => Build(
                definition, CriterionOutcome.Unknown, rules,
                $"Kurumsal yönetim düzenlemesinin {Ad(company.LegalType)} için kapsamı belgeden çıkarılamadı.",
                "Düzenlemenin hangi şirket türlerini kapsadığı resmî metinden doğrulanmalıdır."),

            RegulationDomain.DataProtection => Build(
                definition, CriterionOutcome.Met, rules,
                "Kişisel veri düzenlemeleri veri işleyen her işletmeyi ilgilendirir."),

            _ => Build(
                definition, CriterionOutcome.Unknown, rules,
                "Düzenlemenin alanı sınıflandırılamadı.",
                "Düzenleme alanı belirlenmeden firmaya etkisi değerlendirilemez.")
        };
    }

    private static CriterionResult SectorCriterion(
        Company company,
        IReadOnlyList<RegulationEvidence> evidence,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.RegulationSector);
        var bulunan = Find(evidence, SectorMarkers);

        if (bulunan.Count == 0)
        {
            return Build(definition, CriterionOutcome.NotApplicable, rules,
                "Belge belirli bir sektöre atıf yapmıyor; sektör kısıtı görünmüyor.");
        }

        var firmaSektoru = company.MainSector;
        if (string.IsNullOrWhiteSpace(firmaSektoru))
        {
            return Build(definition, CriterionOutcome.Unknown, rules,
                $"Belge {bulunan.Count} yerde sektör adı geçiriyor ancak firmanın ana sektörü girilmemiş.",
                "Firmanın ana sektörü profile girilmeden sektör kapsamı karşılaştırılamaz.",
                bulunan);
        }

        var katlanmisSektor = TurkceMetin.Katla(firmaSektoru);
        var eslesen = bulunan.Any(e => SectorMarkers.Any(m =>
            TurkceMetin.Katla(e.Excerpt).Contains(m, StringComparison.Ordinal)
            && katlanmisSektor.Contains(m, StringComparison.Ordinal)));

        return eslesen
            ? Build(definition, CriterionOutcome.Met, rules,
                $"Belgede geçen sektör ifadesi firmanın ana sektörü ({firmaSektoru}) ile örtüşüyor.", null, bulunan)
            : Build(definition, CriterionOutcome.Unknown, rules,
                $"Belgede sektör adı geçiyor ancak firmanın ana sektörü ({firmaSektoru}) ile eşleşmiyor.",
                "Düzenlemenin yalnızca adı geçen sektörlerle sınırlı olup olmadığı resmî metinden doğrulanmalıdır.",
                bulunan);
    }

    private static CriterionResult SizeCriterion(
        Company company,
        IReadOnlyList<RegulationEvidence> evidence,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.RegulationCompanySize);

        var kobi = Find(evidence, SmeMarkers);
        var buyuk = Find(evidence, LargeScaleMarkers);

        if (kobi.Count == 0 && buyuk.Count == 0)
        {
            return Build(definition, CriterionOutcome.NotApplicable, rules,
                "Belge işletme ölçeğine göre bir ayrım yapmıyor.");
        }

        var firmaKobi = company.Size != EnterpriseSize.Large;

        if (kobi.Count > 0 && buyuk.Count > 0)
        {
            return Build(definition, CriterionOutcome.ConflictingEvidence, rules,
                "Belge hem KOBİ hem büyük ölçekli işletme ifadeleri içeriyor; kapsam tek okumaya indirgenemiyor.",
                "Ölçek kapsamı resmî metinden doğrulanmalıdır.",
                [.. kobi, .. buyuk]);
        }

        if (kobi.Count > 0)
        {
            return firmaKobi
                ? Build(definition, CriterionOutcome.Met, rules,
                    $"Belge KOBİ'lere atıf yapıyor; firma {Ad(company.Size)} ölçeğinde.", null, kobi)
                : Build(definition, CriterionOutcome.NotMet, rules,
                    "Belge KOBİ'lere atıf yapıyor; firma büyük ölçekli.", null, kobi);
        }

        return firmaKobi
            ? Build(definition, CriterionOutcome.NotMet, rules,
                $"Belge büyük ölçekli işletmelere atıf yapıyor; firma {Ad(company.Size)} ölçeğinde.", null, buyuk)
            : Build(definition, CriterionOutcome.Met, rules,
                "Belge büyük ölçekli işletmelere atıf yapıyor; firma bu ölçekte.", null, buyuk);
    }

    private static CriterionResult EmployerCriterion(
        Company company,
        IReadOnlyList<RegulationEvidence> evidence,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.RegulationEmployerStatus);
        var bulunan = Find(evidence, EmployerMarkers);

        if (bulunan.Count == 0)
        {
            return Build(definition, CriterionOutcome.NotApplicable, rules,
                "Belge işveren sıfatına atıf yapmıyor.");
        }

        if (company.Workforce.EmployeeCount == 0)
        {
            return Build(definition, CriterionOutcome.Unknown, rules,
                "Belge işverenlere yönelik ancak firmanın çalışan sayısı girilmemiş.",
                "Çalışan sayısı girilmeden firmanın işveren sıfatı taşıyıp taşımadığı belirlenemez.",
                bulunan);
        }

        return Build(definition, CriterionOutcome.Met, rules,
            $"Belge işverenlere yönelik; firma {company.Workforce.EmployeeCount} çalışanla işveren sıfatı taşıyor.",
            null, bulunan);
    }

    private static CriterionResult TaxAndSocialSecurityCriterion(
        RegulatoryChange change,
        IReadOnlyList<RegulationEvidence> evidence,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.RegulationTaxAndSocialSecurity);

        var vergi = Find(evidence, TaxMarkers);
        var sgk = Find(evidence, SocialSecurityMarkers);
        var bulunan = (List<RegulationEvidence>)[.. vergi, .. sgk];

        if (bulunan.Count == 0)
        {
            return Build(definition, CriterionOutcome.NotApplicable, rules,
                "Belge vergi veya SGK niteliğine bağlı bir ayrım yapmıyor.");
        }

        var nitelik = vergi.Count > 0 && sgk.Count > 0 ? "vergi ve SGK"
            : vergi.Count > 0 ? "vergi"
            : "SGK";

        // Her faal işletme vergi mükellefidir; SGK niteliği çalışan varlığına bağlıdır ve
        // o ayrım işverenlik kriterinde yapılır. Burada yalnızca konunun ilgili olduğu
        // söylenir — belgede yazmayan bir mükellefiyet üretilmez.
        return Build(definition, CriterionOutcome.Met, rules,
            $"Belge {nitelik} yükümlülüklerine değiniyor; {Ad(change.RegulationDomain)} alanındaki bu konu "
            + "faaliyet gösteren işletmeleri ilgilendirir.",
            null, bulunan);
    }

    private static CriterionResult JurisdictionCriterion(
        Company company,
        RegulatoryChange change,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.RegulationJurisdiction);

        if (string.Equals(change.Jurisdiction, "TR", StringComparison.OrdinalIgnoreCase))
        {
            return Build(definition, CriterionOutcome.Met, rules,
                "Düzenleme Türkiye mevzuatıdır; firma Türkiye'de kayıtlıdır.");
        }

        if (string.Equals(change.Jurisdiction, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return company.ExportFlag
                ? Build(definition, CriterionOutcome.Unknown, rules,
                    "Düzenleme AB mevzuatıdır; firma ihracat yapıyor ancak AB pazarına satış yapıp yapmadığı bilinmiyor.",
                    "Firmanın AB'ye satış yapıp yapmadığı doğrulanmadan AB düzenlemesinin etkisi belirlenemez.")
                : Build(definition, CriterionOutcome.NotMet, rules,
                    "Düzenleme AB mevzuatıdır; firma kaydında ihracat faaliyeti görünmüyor.");
        }

        return Build(definition, CriterionOutcome.Unknown, rules,
            $"Düzenlemenin yargı alanı ({change.Jurisdiction}) firmanın faaliyet alanıyla eşleştirilemedi.",
            "Yargı alanı doğrulanmadan etki değerlendirilemez.");
    }

    private static CriterionResult EffectiveDateCriterion(
        RegulatoryChange change,
        DateTimeOffset asOf,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.RegulationEffectiveDate);

        if (change.EffectiveDate is null)
        {
            return change.PublicationDate is null
                ? Build(definition, CriterionOutcome.Unknown, rules,
                    "Belgede yayın ve yürürlük tarihi bulunamadı.",
                    "Yürürlük tarihi resmî metinden doğrulanmalıdır; tarih tahmin edilmez.")
                : Build(definition, CriterionOutcome.Unknown, rules,
                    $"Belge {change.PublicationDate:dd.MM.yyyy} tarihinde yayımlanmış ancak yürürlük tarihi yazmıyor.",
                    "Yürürlük tarihi resmî metinden doğrulanmalıdır; yayın tarihi yürürlük tarihi sayılmaz.");
        }

        return change.EffectiveDate <= asOf
            ? Build(definition, CriterionOutcome.Met, rules,
                $"Düzenleme {change.EffectiveDate:dd.MM.yyyy} tarihinde yürürlüğe girdi.")
            : Build(definition, CriterionOutcome.NotMet, rules,
                $"Düzenleme {change.EffectiveDate:dd.MM.yyyy} tarihinde yürürlüğe girecek; henüz yürürlükte değil.");
    }

    private static CriterionResult TransitionCriterion(
        IReadOnlyList<RegulationEvidence> evidence,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.RegulationTransitionPeriod);
        var bulunan = Find(evidence, TransitionMarkers);

        return bulunan.Count == 0
            ? Build(definition, CriterionOutcome.NotApplicable, rules,
                "Belgede geçiş süresine ilişkin bir ifade bulunamadı.")
            : Build(definition, CriterionOutcome.Met, rules,
                $"Belgede {bulunan.Count} yerde geçiş süresine ilişkin ifade var.", null, bulunan);
    }

    private static CriterionResult ObligationCriterion(
        IReadOnlyList<RegulationEvidence> evidence,
        AnalysisRuleSet rules)
    {
        var definition = CriterionCatalog.Get(CriterionCatalog.RegulationObligations);
        var bulunan = Find(evidence, ObligationMarkers);

        return bulunan.Count == 0
            ? Build(definition, CriterionOutcome.Unknown, rules,
                "Belgede firmayı bağlayan açık bir yükümlülük ifadesi bulunamadı.",
                "Yükümlülük belgede açıkça yazmıyorsa üretilmez; metnin tamamı incelenmelidir.")
            : Build(definition, CriterionOutcome.Met, rules,
                $"Belgede {bulunan.Count} yerde açık yükümlülük ifadesi var.", null, bulunan);
    }

    /// <summary>Belge metninde işareti geçen kanıt parçalarını döner; Türkçe katlama uygulanır.</summary>
    private static List<RegulationEvidence> Find(IReadOnlyList<RegulationEvidence> evidence, string[] markers) =>
        evidence
            .Where(e => markers.Any(m => TurkceMetin.Katla(e.Excerpt).Contains(m, StringComparison.Ordinal)))
            .ToList();

    private static CriterionResult Build(
        CriterionDefinition definition,
        CriterionOutcome outcome,
        AnalysisRuleSet rules,
        string rationale,
        string? explanation = null,
        IReadOnlyList<RegulationEvidence>? evidence = null) =>
        new()
        {
            Code = definition.Code,
            Name = definition.Name,
            // Mevzuat etkisinde "zorunlu kriter" yoktur: uyum bir başvuru koşulu değildir
            // ve tek bir kriterin sağlanmaması firmayı kapsam dışına çıkarmaz.
            IsMandatory = false,
            Outcome = outcome,
            Rationale = rationale,
            Evidence = (evidence ?? [])
                .Select(e => new CriterionEvidence
                {
                    EvidenceChunkId = e.EvidenceChunkId,
                    DocumentVersionId = e.DocumentVersionId,
                    Excerpt = e.Excerpt,
                    Locator = e.Locator
                })
                .ToList(),
            ScoreImpact = outcome switch
            {
                CriterionOutcome.Met => 1m,
                CriterionOutcome.Unknown => AnalysisRuleSet.UnknownCredit,
                CriterionOutcome.ConflictingEvidence => AnalysisRuleSet.ConflictCredit,
                _ => 0m
            },
            MissingOrConflictExplanation = explanation,
            RuleSetVersion = rules.Version,
            Group = definition.Group
        };

    private static string Ad(RegulationDomain domain) => domain switch
    {
        RegulationDomain.Tax => "Vergi",
        RegulationDomain.SocialSecurity => "Sosyal güvenlik",
        RegulationDomain.LabourLaw => "İş hukuku",
        RegulationDomain.CommercialLaw => "Ticaret hukuku",
        RegulationDomain.DataProtection => "Kişisel verilerin korunması",
        RegulationDomain.CorporateGovernance => "Kurumsal yönetim",
        _ => "Diğer"
    };

    private static string Ad(LegalType type) => type switch
    {
        LegalType.SoleProprietorship => "şahıs işletmesi",
        LegalType.LimitedCompany => "limited şirket",
        LegalType.JointStockCompany => "anonim şirket",
        LegalType.Cooperative => "kooperatif",
        LegalType.Association => "dernek",
        LegalType.Foundation => "vakıf",
        LegalType.PublicEntity => "kamu kurumu",
        _ => "türü belirsiz işletme"
    };

    private static string Ad(EnterpriseSize size) => size switch
    {
        EnterpriseSize.Micro => "mikro",
        EnterpriseSize.Small => "küçük",
        EnterpriseSize.Medium => "orta",
        _ => "büyük"
    };
}

/// <summary>
/// Mevzuat metninden alınmış tek bir kanıt parçası.
///
/// <para>
/// Domain katmanı belge deposunu tanımaz; kanıtlar dışarıdan bu sade biçimde verilir.
/// Böylece motor test edilebilir kalır ve veri erişimi katman sınırını geçmez.
/// </para>
/// </summary>
public sealed record RegulationEvidence
{
    public Guid? EvidenceChunkId { get; init; }

    public Guid? DocumentVersionId { get; init; }

    public required string Excerpt { get; init; }

    public string? Locator { get; init; }
}
