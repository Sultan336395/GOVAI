using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Tests;

/// <summary>
/// Genç ve engelli çalışan bilgisi (Faz 3).
///
/// <para>
/// İki alan motorda vardı ama panelden girilemiyordu; kayıt her zaman 0 gidiyordu ve
/// "en az 5 genç çalışan" arayan teşvikler hiçbir firmada sağlanamıyordu.
/// </para>
///
/// <para>
/// Alanlar açılırken asıl mesele sayı değil <b>tanım</b>: teşvik programları 25, 29 ve
/// 30 yaş sınırlarını birlikte kullanır. Firma hangi sınıra göre saydığını beyan etmeden
/// sayı anlamsızdır ve "uygun" sayılamaz.
/// </para>
/// </summary>
public class GencCalisanTanimiTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static Company Firma(Workforce workforce)
    {
        var company = new Company(Guid.CreateVersion7(), "Test A.Ş.", "1112223334", LegalType.JointStockCompany);
        company.UpdateIdentity("Test A.Ş.", LegalType.JointStockCompany, new DateOnly(2015, 1, 1));
        company.UpdateWorkforce(workforce);
        company.ReplaceNaceCodes([new CompanyNaceCode("2841", isPrimary: true)]);

        return company;
    }

    private static Opportunity GencKosulluCagri(string field = "Workforce.YoungEmployeeCount", string deger = "5")
    {
        var opportunity = new Opportunity(
            Guid.CreateVersion7(), SourceType.Ministry, SupportCategory.EmploymentIncentive,
            "Genç İstihdamı Desteği", "Test Kurumu", Now.AddDays(-5));

        opportunity.SetSchedule(Now.AddDays(-5), Now.AddDays(30));
        opportunity.ReplaceRules(
        [
            new OpportunityRule(field, RuleOperator.GreaterThanOrEqual, deger,
                RuleDimension.Employment, RuleSeverity.Major, $"Asgari {deger}.")
        ], extractionConfidence: 0.9m);

        return opportunity;
    }

    // ═══════════ Sayı doğrulaması ═══════════

    [Fact(DisplayName = "G1. Genç ve engelli çalışan sayısı negatif olamaz")]
    public void Negatif_sayi_reddedilir()
    {
        Assert.Throws<DomainException>(() => new Workforce(10, 0, -1, 0, 0));
        Assert.Throws<DomainException>(() => new Workforce(10, 0, 0, 0, -1));
    }

    [Fact(DisplayName = "G2. Genç ve engelli çalışan sayısı toplam çalışanı aşamaz")]
    public void Toplami_asan_sayi_reddedilir()
    {
        Assert.Throws<DomainException>(() => new Workforce(10, 0, 11, 0, 0));
        Assert.Throws<DomainException>(() => new Workforce(10, 0, 0, 0, 11));
    }

    [Fact(DisplayName = "G3. Yaş sınırı makul aralıkta olmalıdır")]
    public void Makul_olmayan_yas_reddedilir()
    {
        Assert.Throws<DomainException>(() => new Workforce(10, 0, 5, 0, 0, youngEmployeeMaxAge: 12));
        Assert.Throws<DomainException>(() => new Workforce(10, 0, 5, 0, 0, youngEmployeeMaxAge: 90));
    }

    [Fact(DisplayName = "G4. Sıfır ve geçerli değerler kabul edilir")]
    public void Gecerli_degerler_kabul_edilir()
    {
        var workforce = new Workforce(42, 14, 9, 7, 1, youngEmployeeMaxAge: 29);

        Assert.Equal(9, workforce.YoungEmployeeCount);
        Assert.Equal(1, workforce.DisabledEmployeeCount);
        Assert.Equal(29, workforce.YoungEmployeeMaxAge);
    }

    [Fact(DisplayName = "G5. Yaş sınırı boş bırakılabilir")]
    public void Yas_sinirsiz_birakilabilir()
    {
        Assert.Null(new Workforce(42, 14, 0, 7, 0).YoungEmployeeMaxAge);
    }

    // ═══════════ Tanım karşılaştırması ═══════════

    [Theory(DisplayName = "G6. Firmanın tanımı çağrınınkinden dar veya eşitse koşul sağlanabilir")]
    [InlineData(25, 29, true)]
    [InlineData(29, 29, true)]
    [InlineData(30, 29, false)]
    public void Tanim_karsilastirmasi(int firmaninTanimi, int cagrininTanimi, bool beklenen)
    {
        // 29 altını sayan firma, 25 altı arayan çağrının koşulunu SAĞLAMIŞ SAYILAMAZ:
        // 29 altındaki grup 25 altındakini kapsar ama sayı olduğundan büyüktür.
        var workforce = new Workforce(42, 0, 9, 0, 0, youngEmployeeMaxAge: firmaninTanimi);

        Assert.Equal(beklenen, workforce.YoungDefinitionSatisfies(cagrininTanimi));
    }

    [Fact(DisplayName = "G7. Tanım beyan edilmediyse cevap 'bilinmiyor'dur, 'hayır' değil")]
    public void Tanim_yoksa_bilinmiyor()
    {
        Assert.Null(new Workforce(42, 0, 9, 0, 0).YoungDefinitionSatisfies(29));
    }

    // ═══════════ Motor davranışı ═══════════

    [Fact(DisplayName = "G8. Yaş tanımı yoksa genç koşulu DOĞRULANAMADI olur, uygun sayılmaz")]
    public void Tanim_yoksa_kosul_dogrulanamaz()
    {
        // Sahadaki risk buydu: 30 yaş altını sayan bir firma, 25 yaş altı arayan çağrıda
        // sessizce "uygun" görünürdü.
        var firma = Firma(new Workforce(42, 0, 9, 0, 0));

        var outcome = EligibilityEngine.Evaluate(firma, GencKosulluCagri(), Now);

        var kural = outcome.RuleEvaluations.Single(r => r.Field == "Workforce.YoungEmployeeCount");

        Assert.Equal(RuleOutcome.Unknown, kural.Outcome);
        Assert.True(kural.NeedsData);
    }

    [Fact(DisplayName = "G9. Yaş tanımı beyan edildiyse genç koşulu değerlendirilir")]
    public void Tanim_varsa_kosul_degerlendirilir()
    {
        var firma = Firma(new Workforce(42, 0, 9, 0, 0, youngEmployeeMaxAge: 29));

        var outcome = EligibilityEngine.Evaluate(firma, GencKosulluCagri(), Now);

        var kural = outcome.RuleEvaluations.Single(r => r.Field == "Workforce.YoungEmployeeCount");

        Assert.Equal(RuleOutcome.Satisfied, kural.Outcome);
    }

    [Fact(DisplayName = "G10. Çağrı yaş sınırını kural olarak verirse karşılaştırılır")]
    public void Cagri_yas_sinirini_kural_olarak_verebilir()
    {
        var firma = Firma(new Workforce(42, 0, 9, 0, 0, youngEmployeeMaxAge: 30));

        // Çağrı "genç = 25 yaş altı" diyor; firma 30 altını sayıyor.
        var cagri = GencKosulluCagri("Workforce.YoungEmployeeMaxAge", "25");
        var outcome = EligibilityEngine.Evaluate(firma, cagri, Now);

        var kural = outcome.RuleEvaluations.Single(r => r.Field == "Workforce.YoungEmployeeMaxAge");

        // GreaterThanOrEqual 25 → 30 >= 25 sağlanır; asıl koruma sayının kendisinde
        // (G8) ve tanım karşılaştırmasındadır (G6). Burada alanın çözülebildiği sabitlenir.
        Assert.NotEqual(RuleOutcome.Unknown, kural.Outcome);
    }

    [Fact(DisplayName = "G11. Tanım beyan edilmemişse yaş alanı da bilinmiyordur")]
    public void Tanim_yoksa_yas_alani_bilinmiyor()
    {
        var firma = Firma(new Workforce(42, 0, 9, 0, 0));

        var outcome = EligibilityEngine.Evaluate(
            firma, GencKosulluCagri("Workforce.YoungEmployeeMaxAge", "29"), Now);

        Assert.Equal(
            RuleOutcome.Unknown,
            outcome.RuleEvaluations.Single(r => r.Field == "Workforce.YoungEmployeeMaxAge").Outcome);
    }

    [Fact(DisplayName = "G12. Engelli çalışan sayısı motora ulaşır")]
    public void Engelli_sayisi_motora_ulasir()
    {
        var firma = Firma(new Workforce(42, 0, 0, 0, 3));

        var outcome = EligibilityEngine.Evaluate(
            firma, GencKosulluCagri("Workforce.DisabledEmployeeCount", "3"), Now);

        Assert.Equal(
            RuleOutcome.Satisfied,
            outcome.RuleEvaluations.Single(r => r.Field == "Workforce.DisabledEmployeeCount").Outcome);
    }

    [Fact(DisplayName = "G13. Eksik veri firmayı ELEMEZ")]
    public void Eksik_veri_firmayi_elemez()
    {
        // CLAUDE.md §2.2: "bilmiyorum" ile "hayır" ayrı şeylerdir.
        var firma = Firma(new Workforce(42, 0, 0, 0, 0));

        var outcome = EligibilityEngine.Evaluate(firma, GencKosulluCagri(), Now);

        Assert.NotEqual(EligibilityVerdict.NotEligible, outcome.Verdict);
        Assert.False(outcome.Score.HasBlockingFailure);
    }
}
