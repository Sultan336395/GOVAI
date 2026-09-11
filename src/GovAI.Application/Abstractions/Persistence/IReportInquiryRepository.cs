using GovAI.Domain.Reporting;

namespace GovAI.Application.Abstractions.Persistence;

/// <summary>
/// Rapora sorulan soruların veri erişimi.
///
/// <para>
/// Silme yöntemi <b>bilerek yoktur</b>. Kayıt hem cevabın kendisi hem de hak sayacının
/// dayanağıdır; silinebilmesi, harcanmış hakkın geri kazanılması demek olurdu.
/// </para>
/// </summary>
public interface IReportInquiryRepository
{
    /// <summary>Bir rapora sorulmuş sorular, sorulma sırasına göre.</summary>
    Task<IReadOnlyList<ReportInquiry>> ListForReportAsync(
        Guid reportId,
        CancellationToken cancellationToken = default);

    /// <summary>Firmanın son soruları, en yenisi başta. Hareket akışı için.</summary>
    Task<IReadOnlyList<ReportInquiry>> ListRecentForCompanyAsync(
        Guid companyId,
        int limit,
        CancellationToken cancellationToken = default);

    Task AddAsync(ReportInquiry inquiry, CancellationToken cancellationToken = default);
}
