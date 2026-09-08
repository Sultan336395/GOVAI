using GovAI.Domain.Analysis;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Tests;

/// <summary>
/// Açıklanabilir puan ve güven seviyesinin davranış sözleşmesi (Faz 3 — Aşama 1).
///
/// İki değer <b>ayrı</b> hesaplanır ve ayrı anlamlar taşır:
/// puan "firma koşulları ne kadar karşılıyor", güven "sistem ne kadar biliyor".
/// İkisini tek sayıya indirmek, verisi eksik bir firmayı uygun olmayan firmayla aynı
/// yere koyar; kullanıcı ilkinde veri tamamlar, ikincisinde başvurudan vazgeçer.
///
/// Ağırlıklar sürümlü kural setindedir; koda dağılırsa "skorum neden değişti" sorusu
/// cevaplanamaz hâle gelir.
/// </summary>
public class ExplainableScoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static Company Firma()
    {
        var company = new Company(Guid.CreateVersion7(), "Örnek Üretim A.Ş.", "1112223334", LegalType.JointStockCompany);
        company.UpdateIdentity("Örnek Üretim A.Ş.", LegalType.JointStockCompany, new DateOnly(2015, 1, 1));
        company.UpdateWorkforce(new Workforce(42, 14, 10, 7, 2, youngEmployeeMaxAge: 29));
        company.UpdateFinancials(new Financials(50_000_000m, 30_000_000m, 12_000_000m, 8_000_000m, "TRY", 2025));
        company.ReplaceNaceCodes([new CompanyNaceCode("2562", isPrimary: true)]);
        company.ReplaceLocations([new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: true)]);

        return company;
    }

    private static Opportunity Cagri()
    {
        var opportunity = new Opportunity(
            Guid.CreateVersion7(), SourceType.KosgebOrSimilar, SupportCategory.Grant,
            "Test Çağrısı", "KOSGEB", Now.AddDays(-3));

        opportunity.SetSchedule(Now.AddDays(-3), Now.AddDays(20));

        return opportunity;
    }

    [Fact(DisplayName = "P1. Kırılım ağırlıklarının toplamı 1.0'dır")]
    public void Agirliklarin_toplami_bir()
    {
        var toplam = AnalysisRuleSet.Current.GroupWeights.Values.Sum();

        Assert.Equal(1.0m, toplam);
    }

    [Fact(DisplayName = "P2. Güven bileşeni ağırlıklarının toplamı 1.0'dır")]
    public void Guven_agirliklarinin_toplami_bir()
    {
        var toplam = AnalysisRuleSet.Current.ConfidenceWeights.Values.Sum();

        Assert.Equal(1.0m, toplam);
    }

    [Fact(DisplayName = "P3. Toplamı 1.0 olmayan ağırlık seti reddedilir")]
    public void Bozuk_agirlik_seti_reddedilir()
    {
        var hata = Assert.Throws<DomainException>(() => AnalysisRuleSet.Current with
        {
            GroupWeights = new Dictionary<ScoreGroup, decimal> { [ScoreGroup.Mandatory] = 0.5m }
        });

        Assert.Contains("1.0", hata.Message);
    }

    [Fact(DisplayName = "P4. Puan 0–100 aralığındadır ve kırılım her başlığı içerir")]
    public void Puan_araliginda_ve_kirilimli()
    {
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), Cagri(), Now);

        Assert.InRange(analiz.Score.Value, 0m, 100m);
        Assert.Equal(Enum.GetValues<ScoreGroup>().Length, analiz.Score.Components.Count);
        Assert.All(analiz.Score.Components, c => Assert.False(string.IsNullOrWhiteSpace(c.Rationale)));
    }

    [Fact(DisplayName = "P5. Puan hiçbir yerde 'kazanma ihtimali' olarak adlandırılmaz")]
    public void Puan_kazanma_ihtimali_diye_adlandirilmaz()
    {
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), Cagri(), Now);

        var metinler = analiz.Score.Components.Select(c => c.Rationale)
            .Concat(analiz.Criteria.Select(c => c.Rationale))
            .Concat(analiz.Confidence.Factors.Select(f => f.Explanation));

        Assert.All(metinler, metin =>
        {
            Assert.DoesNotContain("kazanma ihtimali", metin, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("kazanma olasılığı", metin, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact(DisplayName = "P6. Zorunlu kriter başarısızlığı puanı sıfırlar")]
    public void Zorunlu_basarisizlik_puani_sifirlar()
    {
        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Company.LegalType", RuleOperator.Equals, "Cooperative",
                RuleDimension.Financial, RuleSeverity.Blocking, "Yalnızca kooperatifler.")],
            extractionConfidence: 0.9m);

        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now);

        Assert.Equal(0m, analiz.Score.Value);
        Assert.True(analiz.Score.HasMandatoryFailure);
    }

    [Fact(DisplayName = "P7. Sektör kuralı olmayan çağrı sektör başlığından tam puan ALAMAZ")]
    public void Kuralsiz_sektor_tam_puan_almaz()
    {
        // CLAUDE.md §2.2.1: kural yokluğu "her sektör uygun" demek değildir.
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), Cagri(), Now);

        var sektor = analiz.Score.Components.Single(c => c.Group == ScoreGroup.SectorNace);

        Assert.Equal(0.5m, sektor.Value);
    }

    [Fact(DisplayName = "P8. Eksik veri puanı düşürür ama 'eksik veri etkisi' olarak raporlanır")]
    public void Eksik_veri_etkisi_raporlanir()
    {
        var eksikFirma = new Company(Guid.CreateVersion7(), "Yeni Ltd. Şti.", "9998887776", LegalType.LimitedCompany);

        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
                RuleDimension.Employment, RuleSeverity.Major, "Asgari 10 çalışan.")],
            extractionConfidence: 0.8m);

        var analiz = OpportunityCriteriaEvaluator.Evaluate(eksikFirma, cagri, Now);

        Assert.True(analiz.Score.MissingDataEffect > 0m,
            "Eksik veri nedeniyle kaybedilen puan raporlanmalı.");
        Assert.True(analiz.Score.Value + analiz.Score.MissingDataEffect <= 100m);
    }

    [Fact(DisplayName = "P9. Eksik verisi olmayan analizde eksik veri etkisi sıfırdır")]
    public void Eksiksiz_analizde_etki_sifir()
    {
        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
                RuleDimension.Employment, RuleSeverity.Major, "Asgari 10 çalışan.")],
            extractionConfidence: 0.8m);

        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now);

        Assert.Empty(analiz.Missing);
        Assert.Equal(0m, analiz.Score.MissingDataEffect);
    }

    [Fact(DisplayName = "P10. Güven seviyesi hem sayısal hem Türkçe kademe üretir")]
    public void Guven_sayisal_ve_kademeli()
    {
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), Cagri(), Now);

        Assert.InRange(analiz.Confidence.Value, 0m, 1m);
        Assert.Contains(analiz.Confidence.LevelLabel, new[] { "Yüksek", "Orta", "Düşük" });
    }

    [Fact(DisplayName = "P11. Ölçülemeyen güven bileşeni ağırlığıyla birlikte dışarıda kalır")]
    public void Olculemeyen_bilesen_disarida_kalir()
    {
        // Yapay zekâ bağlı değilken bileşen sıfır sayılsaydı güven haksız yere düşerdi.
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), Cagri(), Now);

        var yapayZeka = analiz.Confidence.Factors
            .Single(f => f.Code == ConfidenceFactors.AiEvidenceAgreement);

        Assert.True(yapayZeka.NotMeasured);

        var olculen = analiz.Confidence.Factors.Where(f => !f.NotMeasured).ToList();
        var beklenen = Math.Round(
            olculen.Sum(f => f.Value * f.Weight) / olculen.Sum(f => f.Weight), 4);

        Assert.Equal(beklenen, analiz.Confidence.Value);
    }

    [Fact(DisplayName = "P12. Eksik profil güveni düşürür, puanı sıfırlamaz")]
    public void Eksik_profil_guveni_dusurur()
    {
        var eksikFirma = new Company(Guid.CreateVersion7(), "Yeni Ltd. Şti.", "9998887776", LegalType.LimitedCompany);

        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
                RuleDimension.Employment, RuleSeverity.Major, "Asgari 10 çalışan.")],
            extractionConfidence: 0.8m);

        var dolu = OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now);
        var eksik = OpportunityCriteriaEvaluator.Evaluate(eksikFirma, cagri, Now);

        Assert.True(eksik.Confidence.Value < dolu.Confidence.Value);
        Assert.True(eksik.Score.Value > 0m, "Eksik profil firmayı elemez.");
    }

    [Fact(DisplayName = "P13. Danışman onaylı çağrıda ayrıştırma kalitesi tam puandır")]
    public void Danisman_onayi_ayristirma_kalitesini_yukseltir()
    {
        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
                RuleDimension.Employment, RuleSeverity.Major, "Asgari 10 çalışan.")],
            extractionConfidence: 0.4m);

        var oncesi = OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now).Confidence;
        cagri.MarkReviewed();
        var sonrasi = OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now).Confidence;

        var oncekiKalite = oncesi.Factors.Single(f => f.Code == ConfidenceFactors.ParseQuality);
        var sonrakiKalite = sonrasi.Factors.Single(f => f.Code == ConfidenceFactors.ParseQuality);

        Assert.Equal(0.4m, oncekiKalite.Value);
        Assert.Equal(1m, sonrakiKalite.Value);
        Assert.True(sonrasi.Value > oncesi.Value);
    }

    [Fact(DisplayName = "P14. Mali veri girilmemişse güven düşer, puan cezalandırılmaz")]
    public void Mali_veri_yoksa_guven_duser()
    {
        var maliVerisiz = new Company(Guid.CreateVersion7(), "Yeni Ltd. Şti.", "9998887776", LegalType.LimitedCompany);
        maliVerisiz.UpdateWorkforce(new Workforce(20, 8, 4, 2, 1, youngEmployeeMaxAge: 29));

        var analiz = OpportunityCriteriaEvaluator.Evaluate(maliVerisiz, Cagri(), Now);

        var mali = analiz.Confidence.Factors.Single(f => f.Code == ConfidenceFactors.FinancialFreshness);

        Assert.Equal(0m, mali.Value);
        Assert.False(mali.NotMeasured);
        Assert.Contains("mali verisi girilmemiş", mali.Explanation);
    }

    [Fact(DisplayName = "P15. Puan kırılımının katkıları toplamı nihai puanı verir")]
    public void Kirilim_katkilari_puani_verir()
    {
        var cagri = Cagri();
        cagri.ReplaceRules(
            [new OpportunityRule("Company.NaceCodes", RuleOperator.NaceMatch, "25",
                RuleDimension.Sector, RuleSeverity.Major, "İmalat sektörü.")],
            extractionConfidence: 0.8m);

        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), cagri, Now);

        var toplam = Math.Round(analiz.Score.Components.Sum(c => c.Contribution) * 100m, 2);

        Assert.Equal(toplam, analiz.Score.Value);
    }

    [Fact(DisplayName = "P16. Kural seti sürümü puanla birlikte kaydedilir")]
    public void Kural_seti_surumu_kaydedilir()
    {
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), Cagri(), Now);

        Assert.Equal(AnalysisRuleSet.Current.Version, analiz.Score.RuleSetVersion);
        Assert.Equal(AnalysisRuleSet.Current.Version, analiz.Confidence.RuleSetVersion);
        Assert.Matches(@"^\d{4}\.\d{2}\.\d+$", analiz.Score.RuleSetVersion);
    }
}
