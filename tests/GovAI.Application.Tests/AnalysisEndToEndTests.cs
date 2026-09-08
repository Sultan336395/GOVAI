using GovAI.Application.Common;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Analysis;
using GovAI.Domain.Analysis;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovAI.Application.Tests;

/// <summary>
/// Analiz yaşam döngüsünün uçtan uca garantileri (Faz 3 — Aşama 3).
///
/// <para>
/// Tek tek servisler doğru çalışsa bile aralarındaki bağlantı kopuksa kullanıcı
/// eskimiş bir sonuca bakar ve düzelttiği verinin işe yaramadığını sanır. Faz 2'de
/// tam olarak bu olmuştu: panelden sektör düzeltiliyor, liste eski sektörle
/// hesaplanmış hâlde kalıyordu.
/// </para>
/// </summary>
public class AnalysisEndToEndTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "U1. Profil değişince analiz güncellikten düşer, kayıt silinmez")]
    public async Task Profil_degisince_analiz_eskir()
    {
        var (service, invalidation, company, opportunity, runs) = Build();

        await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id);
        var ilk = runs.Saved.Single();
        Assert.True(ilk.IsLatest);

        company.UpdateWorkforce(new Workforce(30, 10, 6, 3, 1, youngEmployeeMaxAge: 29));
        await invalidation.InvalidateForCompanyAsync(company.Id);

        Assert.False(ilk.IsLatest);
        // Kayıt duruyor: "üç ay önce neden uygun görünüyordum" sorusu cevaplanabilir.
        Assert.Single(runs.Saved);
    }

    [Fact(DisplayName = "U2. Profil değişince yeniden analiz yeni kayıt üretir, eskisi korunur")]
    public async Task Profil_degisince_yeni_analiz_uretilir()
    {
        var (service, _, company, opportunity, runs) = Build();

        await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id);
        var ilk = runs.Saved.Single();

        company.UpdateWorkforce(new Workforce(30, 10, 6, 3, 1, youngEmployeeMaxAge: 29));
        await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id);

        Assert.Equal(2, runs.Saved.Count);
        Assert.False(ilk.IsLatest);
        Assert.NotEqual(ilk.IdempotencyKey, runs.Saved[1].IdempotencyKey);
    }

    [Fact(DisplayName = "U3. Belge sürümü değişince yeni analiz üretilir, eskisi korunur")]
    public async Task Belge_surumu_degisince_yeni_analiz()
    {
        var changes = new FakeRegulatoryChangeRepository();
        var (service, _, company, _, runs) = Build(changes: changes);

        var (ilkKayit, _) = changes.SeedWithEvidence("İşverenler bildirmek zorundadır.", Now);
        await service.AnalyzeRegulationAsync(company.Id, ilkKayit.Id);
        var ilkAnaliz = runs.Saved.Single();

        // Aynı mevzuat, yeni belge sürümü: kanıt kimlikleri ve sürüm değişti.
        var (yeniKayit, _) = changes.SeedWithEvidence(
            "İşverenler bildirmek zorundadır. Geçiş süresi tanınmıştır.", Now);
        await service.AnalyzeRegulationAsync(company.Id, yeniKayit.Id);

        Assert.Equal(2, runs.Saved.Count);
        Assert.NotEqual(ilkAnaliz.IdempotencyKey, runs.Saved[1].IdempotencyKey);
        // Eski analiz silinmedi.
        Assert.Contains(runs.Saved, r => r.Id == ilkAnaliz.Id);
    }

    [Fact(DisplayName = "U4. Fırsat belgesi değişince o fırsatın analizleri eskir")]
    public async Task Firsat_degisince_analizler_eskir()
    {
        var (service, invalidation, company, opportunity, runs) = Build();

        await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id);
        var ilk = runs.Saved.Single();

        await invalidation.InvalidateForTargetAsync(opportunity.Id);

        Assert.False(ilk.IsLatest);
        Assert.Single(runs.Saved);
    }

    [Fact(DisplayName = "U5. Aynı mesaj ikinci sonuç üretmez")]
    public async Task Ayni_mesaj_ikinci_sonuc_uretmez()
    {
        var (service, _, company, opportunity, runs) = Build();

        await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id, "mesaj-1");
        await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id, "mesaj-1");
        await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id, "mesaj-1");

        Assert.Single(runs.Saved);
    }

    [Fact(DisplayName = "U6. Süresi geçmiş fırsat aktif eşleşme sayılmaz")]
    public async Task Suresi_gecmis_firsat_aktif_degildir()
    {
        var (service, _, company, opportunity, _) = Build(sonBasvuruGun: -10);

        var sonuc = await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id);

        Assert.Equal(EligibilityVerdict.NotEligible, sonuc.Verdict);
        Assert.Equal(0m, sonuc.Score.Value);

        var basvuruDonemi = sonuc.Criteria.Single(c => c.Code == CriterionCatalog.ApplicationWindow);
        Assert.Equal(CriterionOutcome.NotMet, basvuruDonemi.Outcome);
        Assert.True(basvuruDonemi.IsMandatory);
    }

    [Fact(DisplayName = "U7. Karantinadaki fırsat analiz edilmez")]
    public async Task Karantinadaki_firsat_analiz_edilmez()
    {
        var (service, _, company, opportunity, runs) = Build();
        opportunity.Quarantine(QuarantineReason.InvalidSourcePage, "Kaynak sayfa geçersiz.");

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.AnalyzeOpportunityAsync(company.Id, opportunity.Id));

        Assert.Empty(runs.Saved);
    }

    [Fact(DisplayName = "U8. Model kullanılamazsa kural tabanlı sonuç ve açık uyarı gelir")]
    public async Task Model_yoksa_uyarili_kural_sonucu()
    {
        var (service, _, company, opportunity, runs) =
            Build(provider: new FakeAnalysisAiProvider(configured: false));

        var sonuc = await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id);

        Assert.NotEmpty(sonuc.Criteria);
        Assert.False(sonuc.HasAiContribution);
        Assert.Contains("yalnızca resmî belgedeki kurallara göre", sonuc.Contribution.Warning);
        Assert.Equal(AnalysisRunStatus.CompletedWithoutAi, runs.Saved.Single().Status);
    }

    [Fact(DisplayName = "U9. Analiz sonucu sürüm künyesiyle birlikte döner")]
    public async Task Sonuc_surum_kunyesiyle_doner()
    {
        var (service, _, company, opportunity, _) = Build();

        var sonuc = await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id, "korelasyon-9");

        Assert.NotEqual(Guid.Empty, sonuc.Version.AnalysisRunId);
        Assert.Equal(company.ProfileVersion, sonuc.Version.CompanyProfileVersion);
        Assert.Equal(company.FinancialDataVersion, sonuc.Version.FinancialDataVersion);
        Assert.Equal(AnalysisRuleSet.Current.Version, sonuc.Version.RuleSetVersion);
        Assert.Equal("korelasyon-9", sonuc.Version.CorrelationId);
        Assert.NotNull(sonuc.Version.CompletedAt);
    }

    private static (HybridAnalysisService Service, AnalysisInvalidationService Invalidation,
        Company Company, Opportunity Opportunity, FakeAnalysisRunRepository Runs) Build(
        IAnalysisAiProvider? provider = null,
        FakeRegulatoryChangeRepository? changes = null,
        int sonBasvuruGun = 50)
    {
        var currentUser = new FakeCurrentUser();
        var tenantId = currentUser.TenantId!.Value;

        var company = new Company(tenantId, "Uçtan Uca A.Ş.", "5556667778", LegalType.LimitedCompany);
        company.UpdateIdentity("Uçtan Uca A.Ş.", LegalType.LimitedCompany, new DateOnly(2018, 6, 1));
        company.UpdateWorkforce(new Workforce(20, 7, 5, 3, 1, youngEmployeeMaxAge: 29));
        company.UpdateSectors("İmalat", null, null);
        company.ReplaceNaceCodes([new CompanyNaceCode("2562", isPrimary: true)]);
        company.ReplaceLocations([new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: true)]);

        var yayin = Now.AddDays(Math.Min(-5, sonBasvuruGun - 60));

        var opportunity = new Opportunity(
            Guid.CreateVersion7(), SourceType.DevelopmentAgency, SupportCategory.Grant,
            "Uçtan Uca Programı", "Test Ajansı", yayin);

        opportunity.SetSchedule(yayin, Now.AddDays(sonBasvuruGun));
        opportunity.ReplaceRules(
        [
            new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
                RuleDimension.Employment, RuleSeverity.Major, "Asgari 10 çalışan.",
                sourceExcerpt: "Başvuru sahibinin en az 10 çalışanı olmalıdır.")
        ], extractionConfidence: 0.9m);

        var companies = new FakeCompanyRepository();
        companies.Seed(company);

        var opportunities = new FakeOpportunityRepository();
        opportunities.Seed(opportunity);

        var memberships = new FakeUserCompanyRepository();
        memberships.Seed(tenantId, currentUser.UserId!.Value, company.Id);

        var runs = new FakeAnalysisRunRepository();
        var access = new CompanyAccessGuard(companies, memberships, currentUser);

        var service = new HybridAnalysisService(
            companies,
            opportunities,
            changes ?? new FakeRegulatoryChangeRepository(),
            runs,
            new FakeUnitOfWork(),
            provider ?? new FakeAnalysisAiProvider(),
            access,
            new FixedClock(Now),
            NullLogger<HybridAnalysisService>.Instance);

        var invalidation = new AnalysisInvalidationService(
            runs, NullLogger<AnalysisInvalidationService>.Instance);

        return (service, invalidation, company, opportunity, runs);
    }
}
