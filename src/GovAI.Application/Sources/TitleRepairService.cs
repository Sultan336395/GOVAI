using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Application.Opportunities;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Sources;

/// <summary>Yeniden ayrıştırılacak tek bir belge ve gerekçesi.</summary>
public sealed record TitleRepairCandidate(
    Guid DocumentId,
    Guid SourceId,
    string SourceName,
    string CurrentTitle,

    /// <summary>
    /// Başlığın yeniden çözüldüğünde nasıl görüneceğinin <b>tahmini</b>. Yalnızca
    /// raporda gösterilir; veritabanına bu değer yazılmaz. Gerçek başlık resmî
    /// kaynaktan yeniden indirilerek belirlenir.
    /// </summary>
    string? PreviewTitle,

    /// <summary>Doğrulanmış resmî adres; yoksa kayıt onarılamaz.</summary>
    string? OfficialUrl,

    bool CanRepair,

    /// <summary>Onarılamıyorsa sebebi. Onarılabiliyorsa <c>null</c>.</summary>
    string? SkipReason);

/// <summary>Kuru çalıştırma raporu. Hiçbir kayıt değişmez.</summary>
public sealed record TitleRepairPlan(
    int Scanned,
    int Corrupt,
    int Repairable,
    int Skipped,
    IReadOnlyList<TitleRepairCandidate> Candidates);

/// <summary>Uygulama sonucu; her belge için ne olduğu tek tek durur.</summary>
public sealed record TitleRepairOutcome(
    Guid DocumentId,
    string PreviousTitle,
    string? NewTitle,
    bool ContentChanged,
    bool VersionCreated,
    string Result);

public sealed record TitleRepairReport(
    int Attempted,
    int Repaired,
    int Unchanged,
    int Failed,
    IReadOnlyList<TitleRepairOutcome> Outcomes);

