using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Analysis;

namespace GovAI.Application.Tests;

/// <summary>
/// Testlerde kullanılan <b>deterministik</b> yapay zekâ sağlayıcısı.
///
/// <para>
/// Gerçek model testte kullanılamaz: ağ gerektirir, ücretlidir ve aynı girdiye farklı
/// cevap verir — üç özellik de testi işe yaramaz hâle getirir. Bu sağlayıcı ne
/// döneceği önceden yazılmış bir kayıttır; halüsinasyon senaryoları da buradan
/// beslenir.
/// </para>
/// </summary>
internal sealed class FakeAnalysisAiProvider(AiAnalysisOutput? output = null, bool configured = true)
    : IAnalysisAiProvider
{
    /// <summary>Son gelen istek; kişisel veri sızmadığını doğrulayan testler bunu okur.</summary>
    public AnalysisAiRequest? LastRequest { get; private set; }

    public bool ThrowOnAnalyze { get; init; }

    public string ProviderName => "fake";

    public bool IsConfigured { get; } = configured;

    public Task<AiAnalysisOutput> AnalyzeAsync(
        AnalysisAiRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;

        if (ThrowOnAnalyze)
        {
            throw new InvalidOperationException("Model erişilemez.");
        }

        return Task.FromResult(output ?? new AiAnalysisOutput
        {
            Status = AiAnalysisStatus.Succeeded,
            Claims = [],
            ModelProvider = "fake",
            ModelName = "fake-deterministic",
            PromptVersion = "test",
            OutputSchemaVersion = "claims-v1"
        });
    }
}

/// <summary>Bellek içi analiz kaydı deposu; idempotency davranışını da taşır.</summary>
internal sealed class FakeAnalysisRunRepository : IAnalysisRunRepository
{
    public List<AnalysisRun> Saved { get; } = [];

    public Task<AnalysisRun?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey));

    public Task<AnalysisRun?> GetLatestAsync(
        AnalysisKind kind,
        Guid companyId,
        Guid targetId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved
            .Where(r => r.Kind == kind && r.CompanyId == companyId && r.TargetId == targetId && r.IsLatest)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<AnalysisRun>> ListLatestForTargetAsync(
        Guid targetId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AnalysisRun>>(
            Saved.Where(r => r.TargetId == targetId && r.IsLatest).ToList());

    public Task<IReadOnlyList<AnalysisRun>> ListLatestForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AnalysisRun>>(
            Saved.Where(r => r.CompanyId == companyId && r.IsLatest).ToList());

    public Task AddAsync(AnalysisRun run, CancellationToken cancellationToken = default)
    {
        Saved.Add(run);
        return Task.CompletedTask;
    }
}
