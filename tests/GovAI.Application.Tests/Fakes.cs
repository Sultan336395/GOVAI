using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Assessments;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Identity;
using GovAI.Domain.Opportunities;

namespace GovAI.Application.Tests;

/// <summary>Sabit zaman; skorların test içinde tekrarlanabilir olmasını sağlar.</summary>
internal sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = now;
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; init; } = Guid.CreateVersion7();

    public Guid? TenantId { get; init; } = Guid.CreateVersion7();

    public Guid? ActiveCompanyId { get; init; }

    public string? Email => "test@govai.local";

    public UserRole? Role => UserRole.CompanyManager;

    public string? IpAddress => "127.0.0.1";

    public string? UserAgent => "tests";

    public string? CorrelationId => "test-correlation";

    public bool IsAuthenticated => true;

    public HashSet<Guid>? AllowedCompanies { get; init; }

    public bool CanAccessCompany(Guid companyId) => AllowedCompanies is null || AllowedCompanies.Contains(companyId);
}

/// <summary>
/// Bellek içi üyelik deposu. Faz 1'de şirket erişimi UserCompany üzerinden
/// doğrulandığı için servis testlerinin de üyelik kurması gerekir.
/// </summary>
internal sealed class FakeUserCompanyRepository : IUserCompanyRepository
{
    private readonly List<UserCompany> _memberships = [];

    public void Seed(Guid tenantId, Guid userId, Guid companyId, CompanyRole role = CompanyRole.CompanyOwner) =>
        _memberships.Add(new UserCompany(tenantId, userId, companyId, role, isDefault: _memberships.Count == 0));

    public Task<UserCompany?> GetAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_memberships.FirstOrDefault(m => m.UserId == userId && m.CompanyId == companyId));

    public Task<UserCompany?> GetByIdAsync(Guid membershipId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_memberships.FirstOrDefault(m => m.Id == membershipId));

    public Task<IReadOnlyList<UserCompany>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserCompany>>(_memberships.Where(m => m.UserId == userId).ToList());

    public Task<IReadOnlyList<UserCompany>> ListForUserAtLoginAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserCompany>>(
            _memberships.Where(m => m.UserId == userId && m.TenantId == tenantId && !m.IsDeleted).ToList());

    public Task<IReadOnlyList<UserCompany>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserCompany>>(_memberships.Where(m => m.CompanyId == companyId).ToList());

    public Task<int> CountActiveOwnersAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_memberships.Count(m =>
            m.CompanyId == companyId && m.IsActive && m.CompanyRole == CompanyRole.CompanyOwner));

    public Task AddAsync(UserCompany membership, CancellationToken cancellationToken = default)
    {
        _memberships.Add(membership);
        return Task.CompletedTask;
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(0);
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) =>
        operation(cancellationToken);
}

internal sealed class FakeCompanyRepository : ICompanyRepository
{
    private readonly Dictionary<Guid, Company> _companies = [];

    public void Seed(Company company) => _companies[company.Id] = company;

    public Task<Company?> GetWithDetailsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_companies.GetValueOrDefault(companyId));

    public Task<Company?> GetByTaxNumberAsync(Guid tenantId, string taxNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(_companies.Values.FirstOrDefault(c => c.TenantId == tenantId && c.TaxNumber == taxNumber));

    public Task<IReadOnlyList<Company>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Company>>(_companies.Values.Where(c => c.TenantId == tenantId).ToList());

    public Task<int> CountAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_companies.Values.Count(c => c.TenantId == tenantId));

    public Task AddAsync(Company company, CancellationToken cancellationToken = default)
    {
        _companies[company.Id] = company;
        return Task.CompletedTask;
    }

    public void Remove(Company company) => _companies.Remove(company.Id);
}

internal sealed class FakeOpportunityRepository : IOpportunityRepository
{
    private readonly List<Opportunity> _opportunities = [];

    public void Seed(Opportunity opportunity) => _opportunities.Add(opportunity);

    public Task<Opportunity?> GetWithRulesAsync(Guid opportunityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_opportunities.FirstOrDefault(o => o.Id == opportunityId));

    public Task<IReadOnlyList<Opportunity>> ListForEvaluationAsync(
        DateTimeOffset asOf,
        IReadOnlyCollection<SupportCategory>? categories,
        CancellationToken cancellationToken = default)
    {
        var items = _opportunities
            .Where(o => o.IsOpenOn(asOf))
            .Where(o => categories is null || categories.Count == 0 || categories.Contains(o.SupportCategory))
            .ToList();

        return Task.FromResult<IReadOnlyList<Opportunity>>(items);
    }

    public Task<PagedResult<Opportunity>> SearchAsync(OpportunityQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<Opportunity>(_opportunities, _opportunities.Count, query.Page, query.PageSize));

    public Task<Opportunity?> GetBySourceDocumentAsync(Guid sourceDocumentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_opportunities.FirstOrDefault(o => o.SourceDocumentId == sourceDocumentId));

    public Task AddAsync(Opportunity opportunity, CancellationToken cancellationToken = default)
    {
        _opportunities.Add(opportunity);
        return Task.CompletedTask;
    }

    public void Remove(Opportunity opportunity) => _opportunities.Remove(opportunity);
}

internal sealed class FakeScenarioRepository : IScenarioSimulationRepository
{
    public List<ScenarioSimulation> Saved { get; } = [];

    public Task<ScenarioSimulation?> GetWithImpactsAsync(Guid simulationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.FirstOrDefault(s => s.Id == simulationId));

    public Task<IReadOnlyList<ScenarioSimulation>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScenarioSimulation>>(Saved.Where(s => s.CompanyId == companyId).ToList());

    public Task AddAsync(ScenarioSimulation simulation, CancellationToken cancellationToken = default)
    {
        Saved.Add(simulation);
        return Task.CompletedTask;
    }
}
