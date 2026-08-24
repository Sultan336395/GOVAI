using System.Globalization;
using System.Net;
using System.Text;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Application.Eligibility;
using GovAI.Domain.Common;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Reporting;

/// <summary>Yönetici Dashboard'ının (Modül 9) özet verisi.</summary>
public sealed record DashboardDto(
    Guid CompanyId,
    string CompanyName,
    decimal ProfileCompleteness,
    int TotalEvaluatedOpportunities,
    int EligibleCount,
    int ConditionallyEligibleCount,
    int NotEligibleCount,
    int IndeterminateCount,
    decimal AverageScore,
    int ClosingWithin15Days,
    int MissingMandatoryDocumentTotal,
    int DataGapTotal,
    IReadOnlyList<CategoryBreakdownDto> CategoryBreakdown,
    IReadOnlyList<DimensionAverageDto> DimensionAverages,
    IReadOnlyList<OpportunityMatchDto> TopOpportunities,
    IReadOnlyList<OpportunityMatchDto> ClosingSoon);

public sealed record CategoryBreakdownDto(SupportCategory Category, string CategoryLabel, int Count, int EligibleCount, decimal AverageScore);

public sealed record DimensionAverageDto(RuleDimension Dimension, string Label, decimal AverageValue);

public sealed record ExportRequest(Guid CompanyId, decimal? MinScore, int TopCount = 25);

