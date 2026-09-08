using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// Geriye dönük kanıt bağlamanın veri erişimi (Faz 3).
///
/// <para>
/// Fırsat kataloğu ortaktır; kiracı filtresi yoktur. Yine de <c>IgnoreQueryFilters</c>
/// kullanılır: bu bir platform bakım işlemidir ve yumuşak silinmiş ya da filtreye
/// takılan kayıtların da <b>raporda görünmesi</b> gerekir — dokunulmasa bile.
/// </para>
/// </summary>
public sealed class RuleEvidenceBackfillRepository(GovAiDbContext context)
    : IRuleEvidenceBackfillRepository
{
    public async Task<IReadOnlyList<Guid>> ListCandidateIdsAsync(
        Guid? afterOpportunityId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var sirali = await TumKimliklerAsync(cancellationToken);

        return [.. Sonrasi(sirali, afterOpportunityId).Take(take)];
    }

    public async Task<bool> HasMoreAsync(
        Guid afterOpportunityId,
        CancellationToken cancellationToken = default)
    {
        var sirali = await TumKimliklerAsync(cancellationToken);

        return Sonrasi(sirali, afterOpportunityId).Any();
    }

    /// <summary>
    /// Kataloğun tüm fırsat kimlikleri, .NET sıralamasıyla.
    ///
    /// <para>
    /// Sıralama ve imleç karşılaştırması bilerek <b>bellekte</b> yapılır. PostgreSQL'in
    /// <c>uuid</c> sıralaması ile .NET'in <see cref="Guid"/> sıralaması aynı bayt
    /// düzenini kullanmaz; sıralamayı veritabanına, karşılaştırmayı .NET'e bırakmak
    /// imleci kaydırır ve bazı kayıtlar hiç işlenmezdi. Okunan tek şey kimlik
    /// kolonudur — katalog için ucuzdur.
    /// </para>
    /// </summary>
    private async Task<List<Guid>> TumKimliklerAsync(CancellationToken cancellationToken)
    {
        var kimlikler = await context.Opportunities
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        kimlikler.Sort();
        return kimlikler;
    }

    private static IEnumerable<Guid> Sonrasi(List<Guid> sirali, Guid? imlec) =>
        imlec is { } deger
            ? sirali.Where(id => id.CompareTo(deger) > 0)
            : sirali;

    public async Task<RuleEvidenceBackfillContext?> LoadContextAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        // Kanıt bağlantıları DA yüklenir. Yüklenmezse servis "bu kuralın kanıtı yok"
        // sanır ve her koşuda yeniden bağlamaya çalışırdı; işlem idempotent olmazdı.
        var opportunity = await context.Opportunities
            .IgnoreQueryFilters()
            .Include(o => o.Rules)
                .ThenInclude(r => r.Evidence)
            .FirstOrDefaultAsync(o => o.Id == opportunityId, cancellationToken);

        if (opportunity is null)
        {
            return null;
        }

        if (opportunity.SourceDocumentId is not { } documentId)
        {
            return new RuleEvidenceBackfillContext(opportunity, null, null, null);
        }

        var document = await context.SourceDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null)
        {
            return new RuleEvidenceBackfillContext(opportunity, null, null, null);
        }

        // Yalnızca EN SON sürüm ve onun parçaları yüklenir. Tüm sürüm zincirini ham
        // içerikleriyle çekmek, tek kayıt için onlarca megabayt okumak olurdu.
        var version = await context.SourceDocumentVersions
            .Include(v => v.Chunks)
            .Where(v => v.SourceDocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var source = await context.Sources
            .FirstOrDefaultAsync(s => s.Id == document.SourceId, cancellationToken);

        return new RuleEvidenceBackfillContext(opportunity, document, version, source);
    }
}
