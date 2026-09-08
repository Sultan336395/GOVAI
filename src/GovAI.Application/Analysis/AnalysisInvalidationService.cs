using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Analysis;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Analysis;

/// <summary>
/// Girdisi değişen analizleri güncellikten düşürür (Faz 3 — Aşama 3).
///
/// <para>
/// Analiz <b>silinmez</b>: "üç ay önce neden uygun görünüyordum" sorusunun cevabı
/// kayıtta durmalıdır. Yapılan iş yalnızca "güncel" işaretini kaldırmaktır; böylece
/// ekran eskimiş bir sonucu geçerli gibi göstermez ve yeniden analiz beklenir.
/// </para>
///
/// <para>
/// Ayrı bir servis olmasının sebebi bağımlılık yönü: firma kaydını güncelleyen servis
/// analiz motorunu tanımak zorunda değildir, yalnızca "bu firmanın analizleri eskidi"
/// diyebilmelidir. <see cref="HybridAnalysisService"/> enjekte edilseydi profil
/// güncelleme yolu model istemcisine ve fırsat deposuna da bağımlı hâle gelirdi.
/// </para>
/// </summary>
public sealed class AnalysisInvalidationService(
    IAnalysisRunRepository runs,
    ILogger<AnalysisInvalidationService> logger)
{
    /// <summary>
    /// Firma profili veya mali verisi değişti; o firmanın tüm analizleri eskidi.
    /// </summary>
    public async Task<int> InvalidateForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var guncel = await runs.ListLatestForCompanyAsync(companyId, cancellationToken);

        foreach (var run in guncel)
        {
            run.Supersede();
        }

        if (guncel.Count > 0)
        {
            logger.LogInformation(
                "Firma profili değişti; analizler güncellikten düşürüldü. CompanyId={CompanyId} Adet={Count}",
                companyId, guncel.Count);
        }

        return guncel.Count;
    }

    /// <summary>
    /// Fırsat veya mevzuat kaydının belgesi değişti; o hedefe ait tüm analizler eskidi.
    /// </summary>
    public async Task<int> InvalidateForTargetAsync(Guid targetId, CancellationToken cancellationToken = default)
    {
        var guncel = await runs.ListLatestForTargetAsync(targetId, cancellationToken);

        foreach (var run in guncel)
        {
            run.Supersede();
        }

        if (guncel.Count > 0)
        {
            logger.LogInformation(
                "Hedef belgesi değişti; analizler güncellikten düşürüldü. TargetId={TargetId} Adet={Count}",
                targetId, guncel.Count);
        }

        return guncel.Count;
    }
}
