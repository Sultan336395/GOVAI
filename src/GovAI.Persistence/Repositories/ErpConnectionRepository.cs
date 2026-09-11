using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Integrations;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// ERP bağlantılarının veri erişimi.
///
/// <para>
/// Kayıtlar kiracıya aittir ve global kiracı süzgecine tabidir; burada ayrıca filtre
/// yazılmaz.
/// </para>
/// </summary>
public sealed class ErpConnectionRepository(GovAiDbContext context) : IErpConnectionRepository
{
    public Task<ErpConnection?> GetByCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        context.ErpConnections.FirstOrDefaultAsync(c => c.CompanyId == companyId, cancellationToken);

    public async Task<IReadOnlyList<ErpConnection>> ListEnabledAsync(
        CancellationToken cancellationToken = default) =>
        await context.ErpConnections
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.CompanyId)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ErpConnection connection, CancellationToken cancellationToken = default) =>
        await context.ErpConnections.AddAsync(connection, cancellationToken);

    public void Remove(ErpConnection connection) => context.ErpConnections.Remove(connection);
}
