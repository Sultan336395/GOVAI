using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Sources;
using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Regulatory;
using GovAI.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// Katalog onarımının veri erişimi (Faz 3).
///
/// <para>
/// Fırsatlar ve mevzuat kayıtları ortak katalogdur; kiracı filtresi <b>yoktur</b>.
/// Bu yüzden sorgular doğrudan kataloğa gider. Bağlı değerlendirmeler ise kiracıya
/// aittir ve filtre atlanır (<c>IgnoreQueryFilters</c>): onarım bir sistem işlemidir,
/// tek bir kiracının gözünden bakamaz.
/// </para>
/// </summary>
public sealed class CatalogRepairRepository(GovAiDbContext context) : ICatalogRepairRepository
{
    public async Task<IReadOnlyList<CatalogRepairCandidate>> ListRepairCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        // Fırsatlar: belge sürümünün başlığı da alınır (yeniden adlandırma için).
        var firsatlar = await (
            from o in context.Opportunities.IgnoreQueryFilters()
            join v in context.Set<SourceDocumentVersion>()
                on o.SourceDocumentId equals v.SourceDocumentId into surumler
            select new
            {
                o.Id,
                o.Title,
                Url = o.SourceUrl,
                Quarantined = o.QuarantineReason != QuarantineReason.None,
                DocumentTitle = surumler
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => v.Title)
                    .FirstOrDefault(),
                AssessmentCount = context.Assessments
                    .IgnoreQueryFilters()
                    .Count(a => a.OpportunityId == o.Id)
            }).ToListAsync(cancellationToken);

        var mevzuat = await (
            from r in context.RegulatoryChanges
            join v in context.Set<SourceDocumentVersion>() on r.DocumentVersionId equals v.Id into surumler
            from v in surumler.DefaultIfEmpty()
            select new
            {
                r.Id,
                r.Title,
                Url = r.OfficialUrl,
                Quarantined = r.Status == RegulatoryChangeStatus.Quarantined,
                DocumentTitle = v != null ? v.Title : null
            }).ToListAsync(cancellationToken);

        return
        [
            .. firsatlar.Select(o => new CatalogRepairCandidate(
                o.Id, CatalogRepairTarget.Opportunity, o.Title, o.Url, o.Quarantined,
                o.DocumentTitle, o.AssessmentCount)),

            // Mevzuat kaydına bağlı değerlendirme yoktur: mevzuata başvurulmaz, skorlanmaz.
            .. mevzuat.Select(r => new CatalogRepairCandidate(
                r.Id, CatalogRepairTarget.RegulatoryChange, r.Title, r.Url, r.Quarantined,
                r.DocumentTitle, 0))
        ];
    }

    public async Task<int> QuarantineAsync(
        CatalogRepairTarget target,
        Guid recordId,
        QuarantineReason reason,
        string? note,
        CancellationToken cancellationToken = default)
    {
        if (target == CatalogRepairTarget.RegulatoryChange)
        {
            var kayit = await context.RegulatoryChanges
                .FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken);

            kayit?.Quarantine(reason, note);
            return 0;
        }

        var firsat = await context.Opportunities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == recordId, cancellationToken);

        if (firsat is null)
        {
            return 0;
        }

        firsat.Quarantine(reason, note);

        // Bağlı değerlendirmeler SİLİNMEZ; yalnızca güncelliğini yitirir. "Üç ay önce
        // bu çağrıya neden uygun görünüyordum" sorusu cevaplanabilir kalır.
        var degerlendirmeler = await context.Assessments
            .IgnoreQueryFilters()
            .Where(a => a.OpportunityId == recordId && a.IsLatest)
            .ToListAsync(cancellationToken);

        foreach (var degerlendirme in degerlendirmeler)
        {
            degerlendirme.Supersede();
        }

        // Aynı fırsata ait DeepTech analizleri de eskir.
        var analizler = await context.AnalysisRuns
            .IgnoreQueryFilters()
            .Where(a => a.TargetId == recordId && a.IsLatest)
            .ToListAsync(cancellationToken);

        foreach (var analiz in analizler)
        {
            analiz.Supersede();
        }

        return degerlendirmeler.Count + analizler.Count;
    }

    public async Task<int> ReleaseAsync(
        CatalogRepairTarget target,
        Guid recordId,
        CancellationToken cancellationToken = default)
    {
        if (target == CatalogRepairTarget.RegulatoryChange)
        {
            var kayit = await context.RegulatoryChanges
                .FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken);

            // Karantinada DEĞİLSE dokunulmaz: aradan başka bir işlem geçmiş olabilir
            // ve geri alma, kendi yapmadığı bir değişikliği bozmamalıdır.
            if (kayit is null || kayit.Status != RegulatoryChangeStatus.Quarantined)
            {
                return 0;
            }

            kayit.ReleaseFromQuarantine();
            return 1;
        }

        var firsat = await context.Opportunities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == recordId, cancellationToken);

        if (firsat is null || firsat.IsPublishable)
        {
            return 0;
        }

        firsat.ReleaseFromQuarantine();

        // Değerlendirmeler ve analizler GERİ GETİRİLMEZ: onarım onları silmedi,
        // "yeniden değerlendirilmeli" olarak işaretledi. Doğru olan yeniden
        // hesaplanmalarıdır; eski sonucu diriltmek yanlış veriyi geri getirirdi.
        return 1;
    }

    public async Task<int> RetitleAsync(
        CatalogRepairTarget target,
        Guid recordId,
        string title,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (target == CatalogRepairTarget.RegulatoryChange)
        {
            var kayit = await context.RegulatoryChanges
                .FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken);

            kayit?.Retitle(title, now);
            return kayit is null ? 0 : 1;
        }

        var firsat = await context.Opportunities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == recordId, cancellationToken);

        firsat?.RefreshTitle(title);
        return firsat is null ? 0 : 1;
    }
}
