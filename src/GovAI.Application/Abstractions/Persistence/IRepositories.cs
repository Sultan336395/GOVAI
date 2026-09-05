using GovAI.Domain.Assessments;
using GovAI.Domain.Auditing;
using GovAI.Domain.Companies;
using GovAI.Application.Opportunities;
using GovAI.Domain.Common;
using GovAI.Domain.Identity;
using GovAI.Domain.Notifications;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;
using GovAI.Application.Sources;
using GovAI.Domain.Regulatory;
using GovAI.Application.Regulatory;

namespace GovAI.Application.Abstractions.Persistence;

/// <summary>
/// Değişikliklerin tek bir işlemde kalıcılaştırılmasını sağlar.
/// Application katmanı EF Core'u tanımaz; yalnızca bu soyutlamayı çağırır.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}

public interface ICompanyRepository
{
    /// <summary>Firma kartını tüm alt koleksiyonlarıyla (NACE, lokasyon, sertifika, yatırım) yükler.</summary>
    Task<Company?> GetWithDetailsAsync(Guid companyId, CancellationToken cancellationToken = default);

    Task<Company?> GetByTaxNumberAsync(Guid tenantId, string taxNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Company>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task AddAsync(Company company, CancellationToken cancellationToken = default);

    void Remove(Company company);
}

public interface IOpportunityRepository
{
    Task<Opportunity?> GetWithRulesAsync(Guid opportunityId, CancellationToken cancellationToken = default);

    /// <summary>Değerlendirmeye girecek açık çağrıları kurallarıyla birlikte getirir.</summary>
    Task<IReadOnlyList<Opportunity>> ListForEvaluationAsync(
        DateTimeOffset asOf,
        IReadOnlyCollection<SupportCategory>? categories,
        CancellationToken cancellationToken = default);

    Task<PagedResult<Opportunity>> SearchAsync(OpportunityQuery query, CancellationToken cancellationToken = default);

    Task<Opportunity?> GetBySourceDocumentAsync(Guid sourceDocumentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fırsatın dayandığı resmî belgenin künyesini ve kanıt parçalarını getirir.
    ///
    /// Fırsat elle açılmışsa ya da kaynak belgesi yoksa <c>null</c> döner; bu bir hata
    /// değildir, arayüz o zaman kanıt bölümünü göstermez.
    /// </summary>
    Task<OpportunityProvenanceDto?> GetProvenanceAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Opportunity opportunity, CancellationToken cancellationToken = default);

