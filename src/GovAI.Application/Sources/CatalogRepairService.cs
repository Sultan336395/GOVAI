using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Common;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Sources;

/// <summary>Bir onarım adımının tek bir kayıt üzerindeki planı.</summary>
public sealed record CatalogRepairMatch(
    string StepCode,
    CatalogRepairTarget Target,
    CatalogRepairAction Action,
    Guid RecordId,
    string CurrentTitle,
    string? OfficialUrl,

    /// <summary>Yeniden adlandırmada belge sürümünden okunan gerçek başlık.</summary>
    string? ProposedTitle,

    /// <summary>Bu kayıt için bir şey değişecek mi? İkinci koşuda <c>false</c> olur.</summary>
    bool WillChange,

    /// <summary>Değişmeyecekse sebebi (zaten karantinada, başlık zaten doğru vb.).</summary>
    string? SkipReason,

    /// <summary>Bu kayda bağlı, yeniden değerlendirilmesi gerekecek analiz/değerlendirme sayısı.</summary>
    int AffectedAssessmentCount);

/// <summary>Kuru çalıştırma raporu. <b>Hiçbir kayıt değişmez.</b></summary>
public sealed record CatalogRepairPlanReport(
    int StepCount,
    int MatchedRecordCount,
    int WillChangeCount,
    int AlreadyDoneCount,
    IReadOnlyList<CatalogRepairMatch> Matches);

/// <summary>Uygulama sonucu; her kayıt için ne olduğu tek tek durur.</summary>
public sealed record CatalogRepairOutcome(
    string StepCode,
    Guid RecordId,
    string Result,
    int AffectedAssessmentCount);

public sealed record CatalogRepairReport(
    int Attempted,
    int Changed,
    int AlreadyDone,
    IReadOnlyList<CatalogRepairOutcome> Outcomes);

