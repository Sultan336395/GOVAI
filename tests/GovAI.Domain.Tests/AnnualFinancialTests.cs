using GovAI.Domain.Analysis;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Tests;

/// <summary>
/// Yıl bazlı toplu mali verinin davranış sözleşmesi (Faz 3 — Aşama 1).
///
/// Ciro eşiği arayan çağrılarda "hangi yılın cirosu" sorusunun cevabı gerekiyordu;
/// tek dönemlik <see cref="Financials"/> bunu veremiyor. Yeni kayıt eskisinin
/// <b>yerine geçmez</b>, yanına eklenir: Faz 3 öncesi girilmiş profiller bozulmaz.
///
/// İki davranış kritiktir:
/// 1. <c>null</c> "girilmedi" demektir, sıfır değil — cirosu girilmemiş firma
///    "cirosu 0" diye elenmez.
/// 2. Kayıtta kişisel veri yoktur ve olmayacaktır.
/// </summary>
public class AnnualFinancialTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static Company Firma()
    {
        var company = new Company(Guid.CreateVersion7(), "Örnek Üretim A.Ş.", "1112223334", LegalType.JointStockCompany);
        company.UpdateIdentity("Örnek Üretim A.Ş.", LegalType.JointStockCompany, new DateOnly(2015, 1, 1));
        company.UpdateWorkforce(new Workforce(42, 14, 10, 7, 2, youngEmployeeMaxAge: 29));
        company.ReplaceNaceCodes([new CompanyNaceCode("2562", isPrimary: true)]);
        company.ReplaceLocations([new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: true)]);

        return company;
    }

    private static Opportunity CiroCagrisi(string esik)
    {
        var opportunity = new Opportunity(
            Guid.CreateVersion7(), SourceType.KosgebOrSimilar, SupportCategory.Grant,
            "Ciro Eşikli Çağrı", "KOSGEB", Now.AddDays(-3));

        opportunity.SetSchedule(Now.AddDays(-3), Now.AddDays(20));
        opportunity.ReplaceRules(
            [new OpportunityRule("Financials.AnnualRevenue", RuleOperator.GreaterThanOrEqual, esik,
                RuleDimension.Financial, RuleSeverity.Major, $"Asgari {esik} TL ciro.")],
            extractionConfidence: 0.8m);

        return opportunity;
    }

    [Fact(DisplayName = "F1. Aynı mali yıl iki kez eklenmez, güncellenir")]
    public void Ayni_yil_iki_kez_eklenmez()
    {
        var firma = Firma();

        firma.UpsertAnnualFinancials(2025, "TRY", 10_000_000m, null, null, null, null,
            FinancialDataSource.SelfDeclared, FinancialVerificationStatus.Unverified, Now);
        firma.UpsertAnnualFinancials(2025, "TRY", 12_000_000m, null, null, null, null,
            FinancialDataSource.AccountantStatement, FinancialVerificationStatus.Verified, Now);

        Assert.Single(firma.AnnualFinancials);
        Assert.Equal(12_000_000m, firma.AnnualFinancials.Single().AnnualRevenue);
        Assert.Equal(FinancialVerificationStatus.Verified, firma.AnnualFinancials.Single().VerificationStatus);
    }

    [Fact(DisplayName = "F2. Geçmiş yıllar korunur; en yeni yıl kullanılır")]
    public void Gecmis_yillar_korunur()
    {
        var firma = Firma();

        firma.UpsertAnnualFinancials(2023, "TRY", 5_000_000m, null, null, null, null,
            FinancialDataSource.SelfDeclared, FinancialVerificationStatus.Unverified, Now);
        firma.UpsertAnnualFinancials(2025, "TRY", 12_000_000m, null, null, null, null,
            FinancialDataSource.SelfDeclared, FinancialVerificationStatus.Unverified, Now);

        Assert.Equal(2, firma.AnnualFinancials.Count);
        Assert.Equal(2025, firma.LatestAnnualFinancials()!.FiscalYear);
    }

    [Fact(DisplayName = "F3. Mali veri sürümü her güncellemede artar")]
    public void Mali_veri_surumu_artar()
    {
        var firma = Firma();
        var once = firma.FinancialDataVersion;

        firma.UpsertAnnualFinancials(2025, "TRY", 12_000_000m, null, null, null, null,
            FinancialDataSource.SelfDeclared, FinancialVerificationStatus.Unverified, Now);

        Assert.Equal(once + 1, firma.FinancialDataVersion);
    }

    [Fact(DisplayName = "F4. Hiç sayı girilmemiş yıl kaydı 'boş' sayılır ve kullanılmaz")]
    public void Bos_kayit_kullanilmaz()
    {
        var firma = Firma();

        firma.UpsertAnnualFinancials(2025, "TRY", null, null, null, null, null,
            FinancialDataSource.Unspecified, FinancialVerificationStatus.Unverified, Now);

        Assert.True(firma.AnnualFinancials.Single().IsEmpty);
        Assert.Null(firma.LatestAnnualFinancials());
    }

    [Fact(DisplayName = "F5. Mali verisi olmayan firmada ciro kriteri Unknown kalır, NotMet değil")]
    public void Mali_verisiz_firma_elenmez()
    {
        var analiz = OpportunityCriteriaEvaluator.Evaluate(Firma(), CiroCagrisi("1000000"), Now);
        var kriter = analiz.Criteria.Single(c => c.Code == CriterionCatalog.RevenueAndFinancials);

        Assert.Equal(CriterionOutcome.Unknown, kriter.Outcome);
        Assert.True(analiz.Score.Value > 0m);
    }

    [Fact(DisplayName = "F6. Yıllık kayıt eski tek dönemlik alanın önüne geçer")]
    public void Yillik_kayit_onceliklidir()
    {
        var firma = Firma();
        // Eski alan düşük, yeni yıllık kayıt yüksek: eşiği yeni kayıt karşılıyor.
        firma.UpdateFinancials(new Financials(500_000m, 0m, 0m, 0m, "TRY", 2023));
        firma.UpsertAnnualFinancials(2025, "TRY", 12_000_000m, null, null, null, null,
            FinancialDataSource.AccountantStatement, FinancialVerificationStatus.Verified, Now);

        var deger = CompanyFieldResolver.Resolve(firma, "Financials.AnnualRevenue", DateOnly.FromDateTime(Now.UtcDateTime));

        Assert.Equal(12_000_000m, deger.Number);
    }

    [Fact(DisplayName = "F7. Yıllık kayıt yoksa eski alan çalışmaya devam eder")]
    public void Yillik_kayit_yoksa_eski_alan_calisir()
    {
        var firma = Firma();
        firma.UpdateFinancials(new Financials(7_500_000m, 0m, 0m, 0m, "TRY", 2024));

        var deger = CompanyFieldResolver.Resolve(firma, "Financials.AnnualRevenue", DateOnly.FromDateTime(Now.UtcDateTime));

        Assert.Equal(7_500_000m, deger.Number);
    }

    [Fact(DisplayName = "F8. Ciro eşiği sağlanıyorsa kriter Met olur")]
    public void Ciro_esigi_saglaniyorsa_met()
    {
        var firma = Firma();
        firma.UpsertAnnualFinancials(2025, "TRY", 12_000_000m, null, null, null, null,
            FinancialDataSource.AccountantStatement, FinancialVerificationStatus.Verified, Now);

        var kriter = OpportunityCriteriaEvaluator.Evaluate(firma, CiroCagrisi("1000000"), Now)
            .Criteria.Single(c => c.Code == CriterionCatalog.RevenueAndFinancials);

        Assert.Equal(CriterionOutcome.Met, kriter.Outcome);
    }

    [Fact(DisplayName = "F9. Ciro eşiği sağlanmıyorsa kriter NotMet olur")]
    public void Ciro_esigi_saglanmiyorsa_notmet()
    {
        var firma = Firma();
        firma.UpsertAnnualFinancials(2025, "TRY", 800_000m, null, null, null, null,
            FinancialDataSource.SelfDeclared, FinancialVerificationStatus.Unverified, Now);

        var kriter = OpportunityCriteriaEvaluator.Evaluate(firma, CiroCagrisi("1000000"), Now)
            .Criteria.Single(c => c.Code == CriterionCatalog.RevenueAndFinancials);

        Assert.Equal(CriterionOutcome.NotMet, kriter.Outcome);
    }

    [Fact(DisplayName = "F10. Net kâr/zarar sıfır girilebilir; sıfır 'girilmedi' değildir")]
    public void Sifir_kar_gecerli_degerdir()
    {
        var firma = Firma();
        firma.UpsertAnnualFinancials(2025, "TRY", 12_000_000m, null, null, 0m, null,
            FinancialDataSource.AccountantStatement, FinancialVerificationStatus.Verified, Now);

        var deger = CompanyFieldResolver.Resolve(firma, "Financials.NetProfitOrLoss", DateOnly.FromDateTime(Now.UtcDateTime));

        Assert.True(deger.IsKnown);
        Assert.Equal(0m, deger.Number);
    }

    [Fact(DisplayName = "F11. Girilmemiş yıllık gelir Unknown döner")]
    public void Girilmemis_gelir_unknown_doner()
    {
        var firma = Firma();
        firma.UpsertAnnualFinancials(2025, "TRY", 12_000_000m, null, null, null, null,
            FinancialDataSource.SelfDeclared, FinancialVerificationStatus.Unverified, Now);

        var deger = CompanyFieldResolver.Resolve(firma, "Financials.AnnualIncome", DateOnly.FromDateTime(Now.UtcDateTime));

        Assert.False(deger.IsKnown);
    }

    [Fact(DisplayName = "F12. Zarar negatif değer olarak saklanabilir")]
    public void Zarar_negatif_saklanir()
    {
        var firma = Firma();
        firma.UpsertAnnualFinancials(2025, "TRY", 12_000_000m, null, null, -2_000_000m, null,
            FinancialDataSource.AccountantStatement, FinancialVerificationStatus.DocumentChecked, Now);

        Assert.Equal(-2_000_000m, firma.LatestAnnualFinancials()!.NetProfitOrLoss);
    }

    [Fact(DisplayName = "F13. Negatif ciro reddedilir")]
    public void Negatif_ciro_reddedilir()
    {
        var firma = Firma();

        Assert.Throws<DomainException>(() => firma.UpsertAnnualFinancials(
            2025, "TRY", -1m, null, null, null, null,
            FinancialDataSource.SelfDeclared, FinancialVerificationStatus.Unverified, Now));
    }

    [Fact(DisplayName = "F14. Geçersiz mali yıl reddedilir")]
    public void Gecersiz_yil_reddedilir()
    {
        var firma = Firma();

        Assert.Throws<DomainException>(() => firma.UpsertAnnualFinancials(
            1900, "TRY", 1m, null, null, null, null,
            FinancialDataSource.SelfDeclared, FinancialVerificationStatus.Unverified, Now));
    }

    [Fact(DisplayName = "F15. Bayat mali veri güveni düşürür")]
    public void Bayat_mali_veri_guveni_dusurur()
    {
        var guncel = Firma();
        guncel.UpsertAnnualFinancials(2026, "TRY", 12_000_000m, null, null, null, null,
            FinancialDataSource.AccountantStatement, FinancialVerificationStatus.Verified, Now);

        var bayat = Firma();
        bayat.UpsertAnnualFinancials(2020, "TRY", 12_000_000m, null, null, null, null,
            FinancialDataSource.AccountantStatement, FinancialVerificationStatus.Verified, Now);

        var cagri = CiroCagrisi("1000000");

        var guncelGuven = OpportunityCriteriaEvaluator.Evaluate(guncel, cagri, Now).Confidence.Value;
        var bayatGuven = OpportunityCriteriaEvaluator.Evaluate(bayat, cagri, Now).Confidence.Value;

        Assert.True(bayatGuven < guncelGuven);
    }

    [Fact(DisplayName = "F16. Mali kayıt modelinde kişisel veri alanı yoktur")]
    public void Kisisel_veri_alani_yoktur()
    {
        // Sözleşme testi: modele isim, kimlik veya ücret alanı eklenirse burası kırılır.
        var yasakli = new[]
        {
            "Name", "FullName", "Tckn", "TcKimlik", "IdentityNumber", "Salary", "Wage",
            "Iban", "BankAccount", "BirthDate", "Email", "Phone"
        };

        var alanlar = typeof(AnnualFinancialRecord)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var yasak in yasakli)
        {
            Assert.DoesNotContain(yasak, alanlar);
        }
    }
}
