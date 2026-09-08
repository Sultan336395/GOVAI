using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// Bakım çalıştırmalarının veri erişimi (Faz 3).
///
/// <para>
/// Bu kayıtlar ortak kataloğa aittir, bir kiracıya değil: kiracı filtresi yoktur ve
/// yalnızca platform rolleri okur. Silme yöntemi <b>bilerek yoktur</b> — geri alınmış
/// bir çalıştırma da denetim izinin parçasıdır.
/// </para>
/// </summary>
public sealed class MaintenanceRunRepository(GovAiDbContext context) : IMaintenanceRunRepository
{
    public Task<MaintenanceRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default) =>
        context.MaintenanceRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    public async Task<IReadOnlyList<MaintenanceRun>> ListRecentAsync(
        int take,
        CancellationToken cancellationToken = default) =>
        await context.MaintenanceRuns
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

    public async Task AddAsync(MaintenanceRun run, CancellationToken cancellationToken = default) =>
        await context.MaintenanceRuns.AddAsync(run, cancellationToken);
}
