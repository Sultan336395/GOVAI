using GovAI.Domain.Common;
using GovAI.Domain.Companies;

namespace GovAI.Application.Companies;

public sealed record CompanySummaryDto(
    Guid Id,
    string LegalName,
    string TaxNumber,
    LegalType LegalType,
    EnterpriseSize Size,
    string? PrimaryNaceCode,
    int EmployeeCount,
    decimal AnnualRevenue,
    DateTimeOffset? LastSyncedAt,
    int ProfileVersion);

public sealed record CompanyDetailDto(
    Guid Id,
    string LegalName,
    string TaxNumber,
    LegalType LegalType,
    EnterpriseSize Size,
    DateOnly? FoundedOn,
    WorkforceDto Workforce,
    FinancialsDto Financials,
    bool ExportFlag,
    bool TechnologyFlag,
    int PreviousSuccessfulApplications,
    IReadOnlyList<NaceCodeDto> NaceCodes,
    IReadOnlyList<LocationDto> Locations,
    IReadOnlyList<CertificateDto> Certificates,
    IReadOnlyList<InvestmentDto> ActiveInvestments,
    DateTimeOffset? LastSyncedAt,
    int ProfileVersion,
    decimal ProfileCompleteness);

/// <summary>
/// Personel yapısı.
///
/// <para>
/// Kırılım alanları <b>boş bırakılabilir</b> ve boş olmak "sıfır" demek değildir:
/// <c>null</c> beyan edilmedi, <c>0</c> ise firmanın "yok" beyanıdır. API cevabında
/// beyan edilmemiş alan hiç görünmez (null alanlar yazılmaz), böylece istemci
/// "girilmemiş" ile "sıfır"ı ayırt edebilir.
/// </para>
/// </summary>
public sealed record WorkforceDto(
    int EmployeeCount,
    int? WomenEmployeeCount,
    int? YoungEmployeeCount,
    int? RAndDEmployeeCount,
    int? DisabledEmployeeCount,
    /// <summary>Firmanın genç çalışan sayarken kullandığı azami yaş.</summary>
    int? YoungEmployeeMaxAge = null)
{
    public Workforce ToDomain() => new(
        EmployeeCount,
        WomenEmployeeCount,
        YoungEmployeeCount,
        RAndDEmployeeCount,
        DisabledEmployeeCount,
        YoungEmployeeMaxAge);

    public static WorkforceDto FromDomain(Workforce workforce) => new(
        workforce.EmployeeCount,
        workforce.WomenEmployeeCount,
        workforce.YoungEmployeeCount,
        workforce.RAndDEmployeeCount,
        workforce.DisabledEmployeeCount,
        workforce.YoungEmployeeMaxAge);
}

public sealed record FinancialsDto(
    decimal AnnualRevenue,
    decimal BalanceSize,
    decimal Equity,
    decimal ExportRevenue,
    string Currency,
    int? FiscalYear)
{
    public Financials ToDomain() => new(AnnualRevenue, BalanceSize, Equity, ExportRevenue, Currency, FiscalYear);

    public static FinancialsDto FromDomain(Financials financials) => new(
        financials.AnnualRevenue,
        financials.BalanceSize,
        financials.Equity,
        financials.ExportRevenue,
        financials.Currency,
        financials.FiscalYear);
}

public sealed record NaceCodeDto(string Code, bool IsPrimary, string? Description);

public sealed record LocationDto(string City, string? District, string? Nuts2Code, bool IsHeadquarters, bool IsInTechnopark);

public sealed record CertificateDto(string Code, string Name, DateOnly? IssuedOn, DateOnly? ValidUntil, string? DocumentUri);

public sealed record InvestmentDto(string Title, SupportCategory RelatedCategory, decimal PlannedBudget, DateOnly? PlannedStart, DateOnly? PlannedEnd);

/// <summary>Firma kartı oluşturma/güncelleme isteği. ERP eşitlemesi de aynı sözleşmeyi kullanır.</summary>
public sealed record UpsertCompanyRequest
{
    public required string LegalName { get; init; }

    public required string TaxNumber { get; init; }

    public LegalType LegalType { get; init; } = LegalType.LimitedCompany;

    public DateOnly? FoundedOn { get; init; }

    public WorkforceDto Workforce { get; init; } = new(0, 0, 0, 0, 0);

    public FinancialsDto Financials { get; init; } = new(0, 0, 0, 0, "TRY", null);

    public bool ExportFlag { get; init; }

    public bool TechnologyFlag { get; init; }

    public int PreviousSuccessfulApplications { get; init; }

    public IReadOnlyList<NaceCodeDto> NaceCodes { get; init; } = [];

    public IReadOnlyList<LocationDto> Locations { get; init; } = [];

    public IReadOnlyList<CertificateDto> Certificates { get; init; } = [];

    public IReadOnlyList<InvestmentDto> ActiveInvestments { get; init; } = [];
}

/// <summary>ERP entegrasyon katmanından gelen kısmi eşitleme paketi (Modül 2).</summary>
public sealed record ErpSyncRequest
{
    public required string TaxNumber { get; init; }

    /// <summary>Kaynak sistem adı (ör. "Logo", "Netsis", "SAP") — audit log'a yazılır.</summary>
    public required string SourceSystem { get; init; }

    public WorkforceDto? Workforce { get; init; }

    public FinancialsDto? Financials { get; init; }

    public IReadOnlyList<NaceCodeDto>? NaceCodes { get; init; }

    public IReadOnlyList<LocationDto>? Locations { get; init; }

    public IReadOnlyList<CertificateDto>? Certificates { get; init; }
}

public sealed record ErpSyncResult(Guid CompanyId, int ProfileVersion, IReadOnlyList<string> UpdatedSections, bool RescoringQueued);
