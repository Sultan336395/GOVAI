using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Common;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Companies;

/// <summary>
/// Kurumsal Profil Motoru'nun (Modül 3) use-case servisi.
/// MediatR kullanılmaz; controller doğrudan bu servisi çağırır.
/// </summary>
public sealed class CompanyProfileService(
    ICompanyRepository companies,
    IUserCompanyRepository memberships,
    ITenantRepository tenants,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    IEventPublisher events,
    ICacheService cache,
    ILogger<CompanyProfileService> logger)
{
    public async Task<IReadOnlyList<CompanySummaryDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var userId = currentUser.UserId ?? throw new ForbiddenException("İstek bir kullanıcıya bağlı değil.");

        // Liste artık JWT kapsamından değil, veritabanındaki aktif üyeliklerden türetilir.
        var accessible = (await memberships.ListForUserAsync(userId, cancellationToken))
            .Where(uc => uc.GrantsAccess)
            .Select(uc => uc.CompanyId)
            .ToHashSet();

        var items = await companies.ListAsync(tenantId, cancellationToken);

        return items
            .Where(c => accessible.Contains(c.Id))
            .Select(ToSummary)
            .ToList();
    }

    public async Task<CompanyDetailDto> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        // Okuma için üyelik yeterlidir: CompanyViewer da firma kartını görebilmelidir.
        var company = await LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);
        return ToDetail(company);
    }

    public async Task<CompanyDetailDto> CreateAsync(UpsertCompanyRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await EnsureCompanyQuotaAsync(tenantId, cancellationToken);

        var existing = await companies.GetByTaxNumberAsync(tenantId, request.TaxNumber, cancellationToken);
        if (existing is not null)
        {
            throw new ValidationException(nameof(request.TaxNumber), "Bu vergi numarasıyla kayıtlı bir firma zaten var.");
        }

        var company = new Company(tenantId, request.LegalName, request.TaxNumber, request.LegalType);
        Apply(company, request);

        await companies.AddAsync(company, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Firma kartı oluşturuldu. CompanyId={CompanyId} TenantId={TenantId}", company.Id, tenantId);
        await InvalidateAsync(company.Id, cancellationToken);

        return ToDetail(company);
    }

    public async Task<CompanyDetailDto> UpdateAsync(Guid companyId, UpsertCompanyRequest request, CancellationToken cancellationToken = default)
    {
        var company = await LoadAccessibleAsync(companyId, cancellationToken: cancellationToken);
        var previousVersion = company.ProfileVersion;

        Apply(company, request);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await InvalidateAsync(company.Id, cancellationToken);

        // Profil değiştiyse tüm skorlar bayatlar; yeniden hesaplama kuyruğa bırakılır.
        if (company.ProfileVersion != previousVersion)
        {
            await QueueRescoringAsync(company.Id, cancellationToken);
        }

        return ToDetail(company);
    }

    /// <summary>
    /// ERP / İK / muhasebe sisteminden gelen kısmi veriyi firma kartına işler (Modül 2).
    /// Yalnızca gönderilen bölümler güncellenir; gönderilmeyen alanlar korunur.
    /// </summary>
    public async Task<ErpSyncResult> SyncFromErpAsync(ErpSyncRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var company = await companies.GetByTaxNumberAsync(tenantId, request.TaxNumber, cancellationToken)
                      ?? throw new NotFoundException("Firma", request.TaxNumber);

        if (!currentUser.CanAccessCompany(company.Id))
        {
            throw new ForbiddenException("Bu firma üzerinde işlem yetkiniz yok.");
        }

        var previousVersion = company.ProfileVersion;
        var updatedSections = new List<string>();

        if (request.Workforce is not null)
        {
            company.UpdateWorkforce(request.Workforce.ToDomain());
            updatedSections.Add("Workforce");
        }

        if (request.Financials is not null)
        {
            company.UpdateFinancials(request.Financials.ToDomain());
            updatedSections.Add("Financials");
        }

        if (request.NaceCodes is not null)
        {
            company.ReplaceNaceCodes(request.NaceCodes.Select(ToDomain));
            updatedSections.Add("NaceCodes");
        }

        if (request.Locations is not null)
        {
            company.ReplaceLocations(request.Locations.Select(ToDomain));
            updatedSections.Add("Locations");
        }

        if (request.Certificates is not null)
        {
            company.ReplaceCertificates(request.Certificates.Select(ToDomain));
            updatedSections.Add("Certificates");
        }

        company.MarkSynced(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await InvalidateAsync(company.Id, cancellationToken);

        var changed = company.ProfileVersion != previousVersion;
        if (changed)
        {
            await QueueRescoringAsync(company.Id, cancellationToken);
        }

        logger.LogInformation(
            "ERP eşitlemesi tamamlandı. CompanyId={CompanyId} Kaynak={SourceSystem} Bölümler={Sections}",
            company.Id, request.SourceSystem, string.Join(",", updatedSections));

        return new ErpSyncResult(company.Id, company.ProfileVersion, updatedSections, changed);
    }

    public async Task DeleteAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var company = await LoadAccessibleAsync(companyId, cancellationToken: cancellationToken);
        companies.Remove(company);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await InvalidateAsync(companyId, cancellationToken);
    }

    /// <summary>
    /// Profil doluluk oranı (0..1). Eksik alanlar skorların "Unknown" ile sonuçlanmasına yol açtığı için
    /// kullanıcıya doğrudan gösterilir: "profilinizin %62'si dolu, kalan alanlar 14 fırsatı etkiliyor".
    /// </summary>
    public static decimal CalculateCompleteness(Company company)
    {
        var checks = new[]
        {
            company.NaceCodes.Count > 0,
            company.Locations.Count > 0,
            company.Workforce.EmployeeCount > 0,
            company.Financials.AnnualRevenue > 0,
            company.Financials.BalanceSize > 0,
            company.FoundedOn is not null,
            company.Financials.FiscalYear is not null,
            company.Certificates.Count > 0,
            company.ActiveInvestments.Count > 0,
            company.Locations.Any(l => !string.IsNullOrWhiteSpace(l.Nuts2Code))
        };

        return Math.Round((decimal)checks.Count(c => c) / checks.Length, 2);
    }

    private void Apply(Company company, UpsertCompanyRequest request)
    {
        company.UpdateIdentity(request.LegalName, request.LegalType, request.FoundedOn);
        company.UpdateWorkforce(request.Workforce.ToDomain());
        company.UpdateFinancials(request.Financials.ToDomain());
        company.UpdateFlags(request.ExportFlag, request.TechnologyFlag, request.PreviousSuccessfulApplications);
        company.ReplaceNaceCodes(request.NaceCodes.Select(ToDomain));
        company.ReplaceLocations(request.Locations.Select(ToDomain));
        company.ReplaceCertificates(request.Certificates.Select(ToDomain));
        company.ReplaceInvestments(request.ActiveInvestments.Select(ToDomain));
    }

    // Bu servisin özgün kontrolü, tüm servislerin ortak kullandığı CompanyAccessGuard'a taşındı.
    private Task<Company> LoadAccessibleAsync(
        Guid companyId,
        CompanyPermission required = CompanyPermission.ManageProfile,
        CancellationToken cancellationToken = default) =>
        access.LoadAccessibleAsync(companyId, required, cancellationToken);

    private async Task EnsureCompanyQuotaAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetAsync(tenantId, cancellationToken)
                     ?? throw new NotFoundException("Kiracı", tenantId);

        var count = await companies.CountAsync(tenantId, cancellationToken);
        if (count >= tenant.MaxCompanies)
        {
            throw new ValidationException(
                "Plan",
                $"'{tenant.Plan}' paketi en fazla {tenant.MaxCompanies} firma içerir. Paket yükseltmesi gerekiyor.");
        }
    }

    private Guid RequireTenant() => access.RequireTenant();

    private Task InvalidateAsync(Guid companyId, CancellationToken cancellationToken) =>
        cache.RemoveByPrefixAsync($"company:{companyId}", cancellationToken);

    private Task QueueRescoringAsync(Guid companyId, CancellationToken cancellationToken) =>
        events.PublishAsync(
            QueueNames.ScoringRequested,
            new { CompanyId = companyId, RequestedAt = clock.UtcNow, Reason = "CompanyProfileChanged" },
            cancellationToken);

    private static CompanyNaceCode ToDomain(NaceCodeDto dto) => new(dto.Code, dto.IsPrimary, dto.Description);

    private static CompanyLocation ToDomain(LocationDto dto) =>
        new(dto.City, dto.District, dto.Nuts2Code, dto.IsHeadquarters, dto.IsInTechnopark);

    private static CompanyCertificate ToDomain(CertificateDto dto) =>
        new(dto.Code, dto.Name, dto.IssuedOn, dto.ValidUntil, dto.DocumentUri);

    private static CompanyInvestment ToDomain(InvestmentDto dto) =>
        new(dto.Title, dto.RelatedCategory, dto.PlannedBudget, dto.PlannedStart, dto.PlannedEnd);

    private static CompanySummaryDto ToSummary(Company company) => new(
        company.Id,
        company.LegalName,
        company.TaxNumber,
        company.LegalType,
        company.Size,
        company.PrimaryNaceCode,
        company.Workforce.EmployeeCount,
        company.Financials.AnnualRevenue,
        company.LastSyncedAt,
        company.ProfileVersion);

    public static CompanyDetailDto ToDetail(Company company) => new(
        company.Id,
        company.LegalName,
        company.TaxNumber,
        company.LegalType,
        company.Size,
        company.FoundedOn,
        WorkforceDto.FromDomain(company.Workforce),
        FinancialsDto.FromDomain(company.Financials),
        company.ExportFlag,
        company.TechnologyFlag,
        company.PreviousSuccessfulApplications,
        company.NaceCodes.Select(n => new NaceCodeDto(n.Code, n.IsPrimary, n.Description)).ToList(),
        company.Locations.Select(l => new LocationDto(l.City, l.District, l.Nuts2Code, l.IsHeadquarters, l.IsInTechnopark)).ToList(),
        company.Certificates.Select(c => new CertificateDto(c.Code, c.Name, c.IssuedOn, c.ValidUntil, c.DocumentUri)).ToList(),
        company.ActiveInvestments.Select(i => new InvestmentDto(i.Title, i.RelatedCategory, i.PlannedBudget, i.PlannedStart, i.PlannedEnd)).ToList(),
        company.LastSyncedAt,
        company.ProfileVersion,
        CalculateCompleteness(company));
}
