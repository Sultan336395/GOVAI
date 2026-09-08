using GovAI.Domain.Analysis;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Tests;

/// <summary>
/// Kural–kanıt bağlantısının davranış sözleşmesi (Faz 3 — kritik eksik 2).
///
/// Bir kriter birden çok kanıt parçasına dayanabilir: son başvuru tarihi bir
/// paragrafta, bütçe başkasında, sektör kısıtı üçüncüsünde yazar. Kurala tek bir
/// nullable kanıt kolonu eklenseydi ikinci kanıt kaydedilemez, kaydedilen tek kanıt da
/// hangi bilginin dayanağı olduğunu söyleyemezdi.
///
/// Kanıtsız kural <b>silinmez</b> ve deterministik motorda çalışmaya devam eder;
/// yalnızca yapay zekâ o kural hakkında "belgede şöyle yazıyor" diyemez.
/// </summary>
public class RuleEvidenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid BelgeSurumu = Guid.CreateVersion7();

    private static OpportunityRule Kural(string alan = "Workforce.EmployeeCount") =>
        new(alan, RuleOperator.GreaterThanOrEqual, "10", RuleDimension.Employment,
            RuleSeverity.Major, "Asgari 10 çalışan.",
            sourceExcerpt: "Başvuru sahibinin en az 10 çalışanı olmalıdır.");

    private static OpportunityRuleEvidence Kanit(
        RuleEvidenceRole rol = RuleEvidenceRole.ValueSource,
        Guid? parcaId = null,
        int baslangic = 100,
        int bitis = 180) =>
        new(parcaId ?? Guid.CreateVersion7(), BelgeSurumu, rol, baslangic, bitis, Now,
            pageNumber: 1, sectionTitle: "Başvuru Şartları");

    [Fact(DisplayName = "KK1. Bir kural birden fazla kanıt parçasına bağlanabilir")]
    public void Kural_birden_fazla_kanita_baglanir()
    {
        var kural = Kural();

        kural.AttachEvidence(Kanit(RuleEvidenceRole.ValueSource));
        kural.AttachEvidence(Kanit(RuleEvidenceRole.ConditionText));
        kural.AttachEvidence(Kanit(RuleEvidenceRole.Supporting));

        Assert.Equal(3, kural.Evidence.Count);
    }

    [Fact(DisplayName = "KK2. Aynı parça aynı rolle iki kez bağlanmaz")]
    public void Ayni_kanit_mukerrer_baglanmaz()
    {
        var kural = Kural();
        var parcaId = Guid.CreateVersion7();

        kural.AttachEvidence(Kanit(RuleEvidenceRole.ValueSource, parcaId));
        kural.AttachEvidence(Kanit(RuleEvidenceRole.ValueSource, parcaId));

        Assert.Single(kural.Evidence);
    }

    [Fact(DisplayName = "KK3. Aynı parça farklı rolle bağlanabilir")]
    public void Ayni_parca_farkli_rolle_baglanabilir()
    {
        var kural = Kural();
        var parcaId = Guid.CreateVersion7();

        kural.AttachEvidence(Kanit(RuleEvidenceRole.ValueSource, parcaId));
        kural.AttachEvidence(Kanit(RuleEvidenceRole.ConditionText, parcaId));

        Assert.Equal(2, kural.Evidence.Count);
    }

    [Fact(DisplayName = "KK4. Kanıtsız kural yapay zekâ iddiasına kapalıdır")]
    public void Kanitsiz_kural_iddiaya_kapali()
    {
        var kural = Kural();

        Assert.False(kural.SupportsAiClaims);
    }

    [Fact(DisplayName = "KK5. Yalnızca bağlam parçası olan kural da iddiaya kapalıdır")]
    public void Baglam_parcasi_tek_basina_yetmez()
    {
        // Bölüm başlığına dayanan "belgede yazıyor" iddiası doğrulanamaz.
        var kural = Kural();
        kural.AttachEvidence(Kanit(RuleEvidenceRole.Supporting));

        Assert.False(kural.SupportsAiClaims);
        Assert.Single(kural.Evidence);
    }

    [Fact(DisplayName = "KK6. Değer kanıtı olan kural iddiaya açıktır")]
    public void Deger_kaniti_iddiaya_acar()
    {
        var kural = Kural();
        kural.AttachEvidence(Kanit(RuleEvidenceRole.ValueSource));

        Assert.True(kural.SupportsAiClaims);
    }

    [Fact(DisplayName = "KK7. Geçersiz kanıt aralığı reddedilir")]
    public void Gecersiz_aralik_reddedilir()
    {
        Assert.Throws<DomainException>(() => Kanit(baslangic: 200, bitis: 100));
        Assert.Throws<DomainException>(() =>
            new OpportunityRuleEvidence(Guid.Empty, BelgeSurumu, RuleEvidenceRole.ValueSource, 0, 1, Now));
        Assert.Throws<DomainException>(() =>
            new OpportunityRuleEvidence(Guid.CreateVersion7(), Guid.Empty, RuleEvidenceRole.ValueSource, 0, 1, Now));
    }

    [Fact(DisplayName = "KK8. Kriter kanıtı gerçek parça kimliğini taşır")]
    public void Kriter_kaniti_parca_kimligini_tasir()
    {
        var parcaId = Guid.CreateVersion7();
        var kural = Kural();
        kural.AttachEvidence(Kanit(RuleEvidenceRole.ValueSource, parcaId));

        var cagri = Cagri([kural]);
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now);

        var kriter = analiz.Criteria.Single(c => c.Code == CriterionCatalog.EmployeeCount);
        var kanit = Assert.Single(kriter.Evidence);

        Assert.Equal(parcaId, kanit.EvidenceChunkId);
        Assert.Equal(BelgeSurumu, kanit.DocumentVersionId);
        Assert.Contains("en az 10 çalışanı", kanit.Excerpt);
    }

    [Fact(DisplayName = "KK9. Kanıtsız kuralın kriteri de kanıt kimliği taşımaz")]
    public void Kanitsiz_kural_kimliksiz_kanit_uretir()
    {
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), Cagri([Kural()]), Now);

        var kriter = analiz.Criteria.Single(c => c.Code == CriterionCatalog.EmployeeCount);
        var kanit = Assert.Single(kriter.Evidence);

        // Alıntı var (kural metninden), kimlik yok: yapay zekâ buna dayanamaz.
        Assert.Null(kanit.EvidenceChunkId);
        Assert.False(string.IsNullOrWhiteSpace(kanit.Excerpt));
    }

    [Fact(DisplayName = "KK10. Kanıtsız kural hakkında yapay zekâ iddiası reddedilir")]
    public void Kanitsiz_kural_hakkinda_iddia_reddedilir()
    {
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), Cagri([Kural()]), Now);

        // Model, kanıt kümesinde olmayan bir parçaya atıf yapıyor. Kanıt kümesi boş
        // olduğu için (kural kanıtsız) iddia doğrulanamaz.
        var cikti = new AiAnalysisOutput
        {
            Status = AiAnalysisStatus.Succeeded,
            Claims =
            [
                new AiClaim
                {
                    ClaimType = AiClaimType.SupportsCriterion,
                    CriterionCode = CriterionCatalog.EmployeeCount,
                    EvidenceChunkId = Guid.CreateVersion7(),
                    Explanation = "Belge en az 10 çalışan istiyor.",
                    Confidence = 0.95m
                }
            ]
        };

        var birlesik = DecisionMerger.Merge(analiz.Criteria, cikti, []);

        Assert.Equal(ClaimRejectionReason.UnknownEvidence, birlesik.RejectedClaims[0].RejectionReason);
        Assert.Empty(birlesik.AiExplanations);
    }

    [Fact(DisplayName = "KK11. Zorunlu kriter bilinmiyorsa sonuç kesin 'Uygun' olmaz")]
    public void Zorunlu_unknown_kesin_uygun_uretmez()
    {
        // Profili boş firma + engelleyici koşul: kriter Unknown kalır, elenmez.
        var profilsiz = new Company(Guid.CreateVersion7(), "Yeni Ltd. Şti.", "9998887776", LegalType.LimitedCompany);

        var engelleyici = new OpportunityRule(
            "Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
            RuleDimension.Employment, RuleSeverity.Blocking, "Asgari 10 çalışan.");

        var analiz = OpportunityCriteriaEvaluator.Evaluate(profilsiz, Cagri([engelleyici]), Now);
        var kriter = analiz.Criteria.Single(c => c.Code == CriterionCatalog.EmployeeCount);

        Assert.Equal(CriterionOutcome.Unknown, kriter.Outcome);
        Assert.True(kriter.IsMandatory);
        Assert.NotEqual(EligibilityVerdict.Eligible, analiz.Verdict);
        Assert.Equal(EligibilityVerdict.Indeterminate, analiz.Verdict);
        // Eksik veri firmayı ELEMEZ: puan sıfırlanmaz.
        Assert.True(analiz.Score.Value > 0m);
    }

    private static Company Firma()
    {
        var company = new Company(Guid.CreateVersion7(), "Kanıt Testi A.Ş.", "1112223334", LegalType.JointStockCompany);
        company.UpdateIdentity("Kanıt Testi A.Ş.", LegalType.JointStockCompany, new DateOnly(2015, 1, 1));
        company.UpdateWorkforce(new Workforce(42, 14, 10, 7, 2, youngEmployeeMaxAge: 29));
        company.ReplaceNaceCodes([new CompanyNaceCode("2562", isPrimary: true)]);
        company.ReplaceLocations([new CompanyLocation("Mersin", "Merkez", "TR62", isHeadquarters: true)]);

        return company;
    }

    private static Opportunity Cagri(IEnumerable<OpportunityRule> kurallar)
    {
        var opportunity = new Opportunity(
            Guid.CreateVersion7(), SourceType.KosgebOrSimilar, SupportCategory.Grant,
            "Kanıt Testi Çağrısı", "KOSGEB", Now.AddDays(-5));

        opportunity.SetSchedule(Now.AddDays(-5), Now.AddDays(30));
        opportunity.ReplaceRules(kurallar, extractionConfidence: 0.9m);

        return opportunity;
    }
}
