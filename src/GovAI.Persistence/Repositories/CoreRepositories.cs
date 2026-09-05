using GovAI.Application.Opportunities;
using GovAI.Application.Regulatory;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

public sealed class CompanyRepository(GovAiDbContext context) : ICompanyRepository
{
    public Task<Company?> GetWithDetailsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        context.Companies
            .Include(c => c.NaceCodes)
            .Include(c => c.Locations)
            .Include(c => c.Certificates)
            .Include(c => c.ActiveInvestments)
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

    public Task<Company?> GetByTaxNumberAsync(Guid tenantId, string taxNumber, CancellationToken cancellationToken = default) =>
        context.Companies
            .Include(c => c.NaceCodes)
            .Include(c => c.Locations)
            .Include(c => c.Certificates)
            .Include(c => c.ActiveInvestments)
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.TaxNumber == taxNumber, cancellationToken);

    public async Task<IReadOnlyList<Company>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.Companies
            .Include(c => c.NaceCodes)
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.LegalName)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        context.Companies.CountAsync(c => c.TenantId == tenantId, cancellationToken);

    public async Task AddAsync(Company company, CancellationToken cancellationToken = default) =>
        await context.Companies.AddAsync(company, cancellationToken);

    public void Remove(Company company) => context.Companies.Remove(company);
}

public sealed class OpportunityRepository(GovAiDbContext context) : IOpportunityRepository
{
    public Task<Opportunity?> GetWithRulesAsync(Guid opportunityId, CancellationToken cancellationToken = default) =>
        context.Opportunities
            .Include(o => o.Rules)
            .Include(o => o.DocumentChecklist)
            .FirstOrDefaultAsync(o => o.Id == opportunityId, cancellationToken);

