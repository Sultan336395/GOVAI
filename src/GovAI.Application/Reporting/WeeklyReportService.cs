using System.Text.Json;
using System.Text.Json.Serialization;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Application.Eligibility;
using GovAI.Domain.Common;
using GovAI.Domain.Reporting;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Reporting;

/// <summary>
/// Haftalık raporun üretimi, saklanması ve dışa aktarımı.
///
/// <para>
/// Servis rapor <b>kurmaz</b>; kurma işi <see cref="WeeklyReportBuilder"/> içindedir ve
/// veritabanını tanımaz. Buradaki iş yalnızca veriyi toplamak, sonucu saklamak ve
/// yetkiyi doğrulamaktır. Ayrım bilinçlidir: rapor mantığı böylece veritabanı olmadan
/// test edilebilir ve determinizmi kanıtlanabilir kalır.
/// </para>
/// </summary>
public sealed class WeeklyReportService(
    IWeeklyReportRepository reports,
    IAssessmentRepository assessments,
    ICompanyRepository companies,
    IUnitOfWork unitOfWork,
    IReportRenderer renderer,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    ILogger<WeeklyReportService> logger)
{
    private const int DefaultHistoryLimit = 52;

    private static readonly JsonSerializerOptions ContentJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    // ── Üretim ──────────────────────────────────────────────────────────────

    /// <summary>Tek bir firmanın raporunu üretir. Aynı hafta yeniden istenirse kayıt güncellenir.</summary>
    public async Task<WeeklyReportDetailDto> GenerateAsync(
        Guid companyId,
        GenerateWeeklyReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var company = await access.LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);

        var now = clock.UtcNow;
        var week = request.WeekOf is { } gun
            ? ReportWeek.Containing(new DateTimeOffset(gun.ToDateTime(TimeOnly.MinValue), ReportWeek.TurkiyeFarki))
            : ReportWeek.CompletedBefore(now);

        var report = await BuildAndSaveAsync(
            access.RequireTenant(), companyId, company.LegalName, week, now, ReportTrigger.Manual, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDetail(report);
    }

    /// <summary>
    /// Kiracıdaki bütün firmalar için rapor üretir; haftalık takvim bunu çağırır.
    ///
    /// <para>
    /// Bir firmanın hatası turu durdurmaz. Aksi hâlde tek bir bozuk profil, o hafta
    /// hiçbir firmanın rapor almamasına yol açardı.
    /// </para>
    /// </summary>
    public async Task<WeeklyReportBatchResult> GenerateForTenantAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();
        var now = clock.UtcNow;
        var week = ReportWeek.CompletedBefore(now);

        var all = await companies.ListAsync(tenantId, cancellationToken);

        var uretilen = 0;
        var hatali = 0;

        foreach (var company in all)
        {
            try
            {
                await BuildAndSaveAsync(
                    tenantId, company.Id, company.LegalName, week, now, ReportTrigger.Scheduled, cancellationToken);

                await unitOfWork.SaveChangesAsync(cancellationToken);
                uretilen++;
            }
            catch (Exception exception)
            {
                hatali++;
                logger.LogError(
                    exception,
                    "Haftalık raporda firma atlandı. TenantId={TenantId} CompanyId={CompanyId} Dönem={Period}",
                    tenantId, company.Id, week.ToString());
            }
        }

        logger.LogInformation(
            "Haftalık rapor turu bitti. TenantId={TenantId} Firma={CompanyCount} Üretilen={Generated} Hata={Failed}",
            tenantId, all.Count, uretilen, hatali);

        return new WeeklyReportBatchResult(all.Count, uretilen, hatali, week.Start, week.End);
    }

    private async Task<WeeklyReport> BuildAndSaveAsync(
        Guid tenantId,
        Guid companyId,
        string companyName,
        ReportWeek week,
        DateTimeOffset now,
        ReportTrigger trigger,
        CancellationToken cancellationToken)
    {
        var input = await CollectAsync(companyId, companyName, week, now, cancellationToken);
        var content = WeeklyReportBuilder.Build(input);
        var counters = WeeklyReportBuilder.Counters(content);
        var json = JsonSerializer.Serialize(content, ContentJsonOptions);

        var mevcut = await reports.GetForWeekAsync(companyId, week.Start, cancellationToken);

        if (mevcut is not null)
        {
            mevcut.Replace(now, trigger, json, counters, companyName);
            return mevcut;
        }

        var report = new WeeklyReport(
            tenantId, companyId, companyName, week.Start, week.End, now, trigger, json, counters);

        await reports.AddAsync(report, cancellationToken);

        return report;
    }

    /// <summary>Raporun dayandığı veriyi toplar. Karar burada verilmez, yalnızca okunur.</summary>
    private async Task<WeeklyReportInput> CollectAsync(
        Guid companyId,
        string companyName,
        ReportWeek week,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var latest = await assessments.ListLatestForCompanyAsync(companyId, cancellationToken);

        var opportunities = await reports.ListOpportunitiesWithRulesAsync(
            latest.Select(a => a.OpportunityId).Distinct().ToList(), cancellationToken);

        var index = opportunities.ToDictionary(o => o.Id);

        var okunamayan = 0;

        var pairs = latest
            .Where(a => index.ContainsKey(a.OpportunityId))
            .Select(a =>
            {
                var detail = ReadDetail(a.DetailJson, a.Id);

                if (detail is null)
                {
                    okunamayan++;
                }

                return new AssessedOpportunity(a, index[a.OpportunityId], detail);
            })
            .ToList();

        var regulatory = await reports.ListPublishedBetweenAsync(
            week.StartUtc, week.EndUtcExclusive, cancellationToken);

        return new WeeklyReportInput
        {
            CompanyId = companyId,
            CompanyName = companyName,
            Week = week,
            AsOf = now,
            Assessments = pairs,
            RegulatoryChanges = regulatory,
            UnreadableDetailCount = okunamayan,
        };
    }

    /// <summary>
    /// Değerlendirmenin ayrıntı gövdesi. Okunamazsa rapor <b>üretilmeye devam eder</b>:
    /// skorlar ve son tarihler kolonlardan gelir, yalnızca risk ve eksik listesi
    /// o değerlendirme için boş kalır. Tek bir bozuk gövde yüzünden firmanın haftalık
    /// raporunu hiç üretmemek, ona hiçbir şey anlatmamak olurdu.
    ///
    /// <para>
    /// Ama sessiz de kalınmaz: okunamayan kayıt sayısı rapora taşınır ve rapor "eksik
    /// görünmüyor" demek yerine okuyamadığını söyler. Sahada bu ayrım hayatidir —
    /// gövde şekli tutmadığında rapor dört eksik zorunlu belgeyi "eksik yok" diye
    /// yazmıştı.
    /// </para>
    /// </summary>
    private AssessmentDetailSnapshot? ReadDetail(string detailJson, Guid assessmentId)
    {
        try
        {
            return JsonSerializer.Deserialize<AssessmentDetailSnapshot>(
                detailJson, AssessmentDetailSnapshot.JsonOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Değerlendirme ayrıntısı okunamadı; rapor bunu açıkça belirtecek. AssessmentId={AssessmentId}",
                assessmentId);

            return null;
        }
    }

    // ── Okuma ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<WeeklyReportSummaryDto>> ListAsync(
        Guid companyId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await access.LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);

        var all = await reports.ListForCompanyAsync(companyId, limit ?? DefaultHistoryLimit, cancellationToken);

        return all.Select(r => new WeeklyReportSummaryDto(
            r.Id,
            r.CompanyId,
            r.CompanyName,
            r.PeriodStart,
            r.PeriodEnd,
            r.GeneratedAt,
            r.Trigger,
            r.OpportunityCount,
            r.TenderCount,
            r.RegulatoryChangeCount,
            r.RiskCount,
            r.ActionCount,
            r.UrgentDeadlineCount,
            r.DeadlineCount,
            r.PastGapCount)).ToList();
    }

    public async Task<WeeklyReportDetailDto> GetAsync(
        Guid reportId,
        CancellationToken cancellationToken = default) =>
        ToDetail(await LoadAccessibleAsync(reportId, cancellationToken));

    /// <summary>Rapor firmaya aittir; erişim firma üyeliğinden geçer.</summary>
    private async Task<WeeklyReport> LoadAccessibleAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var report = await reports.GetAsync(reportId, cancellationToken)
            ?? throw new NotFoundException("Haftalık rapor", reportId);

        await access.LoadAccessibleAsync(report.CompanyId, CompanyPermission.Read, cancellationToken);

        return report;
    }

    private WeeklyReportDetailDto ToDetail(WeeklyReport report) =>
        new(report.Id, report.Trigger, Deserialize(report));

    private WeeklyReportContent Deserialize(WeeklyReport report) =>
        JsonSerializer.Deserialize<WeeklyReportContent>(report.ContentJson, ContentJsonOptions)
        ?? throw new DomainException($"Rapor gövdesi okunamadı. ReportId={report.Id}");

    // ── Dışa aktarım ────────────────────────────────────────────────────────

    public async Task<ExportedFile> ExportPdfAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await LoadAccessibleAsync(reportId, cancellationToken);
        var content = Deserialize(report);

        var bytes = await renderer.RenderPdfAsync(
            $"{report.CompanyName} — Haftalık Rapor ({report.PeriodStart:dd.MM.yyyy} – {report.PeriodEnd:dd.MM.yyyy})",
            WeeklyReportHtml.Build(content),
            cancellationToken);

        return new ExportedFile(FileName(report, "pdf"), "application/pdf", bytes);
    }

    public async Task<ExportedFile> ExportExcelAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await LoadAccessibleAsync(reportId, cancellationToken);
        var content = Deserialize(report);

        var bytes = await renderer.RenderExcelAsync(
            "Haftalık Rapor",
            WeeklyReportSheet.Headers,
            WeeklyReportSheet.Rows(content),
            cancellationToken);

        return new ExportedFile(
            FileName(report, "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            bytes);
    }

    /// <summary>Dosya adı dönemi taşır: indirilen üç rapor birbirine karışmasın.</summary>
    private static string FileName(WeeklyReport report, string extension) =>
        $"govai-haftalik-rapor-{report.PeriodStart:yyyy-MM-dd}.{extension}";
}
