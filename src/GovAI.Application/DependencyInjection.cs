using GovAI.Application.Common;
using GovAI.Application.Companies;
using GovAI.Application.Eligibility;
using GovAI.Application.Identity;
using GovAI.Application.Notifications;
using GovAI.Application.Opportunities;
using GovAI.Application.Reporting;
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
        services.AddScoped<OpportunityService>();
        services.AddScoped<SourceService>();
        services.AddScoped<EligibilityService>();
        services.AddScoped<ScenarioSimulationService>();
        services.AddScoped<ReportingService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<AuthenticationService>();

        return services;
    }
}
