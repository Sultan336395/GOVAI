using GovAI.Application.Common;
using GovAI.Application.Companies;
using GovAI.Application.Simulation;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovAI.Application.Tests;

public class ScenarioSimulationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Personel_artisi_senaryosu_yeni_uygun_firsat_uretir()
    {
        // Firma ISO 9001'e sahip; tek engel çalışan sayısı. Senaryo bunu kapatınca fırsat tam uygun olur.
        var (service, company, _) = BuildService(employeeCount: 6, withIsoCertificate: true);

        var result = await service.RunAsync(
            company.Id,
            new ScenarioRequest { Name = "Personel 12'ye çıkarsa", EmployeeCount = 12 },
            persist: false);

        Assert.Equal(0, result.BaselineEligibleCount);
        Assert.Equal(1, result.SimulatedEligibleCount);
        Assert.Single(result.NewlyEligible);
        Assert.True(result.AverageScoreDelta > 0);
    }

    [Fact]
    public async Task Senaryo_gercek_firma_kaydini_degistirmez()
    {
        var (service, company, _) = BuildService(employeeCount: 6);
        var versionBefore = company.ProfileVersion;

        await service.RunAsync(
            company.Id,
            new ScenarioRequest { Name = "Test", EmployeeCount = 50, AnnualRevenue = 1m },
            persist: false);

        Assert.Equal(6, company.Workforce.EmployeeCount);
        Assert.Equal(versionBefore, company.ProfileVersion);
    }

    [Fact]
    public async Task Sertifika_ekleme_senaryosu_belge_puanini_yukseltir()
    {
        var (service, company, _) = BuildService(employeeCount: 20);

        var result = await service.RunAsync(
            company.Id,
            new ScenarioRequest { Name = "ISO 9001 alınırsa", AddCertificateCodes = ["ISO9001"] },
            persist: false);

        var impact = Assert.Single(result.Impacts);
        Assert.True(impact.Delta > 0, $"Sertifika eklenince skor artmalıydı, delta={impact.Delta}");
    }

    [Fact]
    public async Task Kalici_senaryo_kaydedilir()
    {
        var (service, company, scenarios) = BuildService(employeeCount: 6, withIsoCertificate: true);

        var result = await service.RunAsync(
            company.Id,
            new ScenarioRequest { Name = "Kalıcı senaryo", EmployeeCount = 12 },
            persist: true);

        Assert.NotNull(result.SimulationId);
        var saved = Assert.Single(scenarios.Saved);
        Assert.Equal("Kalıcı senaryo", saved.Name);
        Assert.Equal(1, saved.EligibleCountDelta);
    }

    [Fact]
    public void Profil_dolulugu_eksik_alanlari_yansitir()
    {
        var empty = new Company(Guid.CreateVersion7(), "Boş A.Ş.", "1111111111", LegalType.LimitedCompany);
        Assert.Equal(0m, CompanyProfileService.CalculateCompleteness(empty));

        var filled = BuildCompany(Guid.CreateVersion7(), employeeCount: 10);
        Assert.True(CompanyProfileService.CalculateCompleteness(filled) >= 0.5m);
    }

    private static (ScenarioSimulationService Service, Company Company, FakeScenarioRepository Scenarios) BuildService(
        int employeeCount,
        bool withIsoCertificate = false)
    {
        var currentUser = new FakeCurrentUser();
        var company = BuildCompany(currentUser.TenantId!.Value, employeeCount, withIsoCertificate);

        var companies = new FakeCompanyRepository();
        companies.Seed(company);

        var opportunities = new FakeOpportunityRepository();
        opportunities.Seed(BuildOpportunity());

        var scenarios = new FakeScenarioRepository();

        var service = new ScenarioSimulationService(
            companies,
            opportunities,
            scenarios,
            new FakeUnitOfWork(),
            currentUser,
            new CompanyAccessGuard(companies, currentUser),
            new FixedClock(Now),
            NullLogger<ScenarioSimulationService>.Instance);

        return (service, company, scenarios);
    }

    private static Company BuildCompany(Guid tenantId, int employeeCount, bool withIsoCertificate = false)
    {
        var company = new Company(tenantId, "Senaryo Test A.Ş.", "5556667778", LegalType.LimitedCompany);
        company.UpdateIdentity("Senaryo Test A.Ş.", LegalType.LimitedCompany, new DateOnly(2018, 6, 1));

        company.UpdateWorkforce(new Workforce(employeeCount, employeeCount / 3, employeeCount / 4, employeeCount / 5, 0));
        company.UpdateFinancials(new Financials(40_000_000m, 25_000_000m, 9_000_000m, 3_000_000m, "TRY", 2025));
        company.UpdateFlags(exportFlag: true, technologyFlag: false, previousSuccessfulApplications: 0);

        company.ReplaceNaceCodes([new CompanyNaceCode("2562", isPrimary: true)]);
        company.ReplaceLocations([new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: true)]);
        company.ReplaceInvestments([new CompanyInvestment("Otomasyon", SupportCategory.InvestmentIncentive, 1_000_000m, null, null)]);

        if (withIsoCertificate)
        {
            company.ReplaceCertificates([new CompanyCertificate("ISO9001", "ISO 9001", new DateOnly(2024, 1, 1), new DateOnly(2028, 1, 1))]);
        }

        return company;
    }

    private static Opportunity BuildOpportunity()
    {
        var opportunity = new Opportunity(
            Guid.CreateVersion7(),
            SourceType.DevelopmentAgency,
            SupportCategory.Grant,
            "Senaryo Test Programı",
            "Test Ajansı",
            Now.AddDays(-5));

        opportunity.SetSchedule(Now.AddDays(-5), Now.AddDays(50));

        opportunity.ReplaceRules(
        [
            new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
                RuleDimension.Employment, RuleSeverity.Blocking, "Asgari 10 çalışan."),
            new OpportunityRule("Company.Nuts2Codes", RuleOperator.ContainsAny, "TR62",
                RuleDimension.Region, RuleSeverity.Blocking, "TR62 bölgesi."),
            new OpportunityRule("Company.NaceCodes", RuleOperator.NaceMatch, "25",
                RuleDimension.Sector, RuleSeverity.Blocking, "İmalat sektörü."),
            new OpportunityRule("Company.Certificates", RuleOperator.ContainsAll, "ISO9001",
                RuleDimension.Documentation, RuleSeverity.Minor, "ISO 9001 belgesi puanlamada dikkate alınır.")
        ], extractionConfidence: 0.9m);

        return opportunity;
    }
}
