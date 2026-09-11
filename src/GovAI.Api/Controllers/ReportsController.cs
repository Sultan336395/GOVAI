using GovAI.Api.Infrastructure;
using GovAI.Application.Dashboard;
using GovAI.Application.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/reports</c> — PDF, Excel, dashboard veri setleri.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Policy = Policies.CompanyData)]
public sealed class ReportsController(
    ReportingService service,
    DashboardInsightsService insights) : ControllerBase
{
    /// <summary>Yönetici dashboard verisi: KPI'lar, kategori kırılımı, boyut ortalamaları, öncelikli fırsatlar.</summary>
    [HttpGet("companies/{companyId:guid}/dashboard")]
    [Produces("application/json")]
    public async Task<ActionResult<DashboardDto>> GetDashboard(Guid companyId, CancellationToken cancellationToken) =>
        Ok(await service.GetDashboardAsync(companyId, cancellationToken));

    /// <summary>
    /// Dashboard'un dört ek bölümü: aksiyonlar, profil doluluğu, huni ve eğilim,
    /// son hareketler.
    ///
    /// <para>
    /// Ayrı bir uçtur: ana dashboard sayıları bu bölümler hesaplanamasa da görünmelidir.
    /// </para>
    /// </summary>
    [HttpGet("companies/{companyId:guid}/dashboard/insights")]
    [Produces("application/json")]
    public async Task<ActionResult<DashboardInsightsDto>> GetInsights(
        Guid companyId,
        CancellationToken cancellationToken) =>
        Ok(await insights.GetAsync(companyId, cancellationToken));

    /// <summary>Önceliklendirilmiş fırsat listesini Excel olarak indirir.</summary>
    [HttpGet("companies/{companyId:guid}/export/excel")]
    [Audited("Report.ExcelExported", "Company", RouteKey = "companyId")]
    public async Task<IActionResult> ExportExcel(
        Guid companyId,
        [FromQuery] decimal? minScore,
        [FromQuery] int top = 50,
        CancellationToken cancellationToken = default)
    {
        var file = await service.ExportExcelAsync(new ExportRequest(companyId, minScore, top), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>Yönetici özet raporunu PDF olarak indirir.</summary>
    [HttpGet("companies/{companyId:guid}/export/pdf")]
    [Audited("Report.PdfExported", "Company", RouteKey = "companyId")]
    public async Task<IActionResult> ExportPdf(
        Guid companyId,
        [FromQuery] decimal? minScore,
        [FromQuery] int top = 25,
        CancellationToken cancellationToken = default)
    {
        var file = await service.ExportPdfAsync(new ExportRequest(companyId, minScore, top), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
