using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using GovAI.Domain.Maintenance;
using System.Text.Json;
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

    /// <summary>
    /// Bu planın parmak izi. Uygulama isteği bunu taşımak zorundadır: kullanıcının
    /// görmediği bir plan uygulanamaz, gösterildikten sonra veri değişmişse de tutmaz.
    /// </summary>
    string PlanHash,

    IReadOnlyList<CatalogRepairMatch> Matches);

/// <summary>Uygulama sonucu; her kayıt için ne olduğu tek tek durur.</summary>
public sealed record CatalogRepairOutcome(
    string StepCode,
    Guid RecordId,
    string Result,
    int AffectedAssessmentCount,

    /// <summary>
    /// Başlık düzeltmesinde kaydın <b>önceki</b> başlığı. Geri alma bunu geri yazar;
    /// saklanmasaydı düzeltme tek yönlü olurdu.
    /// </summary>
    string? PreviousTitle = null,

    CatalogRepairTarget Target = CatalogRepairTarget.Opportunity,

    CatalogRepairAction Action = CatalogRepairAction.Quarantine);

public sealed record CatalogRepairReport(
    int Attempted,
    int Changed,
    int AlreadyDone,
    IReadOnlyList<CatalogRepairOutcome> Outcomes,

    /// <summary>Geri alma bu kimlikle yapılır; hiçbir şey değişmediyse <c>null</c>.</summary>
    Guid? RunId = null);

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
    IMaintenanceRunRepository runs,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ICurrentUser currentUser,
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
            Fingerprint(matches),
            matches);
    }

    /// <summary>
    /// Planı uygular.
    ///
    /// <para>
    /// <paramref name="confirmedPlanHash"/> ZORUNLUDUR ve o an hesaplanan planın
    /// parmak iziyle birebir tutmalıdır. Böylece iki şey garanti edilir: kullanıcı
    /// uyguladığı planı <b>görmüştür</b>, ve gördüğünden beri veri <b>değişmemiştir</b>.
    /// Tutmuyorsa hiçbir kayda dokunulmaz; kullanıcı planı yeniden görüp onaylar.
    /// </para>
    ///
    /// <para>
    /// Yalnızca <c>WillChange</c> olan kayıtlara dokunulur; ikinci koşu hiçbir şey
    /// yapmaz.
    /// </para>
    /// </summary>
    public async Task<CatalogRepairReport> ApplyAsync(
        string confirmedPlanHash,
        CancellationToken cancellationToken = default)
    {
        var matches = await MatchAsync(cancellationToken);
        var guncelOzet = Fingerprint(matches);

        if (string.IsNullOrWhiteSpace(confirmedPlanHash))
        {
            throw new ValidationException(
                nameof(confirmedPlanHash),
                "Onaylanan planın özeti gönderilmedi. Önce planı görüntüleyip onaylayın.");
        }

        if (!string.Equals(confirmedPlanHash, guncelOzet, StringComparison.Ordinal))
        {
            throw new ValidationException(
                nameof(confirmedPlanHash),
                "Plan, siz görüntüledikten sonra değişti. Hiçbir kayda dokunulmadı; "
                + "planı yeniden görüntüleyip onaylayın.");
        }

        var outcomes = new List<CatalogRepairOutcome>();
        var now = clock.UtcNow;

        foreach (var match in matches)
        {
            if (!match.WillChange)
            {
                outcomes.Add(new CatalogRepairOutcome(
                    match.StepCode, match.RecordId, match.SkipReason ?? "Zaten uygulanmış.", 0,
                    null, match.Target, match.Action));

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
                match.StepCode, match.RecordId, "Uygulandı.", etkilenen,
                // Eski başlık geri alma için SAKLANIR; kayıtta artık yok.
                step.Action == CatalogRepairAction.RetitleFromDocument ? match.CurrentTitle : null,
                match.Target, match.Action));

            logger.LogInformation(
                "Katalog onarımı uygulandı. Adim={StepCode} Hedef={Target} KayitId={RecordId} "
                + "EtkilenenDegerlendirme={AffectedCount}",
                match.StepCode, match.Target, match.RecordId, etkilenen);
        }

        var degisen = outcomes.Count(o => o.Result == "Uygulandı.");
        Guid? runId = null;

        if (degisen > 0)
        {
            var run = new MaintenanceRun(
                MaintenanceOperation.CatalogRepair,
                guncelOzet,
                JsonSerializer.Serialize(outcomes.Where(o => o.Result == "Uygulandı.").ToList()),
                degisen,
                currentUser.Email,
                now);

            await runs.AddAsync(run, cancellationToken);
            runId = run.Id;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CatalogRepairReport(
            matches.Count,
            degisen,
            outcomes.Count(o => o.Result != "Uygulandı."),
            outcomes,
            runId);
    }

    /// <summary>
    /// Bir onarım çalıştırmasını geri alır.
    ///
    /// <para>
    /// Karantinaya alınan kayıt katalogdaki yerine döner, düzeltilen başlık eski hâline
    /// yazılır. Çalıştırma kaydı <b>silinmez</b>, geri alındı diye damgalanır: hem onarım
    /// hem geri alma denetlenebilir kalır.
    /// </para>
    ///
    /// <para>
    /// Yalnızca bu çalıştırmanın gerçekten değiştirdiği kayıtlara dokunulur. Aradan
    /// başka bir işlem geçtiyse (kayıt elle karantinadan çıkarıldıysa) o kayıt sessizce
    /// atlanır — geri alma, kendi yapmadığı bir değişikliği bozmaz.
    /// </para>
    /// </summary>
    public async Task<CatalogRepairReport> UndoAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await runs.GetAsync(runId, cancellationToken)
                  ?? throw new NotFoundException("Bakım çalıştırması", runId);

        if (run.Operation != MaintenanceOperation.CatalogRepair)
        {
            throw new ValidationException(nameof(runId), "Bu çalıştırma bir katalog onarımı değil.");
        }

        if (!run.CanUndo)
        {
            throw new ValidationException(nameof(runId), "Bu çalıştırma zaten geri alınmış.");
        }

        var uygulananlar = JsonSerializer.Deserialize<List<CatalogRepairOutcome>>(run.DetailJson) ?? [];
        var sonuclar = new List<CatalogRepairOutcome>();
        var now = clock.UtcNow;

        foreach (var uygulanan in uygulananlar)
        {
            var geriAlindi = uygulanan.Action switch
            {
                CatalogRepairAction.Quarantine =>
                    await repository.ReleaseAsync(uygulanan.Target, uygulanan.RecordId, cancellationToken),

                CatalogRepairAction.RetitleFromDocument when uygulanan.PreviousTitle is { Length: > 0 } eski =>
                    await repository.RetitleAsync(uygulanan.Target, uygulanan.RecordId, eski, now, cancellationToken),

                _ => 0
            };

            sonuclar.Add(uygulanan with
            {
                Result = geriAlindi > 0 ? "Geri alındı." : "Değişmemiş; atlandı.",
                AffectedAssessmentCount = geriAlindi
            });
        }

        run.MarkUndone(currentUser.Email, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Katalog onarımı geri alındı. CalistirmaId={RunId} KayitSayisi={Count}",
            runId, sonuclar.Count(s => s.Result == "Geri alındı."));

        return new CatalogRepairReport(
            sonuclar.Count,
            sonuclar.Count(s => s.Result == "Geri alındı."),
            sonuclar.Count(s => s.Result != "Geri alındı."),
            sonuclar,
            runId);
    }

    /// <summary>
    /// Planın parmak izi. Değişecek <b>her şey</b> satıra girer: adım, hedef, kayıt,
    /// eylem, önerilen başlık ve gerçekten değişip değişmeyeceği. Eksik bir alan,
    /// değişmiş bir planın aynı özeti üretmesine yol açardı.
    /// </summary>
    private static string Fingerprint(IReadOnlyList<CatalogRepairMatch> matches) =>
        PlanFingerprint.Compute(matches.Select(m =>
            $"{m.StepCode}|{m.Target}|{m.Action}|{m.RecordId}|{m.ProposedTitle}|{m.WillChange}"));

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
