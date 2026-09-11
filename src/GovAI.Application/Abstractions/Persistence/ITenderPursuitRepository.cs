using GovAI.Domain.Tenders;

namespace GovAI.Application.Abstractions.Persistence;

/// <summary>
/// İhale takip kayıtlarının veri erişimi.
///
/// <para>
/// Silme yöntemi bilerek yoktur. Bir takip bırakıldığında kayıt
/// <see cref="TenderPursuitStatus.Vazgecildi"/> olur; silinseydi "bu ihaleye neden
/// girmedik" sorusunun cevabı da geçmişiyle birlikte kaybolurdu.
/// </para>
/// </summary>
public interface ITenderPursuitRepository
{
    Task<TenderPursuit?> GetAsync(Guid pursuitId, CancellationToken cancellationToken = default);

    /// <summary>Aynı çağrı için ikinci takip açılmasın diye önce buraya bakılır.</summary>
    Task<TenderPursuit?> GetForOpportunityAsync(
        Guid companyId,
        Guid opportunityId,
        CancellationToken cancellationToken = default);

    /// <summary>Firmanın takipleri; en son dokunulan başta.</summary>
    Task<IReadOnlyList<TenderPursuit>> ListForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task AddAsync(TenderPursuit pursuit, CancellationToken cancellationToken = default);
}