    void Remove(Opportunity opportunity);
}

public interface ISourceRepository
{
    Task<Source?> GetAsync(Guid sourceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Source>> ListAsync(bool onlyEnabled, CancellationToken cancellationToken = default);

    Task AddAsync(Source source, CancellationToken cancellationToken = default);

    void Remove(Source source);
}

/// <summary>Triyaj için gereken, belgeye ait özet bilgi.</summary>
public sealed record TriageCandidate(
    Guid DocumentId,
    Guid SourceId,
    string Title,
    string Url,
    string ContentHash,
    int ContentLength,
    DateTimeOffset CollectedAt,
    int LinkedAssessmentCount);

/// <summary>
/// Karantina ekranlarının okuma sorguları.
///
/// Ayrı bir arayüzdür çünkü bu sorgular birden çok tabloyu birleştirir ve tek bir
/// varlığın deposuna ait değildir.
/// </summary>
public interface IQuarantineQueryRepository
{
    /// <summary>Henüz karantinada olmayan tüm belgeler.</summary>
    Task<IReadOnlyList<TriageCandidate>> ListTriageCandidatesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuarantinedDocumentDto>> ListQuarantinedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Bu belgeye dayanan değerlendirmeleri "yeniden değerlendirilmeli" olarak işaretler.
    /// Kayıtlar SİLİNMEZ; yalnızca güncelliğini yitirdiği bildirilir.
    /// </summary>
    Task<int> MarkAssessmentsForReevaluationAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Belgeden türetilmiş fırsatları da karantinaya alır.
    ///
    /// Belgeyi karantinaya alıp fırsatı bırakmak kaydı katalogda görünür bırakırdı;
    /// karantinanın tek anlamı katalogdan ve skorlamadan çıkmaktır.
    /// </summary>
    Task<int> QuarantineOpportunitiesForDocumentAsync(
        Guid documentId,
        QuarantineReason reason,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>Belge karantinadan çıkınca türev fırsatlar da katalogdaki yerine döner.</summary>
    Task<int> ReleaseOpportunitiesForDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Belgeden türetilmiş mevzuat kayıtlarını da karantinaya alır.
    /// Fırsatla aynı gerekçe: karantina, katalogdan ve panelden çıkmak demektir.
    /// </summary>
    Task<int> QuarantineRegulatoryChangesForDocumentAsync(
        Guid documentId,
        QuarantineReason reason,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>Belge karantinadan çıkınca mevzuat kayıtları yeniden doğrulamayı bekler.</summary>
    Task<int> ReleaseRegulatoryChangesForDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}

/// <summary>Mevzuat kayıtlarının deposu (ortak katalog; kiracıya bağlı değildir).</summary>
public interface IRegulatoryChangeRepository
{
    /// <summary>Aynı sürümden aynı içerik ikinci kez kaydedilmesin diye.</summary>
    Task<bool> ExistsAsync(Guid documentVersionId, string contentHash, CancellationToken cancellationToken = default);

    Task AddAsync(RegulatoryChange change, CancellationToken cancellationToken = default);

    /// <summary>Panelde gösterilebilir kayıtlar: yalnızca doğrulanmış olanlar.</summary>
    Task<IReadOnlyList<RegulatoryChangeSummaryDto>> ListPublishableAsync(
        RegulationDomain? domain,
        string? jurisdiction,
        CancellationToken cancellationToken = default);

    Task<RegulatoryChangeDetailDto?> GetDetailAsync(Guid changeId, CancellationToken cancellationToken = default);
}

public interface ISourceDocumentRepository
{
    Task<SourceDocument?> GetAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<SourceDocument?> GetByUrlAsync(Guid sourceId, string url, CancellationToken cancellationToken = default);

    /// <summary>Belgeyi sürüm zinciriyle birlikte yükler (kanıt yazımı için).</summary>
    Task<SourceDocument?> GetWithVersionsAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceDocument>> ListPendingAsync(int take, CancellationToken cancellationToken = default);

    Task AddAsync(SourceDocument document, CancellationToken cancellationToken = default);
}

public interface IAssessmentRepository
{
    /// <summary>Bir fırsata ait, hâlâ güncel sayılan değerlendirmeler (tüm kiracılar).</summary>
    Task<IReadOnlyList<EligibilityAssessment>> ListLatestForOpportunityAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default);

    Task<EligibilityAssessment?> GetAsync(Guid assessmentId, CancellationToken cancellationToken = default);

    Task<EligibilityAssessment?> GetLatestAsync(Guid companyId, Guid opportunityId, CancellationToken cancellationToken = default);

    /// <summary>Firmanın güncel değerlendirmelerini skora göre azalan sırada getirir.</summary>
    Task<PagedResult<EligibilityAssessment>> ListLatestForCompanyAsync(
        AssessmentQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EligibilityAssessment>> ListLatestForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task AddAsync(EligibilityAssessment assessment, CancellationToken cancellationToken = default);

    /// <summary>Yeni hesaplama öncesi eski kayıtları geçmişe alır.</summary>
    Task SupersedePreviousAsync(Guid companyId, Guid opportunityId, CancellationToken cancellationToken = default);
}

public interface IScenarioSimulationRepository
{
    Task<ScenarioSimulation?> GetWithImpactsAsync(Guid simulationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScenarioSimulation>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);

    Task AddAsync(ScenarioSimulation simulation, CancellationToken cancellationToken = default);
}

public interface INotificationRepository
{
    Task<bool> ExistsAsync(string deduplicationKey, CancellationToken cancellationToken = default);

    Task<Notification?> GetAsync(Guid notificationId, CancellationToken cancellationToken = default);

    Task<PagedResult<Notification>> ListAsync(NotificationQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> ListUnsentAsync(int take, CancellationToken cancellationToken = default);

    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);
}

public interface IUserRepository
{
    Task<AppUser?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// E-posta ile kullanıcı arar. <b>Kiracı sınırını bilinçli olarak aşan tek sorgudur.</b>
    ///
    /// Zorunludur, çünkü:
    /// <list type="bullet">
    ///   <item>Girişte kiracı, kullanıcı bulunmadan bilinemez; jetondaki kiracı buradan doğar.</item>
    ///   <item><c>users.email</c> şemada global benzersizdir. Mükerrer kontrolü de kiracılar
    ///         arası olmak zorundadır; aksi hâlde temiz doğrulama hatası yerine veritabanı
    ///         kısıt ihlali alınır.</item>
    /// </list>
    ///
    /// Yalnızca <c>AuthenticationService.LoginAsync</c> ve <c>CreateUserAsync</c> çağırır.
    /// Bulunan kullanıcı çağırana açılmaz; parola doğrulaması ve jeton üretimi dışında kullanılmaz.
    /// Koruyan testler: <c>Giris_kullaniciyi_kendi_kiracisina_baglar</c>,
    /// <c>Mukerrer_eposta_farkli_kiracida_da_reddedilir</c>.
    /// </summary>
    Task<AppUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppUser>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task AddAsync(AppUser user, CancellationToken cancellationToken = default);
}

public interface ITenantRepository
{
    Task<Tenant?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
}

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);

    Task<PagedResult<AuditLogEntry>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}
