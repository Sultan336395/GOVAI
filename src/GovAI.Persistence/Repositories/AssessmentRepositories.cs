using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Assessments;
using GovAI.Domain.Auditing;
using GovAI.Domain.Identity;
using GovAI.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

public sealed class AssessmentRepository(GovAiDbContext context) : IAssessmentRepository
{
    public Task<EligibilityAssessment?> GetAsync(Guid assessmentId, CancellationToken cancellationToken = default) =>
        context.Assessments
            .Include(a => a.Dimensions)
            .FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken);

    public Task<EligibilityAssessment?> GetLatestAsync(Guid companyId, Guid opportunityId, CancellationToken cancellationToken = default) =>
        context.Assessments
            .Include(a => a.Dimensions)
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.OpportunityId == opportunityId && a.IsLatest, cancellationToken);

    public async Task<PagedResult<EligibilityAssessment>> ListLatestForCompanyAsync(
        AssessmentQuery query,
        CancellationToken cancellationToken = default)
    {
        // Değerlendirme ile fırsatı birleştiriyoruz: kategori ve son tarih filtreleri fırsat tarafında.
        var joined =
            from assessment in context.Assessments.Include(a => a.Dimensions)
            join opportunity in context.Opportunities on assessment.OpportunityId equals opportunity.Id
            where assessment.CompanyId == query.CompanyId && assessment.IsLatest
            select new { assessment, opportunity };

        if (query.MinScore is not null)
        {
            joined = joined.Where(x => x.assessment.FinalScore >= query.MinScore);
        }

        if (query.Verdicts is { Count: > 0 })
        {
            joined = joined.Where(x => query.Verdicts.Contains(x.assessment.Verdict));
        }

        if (query.Categories is { Count: > 0 })
        {
            joined = joined.Where(x => query.Categories.Contains(x.opportunity.SupportCategory));
        }

        if (query.DeadlineWithinDays is not null)
        {
            var limit = DateTimeOffset.UtcNow.AddDays(query.DeadlineWithinDays.Value);
            joined = joined.Where(x => x.opportunity.Deadline != null && x.opportunity.Deadline <= limit);
        }

        var total = await joined.CountAsync(cancellationToken);

        joined = query.Sort switch
        {
            AssessmentSort.DeadlineAscending => joined.OrderBy(x => x.opportunity.Deadline ?? DateTimeOffset.MaxValue),
            AssessmentSort.EvaluatedAtDescending => joined.OrderByDescending(x => x.assessment.EvaluatedAt),
            _ => joined.OrderByDescending(x => x.assessment.FinalScore)
        };

        var items = await joined
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(x => x.assessment)
            .ToListAsync(cancellationToken);

        return new PagedResult<EligibilityAssessment>(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<EligibilityAssessment>> ListLatestForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        await context.Assessments
            .Include(a => a.Dimensions)
            .Where(a => a.CompanyId == companyId && a.IsLatest)
            .OrderByDescending(a => a.FinalScore)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(EligibilityAssessment assessment, CancellationToken cancellationToken = default) =>
        await context.Assessments.AddAsync(assessment, cancellationToken);

    public async Task SupersedePreviousAsync(Guid companyId, Guid opportunityId, CancellationToken cancellationToken = default)
    {
        var previous = await context.Assessments
            .Where(a => a.CompanyId == companyId && a.OpportunityId == opportunityId && a.IsLatest)
            .ToListAsync(cancellationToken);

        foreach (var assessment in previous)
        {
            assessment.Supersede();
        }
    }
}

public sealed class ScenarioSimulationRepository(GovAiDbContext context) : IScenarioSimulationRepository
{
    public Task<ScenarioSimulation?> GetWithImpactsAsync(Guid simulationId, CancellationToken cancellationToken = default) =>
        context.ScenarioSimulations
            .Include(s => s.Impacts)
            .FirstOrDefaultAsync(s => s.Id == simulationId, cancellationToken);

    public async Task<IReadOnlyList<ScenarioSimulation>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await context.ScenarioSimulations
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ScenarioSimulation simulation, CancellationToken cancellationToken = default) =>
        await context.ScenarioSimulations.AddAsync(simulation, cancellationToken);
}

public sealed class NotificationRepository(GovAiDbContext context) : INotificationRepository
{
    public Task<bool> ExistsAsync(string deduplicationKey, CancellationToken cancellationToken = default) =>
        context.Notifications.AnyAsync(n => n.DeduplicationKey == deduplicationKey, cancellationToken);

    public Task<Notification?> GetAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        context.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

    public async Task<PagedResult<Notification>> ListAsync(NotificationQuery query, CancellationToken cancellationToken = default)
    {
        // Kiracı sınırı her zaman ilk uygulanan koşuldur; diğer filtreler ve sayfalama
        // yalnızca bu kümenin içinde çalışır. (EF genel sorgu filtresi de ayrıca devrededir.)
        var source = context.Notifications.Where(n => n.TenantId == query.TenantId);

        if (query.CompanyId is not null)
        {
            source = source.Where(n => n.CompanyId == query.CompanyId);
        }

        if (query.OnlyUnread == true)
        {
            source = source.Where(n => n.ReadAt == null);
        }

        if (query.Kinds is { Count: > 0 })
        {
            source = source.Where(n => query.Kinds.Contains(n.Kind));
        }

        var total = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(n => n.CreatedAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        return new PagedResult<Notification>(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<Notification>> ListUnsentAsync(int take, CancellationToken cancellationToken = default) =>
        await context.Notifications
            .Where(n => n.SentAt == null && n.DeliveryAttemptCount < 3)
            .OrderBy(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default) =>
        await context.Notifications.AddAsync(notification, cancellationToken);
}

public sealed class UserRepository(GovAiDbContext context) : IUserRepository
{
    public Task<AppUser?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<AppUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();

        // Kod tabanındaki TEK IgnoreQueryFilters kullanımı; gerekçesi IUserRepository'de yazılıdır.
        // Filtre tümüyle kalktığı için yumuşak silme koşulu ELLE geri konur — aksi hâlde
        // silinmiş bir hesap yeniden giriş yapabilirdi.
        return context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == normalized && !u.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<AppUser>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.Users
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(AppUser user, CancellationToken cancellationToken = default) =>
        await context.Users.AddAsync(user, cancellationToken);
}

public sealed class TenantRepository(GovAiDbContext context) : ITenantRepository
{
    public Task<Tenant?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        return context.Tenants.FirstOrDefaultAsync(t => t.Slug == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.Tenants.OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default) =>
        await context.Tenants.AddAsync(tenant, cancellationToken);
}

public sealed class AuditLogRepository(GovAiDbContext context) : IAuditLogRepository
{
    public async Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) =>
        await context.AuditLog.AddAsync(entry, cancellationToken);

    public async Task<PagedResult<AuditLogEntry>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.AuditLog.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            source = source.Where(a => a.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            source = source.Where(a => a.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            source = source.Where(a => a.EntityId == query.EntityId);
        }

        if (!string.IsNullOrWhiteSpace(query.UserEmail))
        {
            source = source.Where(a => a.UserEmail == query.UserEmail);
        }

        if (query.From is not null)
        {
            source = source.Where(a => a.OccurredAt >= query.From);
        }

        if (query.To is not null)
        {
            source = source.Where(a => a.OccurredAt <= query.To);
        }

        var total = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(a => a.OccurredAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogEntry>(items, total, query.Page, query.PageSize);
    }
}
