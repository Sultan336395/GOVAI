namespace GovAI.Application.Integrations;

/// <summary>
/// ERP entegrasyonunun <b>şirket seviyesi</b> anlık görüntüsü (Faz 3).
///
/// <para>
/// Sözleşme bilinçli olarak dardır: çalışan adı, T.C. kimlik numarası, bireysel ücret,
/// banka bilgisi ve özel nitelikli kişisel veriler için <b>alan yoktur</b>. "Göndermiyoruz"
/// demek yetmez — alan varsa bir gün dolar. GOVAI'nin skorlaması zaten şirket seviyesinde
/// çalışır ve çalışan kırılımına ihtiyaç duymaz.
/// </para>
///
/// <para>
/// Tip IKPROF'a özel DEĞİLDİR. IKPROF ilk uyarlayıcıdır; sözleşme başka ERP'ler de
/// bağlanabilsin diye kurumdan bağımsız tutulur.
/// </para>
/// </summary>
public sealed record CompanySnapshot
{
    /// <summary>ERP'deki şirket kimliği. Eşlemenin anahtarıdır; <b>şirket oluşturmaz</b>.</summary>
    public required string ExternalCompanyId { get; init; }

    /// <summary>
    /// Vergi numarası. Yalnızca <b>doğrulama</b> içindir: eşlemedeki şirketin numarasıyla
    /// tutmuyorsa istek reddedilir. Üzerine yazılmaz — yanlış eşleme, yanlış firmanın
    /// verisini bir başkasının kaydına taşır.
    /// </summary>
    public required string TaxNumber { get; init; }

    public string? LegalName { get; init; }

    /// <summary>Katalogda karşılığı yoksa istek reddedilir (<c>ActivityCatalog</c>).</summary>
    public string? MainSector { get; init; }

    /// <summary>İlki birincil kod sayılır. Katalog dışı ya da sektörle tutmayan kod reddedilir.</summary>
    public IReadOnlyList<string> NaceCodes { get; init; } = [];

    public IReadOnlyList<string> Cities { get; init; } = [];

    /// <summary>Yalnızca kod, ad ve kişi sayısı. Kişi listesi taşınmaz.</summary>
    public IReadOnlyList<DepartmentSnapshot> Departments { get; init; } = [];

    public int? EmployeeCount { get; init; }

    public EmploymentSnapshot? Employment { get; init; }

    public FinancialSnapshot? Financials { get; init; }

    /// <summary>
    /// ERP'de bu görüntünün alındığı an. Eski damgalı görüntü yeniyi <b>ezmez</b>:
    /// kuyruk sırası bozulduğunda geri gitme olur ve profil eskiye döner.
    /// </summary>
    public required DateTimeOffset ObservedAt { get; init; }
}

public sealed record DepartmentSnapshot(string Code, string Name, int EmployeeCount);

/// <summary>Toplu istihdam göstergeleri. Kişi bazlı hiçbir alan yoktur.</summary>
public sealed record EmploymentSnapshot(
    int? WomenEmployeeCount,
    int? YoungEmployeeCount,
    int? DisabledEmployeeCount,
    int? RAndDEmployeeCount);

public sealed record FinancialSnapshot(
    decimal? AnnualRevenue,
    decimal? BalanceSize,
    string? Currency,
    int? FiscalYear);

/// <summary>Anlık görüntünün uygulanma sonucu.</summary>
public enum SnapshotOutcome
{
    /// <summary>Profil güncellendi.</summary>
    Applied = 1,

    /// <summary>Aynı idempotency anahtarı daha önce işlendi; ikinci kayıt açılmadı.</summary>
    Duplicate = 2,

    /// <summary>Bu ERP kimliği bir GOVAI şirketine bağlı değil.</summary>
    LinkNotFound = 3,

    /// <summary>Gövde sözleşmeye uymuyor.</summary>
    Rejected = 4,

    /// <summary>Gelen görüntü kayıtlı olandan eski; yok sayıldı.</summary>
    Stale = 5
}

public sealed record SnapshotResult(SnapshotOutcome Outcome, Guid? CompanyId, string Message);

/// <summary>
/// GOVAI'den ERP'ye giden sinyal. Yalnızca <b>künye ve bağlantı</b> taşır; müşteri
/// verisi gövdede gitmez — ERP kullanıcısı bağlantıya tıklayıp GOVAI'de kendi yetkisiyle
/// görür.
/// </summary>
public sealed record IntegrationSignal(
    Guid SignalId,
    SignalType Type,
    Guid CompanyId,
    string Title,
    string? Summary,
    DateTimeOffset? Deadline,
    string Link,
    DateTimeOffset PublishedAt);

public enum SignalType
{
    RegulatoryChange = 1,
    OpportunityMatch = 2,
    Deadline = 3,
    ComplianceTask = 4,
    Report = 5
}

/// <summary>
/// ERP bağlantısının limanı (port).
///
/// <para>
/// IKPROF ilk uyarlayıcıdır ama tek olmayacak. Bağlantı bu arayüzün arkasında durur;
/// yeni bir ERP eklendiğinde uygulama katmanı değişmez. Gerçek bağlantı kurulana kadar
/// yerini sahte uyarlayıcı doldurur ve bu <b>"bağlantı kuruldu" diye raporlanmaz</b>.
/// </para>
/// </summary>
public interface IErpIntegrationPort
{
    /// <summary>Uyarlayıcının adı ("IKPROF"); denetim kaydına ve loga yazılır.</summary>
    string PartnerName { get; }

    /// <summary>Gerçek bir bağlantı mı, yoksa sahte uyarlayıcı mı?</summary>
    bool IsLive { get; }

    /// <summary>ERP'nin gönderdiği şirket anlık görüntüsünü alır.</summary>
    Task<SnapshotResult> ApplyCompanySnapshotAsync(
        string idempotencyKey,
        CompanySnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>ERP'nin çekeceği sinyaller.</summary>
    Task<IReadOnlyList<IntegrationSignal>> ListSignalsAsync(
        Guid companyId,
        DateTimeOffset? since,
        CancellationToken cancellationToken = default);
}
