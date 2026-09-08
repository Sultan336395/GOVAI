using GovAI.Domain.Analysis;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Tests;

/// <summary>
/// Kriter modelinin davranış sözleşmesi (Faz 3 — Aşama 1).
///
/// Kriter, kuralın değil <b>konunun</b> birimidir. Bir çağrıda "çalışan sayısı" için üç
/// ayrı kural olabilir; kullanıcı ekranda tek bir "Çalışan sayısı" satırı görür ve o
/// satırın sonucu üç kuralın birleşimidir.
///
/// Bu testler dört şeyi sabitler:
/// 1. Eksik veri asla <c>NotMet</c> sayılmaz — sistem kendi eksiğini firmaya fatura etmez.
/// 2. Belgedeki çelişki gizlenmez; ayrı bir sonuç olarak görünür.
/// 3. Zorunluluk belgeden gelir, kataloğun varsayımından değil.
/// 4. Her kriter kendi kanıtına ve kural seti sürümüne bağlıdır.
/// </summary>
public class AnalysisCriteriaTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static Company Firma(
        int calisan = 42,
        int? gencYasSiniri = null,
        int genc = 10)
    {
        var company = new Company(Guid.CreateVersion7(), "Örnek Üretim A.Ş.", "1112223334", LegalType.JointStockCompany);
        company.UpdateIdentity("Örnek Üretim A.Ş.", LegalType.JointStockCompany, new DateOnly(2015, 1, 1));
        company.UpdateWorkforce(new Workforce(calisan, 14, genc, 7, 2, gencYasSiniri));
        company.UpdateFinancials(new Financials(50_000_000m, 30_000_000m, 12_000_000m, 8_000_000m, "TRY", 2025));
        company.ReplaceNaceCodes([new CompanyNaceCode("2562", isPrimary: true)]);
        company.ReplaceLocations([new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: true)]);

        return company;
    }

    /// <summary>Profili boş firma: çalışan sayısı bile girilmemiş.</summary>
    private static Company ProfilsizFirma()
    {
        var company = new Company(Guid.CreateVersion7(), "Yeni Ltd. Şti.", "9998887776", LegalType.LimitedCompany);
        return company;
    }

    private static Opportunity Cagri(string baslik = "Test Çağrısı", int sonBasvuruGun = 20)
    {
        // Süresi geçmiş çağrı senaryosunda yayın tarihi de geriye alınır; son başvuru
        // tarihi yayın tarihinden önce olamaz.
        var yayin = Now.AddDays(Math.Min(-3, sonBasvuruGun - 30));

        var opportunity = new Opportunity(
            Guid.CreateVersion7(), SourceType.KosgebOrSimilar, SupportCategory.Grant,
            baslik, "KOSGEB", yayin);

        opportunity.SetSchedule(yayin, Now.AddDays(sonBasvuruGun));

        return opportunity;
    }

    private static CriterionResult Kriter(CompanyOpportunityAnalysis analiz, string kod) =>
        analiz.Criteria.Single(c => c.Code == kod);

    [Fact(DisplayName = "K1. Kriter kataloğundaki her kod tanımlıdır ve Türkçe adı vardır")]
    public void Katalog_kodlari_tanimlidir()
    {
        foreach (var kod in CriterionCatalog.OpportunityCriteria.Concat(CriterionCatalog.RegulationCriteria))
        {
            var tanim = CriterionCatalog.Get(kod);

            Assert.False(string.IsNullOrWhiteSpace(tanim.Name));
            Assert.False(string.IsNullOrWhiteSpace(tanim.Question));
            // Ham enum adı kullanıcıya sızmamalı.
            Assert.DoesNotContain('_', tanim.Name);
        }
    }

    [Fact(DisplayName = "K2. Eksik firma verisi NotMet değil Unknown üretir")]
    public void Eksik_veri_notmet_sayilmaz()
    {
        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
                RuleDimension.Employment, RuleSeverity.Blocking, "Asgari 10 çalışan.")],
            extractionConfidence: 0.8m);

        var analiz = OpportunityCriteriaEvaluator.Evaluate(ProfilsizFirma(), cagri, Now);
        var kriter = Kriter(analiz, CriterionCatalog.EmployeeCount);

        Assert.Equal(CriterionOutcome.Unknown, kriter.Outcome);
        Assert.NotEqual(CriterionOutcome.NotMet, kriter.Outcome);
        // Zorunlu kriterde başarısızlık sayılmaz: firma elenmez.
        Assert.False(kriter.IsMandatoryFailure);
        Assert.False(analiz.HasMandatoryFailure);
    }

    [Fact(DisplayName = "K3. Eksik veri açıklaması hangi alanın doldurulacağını söyler")]
    public void Eksik_veri_aciklamasi_alani_soyler()
    {
        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
                RuleDimension.Employment, RuleSeverity.Major, "Asgari 10 çalışan.")],
            extractionConfidence: 0.8m);

        var kriter = Kriter(OpportunityCriteriaEvaluator.Evaluate(ProfilsizFirma(), cagri, Now),
            CriterionCatalog.EmployeeCount);

        Assert.NotNull(kriter.MissingOrConflictExplanation);
        Assert.Contains("Workforce.EmployeeCount", kriter.MissingOrConflictExplanation);
    }

    [Fact(DisplayName = "K4. Karşılanmayan koşul NotMet üretir ve firma değerini yazar")]
    public void Karsilanmayan_kosul_notmet_uretir()
    {
        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Workforce.EmployeeCount", RuleOperator.LessThanOrEqual, "10",
                RuleDimension.Employment, RuleSeverity.Major, "En fazla 10 çalışan.")],
            extractionConfidence: 0.8m);

        var kriter = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(calisan: 42), cagri, Now),
            CriterionCatalog.EmployeeCount);

        Assert.Equal(CriterionOutcome.NotMet, kriter.Outcome);
        Assert.Contains("42", kriter.Rationale);
    }

    [Fact(DisplayName = "K5. Aynı alanda çelişen iki koşul ConflictingEvidence üretir")]
    public void Celisen_kosullar_celiski_uretir()
    {
        // Gerçek belgelerde görülen durum: aynı sayfada iki farklı ölçek koşulu yazıyor.
        var cagri = Cagri();
        cagri.ReplaceRules(
            [
                new OpportunityRule("Workforce.EmployeeCount", RuleOperator.LessThanOrEqual, "10",
                    RuleDimension.Employment, RuleSeverity.Major, "En fazla 10 çalışan."),
                new OpportunityRule("Workforce.EmployeeCount", RuleOperator.LessThanOrEqual, "250",
                    RuleDimension.Employment, RuleSeverity.Major, "En fazla 250 çalışan.")
            ],
            extractionConfidence: 0.8m);

        var kriter = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(calisan: 42), cagri, Now),
            CriterionCatalog.EmployeeCount);

        Assert.Equal(CriterionOutcome.ConflictingEvidence, kriter.Outcome);
        Assert.NotNull(kriter.MissingOrConflictExplanation);
    }

    [Fact(DisplayName = "K6. Çelişki kesin karar verdirmez; sonuç belirsiz kalır")]
    public void Celiski_kesin_karar_verdirmez()
    {
        var cagri = Cagri();
        cagri.ReplaceRules(
            [
                new OpportunityRule("Workforce.EmployeeCount", RuleOperator.LessThanOrEqual, "10",
                    RuleDimension.Employment, RuleSeverity.Major, "En fazla 10 çalışan."),
                new OpportunityRule("Workforce.EmployeeCount", RuleOperator.LessThanOrEqual, "250",
                    RuleDimension.Employment, RuleSeverity.Major, "En fazla 250 çalışan.")
            ],
            extractionConfidence: 0.8m);

        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(calisan: 42), cagri, Now);

        Assert.Equal(EligibilityVerdict.Indeterminate, analiz.Verdict);
        Assert.Single(analiz.Conflicting);
    }

    [Fact(DisplayName = "K7. Bağımsız iki koşulun biri tutmuyorsa bu çelişki DEĞİLDİR")]
    public void Bagimsiz_kosullar_celiski_sayilmaz()
    {
        // "Ciro ≥ 1M" ve "bilanço ≥ 100M" birlikte istenebilir; biri tutmayınca belge
        // çelişmiş olmaz, koşul sağlanmamış olur.
        var cagri = Cagri();
        cagri.ReplaceRules(
            [
                new OpportunityRule("Financials.AnnualRevenue", RuleOperator.GreaterThanOrEqual, "1000000",
                    RuleDimension.Financial, RuleSeverity.Major, "Asgari 1.000.000 TL ciro."),
                new OpportunityRule("Financials.BalanceSize", RuleOperator.GreaterThanOrEqual, "100000000",
                    RuleDimension.Financial, RuleSeverity.Major, "Asgari 100.000.000 TL bilanço.")
            ],
            extractionConfidence: 0.8m);

        var kriter = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now),
            CriterionCatalog.RevenueAndFinancials);

        Assert.Equal(CriterionOutcome.NotMet, kriter.Outcome);
    }

    [Fact(DisplayName = "K8. Zorunluluk belgeden gelir: Blocking kural kriteri zorunlu yapar")]
    public void Zorunluluk_belgeden_gelir()
    {
        var engelleyici = Cagri();
        engelleyici.ReplaceRules(
            [new OpportunityRule("Company.LegalType", RuleOperator.Equals, "Cooperative",
                RuleDimension.Financial, RuleSeverity.Blocking, "Yalnızca kooperatifler başvurabilir.")],
            extractionConfidence: 0.9m);

        var bilgilendirici = Cagri();
        bilgilendirici.ReplaceRules(
            [new OpportunityRule("Company.LegalType", RuleOperator.Equals, "Cooperative",
                RuleDimension.Financial, RuleSeverity.Minor, "Kooperatifler tercih edilir.")],
            extractionConfidence: 0.9m);

        var zorunlu = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(), engelleyici, Now),
            CriterionCatalog.CompanyType);
        var tercih = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(), bilgilendirici, Now),
            CriterionCatalog.CompanyType);

        Assert.True(zorunlu.IsMandatory);
        Assert.False(tercih.IsMandatory);
    }

    [Fact(DisplayName = "K9. Zorunlu kriter sağlanmıyorsa karar 'uygun değil' olur")]
    public void Zorunlu_kriter_saglanmazsa_uygun_degil()
    {
        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Company.LegalType", RuleOperator.Equals, "Cooperative",
                RuleDimension.Financial, RuleSeverity.Blocking, "Yalnızca kooperatifler başvurabilir.")],
            extractionConfidence: 0.9m);

        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now);

        Assert.True(analiz.HasMandatoryFailure);
        Assert.Equal(EligibilityVerdict.NotEligible, analiz.Verdict);
        Assert.Equal(0m, analiz.Score.Value);
    }

    [Fact(DisplayName = "K10. Koşul içermeyen başlık NotApplicable olur, sıfır puan değil")]
    public void Kosulsuz_baslik_notapplicable_olur()
    {
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), Cagri(), Now);
        var kriter = Kriter(analiz, CriterionCatalog.Geography);

        Assert.Equal(CriterionOutcome.NotApplicable, kriter.Outcome);
    }

    [Fact(DisplayName = "K11. Süresi geçmiş çağrıda başvuru dönemi zorunlu başarısızlıktır")]
    public void Suresi_gecmis_cagri_zorunlu_basarisizlik()
    {
        var gecmis = Cagri(sonBasvuruGun: -5);

        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), gecmis, Now);
        var kriter = Kriter(analiz, CriterionCatalog.ApplicationWindow);

        Assert.Equal(CriterionOutcome.NotMet, kriter.Outcome);
        Assert.True(kriter.IsMandatory);
        Assert.Equal(EligibilityVerdict.NotEligible, analiz.Verdict);
    }

    [Fact(DisplayName = "K12. Son başvuru tarihi yazmayan çağrı Unknown olur, süresiz sayılmaz")]
    public void Tarihsiz_cagri_unknown_olur()
    {
        var cagri = new Opportunity(
            Guid.CreateVersion7(), SourceType.KosgebOrSimilar, SupportCategory.Grant,
            "Tarihsiz Çağrı", "KOSGEB", Now.AddDays(-3));

        var kriter = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now),
            CriterionCatalog.ApplicationWindow);

        Assert.Equal(CriterionOutcome.Unknown, kriter.Outcome);
        Assert.False(kriter.IsMandatory);
        Assert.Contains("tahmin edilmez", kriter.MissingOrConflictExplanation);
    }

    [Fact(DisplayName = "K13. Genç çalışan yaş tanımı uyuşmazlığı Unknown üretir")]
    public void Genc_calisan_yas_tanimi_uyusmazligi()
    {
        // Firma 30 yaş altını sayıyor, çağrı 25 yaş altı istiyor: sayı karşılaştırılamaz.
        var cagri = Cagri();
        cagri.ReplaceRules(
            [
                new OpportunityRule("Workforce.YoungEmployeeMaxAge", RuleOperator.LessThanOrEqual, "25",
                    RuleDimension.Employment, RuleSeverity.Major, "Genç çalışan 25 yaş altı sayılır."),
                new OpportunityRule("Workforce.YoungEmployeeCount", RuleOperator.GreaterThanOrEqual, "5",
                    RuleDimension.Employment, RuleSeverity.Major, "Asgari 5 genç çalışan.")
            ],
            extractionConfidence: 0.8m);

        var kriter = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(gencYasSiniri: 30, genc: 12), cagri, Now),
            CriterionCatalog.YoungEmployees);

        Assert.Equal(CriterionOutcome.Unknown, kriter.Outcome);
        Assert.Contains("30", kriter.MissingOrConflictExplanation);
        Assert.Contains("25", kriter.MissingOrConflictExplanation);
    }

    [Fact(DisplayName = "K14. Yaş tanımı tutuyorsa genç çalışan kriteri normal değerlendirilir")]
    public void Yas_tanimi_tutuyorsa_normal_degerlendirilir()
    {
        var cagri = Cagri();
        cagri.ReplaceRules(
            [
                new OpportunityRule("Workforce.YoungEmployeeMaxAge", RuleOperator.LessThanOrEqual, "30",
                    RuleDimension.Employment, RuleSeverity.Major, "Genç çalışan 30 yaş altı sayılır."),
                new OpportunityRule("Workforce.YoungEmployeeCount", RuleOperator.GreaterThanOrEqual, "5",
                    RuleDimension.Employment, RuleSeverity.Major, "Asgari 5 genç çalışan.")
            ],
            extractionConfidence: 0.8m);

        var kriter = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(gencYasSiniri: 29, genc: 12), cagri, Now),
            CriterionCatalog.YoungEmployees);

        Assert.Equal(CriterionOutcome.Met, kriter.Outcome);
    }

    [Fact(DisplayName = "K15. Eksik zorunlu belge NotMet üretir; 'belgemiz yok' geçerli bir cevaptır")]
    public void Eksik_zorunlu_belge_notmet_uretir()
    {
        var cagri = Cagri();
        cagri.ReplaceDocumentChecklist(
        [
            new DocumentRequirement("ISO9001", "ISO 9001 Kalite Belgesi", isMandatory: true),
            new DocumentRequirement("VERGI_BORCU_YOK", "Vergi borcu yoktur yazısı", isMandatory: true)
        ]);

        var kriter = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now),
            CriterionCatalog.MandatoryDocuments);

        Assert.Equal(CriterionOutcome.NotMet, kriter.Outcome);
        Assert.Contains("ISO 9001", kriter.Rationale);
    }

    [Fact(DisplayName = "K16. Belgedeki hibe ve kredi ayrı ayrı anlatılır; kredi hibe sayılmaz")]
    public void Hibe_ve_kredi_ayri_anlatilir()
    {
        var cagri = Cagri();
        cagri.ReplaceBudgetItems(
        [
            new BudgetItem(BudgetItemType.GrantCeiling, 1_500_000m, "TRY",
                "geri ödemesiz destek üst limiti 1.500.000 TL", 10, 55),
            new BudgetItem(BudgetItemType.CreditCeiling, 20_000_000m, "TRY",
                "İşletme Başına Kredi Üst Limiti: 20.000.000 TL", 56, 101)
        ], []);

        var kriter = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now),
            CriterionCatalog.SupportType);

        Assert.Equal(CriterionOutcome.Met, kriter.Outcome);
        Assert.Contains("hibe (geri ödemesiz)", kriter.Rationale);
        Assert.Contains("kredi (geri ödemeli borç)", kriter.Rationale);
        Assert.Equal(2, kriter.Evidence.Count);
    }

    [Fact(DisplayName = "K17. Türü belirlenemeyen tek tutar destek türünü Unknown bırakır")]
    public void Belirsiz_tutar_destek_turunu_unknown_birakir()
    {
        var cagri = Cagri();
        cagri.ReplaceBudgetItems(
        [
            new BudgetItem(BudgetItemType.Undetermined, 1_000_000m, "TRY",
                "Makine-Teçhizat Desteği üst limiti 1.000.000 TL", 10, 55)
        ], []);

        var kriter = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now),
            CriterionCatalog.SupportType);

        Assert.Equal(CriterionOutcome.Unknown, kriter.Outcome);
        Assert.DoesNotContain("hibe (geri ödemesiz)", kriter.Rationale);
    }

    [Fact(DisplayName = "K18. Her kriter kural seti sürümünü taşır")]
    public void Her_kriter_kural_seti_surumunu_tasir()
    {
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), Cagri(), Now);

        Assert.All(analiz.Criteria, k => Assert.Equal(AnalysisRuleSet.Current.Version, k.RuleSetVersion));
    }

    [Fact(DisplayName = "K19. Kural kaynak metni kriterin kanıtına bağlanır")]
    public void Kaynak_metni_kanita_baglanir()
    {
        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
                RuleDimension.Employment, RuleSeverity.Major, "Asgari 10 çalışan.",
                sourceExcerpt: "Başvuru sahibinin en az 10 çalışanı olmalıdır.")],
            extractionConfidence: 0.8m);

        var kriter = Kriter(OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now),
            CriterionCatalog.EmployeeCount);

        Assert.Single(kriter.Evidence);
        Assert.Contains("en az 10 çalışanı", kriter.Evidence[0].Excerpt);
    }

    [Fact(DisplayName = "K20. Aynı girdi aynı sonucu üretir (deterministik)")]
    public void Ayni_girdi_ayni_sonuc()
    {
        var firma = Firma();
        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Company.NaceCodes", RuleOperator.NaceMatch, "25",
                RuleDimension.Sector, RuleSeverity.Major, "İmalat sektörü.")],
            extractionConfidence: 0.8m);

        var ilk = OpportunityCriteriaEvaluator.Evaluate(firma, cagri, Now);
        var ikinci = OpportunityCriteriaEvaluator.Evaluate(firma, cagri, Now);

        Assert.Equal(ilk.Score.Value, ikinci.Score.Value);
        Assert.Equal(ilk.Confidence.Value, ikinci.Confidence.Value);
        Assert.Equal(
            ilk.Criteria.Select(c => (c.Code, c.Outcome)),
            ikinci.Criteria.Select(c => (c.Code, c.Outcome)));
    }

    [Fact(DisplayName = "K21. Kriter gerekçesi boş bırakılamaz")]
    public void Gerekce_bos_birakilamaz()
    {
        var hata = Assert.Throws<DomainException>(() => new CriterionResult
        {
            Code = "TEST",
            Name = "Test",
            IsMandatory = false,
            Outcome = CriterionOutcome.Met,
            Rationale = "   ",
            ScoreImpact = 1m,
            RuleSetVersion = "test",
            Group = ScoreGroup.Mandatory
        });

        Assert.Contains("gerekçesi", hata.Message);
    }
}