public sealed record ExportedFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Raporlama ve Yönetici Dashboard Modülü (Modül 9).
/// Skorları okur, önceliklendirir ve PDF / Excel çıktısı üretir.
/// </summary>
public sealed class ReportingService(
    IAssessmentRepository assessments,
    IOpportunityRepository opportunities,
    EligibilityService eligibility,
    IReportRenderer renderer,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    ILogger<ReportingService> logger)
{
    private const int ClosingSoonDayThreshold = 15;

    public async Task<DashboardDto> GetDashboardAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        // Kiracı + yetki doğrulaması; şirket nesnesi ancak bu kapıdan geçtikten sonra elde edilir.
        var company = await access.LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);

        var all = await assessments.ListLatestForCompanyAsync(companyId, cancellationToken);
        var now = clock.UtcNow;

        var categoryRows = new List<CategoryBreakdownDto>();
        var closingSoon = new List<OpportunityMatchDto>();
        var top = new List<OpportunityMatchDto>();

        var byCategory = new Dictionary<SupportCategory, List<decimal>>();
        var eligibleByCategory = new Dictionary<SupportCategory, int>();

        foreach (var assessment in all)
        {
            var opportunity = await opportunities.GetWithRulesAsync(assessment.OpportunityId, cancellationToken);
            if (opportunity is null)
            {
                continue;
            }

            var daysLeft = opportunity.DaysUntilDeadline(now);
            var dto = new OpportunityMatchDto(
                assessment.Id,
                opportunity.Id,
                opportunity.Title,
                opportunity.Publisher,
                opportunity.SupportCategory,
                opportunity.Deadline,
                daysLeft,
                assessment.FinalScore,
                assessment.Confidence,
                assessment.Verdict,
                assessment.MissingConditionCount,
                assessment.MissingMandatoryDocumentCount,
                assessment.DataGapCount,
                opportunity.Budget?.MaxAmount,
                assessment.ExecutiveSummary,
                assessment.EvaluatedAt);

            top.Add(dto);

            if (daysLeft is > 0 and <= ClosingSoonDayThreshold && assessment.Verdict != EligibilityVerdict.NotEligible)
            {
                closingSoon.Add(dto);
            }

            if (!byCategory.TryGetValue(opportunity.SupportCategory, out var scores))
            {
                scores = [];
                byCategory[opportunity.SupportCategory] = scores;
            }

            scores.Add(assessment.FinalScore);

            if (assessment.Verdict == EligibilityVerdict.Eligible)
            {
                eligibleByCategory[opportunity.SupportCategory] =
                    eligibleByCategory.GetValueOrDefault(opportunity.SupportCategory) + 1;
            }
        }

        foreach (var (category, scores) in byCategory.OrderByDescending(kv => kv.Value.Count))
        {
            categoryRows.Add(new CategoryBreakdownDto(
                category,
                CategoryLabels.Of(category),
                scores.Count,
                eligibleByCategory.GetValueOrDefault(category),
                Math.Round(scores.Average(), 2)));
        }

        var dimensionAverages = all
            .SelectMany(a => a.Dimensions)
            .GroupBy(d => d.Dimension)
            .Select(g => new DimensionAverageDto(g.Key, DimensionLabels.Of(g.Key), Math.Round(g.Average(d => d.Value), 4)))
            .OrderBy(d => d.Dimension)
            .ToList();

        return new DashboardDto(
            company.Id,
            company.LegalName,
            Companies.CompanyProfileService.CalculateCompleteness(company),
            all.Count,
            all.Count(a => a.Verdict == EligibilityVerdict.Eligible),
            all.Count(a => a.Verdict == EligibilityVerdict.ConditionallyEligible),
            all.Count(a => a.Verdict == EligibilityVerdict.NotEligible),
            all.Count(a => a.Verdict == EligibilityVerdict.Indeterminate),
            all.Count == 0 ? 0m : Math.Round(all.Average(a => a.FinalScore), 2),
            closingSoon.Count,
            all.Sum(a => a.MissingMandatoryDocumentCount),
            all.Sum(a => a.DataGapCount),
            categoryRows,
            dimensionAverages,
            top.OrderByDescending(t => t.FinalScore).Take(10).ToList(),
            closingSoon.OrderBy(t => t.DaysUntilDeadline).Take(10).ToList());
    }

    /// <summary>Önceliklendirilmiş fırsat listesini Excel olarak dışa aktarır.</summary>
    public async Task<ExportedFile> ExportExcelAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        var matches = await LoadMatchesAsync(request, cancellationToken);

        string[] headers =
        [
            "Fırsat", "Yayınlayan", "Destek Türü", "Skor", "Güven", "Karar",
            "Son Başvuru", "Kalan Gün", "Eksik Koşul", "Eksik Zorunlu Belge", "Veri Boşluğu", "Azami Tutar"
        ];

        var rows = matches
            .Select(m => (IReadOnlyList<object?>)
            [
                m.OpportunityTitle,
                m.Publisher,
                CategoryLabels.Of(m.SupportCategory),
                m.FinalScore,
                m.Confidence,
                VerdictLabels.Of(m.Verdict),
                m.Deadline?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                m.DaysUntilDeadline,
                m.MissingConditionCount,
                m.MissingMandatoryDocumentCount,
                m.DataGapCount,
                m.MaxAmount
            ])
            .ToList();

        var content = await renderer.RenderExcelAsync("Fırsatlar", headers, rows, cancellationToken);
        var fileName = $"govai-firsatlar-{clock.UtcNow:yyyyMMdd-HHmm}.xlsx";

        logger.LogInformation("Excel raporu üretildi. CompanyId={CompanyId} Satır={Rows}", request.CompanyId, rows.Count);

        return new ExportedFile(fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", content);
    }

    /// <summary>Yönetici özet raporunu PDF olarak üretir.</summary>
    public async Task<ExportedFile> ExportPdfAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        var dashboard = await GetDashboardAsync(request.CompanyId, cancellationToken);
        var matches = await LoadMatchesAsync(request, cancellationToken);

        var html = BuildReportHtml(dashboard, matches, clock.UtcNow);
        var content = await renderer.RenderPdfAsync($"GOVAI Fırsat Raporu — {dashboard.CompanyName}", html, cancellationToken);
        var fileName = $"govai-rapor-{clock.UtcNow:yyyyMMdd-HHmm}.pdf";

        return new ExportedFile(fileName, "application/pdf", content);
    }

    private async Task<IReadOnlyList<OpportunityMatchDto>> LoadMatchesAsync(ExportRequest request, CancellationToken cancellationToken)
    {
        var page = await eligibility.ListMatchesAsync(
            new AssessmentQuery
            {
                CompanyId = request.CompanyId,
                MinScore = request.MinScore,
                PageSize = Math.Clamp(request.TopCount, 1, 200),
                Sort = AssessmentSort.ScoreDescending
            },
            cancellationToken);

        return page.Items;
    }

    private static string BuildReportHtml(DashboardDto dashboard, IReadOnlyList<OpportunityMatchDto> matches, DateTimeOffset generatedAt)
    {
        var html = new StringBuilder();

        html.Append("<style>")
            .Append("body{font-family:'Segoe UI',Arial,sans-serif;font-size:12px;color:#1f2933;}")
            .Append("h1{font-size:20px;margin:0 0 4px;} h2{font-size:14px;margin:18px 0 8px;}")
            .Append("table{width:100%;border-collapse:collapse;margin-top:8px;}")
            .Append("th,td{border:1px solid #d7dde3;padding:6px 8px;text-align:left;}")
            .Append("th{background:#eef2f6;font-weight:600;}")
            .Append(".kpi{display:inline-block;margin-right:24px;} .kpi b{display:block;font-size:18px;}")
            .Append("</style>");

        html.Append($"<h1>{Encode(dashboard.CompanyName)} — Fırsat Uygunluk Raporu</h1>")
            .Append($"<div>Oluşturulma: {generatedAt:dd.MM.yyyy HH:mm} UTC · Profil doluluğu: %{dashboard.ProfileCompleteness * 100:0}</div>");

        html.Append("<h2>Özet</h2><div>")
            .Append(Kpi("Değerlendirilen", dashboard.TotalEvaluatedOpportunities.ToString()))
            .Append(Kpi("Uygun", dashboard.EligibleCount.ToString()))
            .Append(Kpi("Şartlı uygun", dashboard.ConditionallyEligibleCount.ToString()))
            .Append(Kpi("Ortalama skor", dashboard.AverageScore.ToString("0.0", CultureInfo.InvariantCulture)))
            .Append(Kpi("15 günde kapanan", dashboard.ClosingWithin15Days.ToString()))
            .Append("</div>");

        html.Append("<h2>Skor boyutları (ortalama)</h2><table><tr><th>Boyut</th><th>Ortalama</th></tr>");
        foreach (var dimension in dashboard.DimensionAverages)
        {
            html.Append($"<tr><td>{Encode(dimension.Label)}</td><td>%{dimension.AverageValue * 100:0}</td></tr>");
        }

        html.Append("</table>");

        html.Append("<h2>Öncelikli fırsatlar</h2>")
            .Append("<table><tr><th>#</th><th>Fırsat</th><th>Destek türü</th><th>Skor</th><th>Karar</th><th>Son başvuru</th><th>Eksikler</th></tr>");

        var index = 1;
        foreach (var match in matches)
        {
            html.Append("<tr>")
                .Append($"<td>{index++}</td>")
                .Append($"<td>{Encode(match.OpportunityTitle)}<br/><small>{Encode(match.Publisher)}</small></td>")
                .Append($"<td>{Encode(CategoryLabels.Of(match.SupportCategory))}</td>")
                .Append($"<td>{match.FinalScore:0.0}</td>")
                .Append($"<td>{Encode(VerdictLabels.Of(match.Verdict))}</td>")
                .Append($"<td>{match.Deadline?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-"}</td>")
                .Append($"<td>{match.MissingConditionCount} koşul / {match.MissingMandatoryDocumentCount} belge</td>")
                .Append("</tr>");
        }

        html.Append("</table>");

        return html.ToString();
    }

    private static string Kpi(string label, string value) =>
        $"<span class=\"kpi\">{Encode(label)}<b>{Encode(value)}</b></span>";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}

