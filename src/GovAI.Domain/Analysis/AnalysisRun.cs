using System.Security.Cryptography;
using System.Text;
using GovAI.Domain.Common;

namespace GovAI.Domain.Analysis;

/// <summary>Analizin konusu.</summary>
public enum AnalysisKind
{
    /// <summary>Şirket–fırsat uygunluk analizi.</summary>
    Opportunity = 1,

    /// <summary>Şirket–mevzuat etki analizi.</summary>
    Regulation = 2
}

/// <summary>Analizin bitiş durumu.</summary>
public enum AnalysisRunStatus
{
    Running = 0,
    Completed = 1,

    /// <summary>Kural motoru çalıştı, model kullanılamadı. Sonuç geçerlidir.</summary>
    CompletedWithoutAi = 2,

    /// <summary>Analiz tamamlanamadı; önceki analiz güncel kalır.</summary>
    Failed = 3
}

/// <summary>
/// Tek bir analiz çalıştırmasının sürümlü kaydı (Faz 3 — Aşama 2).
///
/// <para>
/// Kayıt <b>her girdisinin sürümünü</b> taşır: firma profili, mali veri, belge
/// sürümleri, kural seti, prompt, model. Sebep basit — "bu sonuç neden çıktı?" sorusu
/// altı ay sonra da cevaplanabilmeli. Girdilerden biri kaydedilmezse sonuç
/// tekrarlanamaz hâle gelir ve denetlenebilirlik iddiası düşer.
/// </para>
///
/// <para>
/// Eski analizler <b>silinmez</b>. Yeni bir analiz üretildiğinde eskisinin "güncel"
/// işareti kalkar; geçmiş sorgulanabilir kalır.
/// </para>
/// </summary>
public class AnalysisRun : AggregateRoot, IAuditable, ITenantScoped
{
    private AnalysisRun()
    {
    }

    public AnalysisRun(
        Guid tenantId,
        AnalysisKind kind,
        Guid companyId,
        Guid targetId,
        AnalysisVersionSet versions,
        string correlationId,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(versions);
        DomainException.ThrowIf(companyId == Guid.Empty, "Analiz için firma kimliği zorunludur.");
        DomainException.ThrowIf(targetId == Guid.Empty, "Analiz için hedef kimliği zorunludur.");

        TenantId = tenantId;
        Kind = kind;
        CompanyId = companyId;
        TargetId = targetId;

        CompanyProfileVersion = versions.CompanyProfileVersion;
        FinancialDataVersion = versions.FinancialDataVersion;
        DocumentVersionsCsv = versions.DocumentVersionsCsv();
        RuleSetVersion = versions.RuleSetVersion;
        PromptVersion = versions.PromptVersion;
        PromptTemplateHash = versions.PromptTemplateHash;
        ModelProvider = versions.ModelProvider;
        ModelName = versions.ModelName;
        ModelParameters = versions.ModelParameters;
        OutputSchemaVersion = versions.OutputSchemaVersion;
        IdempotencyKey = versions.IdempotencyKey(kind, companyId, targetId);

        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.CreateVersion7().ToString() : correlationId;
        StartedAt = startedAt;
        Status = AnalysisRunStatus.Running;
    }

    public Guid TenantId { get; set; }

