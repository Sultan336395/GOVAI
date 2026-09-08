using GovAI.Domain.Common;

namespace GovAI.Domain.Companies;

/// <summary>
/// Kurumsal Profil Motoru'nun (Modül 3) çıktısı olan firma kartı.
/// ERP, İK ve muhasebe sistemlerinden gelen veri bu tek modelde birleştirilir.
/// Teknik dokümandaki <c>CompanyProfile</c> yapısının kalıcı karşılığıdır.
/// </summary>
public class Company : AggregateRoot, IAuditable, ISoftDeletable, ITenantScoped
{
    private readonly List<CompanyLocation> _locations = [];
    private readonly List<CompanyCertificate> _certificates = [];
    private readonly List<CompanyInvestment> _activeInvestments = [];
    private readonly List<CompanyNaceCode> _naceCodes = [];
    private readonly List<AnnualFinancialRecord> _annualFinancials = [];

    private Company()
    {
    }

    public Company(Guid tenantId, string legalName, string taxNumber, LegalType legalType)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(legalName), "Firma unvanı zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(taxNumber), "Vergi numarası zorunludur.");

        TenantId = tenantId;
        LegalName = legalName.Trim();
        TaxNumber = taxNumber.Trim();
        LegalType = legalType;
    }

    public Guid TenantId { get; set; }

    public string LegalName { get; private set; } = string.Empty;

    public string TaxNumber { get; private set; } = string.Empty;

    public LegalType LegalType { get; private set; }

    /// <summary>Kuruluş tarihi; bazı çağrılar asgari faaliyet süresi arar.</summary>
    public DateOnly? FoundedOn { get; private set; }

    public Workforce Workforce { get; private set; } = Workforce.Empty;

    public Financials Financials { get; private set; } = Financials.Empty;

    /// <summary>İhracat yapıyor mu? (<c>exportFlag</c>)</summary>
    public bool ExportFlag { get; private set; }

    /// <summary>Teknopark / Ar-Ge merkezi / teknoloji firması statüsü var mı? (<c>technologyFlag</c>)</summary>
    public bool TechnologyFlag { get; private set; }

    /// <summary>Daha önce kamu destek programına başvurup kabul aldı mı? Geçmiş başvuru kabiliyeti göstergesi.</summary>
    public int PreviousSuccessfulApplications { get; private set; }

    public IReadOnlyCollection<CompanyNaceCode> NaceCodes => _naceCodes.AsReadOnly();

    /// <summary>
    /// Yıl bazlı toplu mali veriler (Faz 3). İsteğe bağlıdır: girilmediğinde mali
    /// kriterler <b>bilinmiyor</b> kalır, firma elenmez.
    /// </summary>
    public IReadOnlyCollection<AnnualFinancialRecord> AnnualFinancials => _annualFinancials.AsReadOnly();

    /// <summary>
    /// Mali verinin kaçıncı sürümü. Profil sürümünden ayrı tutulur: mali veri
    /// güncellendiğinde analiz tazelenmeli, ancak "profil değişti" bildirimleri
    /// tetiklenmemelidir; ikisi farklı olaylardır ve analiz kaydı ikisini de saklar.
    /// </summary>
    public int FinancialDataVersion { get; private set; } = 1;

    public IReadOnlyCollection<CompanyLocation> Locations => _locations.AsReadOnly();

    public IReadOnlyCollection<CompanyCertificate> Certificates => _certificates.AsReadOnly();

    public IReadOnlyCollection<CompanyInvestment> ActiveInvestments => _activeInvestments.AsReadOnly();

    /// <summary>ERP eşitlemesinin en son başarıyla tamamlandığı an.</summary>
    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Profilin kaçıncı sürümü olduğu; her anlamlı değişiklikte artar ve skor yeniden hesaplanır.</summary>
    public int ProfileVersion { get; private set; } = 1;

    // ───────────── Faz 1: grup ve hiyerarşi ─────────────

    /// <summary>Bağlı olduğu şirket grubu. Grup üyeliği ile ana/bağlı ilişkisi ayrı kavramlardır.</summary>
    public Guid? GroupId { get; private set; }

    /// <summary>Hukuki üst şirket. Aynı kiracıda olmak zorundadır; döngü kurulamaz.</summary>
    public Guid? ParentCompanyId { get; private set; }

    public CompanyRelationshipType RelationshipType { get; private set; } = CompanyRelationshipType.Independent;

    /// <summary>Grubun ana şirketi mi.</summary>
    public bool IsHeadCompany { get; private set; }

    /// <summary>Pasif şirket listelerde gizlenir; kayıt izlenebilirlik için silinmez.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Profil doluluğu (0–100). Kullanıcıya "neyi tamamlamalıyım" sinyali verir.</summary>
    public int ProfileCompletionPercentage { get; private set; }

    // ───────────── Faz 1: sicil ve iletişim bilgileri ─────────────

    public CompanyRegistry Registry { get; private set; } = CompanyRegistry.Empty;

    public CompanyContact Contact { get; private set; } = CompanyContact.Empty;

    /// <summary>Ana sektör (serbest metin; NACE'den bağımsız iş dili).</summary>
    public string? MainSector { get; private set; }

    /// <summary>
    /// Alt sektörler ve hedef ülkeler basit metin listeleridir; yabancı anahtar taşımaz
    /// ve tek başlarına sorgulanmaz. Ayrı tablo yerine jsonb tutulur.
    /// </summary>
    public string? SubSectorsJson { get; private set; }

    public string? TargetCountriesJson { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>KOBİ ölçeği; çalışan sayısı ve ciro eşiklerinden türetilir (2003/361/EC uyarlaması).</summary>
    public EnterpriseSize Size => Workforce.EmployeeCount switch
    {
        < 10 => Financials.AnnualRevenue <= 3_000_000m ? EnterpriseSize.Micro : EnterpriseSize.Small,
        < 50 => Financials.AnnualRevenue <= 25_000_000m ? EnterpriseSize.Small : EnterpriseSize.Medium,
        < 250 => EnterpriseSize.Medium,
        _ => EnterpriseSize.Large
    };

    public string? PrimaryNaceCode => _naceCodes.FirstOrDefault(n => n.IsPrimary)?.Code
                                      ?? _naceCodes.FirstOrDefault()?.Code;

    /// <summary>
    /// Sicil bilgileri (kısa ad, vergi dairesi, MERSİS, ticaret sicil).
    /// Bu alanlar kural motoruna girmez; bu yüzden <c>ProfileVersion</c> artırılmaz
    /// ve gereksiz yeniden skorlama tetiklenmez.
    /// </summary>
    public void UpdateRegistry(CompanyRegistry registry) => Registry = registry;

    /// <summary>İletişim ve adres bilgileri.</summary>
    public void UpdateContact(CompanyContact contact) => Contact = contact;

    public void UpdateSectors(string? mainSector, string? subSectorsJson, string? targetCountriesJson)
    {
        MainSector = string.IsNullOrWhiteSpace(mainSector) ? null : mainSector.Trim();
        SubSectorsJson = subSectorsJson;
        TargetCountriesJson = targetCountriesJson;
    }

    /// <summary>
    /// Grup ve ana şirket bağını kurar. Kiracı eşitliği ve döngü kontrolü servis
    /// katmanındadır: burada yalnızca tek kayıtla doğrulanabilen kural uygulanır.
    /// </summary>
    public void SetGroupAndParent(
        Guid? groupId,
        Guid? parentCompanyId,
        CompanyRelationshipType relationshipType,
        bool isHeadCompany)
    {
        DomainException.ThrowIf(parentCompanyId == Id, "Bir şirket kendisinin ana şirketi olamaz.");
        DomainException.ThrowIf(
            isHeadCompany && parentCompanyId is not null,
            "Ana şirketin kendisi başka bir şirkete bağlı olamaz.");
        DomainException.ThrowIf(
            relationshipType != CompanyRelationshipType.Independent
            && relationshipType != CompanyRelationshipType.HeadCompany
            && parentCompanyId is null,
            "Bu ilişki türü için ana şirket seçilmelidir.");

        GroupId = groupId;
        ParentCompanyId = parentCompanyId;
        RelationshipType = relationshipType;
        IsHeadCompany = isHeadCompany;
    }

    public void LeaveGroup()
    {
        GroupId = null;
        ParentCompanyId = null;
        RelationshipType = CompanyRelationshipType.Independent;
        IsHeadCompany = false;
    }

    public void SetProfileCompletion(int percentage) =>
        ProfileCompletionPercentage = Math.Clamp(percentage, 0, 100);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void UpdateIdentity(string legalName, LegalType legalType, DateOnly? foundedOn)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(legalName), "Firma unvanı zorunludur.");
        LegalName = legalName.Trim();
        LegalType = legalType;
        FoundedOn = foundedOn;
        BumpVersion();
    }

    public void UpdateWorkforce(Workforce workforce)
    {
        Workforce = workforce;
        BumpVersion();
    }

    public void UpdateFinancials(Financials financials)
    {
        Financials = financials;
        BumpVersion();
    }

    public void UpdateFlags(bool exportFlag, bool technologyFlag, int previousSuccessfulApplications)
    {
        DomainException.ThrowIf(previousSuccessfulApplications < 0, "Geçmiş başvuru sayısı negatif olamaz.");
        ExportFlag = exportFlag;
        TechnologyFlag = technologyFlag;
        PreviousSuccessfulApplications = previousSuccessfulApplications;
        BumpVersion();
    }

    public void ReplaceNaceCodes(IEnumerable<CompanyNaceCode> codes)
    {
        _naceCodes.Clear();
        _naceCodes.AddRange(codes);
        DomainException.ThrowIf(_naceCodes.Count(c => c.IsPrimary) > 1, "Yalnızca bir NACE kodu birincil olabilir.");
        BumpVersion();
    }

    public void ReplaceLocations(IEnumerable<CompanyLocation> locations)
    {
        _locations.Clear();
        _locations.AddRange(locations);
        BumpVersion();
    }

    public void ReplaceCertificates(IEnumerable<CompanyCertificate> certificates)
    {
        _certificates.Clear();
        _certificates.AddRange(certificates);
        BumpVersion();
    }

    public void ReplaceInvestments(IEnumerable<CompanyInvestment> investments)
    {
        _activeInvestments.Clear();
        _activeInvestments.AddRange(investments);
        BumpVersion();
    }

    /// <summary>
    /// Bir mali yılın toplu verisini yazar veya günceller. Aynı yıl iki kez eklenmez;
    /// eski kayıt güncellenir, geçmiş yıllar korunur.
    /// </summary>
    public void UpsertAnnualFinancials(
        int fiscalYear,
        string currency,
        decimal? annualRevenue,
        decimal? annualIncome,
        decimal? annualExpense,
        decimal? netProfitOrLoss,
        decimal? balanceTotal,
        FinancialDataSource dataSource,
        FinancialVerificationStatus verificationStatus,
        DateTimeOffset updatedAt)
    {
        var existing = _annualFinancials.FirstOrDefault(f => f.FiscalYear == fiscalYear);

        if (existing is null)
        {
            _annualFinancials.Add(new AnnualFinancialRecord(
                fiscalYear, currency, annualRevenue, annualIncome, annualExpense,
                netProfitOrLoss, balanceTotal, dataSource, verificationStatus, updatedAt));
        }
        else
        {
            existing.Update(
                currency, annualRevenue, annualIncome, annualExpense,
                netProfitOrLoss, balanceTotal, dataSource, verificationStatus, updatedAt);
        }

        FinancialDataVersion++;
    }

    /// <summary>En yeni mali yıla ait kayıt; hiç veri yoksa <c>null</c>.</summary>
    public AnnualFinancialRecord? LatestAnnualFinancials() =>
        _annualFinancials.Where(f => !f.IsEmpty).OrderByDescending(f => f.FiscalYear).FirstOrDefault();

    public void MarkSynced(DateTimeOffset syncedAt) => LastSyncedAt = syncedAt;

    /// <summary>Belirtilen tarihte geçerli olan sertifikaların kodlarını döner.</summary>
    public IReadOnlySet<string> ValidCertificateCodes(DateOnly asOf) =>
        _certificates
            .Where(c => c.ValidUntil is null || c.ValidUntil >= asOf)
            .Select(c => c.Code.ToUpperInvariant())
            .ToHashSet();

    private void BumpVersion() => ProfileVersion++;
}
