using System.Text.Json;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Assessments;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Eligibility;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Simulation;

/// <summary>
/// Senaryo ve Simülasyon Modülü (Modül 7).
/// "Personel sayısını 15'e çıkarır, ISO 9001 alırsam fırsat havuzum nasıl değişir?" sorusunu cevaplar.
///
/// Simülasyon firma kaydına dokunmaz: gerçek profilin bellekte değiştirilmiş bir kopyası üzerinde
/// aynı deterministik kural motoru çalıştırılır, sonuçlar karşılaştırılır.
/// </summary>
public sealed class ScenarioSimulationService(
    ICompanyRepository companies,
    IOpportunityRepository opportunities,
    IScenarioSimulationRepository simulations,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    ILogger<ScenarioSimulationService> logger)
{
    public async Task<ScenarioResultDto> RunAsync(
        Guid companyId,
        ScenarioRequest request,
        bool persist = true,
        CancellationToken cancellationToken = default)
    {
        var company = await access.LoadAccessibleAsync(companyId, cancellationToken);

        var now = clock.UtcNow;
        var openOpportunities = await opportunities.ListForEvaluationAsync(now, request.Categories, cancellationToken);

        var simulatedCompany = ApplyChanges(company, request);

        var impacts = new List<ScenarioImpact>();
        var impactDtos = new List<ScenarioImpactDto>();

        foreach (var opportunity in openOpportunities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var baseline = EligibilityEngine.Evaluate(company, opportunity, now);
            var simulated = EligibilityEngine.Evaluate(simulatedCompany, opportunity, now);

            var impact = new ScenarioImpact(
                opportunity.Id,
                opportunity.Title,
                baseline.Score.FinalScore,
                simulated.Score.FinalScore,
                baseline.Verdict,
                simulated.Verdict);

            impacts.Add(impact);
            impactDtos.Add(new ScenarioImpactDto(
                opportunity.Id,
                opportunity.Title,
                opportunity.SupportCategory,
                baseline.Score.FinalScore,
                simulated.Score.FinalScore,
                impact.Delta,
                baseline.Verdict,
                simulated.Verdict,
                impact.BecameEligible));
        }

        var baselineEligible = impactDtos.Count(i => i.BaselineVerdict == EligibilityVerdict.Eligible);
        var simulatedEligible = impactDtos.Count(i => i.SimulatedVerdict == EligibilityVerdict.Eligible);
        var baselineAverage = impactDtos.Count == 0 ? 0m : impactDtos.Average(i => i.BaselineScore);
        var simulatedAverage = impactDtos.Count == 0 ? 0m : impactDtos.Average(i => i.SimulatedScore);

        Guid? simulationId = null;
        if (persist)
        {
            var simulation = new ScenarioSimulation(
                company.TenantId,
                company.Id,
                request.Name,
                JsonSerializer.Serialize(request));

            simulation.RecordResult(baselineEligible, simulatedEligible, baselineAverage, simulatedAverage, impacts);

            await simulations.AddAsync(simulation, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            simulationId = simulation.Id;
        }

        logger.LogInformation(
            "Senaryo çalıştırıldı. CompanyId={CompanyId} Senaryo={Name} Uygun {Before}→{After}",
            companyId, request.Name, baselineEligible, simulatedEligible);

        return new ScenarioResultDto(
            simulationId,
            company.Id,
            request.Name,
            impactDtos.Count,
            baselineEligible,
            simulatedEligible,
            Math.Round(baselineAverage, 2),
            Math.Round(simulatedAverage, 2),
            impactDtos
                .Where(i => i.Delta != 0 || i.BecameEligible)
                .OrderByDescending(i => i.Delta)
                .ToList());
    }

    public async Task<IReadOnlyList<ScenarioSummaryDto>> ListAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAccessAsync(companyId, cancellationToken);

        var items = await simulations.ListForCompanyAsync(companyId, cancellationToken);

        return items
            .Select(s => new ScenarioSummaryDto(
                s.Id,
                s.Name,
                s.BaselineEligibleCount,
                s.SimulatedEligibleCount,
                s.EligibleCountDelta,
                s.ScoreDelta,
                s.CreatedAt))
            .ToList();
    }

    /// <summary>
    /// Gerçek profilin, senaryo değişiklikleri uygulanmış bellek içi kopyasını üretir.
    /// Kalıcı kayda hiçbir şekilde dokunulmaz.
    /// </summary>
    private static Company ApplyChanges(Company company, ScenarioRequest request)
    {
        var clone = new Company(company.TenantId, company.LegalName, company.TaxNumber, company.LegalType);
        clone.UpdateIdentity(company.LegalName, company.LegalType, company.FoundedOn);

        var workforce = company.Workforce;
        clone.UpdateWorkforce(new Workforce(
            request.EmployeeCount ?? workforce.EmployeeCount,
            request.WomenEmployeeCount ?? workforce.WomenEmployeeCount,
            request.YoungEmployeeCount ?? workforce.YoungEmployeeCount,
            request.RAndDEmployeeCount ?? workforce.RAndDEmployeeCount,
            request.DisabledEmployeeCount ?? workforce.DisabledEmployeeCount));

        var financials = company.Financials;
        clone.UpdateFinancials(new Financials(
            request.AnnualRevenue ?? financials.AnnualRevenue,
            request.BalanceSize ?? financials.BalanceSize,
            request.Equity ?? financials.Equity,
            request.ExportRevenue ?? financials.ExportRevenue,
            financials.Currency,
            financials.FiscalYear));

        clone.UpdateFlags(
            request.ExportFlag ?? company.ExportFlag,
            request.TechnologyFlag ?? company.TechnologyFlag,
            company.PreviousSuccessfulApplications);

        clone.ReplaceNaceCodes(company.NaceCodes.Select(n => new CompanyNaceCode(n.Code, n.IsPrimary, n.Description)));

        clone.ReplaceLocations(company.Locations.Select(l =>
            new CompanyLocation(l.City, l.District, l.Nuts2Code, l.IsHeadquarters, l.IsInTechnopark)));

        var removed = request.RemoveCertificateCodes?.Select(c => c.Trim().ToUpperInvariant()).ToHashSet() ?? [];
        var certificates = company.Certificates
            .Where(c => !removed.Contains(c.Code.ToUpperInvariant()))
            .Select(c => new CompanyCertificate(c.Code, c.Name, c.IssuedOn, c.ValidUntil, c.DocumentUri))
            .ToList();

        foreach (var code in request.AddCertificateCodes ?? [])
        {
            var normalized = code.Trim().ToUpperInvariant();
            if (certificates.All(c => c.Code != normalized))
            {
                certificates.Add(new CompanyCertificate(normalized, normalized, null, null));
            }
        }

        clone.ReplaceCertificates(certificates);

        clone.ReplaceInvestments(company.ActiveInvestments.Select(i =>
            new CompanyInvestment(i.Title, i.RelatedCategory, i.PlannedBudget, i.PlannedStart, i.PlannedEnd)));

        return clone;
    }
}
