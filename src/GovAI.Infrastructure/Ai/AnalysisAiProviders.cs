using GovAI.Application.Abstractions.Services;
using GovAI.Application.Analysis;
using GovAI.Domain.Analysis;

namespace GovAI.Infrastructure.Ai;

/// <summary>
/// Yapılandırma olmadığında bağlanan sağlayıcı (Faz 3 — Aşama 2).
///
/// <para>
/// Model anahtarı yoksa sistem <b>çalışmaya devam eder</b>: kural motoru sonucu üretir,
/// analiz <c>AIUnavailable</c> olarak kaydedilir ve ekranda "kural tabanlı sonuç"
/// uyarısı gösterilir. Bu sağlayıcı hiçbir ağ isteği yapmaz ve hiçbir zaman iddia
/// üretmez — sistemin "hibrit çalışıyor" demediği yer burasıdır.
/// </para>
/// </summary>
public sealed class UnavailableAnalysisAiProvider : IAnalysisAiProvider
{
    public string ProviderName => "unavailable";

    public bool IsConfigured => false;

    public Task<AiAnalysisOutput> AnalyzeAsync(
        AnalysisAiRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AiAnalysisOutput.Unavailable(AnalysisAiStatusDescriptions.NotConfigured));
}
