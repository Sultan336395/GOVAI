using GovAI.Domain.Opportunities;
using GovAI.Domain.Regulatory;
using GovAI.Domain.Reporting;

namespace GovAI.Application.Abstractions.Persistence;

/// <summary>
/// Haftalık rapor kayıtlarının ve raporu kurmak için gereken verinin okunması.
///
/// <para>
/// Rapor tek bir firmanın onlarca değerlendirmesini, o değerlendirmelerin dayandığı
/// çağrıları ve haftanın mevzuat değişikliklerini birlikte okur. Bunları tek tek
/// çekmek her firma için yüzlerce sorgu demek olurdu; toplu okuma yöntemleri bu
/// yüzden burada.
/// </para>
/// </summary>
public interface IWeeklyReportRepository
{
    Task<WeeklyReport?> GetAsync(Guid reportId, CancellationToken cancellationToken = default);

    /// <summary>Aynı haftanın raporu zaten varsa onu döner; yeniden üretim satır açmaz.</summary>
    Task<WeeklyReport?> GetForWeekAsync(
        Guid companyId,
        DateOnly periodStart,
        CancellationToken cancellationToken = default);

    /// <summary>Firmanın rapor geçmişi, en yeni hafta başta.</summary>
    Task<IReadOnlyList<WeeklyReport>> ListForCompanyAsync(
        Guid companyId,
        int limit,
        CancellationToken cancellationToken = default);

    Task AddAsync(WeeklyReport report, CancellationToken cancellationToken = default);

    /// <summary>Verilen çağrıları kurallarıyla birlikte getirir (teknoloji ihalesi ayrımı kurala bakar).</summary>
    Task<IReadOnlyList<Opportunity>> ListOpportunitiesWithRulesAsync(
        IReadOnlyCollection<Guid> opportunityIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dönemde yayımlanan mevzuat değişiklikleri.
    ///
    /// <para>
    /// Kurum yayım tarihini her zaman bildirmez; o hâlde belgenin tespit edildiği an
    /// kullanılır. Tarihi olmayan kaydı listeden düşürmek, gerçekten yayımlanmış bir
    /// değişikliği görünmez yapardı.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<RegulatoryChange>> ListPublishedBetweenAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtcExclusive,
        CancellationToken cancellationToken = default);
}
