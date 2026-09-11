using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Calibration;
using GovAI.Domain.Calibration;
using GovAI.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// Uzman değerlendirmelerinin veri erişimi.
///
/// <para>
/// Kayıtlar kiracıya aittir ve global kiracı süzgecine tabidir; burada ayrıca filtre
/// yazılmaz. Silme yöntemi bilerek yoktur.
/// </para>
/// </summary>
public sealed class ExpertVerdictRepository(GovAiDbContext context) : IExpertVerdictRepository
{
    public Task<ExpertVerdict?> GetByAssessmentAsync(
        Guid assessmentId,
        VerdictSource source,
        CancellationToken cancellationToken = default) =>
        context.ExpertVerdicts.FirstOrDefaultAsync(
            v => v.AssessmentId == assessmentId && v.Source == source, cancellationToken);

    public async Task<IReadOnlySet<Guid>> ListAiReviewedAssessmentsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        (await context.ExpertVerdicts
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.Source == VerdictSource.Ai)
            .Select(v => v.AssessmentId)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    public async Task<IReadOnlyList<ExpertVerdictDto>> ListForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        // Fırsat başlığı kayıtla birlikte okunur; liste ekranı N+1 sorgu yapmasın.
        var satirlar = await context.ExpertVerdicts
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Join(
                context.Opportunities.AsNoTracking().IgnoreQueryFilters(),
                v => v.OpportunityId,
                o => o.Id,
                (v, o) => new { Verdict = v, o.Title })
            .OrderByDescending(x => x.Verdict.RecordedAt)
            .ToListAsync(cancellationToken);

        return satirlar
            .Select(x => new ExpertVerdictDto(
                x.Verdict.Id,
                x.Verdict.AssessmentId,
                x.Verdict.OpportunityId,
                x.Title,
                x.Verdict.SystemVerdict,
                x.Verdict.SystemScore,
                x.Verdict.SystemHadDataGap,
                x.Verdict.ExpertOpinion,
                x.Verdict.DisagreementReason,
                x.Verdict.Note,
                x.Verdict.RecordedAt,
                x.Verdict.RecordedBy,
                x.Verdict.Source,
                x.Verdict.ReviewerModel,
                x.Verdict.AiConfidence,
                x.Verdict.Agrees,
                x.Verdict.IsFalsePositive,
                x.Verdict.IsFalseNegative))
            .ToList();
    }

    public async Task<IReadOnlyList<ExpertVerdict>> ListForCalibrationAsync(
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        var sorgu = context.ExpertVerdicts.AsNoTracking();

        if (companyId is { } id)
        {
            sorgu = sorgu.Where(v => v.CompanyId == id);
        }

        return await sorgu.ToListAsync(cancellationToken);
    }

    public async Task<RuleExtractionQualityDto> GetRuleExtractionQualityAsync(
        CancellationToken cancellationToken = default)
    {
        // Kural kataloğu ortaktır, kiracıya ait değildir: ölçüm tüm katalog üzerinden
        // yapılır, çünkü kural çıkarımının başarısı da kiracıdan bağımsızdır.
        var toplam = await context.Set<OpportunityRule>().CountAsync(cancellationToken);

        var duzeltilen = await context.Set<OpportunityRule>()
            .CountAsync(r => r.IsManuallyOverridden, cancellationToken);

        var oran = toplam == 0 ? 0m : Math.Round((decimal)duzeltilen / toplam, 4);

        return new RuleExtractionQualityDto(toplam, duzeltilen, oran);
    }

    public async Task AddAsync(ExpertVerdict verdict, CancellationToken cancellationToken = default) =>
        await context.ExpertVerdicts.AddAsync(verdict, cancellationToken);
}
