using GovAI.Domain.Assessments;
using GovAI.Domain.Auditing;
using GovAI.Domain.Companies;
using GovAI.Domain.Common;
using GovAI.Domain.Identity;
using GovAI.Domain.Notifications;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;

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

public interface ISourceDocumentRepository
{
    Task<SourceDocument?> GetAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<SourceDocument?> GetByUrlAsync(Guid sourceId, string url, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceDocument>> ListPendingAsync(int take, CancellationToken cancellationToken = default);

    Task AddAsync(SourceDocument document, CancellationToken cancellationToken = default);
}

public interface IAssessmentRepository
{
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