/// <summary>
/// Bekleyen katalog onarımlarının kuru çalıştırması ve uygulanması (Faz 3).
///
/// <para>
/// <b>Hiçbir kayıt silinmez.</b> Çöp kayıtlar karantinaya alınır: kaynak belge, ham
/// içerik, sürüm ve kanıtlar yerinde kalır; kayıt yalnızca katalogdan, skorlamadan,
/// bildirimden ve rapordan çıkar. PlatformReviewer işlemi geri alabilir.
/// </para>
///
/// <para>
/// İşlem <b>idempotenttir</b>: ikinci koşu ilave değişiklik veya mükerrer kayıt
/// üretmez. Zaten karantinada olan kayıt atlanır, başlığı zaten doğru olan kayda
/// dokunulmaz.
/// </para>
///
/// <para>
/// Bağlı değerlendirmeler <b>silinmez</b>; "yeniden değerlendirilmeli" olarak
/// işaretlenir. Silmek, "üç ay önce bu çağrıya neden uygun görünüyordum" sorusunu
/// cevapsız bırakırdı.
/// </para>
/// </summary>
public sealed class CatalogRepairService(
    ICatalogRepairRepository repository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<CatalogRepairService> logger)
{
    /// <summary>Kuru çalıştırma: ne değişeceğini gösterir, hiçbir şeyi değiştirmez.</summary>
    public async Task<CatalogRepairPlanReport> PlanAsync(CancellationToken cancellationToken = default)
    {
        var matches = await MatchAsync(cancellationToken);

        return new CatalogRepairPlanReport(
            CatalogRepairPlan.Steps.Count,
            matches.Count,
            matches.Count(m => m.WillChange),
            matches.Count(m => !m.WillChange),
            matches);
    }

    /// <summary>
    /// Planı uygular.
    ///
    /// <para>
    /// Yalnızca <c>WillChange</c> olan kayıtlara dokunulur; ikinci koşu hiçbir şey
    /// yapmaz. Bu, "yayınla" komutundan sonra çalıştırılacak tek yazma yoludur.
    /// </para>
    /// </summary>
    public async Task<CatalogRepairReport> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var matches = await MatchAsync(cancellationToken);
        var outcomes = new List<CatalogRepairOutcome>();
        var now = clock.UtcNow;

        foreach (var match in matches)
        {
            if (!match.WillChange)
            {
                outcomes.Add(new CatalogRepairOutcome(
                    match.StepCode, match.RecordId, match.SkipReason ?? "Zaten uygulanmış.", 0));

                continue;
            }

            var step = CatalogRepairPlan.Steps.Single(s => s.Code == match.StepCode);

            var etkilenen = step.Action switch
            {
                CatalogRepairAction.Quarantine => await repository.QuarantineAsync(
                    match.Target, match.RecordId, step.Reason, step.Note, cancellationToken),

                CatalogRepairAction.RetitleFromDocument => await repository.RetitleAsync(
                    match.Target, match.RecordId, match.ProposedTitle!, now, cancellationToken),

                _ => 0
            };

            outcomes.Add(new CatalogRepairOutcome(
                match.StepCode, match.RecordId, "Uygulandı.", etkilenen));

            logger.LogInformation(
                "Katalog onarımı uygulandı. Adim={StepCode} Hedef={Target} KayitId={RecordId} "
                + "EtkilenenDegerlendirme={AffectedCount}",
                match.StepCode, match.Target, match.RecordId, etkilenen);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CatalogRepairReport(
            matches.Count,
            outcomes.Count(o => o.Result == "Uygulandı."),
            outcomes.Count(o => o.Result != "Uygulandı."),
            outcomes);
    }

    /// <summary>
    /// Adımları kayıtlarla eşleştirir.
    ///
    /// <para>
    /// Eşleşme Türkçe katlanmış metinle yapılır: "YÜRÜRLÜKTEN KALDIRILAN" ile
    /// "Yürürlükten Kaldırılan" aynı kayıttır, ama <c>ToUpperInvariant</c> bunları
    /// eşleştirmez (noktasız <c>ı</c>).
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<CatalogRepairMatch>> MatchAsync(CancellationToken cancellationToken)
    {
        var kayitlar = await repository.ListRepairCandidatesAsync(cancellationToken);
        var sonuc = new List<CatalogRepairMatch>();

        // Bir kayıt EN FAZLA BİR adımla eşleşir. "Yürürlükten Kaldırılan Destekler"
        // başlığı hem özel adımla hem genel "destekler" adımıyla eşleşiyor; ikisi de
        // uygulanırsa aynı kayıt iki kez karantinaya alınır ve rapor mükerrer satır
        // gösterir. Adımlar özelden genele sıralı; ilk eşleşen kazanır.
        foreach (var kayit in kayitlar)
        {
            var step = CatalogRepairPlan.Steps
                .FirstOrDefault(s => s.Target == kayit.Target && Eslesiyor(s, kayit));

            if (step is null)
            {
                continue;
            }

            var (degisecek, sebep, yeniBaslik) = Degerlendir(step, kayit);

            sonuc.Add(new CatalogRepairMatch(
                step.Code,
                step.Target,
                step.Action,
                kayit.Id,
                kayit.Title,
                kayit.OfficialUrl,
                yeniBaslik,
                degisecek,
                sebep,
                kayit.AssessmentCount));
        }

        return sonuc;
    }

    private static bool Eslesiyor(CatalogRepairStep step, CatalogRepairCandidate kayit)
    {
        if (!string.IsNullOrWhiteSpace(step.UrlContains))
        {
            var adres = TurkceMetin.Katla(kayit.OfficialUrl ?? string.Empty);
            if (!adres.Contains(TurkceMetin.Katla(step.UrlContains), StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(step.TitleContains))
        {
            var baslik = TurkceMetin.Katla(kayit.Title);
            if (!baslik.Contains(TurkceMetin.Katla(step.TitleContains), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Bu kayıt için gerçekten bir şey değişecek mi?</summary>
    private static (bool WillChange, string? SkipReason, string? ProposedTitle) Degerlendir(
        CatalogRepairStep step,
        CatalogRepairCandidate kayit)
    {
        if (step.Action == CatalogRepairAction.Quarantine)
        {
            return kayit.IsQuarantined
                ? (false, "Kayıt zaten karantinada.", null)
                : (true, null, null);
        }

        // Yeniden adlandırma: belge sürümünde gerçek başlık YOKSA dokunulmaz.
        // Uydurma başlık yazmaktansa bozuk başlık bırakmak yeğdir; ikincisi görülür.
        if (string.IsNullOrWhiteSpace(kayit.DocumentTitle))
        {
            return (false, "Belge sürümünde başlık yok; uydurma başlık yazılmaz.", null);
        }

        if (string.Equals(kayit.DocumentTitle.Trim(), kayit.Title.Trim(), StringComparison.Ordinal))
        {
            return (false, "Başlık zaten belge sürümündeki başlıkla aynı.", kayit.DocumentTitle);
        }

        return (true, null, kayit.DocumentTitle.Trim());
    }
}