    public async Task<IReadOnlyList<Opportunity>> ListForEvaluationAsync(
        DateTimeOffset asOf,
        IReadOnlyCollection<SupportCategory>? categories,
        CancellationToken cancellationToken = default)
    {
        var query = context.Opportunities
            .Include(o => o.Rules)
            .Include(o => o.DocumentChecklist)
            // Karantinadaki kayıt SKORLANMAZ: eşleşmeye girmez, bildirim üretmez ve
            // haftalık rapora yazılmaz (Faz 2 veri kalitesi kuralı).
            .Where(o => o.QuarantineReason == QuarantineReason.None)
            .Where(o => o.Deadline == null || o.Deadline >= asOf);

        if (categories is { Count: > 0 })
        {
            query = query.Where(o => categories.Contains(o.SupportCategory));
        }

        return await query
            .OrderBy(o => o.Deadline ?? DateTimeOffset.MaxValue)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<Opportunity>> SearchAsync(OpportunityQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.Opportunities
            .Include(o => o.Rules)
            .Include(o => o.DocumentChecklist)
            // Karantinadaki kayıt fırsat kataloğunda GÖRÜNMEZ.
            .Where(o => o.QuarantineReason == QuarantineReason.None)
            .AsQueryable();

        if (query.OnlyOpen)
        {
            var now = DateTimeOffset.UtcNow;
            source = source.Where(o => o.Deadline == null || o.Deadline >= now);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = $"%{query.SearchTerm.Trim()}%";
            source = source.Where(o =>
                EF.Functions.ILike(o.Title, term) ||
                EF.Functions.ILike(o.Publisher, term) ||
                (o.Summary != null && EF.Functions.ILike(o.Summary, term)));
        }

        if (query.Categories is { Count: > 0 })
        {
            source = source.Where(o => query.Categories.Contains(o.SupportCategory));
        }

        if (query.SourceTypes is { Count: > 0 })
        {
            source = source.Where(o => query.SourceTypes.Contains(o.SourceType));
        }

        if (query.PublishedAfter is not null)
        {
            source = source.Where(o => o.PublishedAt >= query.PublishedAfter);
        }

        if (query.DeadlineBefore is not null)
        {
            source = source.Where(o => o.Deadline != null && o.Deadline <= query.DeadlineBefore);
        }

        if (query.OnlyReviewed is not null)
        {
            source = source.Where(o => o.IsReviewedByConsultant == query.OnlyReviewed);
        }

        var total = await source.CountAsync(cancellationToken);

        source = query.Sort switch
        {
            OpportunitySort.PublishedDescending => source.OrderByDescending(o => o.PublishedAt),
            OpportunitySort.TitleAscending => source.OrderBy(o => o.Title),
            _ => source.OrderBy(o => o.Deadline ?? DateTimeOffset.MaxValue)
        };

        var items = await source
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        return new PagedResult<Opportunity>(items, total, query.Page, query.PageSize);
    }

    public Task<Opportunity?> GetBySourceDocumentAsync(Guid sourceDocumentId, CancellationToken cancellationToken = default) =>
        context.Opportunities
            .Include(o => o.Rules)
            .Include(o => o.DocumentChecklist)
            .FirstOrDefaultAsync(o => o.SourceDocumentId == sourceDocumentId, cancellationToken);

    public async Task AddAsync(Opportunity opportunity, CancellationToken cancellationToken = default) =>
        await context.Opportunities.AddAsync(opportunity, cancellationToken);

    public void Remove(Opportunity opportunity) => context.Opportunities.Remove(opportunity);

    public async Task<OpportunityProvenanceDto?> GetProvenanceAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await context.Opportunities
            .Where(o => o.Id == opportunityId)
            .Select(o => new { o.SourceId, o.SourceDocumentId, o.SourceUrl })
            .FirstOrDefaultAsync(cancellationToken);

        if (opportunity is null)
        {
            return null;
        }

        var source = await context.Sources
            .Where(s => s.Id == opportunity.SourceId)
            .Select(s => new
            {
                s.Id, s.Name, s.Category, s.ConfigurationVerified, s.ConfigurationVerifiedAt,
                s.Health, Domain = s.Profile.OfficialDomain,
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Resmî bağlantı DOĞRULANMADAN gösterilmez. Adres kaynağın resmî alan adında
        // değilse düğme hiç çıkmaz ve sebebi kayıtta durur.
        var link = OfficialLink.Verify(opportunity.SourceUrl, source?.Domain);

        // Belgenin EN SON sürümü; kanıt parçaları o sürümden gelir.
        var version = opportunity.SourceDocumentId is null
            ? null
            : await context.SourceDocumentVersions
                .Include(v => v.Chunks)
                .Where(v => v.SourceDocumentId == opportunity.SourceDocumentId)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync(cancellationToken);

        var contentHash = opportunity.SourceDocumentId is null
            ? null
            : await context.SourceDocuments
                .Where(d => d.Id == opportunity.SourceDocumentId)
                .Select(d => d.ContentHash)
                .FirstOrDefaultAsync(cancellationToken);

        var evidence = version?.Chunks
            .OrderBy(c => c.SequenceNumber)
            .Select(c => new EvidenceChunkDto(
                c.SequenceNumber, c.PageNumber, c.SectionTitle, c.ParagraphNumber,
                c.Text, c.StartOffset, c.EndOffset, c.TextHash))
            .ToList() ?? [];

        return new OpportunityProvenanceDto(
            SourceId: opportunity.SourceId,
            SourceName: source?.Name ?? "(bilinmiyor)",
            SourceCategory: source?.Category ?? SourceCategory.Uncategorized,
            SourceVerified: source?.ConfigurationVerified ?? false,
            SourceVerifiedAt: source?.ConfigurationVerifiedAt,
            SourceHealth: source?.Health ?? SourceHealth.Unverified,
            OfficialDomain: source?.Domain,
            OfficialUrl: link.Url,
            OfficialUrlRejectionReason: link.RejectionReason,
            DocumentId: opportunity.SourceDocumentId,
            DocumentVersion: version?.VersionNumber,
            CanonicalUrl: version?.CanonicalUrl,
            ContentHash: contentHash,
            NormalizedTextHash: version?.NormalizedTextHash,
            Charset: version?.Charset,
            MediaType: version?.MediaType,
            RetrievedAt: version?.RetrievedAt,
            ParseStatus: version?.ParseStatus,
            RequiresOcr: version?.RequiresOcr ?? false,
            PageCount: version?.PageCount,
            ParseError: version?.ParseError,
            Evidence: evidence);
    }
}

public sealed class SourceRepository(GovAiDbContext context) : ISourceRepository
{
    public Task<Source?> GetAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        context.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);

    public async Task<IReadOnlyList<Source>> ListAsync(bool onlyEnabled, CancellationToken cancellationToken = default)
    {
        var query = context.Sources.AsQueryable();

        if (onlyEnabled)
        {
            query = query.Where(s => s.IsEnabled);
        }

        return await query.OrderBy(s => s.Name).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Source source, CancellationToken cancellationToken = default) =>
        await context.Sources.AddAsync(source, cancellationToken);

    public void Remove(Source source) => context.Sources.Remove(source);
}

public sealed class SourceDocumentRepository(GovAiDbContext context) : ISourceDocumentRepository
{
    public Task<SourceDocument?> GetAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        context.SourceDocuments.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

    public Task<SourceDocument?> GetByUrlAsync(Guid sourceId, string url, CancellationToken cancellationToken = default) =>
        context.SourceDocuments
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.SourceId == sourceId && d.Url == url, cancellationToken);

    public Task<SourceDocument?> GetWithVersionsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        context.SourceDocuments
            .Include(d => d.Versions)
            .ThenInclude(v => v.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

    public async Task<IReadOnlyList<SourceDocument>> ListPendingAsync(int take, CancellationToken cancellationToken = default) =>
        await context.SourceDocuments
            .Where(d => d.Status == DocumentProcessingStatus.Raw || d.Status == DocumentProcessingStatus.Parsed)
            .OrderBy(d => d.CollectedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(SourceDocument document, CancellationToken cancellationToken = default) =>
        await context.SourceDocuments.AddAsync(document, cancellationToken);
}
