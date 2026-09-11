using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Tenders;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// İhale takiplerinin veri erişimi.
///
/// <para>
/// Geçmiş (<see cref="TenderPursuitEvent"/>) her okumada birlikte getirilir: takip
/// ekranı "şu an neredeyiz" kadar "buraya nasıl geldik" sorusunu da cevaplar ve ayrı
/// bir uç için ikinci tur istek yapmak listeyi kullanılamaz hâle getirirdi.
/// </para>
/// </summary>
public sealed class TenderPursuitRepository(GovAiDbContext context) : ITenderPursuitRepository
{
    public async Task<TenderPursuit?> GetAsync(
        Guid pursuitId,
        CancellationToken cancellationToken = default) =>
        await context.TenderPursuits
            .Include(p => p.Events)
            .FirstOrDefaultAsync(p => p.Id == pursuitId, cancellationToken);

    public async Task<TenderPursuit?> GetForOpportunityAsync(
        Guid companyId,
        Guid opportunityId,
        CancellationToken cancellationToken = default) =>
        await context.TenderPursuits
            .Include(p => p.Events)
            .FirstOrDefaultAsync(
                p => p.CompanyId == companyId && p.OpportunityId == opportunityId,
                cancellationToken);

    public async Task<IReadOnlyList<TenderPursuit>> ListForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        await context.TenderPursuits
            .AsNoTracking()
            .Include(p => p.Events)
            .Where(p => p.CompanyId == companyId)
            .OrderByDescending(p => p.StatusChangedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(TenderPursuit pursuit, CancellationToken cancellationToken = default) =>
        await context.TenderPursuits.AddAsync(pursuit, cancellationToken);
}
