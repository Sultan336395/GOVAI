using GovAI.Domain.Analysis;
using GovAI.Domain.Assessments;
using GovAI.Domain.Auditing;
using GovAI.Domain.Companies;
using GovAI.Application.Opportunities;
using GovAI.Domain.Common;
using GovAI.Domain.Identity;
using GovAI.Domain.Maintenance;
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

    /// <summary>
    /// Kural–kanıt bağlantılarının gösterim bilgisi: parça metni, özeti, belge sürüm
    /// numarası ve resmî adres.
    ///
    /// <para>
    /// Ayrı sorgu, çünkü bu bilgi kanıt satırında <b>saklanmaz</b>. Metni kopyalamak,
    /// belge yeniden ayrıştırıldığında iki farklı doğruluk kaynağı yaratırdı.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<Guid, RuleEvidenceContext>> GetRuleEvidenceContextAsync(
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

    /// <summary>Etki analizi için kaydın kendisi; DTO değil, kural motoruna verilecek varlık.</summary>
    Task<RegulatoryChange?> GetAsync(Guid changeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kaydın dayandığı belge sürümünün kanıt parçaları.
    ///
    /// <para>
    /// Etki analizi belgede <b>gerçekten yazan</b> ifadeleri arar; kanıt parçası
    /// olmadan yükümlülük üretilemez. Sürüm kimliğiyle okunur ki belge güncellendiğinde
    /// eski analiz eski kanıta bağlı kalsın.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<DocumentEvidenceChunk>> ListEvidenceAsync(
        Guid documentVersionId,
        CancellationToken cancellationToken = default);
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

/// <summary>
/// Analiz çalıştırmalarının deposu (Faz 3).
///
/// <para>
/// Eski analizler silinmez; yeni analiz üretildiğinde eskisinin "güncel" işareti
/// kalkar. Mükerrer mesaj koruması idempotency anahtarı üzerinden yapılır.
/// </para>
/// </summary>
public interface IAnalysisRunRepository
{
    /// <summary>Aynı sürümlerle üretilmiş tamamlanmış analiz; varsa yenisi üretilmez.</summary>
    Task<AnalysisRun?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AnalysisRun?> GetLatestAsync(
        AnalysisKind kind,
        Guid companyId,
        Guid targetId,
        CancellationToken cancellationToken = default);

    /// <summary>Bir hedefin (fırsat/mevzuat) tüm güncel analizleri; sürüm değişince geçersizlenir.</summary>
    Task<IReadOnlyList<AnalysisRun>> ListLatestForTargetAsync(
        Guid targetId,
        CancellationToken cancellationToken = default);

    /// <summary>Bir firmanın tüm güncel analizleri; profil değişince geçersizlenir.</summary>
    Task<IReadOnlyList<AnalysisRun>> ListLatestForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task AddAsync(AnalysisRun run, CancellationToken cancellationToken = default);
}

/// <summary>Onarım adaylarından biri: fırsat ya da mevzuat kaydı.</summary>
public sealed record CatalogRepairCandidate(
    Guid Id,
    CatalogRepairTarget Target,
    string Title,
    string? OfficialUrl,
    bool IsQuarantined,

    /// <summary>Kaydın dayandığı belge sürümündeki gerçek başlık; yoksa <c>null</c>.</summary>
    string? DocumentTitle,

    /// <summary>Bu kayda bağlı değerlendirme sayısı; karantinada yeniden değerlendirmeye düşer.</summary>
    int AssessmentCount);

