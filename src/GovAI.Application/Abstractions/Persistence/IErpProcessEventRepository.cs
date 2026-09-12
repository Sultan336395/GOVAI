using GovAI.Domain.Integrations;

namespace GovAI.Application.Abstractions.Persistence;

/// <summary>
/// Süreç olay günlüğü deposu.
///
/// <para>
/// Yalnızca <b>okuma ve ekleme</b> sunar; güncelleme ve silme yoktur. Bir olay
/// gerçekleşmiştir, sonradan değişmez; üzerine yazılabilseydi geçmiş ölçümler
/// sessizce değişir ve nedensel analiz zeminini kaybederdi.
/// </para>
/// </summary>
public interface IErpProcessEventRepository
{
    /// <summary>
    /// Firmanın kayıtlı olaylarının tekilleştirme anahtarları.
    ///
    /// <para>
    /// Yalnızca <paramref name="since"/> tarihinden sonraki olaylar okunur: günlüğün
    /// tamamını belleğe almak yıllar biriktiğinde imkânsız hâle gelir ve ERP zaten
    /// geçmişi yeniden göndermez.
    /// </para>
    /// </summary>
    Task<IReadOnlySet<string>> ListKeysAsync(
        Guid companyId,
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<ErpProcessEvent> events, CancellationToken cancellationToken = default);

    /// <summary>Firmadaki en son olayın zamanı; hiç olay yoksa <c>null</c>.</summary>
    Task<DateTimeOffset?> LatestOccurredAtAsync(Guid companyId, CancellationToken cancellationToken = default);
}
