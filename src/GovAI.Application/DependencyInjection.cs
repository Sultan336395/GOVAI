using GovAI.Application.Common;
using GovAI.Application.Companies;
using GovAI.Application.Dashboard;
using GovAI.Application.Eligibility;
using GovAI.Application.Identity;
using GovAI.Application.Notifications;
using GovAI.Application.Opportunities;
using GovAI.Application.Calibration;
using GovAI.Application.Integrations;
using GovAI.Application.Reporting;
using GovAI.Application.Tenders;
using GovAI.Application.Simulation;
using GovAI.Application.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Application;

/// <summary>
/// Application katmanının servis kayıtları.
/// MediatR yoktur: her use-case düz bir sınıf olarak kaydedilir ve controller doğrudan çağırır.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Şirket erişim kapısı: servislerden önce kaydedilir, hepsi buna bağımlıdır.
        services.AddScoped<CompanyAccessGuard>();

        services.AddScoped<CompanyProfileService>();
        services.AddScoped<CompanyRegistryService>();
        services.AddScoped<CompanyMembershipService>();
        services.AddScoped<OpportunityService>();
        services.AddScoped<SourceService>();
        services.AddScoped<ManualImportService>();
        services.AddScoped<QuarantineService>();
        services.AddScoped<TitleRepairService>();
        services.AddScoped<PlatformActivationService>();
        services.AddScoped<Regulatory.RegulatoryChangeService>();
        services.AddScoped<EligibilityService>();
        services.AddScoped<Analysis.HybridAnalysisService>();
        services.AddScoped<Analysis.AnalysisInvalidationService>();
        services.AddScoped<Sources.CatalogRepairService>();
        services.AddScoped<Opportunities.RuleEvidenceBackfillService>();
        services.AddScoped<ScenarioSimulationService>();
        services.AddScoped<ReportingService>();
        services.AddScoped<WeeklyReportService>();
        services.AddScoped<ReportInquiryService>();
        services.AddScoped<TenderPursuitService>();
        services.AddScoped<NotificationRecipientService>();
        services.AddScoped<ErpTokenService>();
        services.AddScoped<ErpModuleService>();
        services.AddScoped<ErpServiceIdentityService>();
        services.AddScoped<DashboardInsightsService>();
        services.AddScoped<Evidence.EvidenceReliabilityService>();
        services.AddScoped<CalibrationService>();
        services.AddScoped<AiSecondOpinionService>();
        services.AddScoped<ErpConnectionService>();
        services.AddScoped<ErpPullService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<AuthenticationService>();

        return services;
    }
}
