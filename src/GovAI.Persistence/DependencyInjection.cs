using GovAI.Application.Sources;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<GovAiDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", GovAiDbContext.Schema);

                // Geçici ağ/veritabanı hatalarında yeniden dene; worker yükünde bağlantı dalgalanmaları olağandır.
                npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
            });

            // PostgreSQL tarafında tablo ve kolon adları snake_case tutulur.
            options.UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IOpportunityRepository, OpportunityRepository>();
        services.AddScoped<ISourceRepository, SourceRepository>();
        services.AddScoped<ISourceDocumentRepository, SourceDocumentRepository>();
        services.AddScoped<IQuarantineQueryRepository, QuarantineQueryRepository>();
        services.AddScoped<ITitleRepairQueryRepository, TitleRepairQueryRepository>();
        services.AddScoped<IPlatformActivationRepository, PlatformActivationRepository>();
        services.AddScoped<IRegulatoryChangeRepository, RegulatoryChangeRepository>();
        services.AddScoped<IAssessmentRepository, AssessmentRepository>();
        services.AddScoped<IScenarioSimulationRepository, ScenarioSimulationRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();

        // Faz 1: çoklu şirket
        services.AddScoped<IUserCompanyRepository, UserCompanyRepository>();
        services.AddScoped<ICompanyGroupRepository, CompanyGroupRepository>();
        services.AddScoped<ICompanyInvitationRepository, CompanyInvitationRepository>();
        services.AddScoped<ICompanyVerificationRequestRepository, CompanyVerificationRequestRepository>();
        services.AddScoped<ICrossTenantCompanyLookup, CrossTenantCompanyLookup>();

        return services;
    }
}