/// <summary>
/// Bozuk başlıklı belgelerin resmî kaynağından yeniden ayrıştırılması (Faz 2).
///
/// <para>
/// Karakter kümesi tespiti düzeltildi (<c>collector/fetcher.py</c> ve
/// <c>SafeDocumentDownloader</c>), ama o düzeltmeden <b>önce</b> toplanmış kayıtlar
/// veritabanında bozuk başlıklarla duruyor. Bu servis onları resmî kaynağından
/// yeniden indirir.
/// </para>
///
/// <para>
/// Üç kural işlemi güvenli kılar:
/// </para>
/// <list type="number">
///   <item>
///     <b>Başlık tahmin edilmez.</b> Yeni başlık indirilen belgeden gelir. Resmî
///     adresi doğrulanamayan kayıt <b>atlanır</b> — hiçbir değer uydurulmaz.
///   </item>
///   <item>
///     <b>Hiçbir şey silinmez.</b> Eski belge ve sürümleri yerinde kalır; içerik
///     gerçekten değiştiyse yeni bir sürüm eklenir.
///   </item>
///   <item>
///     <b>İdempotenttir.</b> İkinci çalıştırmada bozuk başlık kalmadığı için hiçbir
///     kayda dokunulmaz; içerik aynıysa yeni sürüm de açılmaz.
///   </item>
/// </list>
///
/// <para>
/// <see cref="PlanAsync"/> <b>ağa çıkmaz</b> ve hiçbir kaydı değiştirmez: yalnızca
/// hangi kayıtların etkileneceğini listeler. Uygulama ayrı bir adımdır.
/// </para>
/// </summary>
public sealed class TitleRepairService(
    ISourceDocumentRepository documents,
    ITitleRepairQueryRepository query,
    IDocumentDownloader downloader,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<TitleRepairService> logger)
{
    /// <summary>Tek çalıştırmada işlenecek en fazla belge; resmî sunucu yorulmaz.</summary>
    public const int MaxBatch = 50;

    /// <summary>
    /// Kuru çalıştırma: hangi kayıtlar değişecek? Ağa çıkmaz, hiçbir şey yazmaz.
    /// </summary>
    public async Task<TitleRepairPlan> PlanAsync(
        int limit = MaxBatch,
        CancellationToken cancellationToken = default)
    {
        var hepsi = await query.ListDocumentTitlesAsync(cancellationToken);
        var adaylar = new List<TitleRepairCandidate>();
        var bozuk = 0;

        foreach (var satir in hepsi)
        {
            if (!TurkceMojibake.Bozuk(satir.Title))
            {
                continue;
            }

            bozuk++;

            if (adaylar.Count >= limit)
            {
                continue;
            }

            var link = OfficialLink.Verify(satir.CanonicalUrl ?? satir.Url, satir.OfficialDomain);
            var onarilmis = TurkceMojibake.Onar(satir.Title);

            adaylar.Add(new TitleRepairCandidate(
                DocumentId: satir.DocumentId,
                SourceId: satir.SourceId,
                SourceName: satir.SourceName,
                CurrentTitle: satir.Title,
                PreviewTitle: string.Equals(onarilmis, satir.Title, StringComparison.Ordinal)
                    ? null
                    : onarilmis,
                OfficialUrl: link.Url,
                CanRepair: link.IsVerified,
                SkipReason: link.RejectionReason));
        }

        var onarilabilir = adaylar.Count(a => a.CanRepair);

        logger.LogInformation(
            "Başlık onarım planı: {Taranan} belge tarandı, {Bozuk} bozuk, {Onarilabilir} onarılabilir.",
            hepsi.Count, bozuk, onarilabilir);

        return new TitleRepairPlan(
            Scanned: hepsi.Count,
            Corrupt: bozuk,
            Repairable: onarilabilir,
            Skipped: adaylar.Count - onarilabilir,
            Candidates: adaylar);
    }

    /// <summary>
    /// Planı uygular: her belgeyi resmî adresinden yeniden indirir.
    ///
    /// <para>
    /// İçerik gerçekten değiştiyse yeni bir sürüm açılır; aynıysa yalnızca başlık
    /// tazelenir. Hiçbir kayıt silinmez ve hiçbir başlık tahmin edilmez.
    /// </para>
    /// </summary>
    public async Task<TitleRepairReport> ApplyAsync(
        int limit = MaxBatch,
        CancellationToken cancellationToken = default)
    {
        var plan = await PlanAsync(limit, cancellationToken);
        var sonuclar = new List<TitleRepairOutcome>();

        foreach (var aday in plan.Candidates.Where(a => a.CanRepair))
        {
            var sonuc = await OnarAsync(aday, cancellationToken);
            sonuclar.Add(sonuc);
        }

        if (sonuclar.Any(s => s.NewTitle is not null))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var onarilan = sonuclar.Count(s => s.NewTitle is not null);
        var basarisiz = sonuclar.Count(s => s.Result.StartsWith("Hata", StringComparison.Ordinal));

        logger.LogInformation(
            "Başlık onarımı: {Denenen} denendi, {Onarilan} onarıldı, {Basarisiz} başarısız.",
            sonuclar.Count, onarilan, basarisiz);

        return new TitleRepairReport(
            Attempted: sonuclar.Count,
            Repaired: onarilan,
            Unchanged: sonuclar.Count - onarilan - basarisiz,
            Failed: basarisiz,
            Outcomes: sonuclar);
    }

    private async Task<TitleRepairOutcome> OnarAsync(
        TitleRepairCandidate aday,
        CancellationToken cancellationToken)
    {
        var belge = await documents.GetWithVersionsAsync(aday.DocumentId, cancellationToken);

        if (belge is null)
        {
            return new TitleRepairOutcome(
                aday.DocumentId, aday.CurrentTitle, null, false, false, "Hata: belge bulunamadı.");
        }

        var indirilen = await downloader.DownloadAsync(aday.OfficialUrl!, cancellationToken);

        if (indirilen is null)
        {
            // İndirilemeyen belgenin başlığı TAHMİN EDİLMEZ; kayıt olduğu gibi kalır.
            return new TitleRepairOutcome(
                aday.DocumentId, aday.CurrentTitle, null, false, false,
                "Hata: resmî kaynaktan indirilemedi; başlık değiştirilmedi.");
        }

        var oncekiBaslik = belge.Title;
        var now = clock.UtcNow;

        // Yeni başlık YALNIZCA indirilen belgeden gelir; tahmin edilmez.
        belge.RefreshTitle(indirilen.Title);

        // İçerik gerçekten değiştiyse yeni sürüm açılır. Aynıysa hiçbir sürüm
        // eklenmez — aksi hâlde her çalıştırma sahte bir sürüm üretir ve işlem
        // idempotent olmaktan çıkardı.
        var icerikDegisti = belge.TryUpdateContent(indirilen.Content, now);
        var surumAcildi = false;

        if (icerikDegisti)
        {
            // Eski sürüm SİLİNMEZ; yenisi eklenir ve kanıt zinciri ikisini de taşır.
            belge.AddVersion(
                belge.Url,
                indirilen.FinalUrl,
                indirilen.HttpStatusCode,
                indirilen.MediaType,
                indirilen.Charset,
                indirilen.Content,
                now);

            surumAcildi = true;
        }

        var degisti = !string.Equals(belge.Title, oncekiBaslik, StringComparison.Ordinal);

        return new TitleRepairOutcome(
            DocumentId: aday.DocumentId,
            PreviousTitle: oncekiBaslik,
            NewTitle: degisti ? belge.Title : null,
            ContentChanged: icerikDegisti,
            VersionCreated: surumAcildi,
            Result: degisti
                ? (surumAcildi
                    ? "Başlık onarıldı, içerik değiştiği için yeni sürüm açıldı."
                    : "Başlık onarıldı; içerik aynı olduğu için yeni sürüm açılmadı.")
                : "Başlık zaten doğruydu; hiçbir değişiklik yapılmadı.");
    }
}

/// <summary>
/// Onarım planının okuma sorgusu.
///
/// Ayrı bir arayüzdür çünkü tüm belgelerin <b>yalnızca künyesini</b> okur; gövdeleri
/// belleğe almak gereksizdir ve büyük kurulumlarda maliyetlidir.
/// </summary>
public interface ITitleRepairQueryRepository
{
    Task<IReadOnlyList<DocumentTitleRow>> ListDocumentTitlesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Plan için gereken en az alan.</summary>
public sealed record DocumentTitleRow(
    Guid DocumentId,
    Guid SourceId,
    string SourceName,
    string Title,
    string Url,
    string? CanonicalUrl,
    string? OfficialDomain);
