using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Integrations;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// Süreç olay günlüğü deposu. Yalnızca okuma ve ekleme; günlük yalnızca büyür.
/// </summary>
public sealed class ErpProcessEventRepository(GovAiDbContext context) : IErpProcessEventRepository
{
    public async Task<IReadOnlySet<string>> ListKeysAsync(
        Guid companyId,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        // Anahtar hesaplanmış bir özelliktir (sütun değil), bu yüzden satırların
        // kimlik alanları çekilip anahtar bellekte kurulur. Pencere daraltıldığı için
        // bu, tüm günlüğü okumak anlamına gelmez.
        var satirlar = await context.ErpProcessEvents
            .IgnoreQueryFilters()
            .Where(e => e.CompanyId == companyId && e.OccurredAt >= since)
            .Select(e => new { e.CaseId, e.Activity, e.OccurredAt, e.ExternalId })
            .ToListAsync(cancellationToken);

        return satirlar
            .Select(s => s.ExternalId is { Length: > 0 }
                ? $"id:{s.ExternalId}"
                : $"vaka:{s.CaseId}|{s.Activity}|{s.OccurredAt.UtcDateTime:O}")
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task AddRangeAsync(
        IEnumerable<ErpProcessEvent> events,
        CancellationToken cancellationToken = default) =>
        await context.ErpProcessEvents.AddRangeAsync(events, cancellationToken);

    public async Task<DateTimeOffset?> LatestOccurredAtAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        await context.ErpProcessEvents
            .IgnoreQueryFilters()
            .Where(e => e.CompanyId == companyId)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => (DateTimeOffset?)e.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);
}
