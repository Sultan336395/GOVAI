using GovAI.Api.Infrastructure;
using GovAI.Application.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/reports/weekly</c> — otonom haftalık rapor.
///
/// <para>
/// Rapor firmaya aittir; yetki firma üyeliğinden geçer ve servis her çağrıda bunu
/// yeniden doğrular. Platform rolleri (katalog yöneticisi, inceleyici) müşteri
/// verisine erişmez, bu yüzden <see cref="Policies.CompanyData"/> altındadır.
/// </para>
/// </summary>
[ApiController]
[Route("api/reports/weekly")]
[Authorize(Policy = Policies.CompanyData)]
public sealed class WeeklyReportsController(
    WeeklyReportService service,
    ReportInquiryService inquiries) : ControllerBase
{
    /// <summary>Firmanın rapor geçmişi; en yeni hafta başta.</summary>
    [HttpGet("companies/{companyId:guid}")]
    [Produces("application/json")]
    public async Task<ActionResult<IReadOnlyList<WeeklyReportSummaryDto>>> List(
        Guid companyId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(companyId, limit, cancellationToken));

    /// <summary>Tek bir raporun tamamı: yedi bölüm.</summary>
    [HttpGet("{reportId:guid}")]
    [Produces("application/json")]
    public async Task<ActionResult<WeeklyReportDetailDto>> Get(
        Guid reportId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(reportId, cancellationToken));

    /// <summary>
    /// Raporu elle üretir. Dönem verilmezse tamamlanmış son hafta alınır.
    ///
    /// <para>
    /// Aynı haftanın raporu zaten varsa <b>güncellenir</b>, ikinci kayıt açılmaz;
    /// bu yüzden yeniden çağırmak geçmişi kopyalarla doldurmaz.
    /// </para>
    /// </summary>
    [HttpPost("companies/{companyId:guid}")]
    [Audited("WeeklyReport.Generated", "Company", RouteKey = "companyId")]
    public async Task<ActionResult<WeeklyReportDetailDto>> Generate(
        Guid companyId,
        [FromBody] GenerateWeeklyReportRequest? request,
        CancellationToken cancellationToken) =>
        Ok(await service.GenerateAsync(companyId, request ?? new GenerateWeeklyReportRequest(), cancellationToken));

    /// <summary>
    /// Rapora sorulabilecek sorular, sorulmuş olanlar ve kalan hak.
    ///
    /// <para>
    /// Sorular rapordan üretilir; kullanıcı soru <b>yazmaz</b>, listeden seçer. Sabit soru
    /// kümesi, her sorunun rapordaki hangi veriden cevaplanacağının önceden belli olmasını
    /// sağlar.
    /// </para>
    /// </summary>
    [HttpGet("{reportId:guid}/questions")]
    [Produces("application/json")]
    public async Task<ActionResult<ReportInquiryStateDto>> Questions(
        Guid reportId,
        CancellationToken cancellationToken) =>
        Ok(await inquiries.GetStateAsync(reportId, cancellationToken));

    /// <summary>
    /// Seçilen soruyu cevaplar.
    ///
    /// <para>
    /// Hak cevap üretilince harcanır. Daha önce sorulmuş bir soru yeniden açılırsa kayıtlı
    /// cevap döner ve <b>hak harcanmaz</b> — kullanıcı listeye dönüp okuduğunu tekrar
    /// görebilmelidir.
    /// </para>
    /// </summary>
    [HttpPost("{reportId:guid}/questions")]
    [Audited("WeeklyReport.QuestionAnswered", "WeeklyReport", RouteKey = "reportId")]
    public async Task<ActionResult<AnswerQuestionResultDto>> Answer(
        Guid reportId,
        [FromBody] AnswerQuestionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await inquiries.AnswerAsync(
            reportId, request.QuestionKey, request.ParentInquiryId, cancellationToken));

    [HttpGet("{reportId:guid}/pdf")]
    [Audited("WeeklyReport.PdfExported", "WeeklyReport", RouteKey = "reportId")]
    public async Task<IActionResult> ExportPdf(Guid reportId, CancellationToken cancellationToken)
    {
        var file = await service.ExportPdfAsync(reportId, cancellationToken);

        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("{reportId:guid}/excel")]
    [Audited("WeeklyReport.ExcelExported", "WeeklyReport", RouteKey = "reportId")]
    public async Task<IActionResult> ExportExcel(Guid reportId, CancellationToken cancellationToken)
    {
        var file = await service.ExportExcelAsync(reportId, cancellationToken);

        return File(file.Content, file.ContentType, file.FileName);
    }
}

/// <summary>
/// <c>/api/reports/weekly-batch</c> — haftalık takvimin çağırdığı toplu üretim.
///
/// <para>
/// Ayrı bir denetleyicidedir çünkü yetkisi farklıdır: bunu bir kullanıcı değil,
/// zamanlayıcı worker'ı çağırır ve tek bir firmaya değil kiracının tamamına dokunur.
/// Aynı ucu müşteri rollerine açmak, bir kullanıcının bütün firmaların raporunu
/// tetikleyebilmesi demek olurdu.
/// </para>
/// </summary>
[ApiController]
[Route("api/reports/weekly-batch")]
[Authorize(Policy = Policies.SystemIngest)]
public sealed class WeeklyReportBatchController(WeeklyReportService service) : ControllerBase
{
    [HttpPost]
    [Audited("WeeklyReport.BatchGenerated", "Tenant")]
    public async Task<ActionResult<WeeklyReportBatchResult>> Generate(CancellationToken cancellationToken) =>
        Ok(await service.GenerateForTenantAsync(cancellationToken));
}
