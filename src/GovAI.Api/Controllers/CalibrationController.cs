using GovAI.Api.Infrastructure;
using GovAI.Application.Calibration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/calibration</c> — uzman değerlendirmesi ve kalibrasyon ölçümü.
///
/// <para>
/// Bu uçlar karar üretmez, <b>karar mekanizmasını ölçer</b>. Uzman görüşü hiçbir skoru
/// değiştirmez; motor deterministik kalır (bkz. <c>docs/adr/0006</c>).
/// </para>
///
/// <para>
/// Kalibrasyon kaydı müşteri verisidir: yetki firma üyeliğinden geçer ve platform
/// rolleri bu uçlara giremez.
/// </para>
/// </summary>
[ApiController]
[Route("api/calibration")]
[Authorize(Policy = Policies.CompanyData)]
public sealed class CalibrationController(CalibrationService service) : ControllerBase
{
    /// <summary>
    /// Danışmanın kararını kaydeder. Aynı değerlendirme için ikinci kayıt açılmaz;
    /// mevcut kayıt güncellenir.
    /// </summary>
    [HttpPost("verdicts")]
    [Audited("Calibration.VerdictRecorded", "Assessment")]
    public async Task<ActionResult<ExpertVerdictDto>> Record(
        [FromBody] RecordExpertVerdictRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.RecordAsync(request, cancellationToken));

    /// <summary>Firmanın uzman değerlendirmeleri, en yeni başta.</summary>
    [HttpGet("companies/{companyId:guid}/verdicts")]
    [Produces("application/json")]
    public async Task<ActionResult<IReadOnlyList<ExpertVerdictDto>>> List(
        Guid companyId,
        CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(companyId, cancellationToken));

    /// <summary>
    /// Kalibrasyon raporu: uyum oranı, yanlış pozitif/negatif sayıları, karışıklık
    /// matrisi, ayrışma sebepleri ve skor ayrımı.
    ///
    /// <para>
    /// <c>companyId</c> verilmezse kiracının tamamı ölçülür. Ağırlık kalibrasyonu tek
    /// firmanın verisiyle yapılamaz; örneklem ancak birden çok firmada anlamlı büyüklüğe
    /// ulaşır.
    /// </para>
    /// </summary>
    [HttpGet("report")]
    [Produces("application/json")]
    public async Task<ActionResult<CalibrationReportDto>> Report(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetReportAsync(companyId, cancellationToken));
}
