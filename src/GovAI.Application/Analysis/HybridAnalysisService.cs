using System.Text.Json;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Analysis;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Regulatory;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Analysis;

/// <summary>
/// Kural motoru + yapay zekâ katmanının birleşik akışı (Faz 3 — Aşama 2).
///
/// <para>
/// Sıra sabittir: <b>kurallar çalışır, sonra model konuşur, sonra birleştirilir.</b>
/// Model çağrısı bir yan yoldur; başarısız olursa akış durmaz, sonuç
/// <see cref="AnalysisRunStatus.CompletedWithoutAi"/> olarak kaydedilir ve ekranda
/// "kural tabanlı sonuç" uyarısı gösterilir.
/// </para>
///
/// <para>
/// Mükerrer koruma sürümler üzerinden: firma profili, mali veri, belge sürümleri,
/// kural seti, prompt ve model bilgisinin tamamından türetilen anahtar aynıysa yeni
/// analiz üretilmez, mevcut kayıt döner. Kuyruk aynı mesajı yeniden teslim ettiğinde
/// iş ikinci kez yapılmaz ve ikinci sonuç oluşmaz.
/// </para>
/// </summary>
public sealed class HybridAnalysisService(
    ICompanyRepository companies,
    IOpportunityRepository opportunities,
    IRegulatoryChangeRepository regulatoryChanges,
    IAnalysisRunRepository runs,
    IUnitOfWork unitOfWork,
    IAnalysisAiProvider ai,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    ILogger<HybridAnalysisService> logger)
{
    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Şirket–fırsat analizini üretir ve sürümüyle kaydeder.
    /// </summary>
    public async Task<OpportunityAnalysisDto> AnalyzeOpportunityAsync(
        Guid companyId,
        Guid opportunityId,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureAccessAsync(companyId, cancellationToken: cancellationToken);

        var company = await companies.GetWithDetailsAsync(companyId, cancellationToken)
                      ?? throw new NotFoundException("Firma", companyId);

        var opportunity = await opportunities.GetWithRulesAsync(opportunityId, cancellationToken)
                          ?? throw new NotFoundException("Fırsat", opportunityId);

        if (!opportunity.IsPublishable)
        {
            throw new ValidationException(
                "opportunityId",
                "Karantinadaki bir çağrı analiz edilemez. Kayıt önce incelenip karantinadan çıkarılmalıdır.");
        }

        var now = clock.UtcNow;
        var analysis = OpportunityCriteriaEvaluator.Evaluate(company, opportunity, now);

        var evidence = await BuildOpportunityEvidenceAsync(opportunity, analysis, cancellationToken);

        var versions = Versions(company, opportunity.SourceDocumentId, evidence);
        var mevcut = await runs.FindByIdempotencyKeyAsync(
            versions.IdempotencyKey(AnalysisKind.Opportunity, companyId, opportunityId), cancellationToken);

        if (mevcut is { Status: not AnalysisRunStatus.Failed })
        {
            logger.LogInformation(
                "Aynı sürümlerle analiz zaten var; yenisi üretilmedi. AnalysisRunId={AnalysisRunId} CompanyId={CompanyId}",
                mevcut.Id, companyId);

            return Map(analysis, opportunity.Title, MergedFor(analysis), mevcut);
        }

        var merged = await RunAiAsync(
            AnalysisKind.Opportunity, opportunity.Title, analysis.Criteria, evidence,
            AnalysisPrompt.CompanyFacts(company, analysis.Criteria, DateOnly.FromDateTime(now.UtcDateTime)),
            correlationId, cancellationToken);

        var final = analysis with
        {
            Criteria = merged.Criteria,
            Score = ScoreCalculator.Calculate(merged.Criteria, AnalysisRuleSet.Current),
            Confidence = ConfidenceCalculator.Calculate(
                company, opportunity, merged.Criteria, now, AnalysisRuleSet.Current, merged.AiEvidenceAgreement)
        };

        var run = await PersistAsync(
            company.TenantId, AnalysisKind.Opportunity, companyId, opportunityId,
            versions, correlationId, now, cancellationToken);

        run.RecordTokenUsage(merged.PromptTokens, merged.CompletionTokens);
        run.CompleteOpportunity(
            final.Score.Value,
            final.Confidence.Value,
            final.Confidence.Level,
            final.Verdict,
            merged.AiStatus,
            JsonSerializer.Serialize(new { final.Criteria, final.Score, final.Confidence, merged.Conflicts }, ResultJsonOptions),
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(final, opportunity.Title, merged, run);
    }

    /// <summary>Şirket–mevzuat etki analizini üretir ve sürümüyle kaydeder.</summary>
    public async Task<RegulationImpactDto> AnalyzeRegulationAsync(
        Guid companyId,
        Guid regulatoryChangeId,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureAccessAsync(companyId, cancellationToken: cancellationToken);

        var company = await companies.GetWithDetailsAsync(companyId, cancellationToken)
                      ?? throw new NotFoundException("Firma", companyId);

        var change = await regulatoryChanges.GetAsync(regulatoryChangeId, cancellationToken)
                     ?? throw new NotFoundException("Mevzuat değişikliği", regulatoryChangeId);

        if (change.Status == RegulatoryChangeStatus.Quarantined)
        {
            throw new ValidationException(
                "regulatoryChangeId",
                "Karantinadaki bir mevzuat kaydı için etki analizi üretilmez. "
                + "Kayıt önce resmî kaynağa karşı doğrulanmalıdır.");
        }

        var chunks = await regulatoryChanges.ListEvidenceAsync(change.DocumentVersionId, cancellationToken);

        var evidence = chunks
            .Select(c => new AnalysisEvidence
            {
                EvidenceChunkId = c.Id,
                DocumentVersionId = c.DocumentVersionId,
                Text = c.Text,
                SectionTitle = c.SectionTitle,
                SequenceNumber = c.SequenceNumber
            })
            .ToList();

        var now = clock.UtcNow;

        var impact = RegulationImpactEvaluator.Evaluate(
            company, change,
            evidence.Select(e => new RegulationEvidence
            {
                EvidenceChunkId = e.EvidenceChunkId,
                DocumentVersionId = e.DocumentVersionId,
                Excerpt = e.Text,
                Locator = e.SectionTitle ?? $"Paragraf {e.SequenceNumber}"
            }).ToList(),
            now);

        var versions = Versions(company, change.DocumentVersionId, evidence);
        var mevcut = await runs.FindByIdempotencyKeyAsync(
            versions.IdempotencyKey(AnalysisKind.Regulation, companyId, regulatoryChangeId), cancellationToken);

        if (mevcut is { Status: not AnalysisRunStatus.Failed })
        {
            return Map(impact, change.Title, MergedFor(impact), mevcut);
        }

        var merged = await RunAiAsync(
            AnalysisKind.Regulation, change.Title, impact.Criteria, evidence,
            AnalysisPrompt.CompanyFacts(company, impact.Criteria, DateOnly.FromDateTime(now.UtcDateTime)),
            correlationId, cancellationToken);

        var final = impact with
        {
            Criteria = merged.Criteria,
            Confidence = RegulationConfidence.Calculate(
                company, change, merged.Criteria, now, AnalysisRuleSet.Current, merged.AiEvidenceAgreement)
        };

        var run = await PersistAsync(
            company.TenantId, AnalysisKind.Regulation, companyId, regulatoryChangeId,
            versions, correlationId, now, cancellationToken);

        run.RecordTokenUsage(merged.PromptTokens, merged.CompletionTokens);
        run.CompleteRegulation(
            final.Confidence.Value,
            final.Confidence.Level,
            final.Impact,
            merged.AiStatus,
            JsonSerializer.Serialize(new { final.Criteria, final.Confidence, merged.Conflicts }, ResultJsonOptions),
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(final, change.Title, merged, run);
    }

    /// <summary>
    /// Fırsat analizinde modele verilecek kanıt kümesi (Faz 3 — Aşama 2 tamamlaması).
    ///
    /// <para>
    /// Yalnızca kriterlerin gerçekten dayandığı parçalar gönderilir; belgenin tamamı
    /// değil. İki sebep: modelin ilgisiz metinden iddia üretmesini engellemek ve
    /// gönderilen metin miktarını sınırlı tutmak.
    /// </para>
    ///
    /// <para>
    /// <b>Karantinadaki veya kaynağı doğrulanmamış belgenin parçası kanıt sayılmaz.</b>
    /// İçeriğine güvenilmeyen bir belgeden çıkarılan iddia, kullanıcıya resmî bilgi
    /// gibi görünür ve en tehlikeli hata biçimi budur.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<AnalysisEvidence>> BuildOpportunityEvidenceAsync(
        Opportunity opportunity,
        CompanyOpportunityAnalysis analysis,
        CancellationToken cancellationToken)
    {
        if (!opportunity.IsPublishable)
        {
            return [];
        }

        var provenance = await opportunities.GetProvenanceAsync(opportunity.Id, cancellationToken);

        // Kaynak yapılandırması doğrulanmamışsa parçaların hangi belgeden geldiği
        // güvenilir değildir; model bu metne dayanarak konuşamaz.
        if (provenance is null || !provenance.SourceVerified)
        {
            return [];
        }

        var context = await opportunities.GetRuleEvidenceContextAsync(opportunity.Id, cancellationToken);
        if (context.Count == 0)
        {
            return [];
        }

        var kullanilan = analysis.Criteria
            .SelectMany(c => c.Evidence)
            .Where(e => e.EvidenceChunkId is not null && e.DocumentVersionId is not null)
            .GroupBy(e => e.EvidenceChunkId!.Value)
            .Select(g => g.First())
            .ToList();

        var sonuc = new List<AnalysisEvidence>();
        var sira = 0;

        foreach (var kanit in kullanilan)
        {
            if (!context.TryGetValue(kanit.EvidenceChunkId!.Value, out var bilgi)
                || string.IsNullOrWhiteSpace(bilgi.Text))
            {
                continue;
            }

            sonuc.Add(new AnalysisEvidence
            {
                EvidenceChunkId = kanit.EvidenceChunkId!.Value,
                DocumentVersionId = kanit.DocumentVersionId!.Value,
                Text = bilgi.Text,
                SectionTitle = kanit.Locator,
                SequenceNumber = ++sira
            });
        }

        return sonuc;
    }

    /// <summary>
    /// Model çağrısı ve birleştirme.
    ///
    /// <para>
    /// İstisna dışarı sızmaz: model hatası analizi düşürmez. Hata metni <b>ham hâliyle
    /// loglanmaz</b> — istek gövdesi ve yanıtı hassas veri taşıyabilir; yalnızca tür
    /// ve korelasyon kimliği yazılır.
    /// </para>
    /// </summary>
    private async Task<MergedAnalysis> RunAiAsync(
        AnalysisKind kind,
        string title,
        IReadOnlyList<CriterionResult> criteria,
        IReadOnlyList<AnalysisEvidence> evidence,
        IReadOnlyDictionary<string, string> companyFacts,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (!ai.IsConfigured || evidence.Count == 0)
        {
            var neden = ai.IsConfigured
                ? "Analizde modele verilecek kanıt parçası yok; sonuç yalnızca kural motorundan üretildi."
                : AnalysisAiStatusDescriptions.NotConfigured;

            return DecisionMerger.Merge(criteria, AiAnalysisOutput.Unavailable(neden), evidence);
        }

        var injection = PromptInjectionGuard.Scan(evidence);
        if (injection.HasFindings)
        {
            logger.LogWarning(
                "Kanıt metninde talimat girişimi bulundu. CorrelationId={CorrelationId} Bölüm={Count}",
                correlationId, injection.SuspiciousChunkIds.Count);
        }

        AiAnalysisOutput output;
        try
        {
            output = await ai.AnalyzeAsync(new AnalysisAiRequest
            {
                Kind = kind,
                TargetTitle = title,
                Criteria = criteria,
                Evidence = evidence,
                CompanyFacts = companyFacts,
                SuspiciousChunkIds = injection.SuspiciousChunkIds,
                CorrelationId = correlationId ?? Guid.CreateVersion7().ToString()
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Yapay zekâ sağlayıcısı hata verdi; kural sonuçları korundu. "
                + "CorrelationId={CorrelationId} HataTuru={ExceptionType}",
                correlationId, exception.GetType().Name);

            output = AiAnalysisOutput.Failed(AiAnalysisStatus.Error, AnalysisAiStatusDescriptions.Unreachable);
        }

        var merged = DecisionMerger.Merge(criteria, output, evidence);

        foreach (var red in merged.RejectedClaims)
        {
            logger.LogWarning(
                "Yapay zekâ iddiası reddedildi. CorrelationId={CorrelationId} Kriter={Criterion} Neden={Reason}",
                correlationId, red.Claim.CriterionCode, red.RejectionReason);
        }

        return merged;
    }

    private async Task<AnalysisRun> PersistAsync(
        Guid tenantId,
        AnalysisKind kind,
        Guid companyId,
        Guid targetId,
        AnalysisVersionSet versions,
        string? correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Eski analiz silinmez; yalnızca güncel işareti kalkar.
        var onceki = await runs.GetLatestAsync(kind, companyId, targetId, cancellationToken);
        onceki?.Supersede();

        var run = new AnalysisRun(
            tenantId, kind, companyId, targetId, versions,
            correlationId ?? Guid.CreateVersion7().ToString(), now);

        await runs.AddAsync(run, cancellationToken);

        return run;
    }

    private AnalysisVersionSet Versions(
        Company company,
        Guid? documentId,
        IReadOnlyList<AnalysisEvidence> evidence)
    {
        var belgeSurumleri = evidence.Count > 0
            ? evidence.Select(e => e.DocumentVersionId).Distinct().ToList()
            : documentId is { } id ? [id] : new List<Guid>();

        // Model yapılandırılmamışsa prompt ve model alanları BOŞ kalır. Uydurma sürüm
        // yazılırsa kayıt "model çalıştı" izlenimi verir.
        return new AnalysisVersionSet
        {
            CompanyProfileVersion = company.ProfileVersion,
            FinancialDataVersion = company.FinancialDataVersion,
            DocumentVersionIds = belgeSurumleri,
            RuleSetVersion = AnalysisRuleSet.Current.Version,
            PromptVersion = ai.IsConfigured ? AnalysisPrompt.Version : null,
            PromptTemplateHash = ai.IsConfigured ? AnalysisPrompt.TemplateHash : null,
            ModelProvider = ai.IsConfigured ? ai.ProviderName : null,
            OutputSchemaVersion = ai.IsConfigured ? AnalysisPrompt.OutputSchemaVersion : null
        };
    }

    /// <summary>Model çalışmadığında kullanılan boş birleştirme.</summary>
    private static MergedAnalysis MergedFor(CompanyOpportunityAnalysis analysis) => new()
    {
        Criteria = analysis.Criteria,
        Claims = [],
        Conflicts = [],
        AiStatus = AiAnalysisStatus.AIUnavailable
    };

    private static MergedAnalysis MergedFor(CompanyRegulationImpact impact) => new()
    {
        Criteria = impact.Criteria,
        Claims = [],
        Conflicts = [],
        AiStatus = AiAnalysisStatus.AIUnavailable
    };

    private static OpportunityAnalysisDto Map(
        CompanyOpportunityAnalysis analysis,
        string title,
        MergedAnalysis merged,
        AnalysisRun run) =>
        AnalysisMapper.ToDto(analysis, title, merged, run);

    private static RegulationImpactDto Map(
        CompanyRegulationImpact impact,
        string title,
        MergedAnalysis merged,
        AnalysisRun run) =>
        AnalysisMapper.ToDto(impact, title, merged, run);
}