/// <summary>Destek türü etiketleri.</summary>
public static class CategoryLabels
{
    private static readonly Dictionary<SupportCategory, string> Labels = new()
    {
        [SupportCategory.EmploymentIncentive] = "İstihdam teşviki",
        [SupportCategory.InvestmentIncentive] = "Yatırım teşviki",
        [SupportCategory.Grant] = "Hibe",
        [SupportCategory.RndSupport] = "Ar-Ge desteği",
        [SupportCategory.DigitalTransformation] = "Dijital dönüşüm",
        [SupportCategory.ExportSupport] = "İhracat desteği",
        [SupportCategory.GreenTransformation] = "Yeşil dönüşüm",
        [SupportCategory.Tender] = "Kamu ihalesi",
        [SupportCategory.Loan] = "Kredi / faiz desteği",
        [SupportCategory.Other] = "Diğer"
    };

    public static string Of(SupportCategory category) =>
        Labels.TryGetValue(category, out var label) ? label : category.ToString();
}

/// <summary>Karar etiketleri.</summary>
public static class VerdictLabels
{
    public static string Of(EligibilityVerdict verdict) => verdict switch
    {
        EligibilityVerdict.Eligible => "Uygun",
        EligibilityVerdict.ConditionallyEligible => "Şartlı uygun",
        EligibilityVerdict.NotEligible => "Uygun değil",
        _ => "Belirsiz"
    };
}