/// <summary>
/// Katalog onarımının veri erişimi (Faz 3).
///
/// <para>
/// Ayrı arayüz, çünkü onarım hem fırsat hem mevzuat kataloğuna dokunuyor ve ikisini
/// tek sorguda birleştirmesi gerekiyor. Mevcut depoların hiçbirine ait değil.
/// </para>
/// </summary>
public interface ICatalogRepairRepository
{
    /// <summary>Karantinada olsun olmasın tüm onarım adayları; eşleştirme serviste yapılır.</summary>
    Task<IReadOnlyList<CatalogRepairCandidate>> ListRepairCandidatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kaydı karantinaya alır ve bağlı değerlendirmeleri "yeniden değerlendirilmeli"
    /// olarak işaretler. Değerlendirmeler SİLİNMEZ. Etkilenen sayıyı döner.
    /// </summary>
    Task<int> QuarantineAsync(
        CatalogRepairTarget target,
        Guid recordId,
        QuarantineReason reason,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kaydı karantinadan çıkarır (geri alma). Kayıt karantinada değilse hiçbir şey
    /// yapmaz ve <c>0</c> döner: geri alma, kendi yapmadığı bir değişikliği bozmaz.
    /// </summary>
    Task<int> ReleaseAsync(
        CatalogRepairTarget target,
        Guid recordId,
        CancellationToken cancellationToken = default);

    /// <summary>Kaydın başlığını belge sürümündeki gerçek başlıkla düzeltir.</summary>
    Task<int> RetitleAsync(
        CatalogRepairTarget target,
        Guid recordId,
        string title,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Mevcut fırsat kayıtlarına geriye dönük kanıt bağlamanın veri erişimi (Faz 3).
///
/// <para>
/// Fırsat, kaynağı, belgesi ve belgenin son sürümü tek bir bağlamda gerekir; ayrı
/// depolardan toplamak her kayıt için dört ayrı gidiş-dönüş demekti. Katalog ortaktır,
/// kiracı filtresi yoktur.
/// </para>
/// </summary>
public interface IRuleEvidenceBackfillRepository
{
    /// <summary>
    /// Kimliğe göre sıralı adaylar. <paramref name="afterOpportunityId"/> imleçtir:
    /// işlem kesilirse kaldığı yerden devam edilir.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListCandidateIdsAsync(
        Guid? afterOpportunityId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Bu imleçten sonra işlenecek başka kayıt var mı?</summary>
    Task<bool> HasMoreAsync(Guid afterOpportunityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fırsatı kuralları ve <b>mevcut kanıt bağlantılarıyla</b>, dayandığı belgeyi son
    /// sürümü ve parçalarıyla yükler. Kanıtlar yüklenmezse ikinci koşu mükerrer bağ
    /// kurmaya çalışırdı.
    /// </summary>
    Task<RuleEvidenceBackfillContext?> LoadContextAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Geri alma: <b>tam olarak verilen</b> (kural, parça, rol) üçlülerine karşılık gelen
    /// bağlantı satırlarını siler ve silinen sayıyı döner. Listede olmayan hiçbir satıra
    /// dokunulmaz; kural, belge, sürüm ve parçalar yerinde kalır.
    /// </summary>
    Task<int> RemoveEvidenceAsync(
        IReadOnlyList<RuleEvidenceLink> links,
        CancellationToken cancellationToken = default);
}

/// <summary>Tek bir fırsatın geriye dönük bağlama bağlamı.</summary>
public sealed record RuleEvidenceBackfillContext(
    Opportunity Opportunity,

    /// <summary>Fırsatın dayandığı belge; elle açılmış kayıtlarda <c>null</c>.</summary>
    SourceDocument? Document,

    /// <summary>Belgenin en yüksek numaralı sürümü, parçalarıyla.</summary>
    SourceDocumentVersion? LatestVersion,

    /// <summary>Belgenin kaynağı; yeniden ayrıştırma mesajı bunu gerektirir.</summary>
    Source? Source);

/// <summary>
/// Uygulanmış bakım işlemlerinin kaydı (Faz 3). Geri alma bu kayıtlardan beslenir.
/// </summary>
public interface IMaintenanceRunRepository
{
    Task<MaintenanceRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>En yeniden eskiye; inceleme ekranı son çalıştırmaları gösterir.</summary>
    Task<IReadOnlyList<MaintenanceRun>> ListRecentAsync(
        int take,
        CancellationToken cancellationToken = default);

    Task AddAsync(MaintenanceRun run, CancellationToken cancellationToken = default);
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

/// <summary>
/// Platform hesabı aktivasyon kayıtları.
///
/// Kiracı sınırına tabi DEĞİLDİR: aktivasyon bağlantısı henüz oturum açmamış bir
/// kişi tarafından, kiracı bağlamı olmadan açılır.
/// </summary>
public interface IPlatformActivationRepository
{
    Task AddAsync(PlatformActivation activation, CancellationToken cancellationToken = default);

    /// <summary>Jeton özetiyle arar. Açık jeton hiçbir zaman sorguya girmez.</summary>
    Task<PlatformActivation?> GetByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>Kullanıcının hâlâ kullanılabilir bağlantıları; yenisi üretilince iptal edilir.</summary>
    Task<IReadOnlyList<PlatformActivation>> ListActiveForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public interface IUserRepository
{
    Task<AppUser?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Herhangi bir kiracının kimliği. Platform hesabı açarken kullanılır: platform
    /// kullanıcıları bir kiracıya ait değildir ama şema kiracı kimliği ister.
    /// </summary>
    Task<Guid?> GetAnyTenantIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Kimliğiyle kullanıcı arar, <b>kiracı sınırını aşarak</b>.
    ///
    /// Yalnızca platform hesabı aktivasyonunda kullanılır: aktivasyon bağlantısını
    /// açan kişinin henüz oturumu ve kiracı bağlamı yoktur, bu yüzden normal okuma
    /// hiçbir kullanıcı bulamaz. Yumuşak silme koşulu yine uygulanır — silinmiş bir
    /// hesap aktive edilemez.
    /// </summary>
    Task<AppUser?> GetForActivationAsync(Guid userId, CancellationToken cancellationToken = default);

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