    public AnalysisKind Kind { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Fırsat ya da mevzuat değişikliği kimliği; <see cref="Kind"/> hangisi olduğunu söyler.</summary>
    public Guid TargetId { get; private set; }

    public int CompanyProfileVersion { get; private set; }

    public int FinancialDataVersion { get; private set; }

    /// <summary>Analizde kullanılan belge sürümleri, virgülle ayrılmış kimlikler.</summary>
    public string DocumentVersionsCsv { get; private set; } = string.Empty;

    public string RuleSetVersion { get; private set; } = string.Empty;

    /// <summary>Model kullanılmadıysa <c>null</c>. Uydurma sürüm yazılmaz.</summary>
    public string? PromptVersion { get; private set; }

    public string? PromptTemplateHash { get; private set; }

    public string? ModelProvider { get; private set; }

    public string? ModelName { get; private set; }

    public string? ModelParameters { get; private set; }

    public string? OutputSchemaVersion { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>
    /// Tüm sürümlerden türetilen anahtar. Aynı anahtarla gelen ikinci mesaj yeni analiz
    /// oluşturmaz — kuyruk aynı mesajı yeniden teslim ettiğinde iş iki kez yapılmaz.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public AnalysisRunStatus Status { get; private set; }

    public AiAnalysisStatus AiStatus { get; private set; } = AiAnalysisStatus.AIUnavailable;

    /// <summary>Hata durumunun Türkçe açıklaması. Ham istisna metni ve secret yazılmaz.</summary>
    public string? ErrorNote { get; private set; }

    /// <summary>0–100 uygunluk puanı; mevzuat analizinde <c>null</c>.</summary>
    public decimal? Score { get; private set; }

    public decimal? Confidence { get; private set; }

    public ConfidenceLevel? ConfidenceLevel { get; private set; }

    public EligibilityVerdict? Verdict { get; private set; }

    public RegulationImpact? Impact { get; private set; }

    /// <summary>Kriter sonuçları, kırılım ve iddiaların tam JSON dökümü (jsonb).</summary>
    public string ResultJson { get; private set; } = "{}";

    /// <summary>Bu analiz güncel mi? Eski kayıtlar silinmez, yalnızca işareti kalkar.</summary>
    public bool IsLatest { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public void CompleteOpportunity(
        decimal score,
        decimal confidence,
        ConfidenceLevel level,
        EligibilityVerdict verdict,
        AiAnalysisStatus aiStatus,
        string resultJson,
        DateTimeOffset completedAt)
    {
        Score = score;
        Confidence = confidence;
        ConfidenceLevel = level;
        Verdict = verdict;
        Finish(aiStatus, resultJson, completedAt);
    }

    public void CompleteRegulation(
        decimal confidence,
        ConfidenceLevel level,
        RegulationImpact impact,
        AiAnalysisStatus aiStatus,
        string resultJson,
        DateTimeOffset completedAt)
    {
        Confidence = confidence;
        ConfidenceLevel = level;
        Impact = impact;
        Finish(aiStatus, resultJson, completedAt);
    }

    /// <summary>Analiz tamamlanamadı. Sonuç yazılmaz; önceki analiz güncel kalır.</summary>
    public void Fail(string note, DateTimeOffset failedAt)
    {
        Status = AnalysisRunStatus.Failed;
        ErrorNote = note;
        CompletedAt = failedAt;
        IsLatest = false;
    }

    /// <summary>Yeni analiz üretildi; bu kayıt geçmişe alınır ama silinmez.</summary>
    public void Supersede() => IsLatest = false;

    private void Finish(AiAnalysisStatus aiStatus, string resultJson, DateTimeOffset completedAt)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(resultJson), "Analiz sonucu boş olamaz.");

        AiStatus = aiStatus;
        ResultJson = resultJson;
        CompletedAt = completedAt;

        // Model kullanılamadığında sonuç GEÇERLİDİR — kural motoru çalıştı. Durum ayrı
        // yazılır ki ekran "kural tabanlı sonuç" uyarısını gösterebilsin.
        Status = aiStatus == AiAnalysisStatus.Succeeded
            ? AnalysisRunStatus.Completed
            : AnalysisRunStatus.CompletedWithoutAi;
    }
}

/// <summary>
/// Bir analizin tüm girdi sürümleri. Idempotency anahtarı buradan türetilir:
/// sürümlerden herhangi biri değişirse anahtar değişir ve yeni analiz üretilir.
/// </summary>
public sealed record AnalysisVersionSet
{
    public required int CompanyProfileVersion { get; init; }

    public required int FinancialDataVersion { get; init; }

    public IReadOnlyList<Guid> DocumentVersionIds { get; init; } = [];

    public required string RuleSetVersion { get; init; }

    public string? PromptVersion { get; init; }

    public string? PromptTemplateHash { get; init; }

    public string? ModelProvider { get; init; }

    public string? ModelName { get; init; }

    public string? ModelParameters { get; init; }

    public string? OutputSchemaVersion { get; init; }

    public string DocumentVersionsCsv() =>
        string.Join(',', DocumentVersionIds.Order());

    /// <summary>
    /// Girdilerin tamamından türetilen kararlı anahtar.
    ///
    /// <para>
    /// Belge sürümleri sıralanır: kanıt parçalarının geliş sırası değişse de aynı
    /// girdi aynı anahtarı üretmeli. Sıralanmasaydı aynı analiz iki farklı anahtar
    /// alır ve mükerrer kayıt oluşurdu.
    /// </para>
    /// </summary>
    public string IdempotencyKey(AnalysisKind kind, Guid companyId, Guid targetId)
    {
        var payload = string.Join('|',
            kind.ToString(),
            companyId.ToString(),
            targetId.ToString(),
            CompanyProfileVersion.ToString(),
            FinancialDataVersion.ToString(),
            DocumentVersionsCsv(),
            RuleSetVersion,
            PromptVersion ?? "-",
            PromptTemplateHash ?? "-",
            ModelProvider ?? "-",
            ModelName ?? "-",
            ModelParameters ?? "-",
            OutputSchemaVersion ?? "-");

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
