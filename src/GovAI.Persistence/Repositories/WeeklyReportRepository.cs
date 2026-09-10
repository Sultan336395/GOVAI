using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Regulatory;
using GovAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// Haftalık rapor veri erişimi.
///
/// <para>
/// Rapor kayıtları kiracıya aittir ve global kiracı süzgecine tabidir; burada ayrıca
/// filtre yazılmaz. Silme yöntemi <b>bilerek yoktur</b>: geçmiş rapor, firmanın o hafta
/// hangi bilgiyle karar aldığının kaydıdır.
/// </para>
/// </summary>
public sealed class WeeklyReportRepository(GovAiDbContext context) : IWeeklyReportRepository
{
    /// <summary>Geçmiş listesi sınırı: sonsuz liste ekranı da sorguyu da taşıyamaz.</summary>
    private const int MaximumHistory = 200;

    public Task<WeeklyReport?> GetAsync(Guid reportId, CancellationToken cancellationToken = default) =>
        context.WeeklyReports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);

    public Task<WeeklyReport?> GetForWeekAsync(
        Guid companyId,
        DateOnly periodStart,
        CancellationToken cancellationToken = default) =>
        context.WeeklyReports.FirstOrDefaultAsync(
            r => r.CompanyId == companyId && r.PeriodStart == periodStart,
            cancellationToken);

    public async Task<IReadOnlyList<WeeklyReport>> ListForCompanyAsync(
        Guid companyId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await context.WeeklyReports
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.PeriodStart)
            .Take(Math.Clamp(limit, 1, MaximumHistory))
            .ToListAsync(cancellationToken);

    public async Task AddAsync(WeeklyReport report, CancellationToken cancellationToken = default) =>
        await context.WeeklyReports.AddAsync(report, cancellationToken);

    public async Task<IReadOnlyList<Opportunity>> ListOpportunitiesWithRulesAsync(
        IReadOnlyCollection<Guid> opportunityIds,
        CancellationToken cancellationToken = default)
    {
        if (opportunityIds.Count == 0)
        {
            return [];
        }

        return await context.Opportunities
            .AsNoTracking()
            .Include(o => o.Rules)
            .Where(o => opportunityIds.Contains(o.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RegulatoryChange>> ListPublishedBetweenAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtcExclusive,
        CancellationToken cancellationToken = default) =>
        await context.RegulatoryChanges
            .AsNoTracking()
            .Where(c => c.Status != RegulatoryChangeStatus.Quarantined)
            .Where(c => (c.PublicationDate ?? c.DetectedAt) >= startUtc
                        && (c.PublicationDate ?? c.DetectedAt) < endUtcExclusive)
            .ToListAsync(cancellationToken);
}
