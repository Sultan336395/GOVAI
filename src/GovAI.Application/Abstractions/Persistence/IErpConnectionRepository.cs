using GovAI.Domain.Integrations;

namespace GovAI.Application.Abstractions.Persistence;

/// <summary>
/// ERP bağlantılarının veri erişimi.
///
/// <para>
/// Bir firmanın <b>tek</b> bağlantısı olur: ikinci bir bağlantı, aynı profile iki farklı
/// kaynaktan yazmak ve hangisinin kazandığının belirsiz kalması demek olurdu.
/// </para>
/// </summary>
public interface IErpConnectionRepository
{
    Task<ErpConnection?> GetByCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>Gece turunun çekeceği etkin bağlantılar.</summary>
    Task<IReadOnlyList<ErpConnection>> ListEnabledAsync(CancellationToken cancellationToken = default);

    Task AddAsync(ErpConnection connection, CancellationToken cancellationToken = default);

    void Remove(ErpConnection connection);
}
