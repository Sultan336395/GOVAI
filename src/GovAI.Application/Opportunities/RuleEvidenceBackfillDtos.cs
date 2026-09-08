namespace GovAI.Application.Opportunities;

/// <summary>Bir fırsat kaydının geriye dönük kanıt bağlamasındaki sonucu.</summary>
public enum RuleEvidenceBackfillOutcome
{
    /// <summary>Yeni kanıt bağlantısı kuruldu.</summary>
    Bound = 1,

    /// <summary>Kuralların tamamı zaten bağlıydı; hiçbir şey değişmedi (idempotentlik).</summary>
    AlreadyBound = 2,

    /// <summary>
    /// Parçalar var ama hiçbiri kurala güvenilir şekilde bağlanamadı. Kayıt
    /// <b>değiştirilmez</b>; kural deterministik motorda çalışmaya devam eder.
    /// </summary>
    NoEvidenceFound = 3,

    /// <summary>
    /// Ham içerik veritabanında duruyor ama kanıt parçası üretilmemiş. İnternete
    /// çıkmadan, saklanan içerikten yeniden ayrıştırılabilir.
    /// </summary>
    NeedsReparse = 4,

    /// <summary>
    /// Ham içerik yok. Kayda <b>dokunulmaz</b>; yalnızca "yeniden indirme gerekli"
    /// olarak raporlanır. Yeniden indirme bu işlemin işi değildir.
    /// </summary>
    NeedsRedownload = 5,

    /// <summary>Fırsat ya da belge karantinada; karantinadaki içerik kanıt olamaz.</summary>
    SkippedQuarantined = 6,

    /// <summary>Kaynağın yapılandırması doğrulanmamış; kanıtın hangi kurumdan geldiği güvenilir değil.</summary>
    SkippedUnverifiedSource = 7,

    /// <summary>Kayıt elle açılmış; dayandığı resmî belge yok.</summary>
    NoSourceDocument = 8,

    /// <summary>Kuralı olmayan fırsat. Bağlanacak bir şey yok.</summary>
    NoRules = 9,

    /// <summary>Beklenmeyen hata. Diğer kayıtlar etkilenmez.</summary>
    Failed = 10
}

/// <summary>Toplu işlemin isteği. Sayfalama, işlemin kaldığı yerden sürmesini sağlar.</summary>
/// <param name="AfterOpportunityId">
/// Bu kimlikten sonraki kayıtlardan devam et. İşlem kesilirse önceki raporun
/// <c>NextCursor</c> değeri buraya verilir.
/// </param>
/// <param name="BatchSize">Bir turda işlenecek azami fırsat sayısı.</param>
public sealed record RuleEvidenceBackfillRequest(
    Guid? AfterOpportunityId = null,
    int BatchSize = 100)
{
    public const int MaximumBatchSize = 500;

    /// <summary>Sınırlar içine çeker; sıfır ya da devasa bir tur istenmesini engeller.</summary>
    public int SafeBatchSize => Math.Clamp(BatchSize, 1, MaximumBatchSize);
}

/// <summary>Tek bir fırsat kaydının sonucu.</summary>
public sealed record RuleEvidenceBackfillItem(
    Guid OpportunityId,
    string Title,
    RuleEvidenceBackfillOutcome Outcome,

    /// <summary>Neden bu sonuç çıktı? Rapor okunabilir olsun diye Türkçe yazılır.</summary>
    string Explanation,

    int RuleCount,

    /// <summary>İşlem öncesinde kanıtı olan kural sayısı.</summary>
    int RulesAlreadyBound,

    /// <summary>Bu turda bağlanan (ya da <c>plan</c>'da bağlanacak) kural sayısı.</summary>
    int RulesBound,

    /// <summary>Kanıtı bulunamayan ve <b>dokunulmayan</b> kural sayısı.</summary>
    int RulesWithoutEvidence,

    /// <summary>Bağlantının gittiği belge sürümü; <c>plan</c>'da da gösterilir.</summary>
    int? DocumentVersionNumber,

    string? DocumentVersionHash,

    /// <summary>Doğrulanmış resmî adres; doğrulanamıyorsa <c>null</c>.</summary>
    string? OfficialUrl);

/// <summary>
/// Toplu işlemin raporu. <c>plan</c> ve <c>apply</c> aynı yapıyı döner; tek fark
/// <see cref="Applied"/> alanıdır.
/// </summary>
public sealed record RuleEvidenceBackfillReport(
    /// <summary><c>false</c> ise hiçbir kayıt değişmedi.</summary>
    bool Applied,

    int TotalExamined,
    int BoundCount,
    int AlreadyBoundCount,
    int NoEvidenceCount,
    int NeedsReparseCount,
    int NeedsRedownloadCount,
    int SkippedQuarantinedCount,
    int SkippedUnverifiedSourceCount,
    int NoSourceDocumentCount,
    int FailedCount,

    /// <summary>Kurulan toplam kural–kanıt bağlantısı sayısı.</summary>
    int EvidenceLinksCreated,

    /// <summary>Kaldığı yerden devam etmek için bir sonraki turun başlangıcı.</summary>
    Guid? NextCursor,

    bool HasMore,
    IReadOnlyList<RuleEvidenceBackfillItem> Items);
