using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// Rapora sorulan soruların veri erişimi.
///
/// <para>
/// Kayıtlar kiracıya aittir ve global kiracı süzgecine tabidir. Silme yöntemi bilerek
/// yoktur: kayıt hak sayacının dayanağıdır.
/// </para>
/// </summary>
public sealed class ReportInquiryRepository(GovAiDbContext context) : IReportInquiryRepository
{
    public async Task<IReadOnlyList<ReportInquiry>> ListForReportAsync(
        Guid reportId,
        CancellationToken cancellationToken = default) =>
        await context.ReportInquiries
            .AsNoTracking()
            .Where(i => i.WeeklyReportId == reportId)
            .OrderBy(i => i.AskedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReportInquiry>> ListRecentForCompanyAsync(
        Guid companyId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await context.ReportInquiries
            .AsNoTracking()
            .Where(i => i.CompanyId == companyId)
            .OrderByDescending(i => i.AskedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ReportInquiry inquiry, CancellationToken cancellationToken = default) =>
        await context.ReportInquiries.AddAsync(inquiry, cancellationToken);
}
