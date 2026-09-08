using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Sources;
using GovAI.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Opportunities;

/// <summary>
/// Mevcut fırsat kurallarını, veritabanında <b>zaten duran</b> belge sürümlerine ve
/// kanıt parçalarına bağlayan toplu bakım işlemi (Faz 3).
///
/// <para>
/// Kanıt bağlaması yalnızca yeni ayrıştırmalarda kuruluyordu; üretimdeki eski kurallar
/// kanıtsız kalıyor ve yapay zekâ onlar hakkında resmî kaynağa dayalı açıklama
/// üretemiyordu. Bu servis o boşluğu <b>internete çıkmadan</b> kapatır.
/// </para>
///
/// <para>
/// İki adım ayrıdır: <see cref="PlanAsync"/> hiçbir kayda dokunmadan ne olacağını
/// söyler, <see cref="ApplyAsync"/> uygular. Uygulama <b>idempotenttir</b> — ikinci
/// koşu ne yeni sürüm ne mükerrer bağlantı üretir.
/// </para>
///
/// <para>
/// Hiçbir şey silinmez: eski belge sürümleri, kurallar, analizler ve mevcut kanıt
/// bağlantıları yerinde kalır. Kanıtı güvenilir biçimde bulunamayan kural
/// <b>dokunulmadan</b> bırakılır ve raporda sayılır; tahmin edilerek bağlanmaz.
/// </para>
/// </summary>
public sealed class RuleEvidenceBackfillService(
    IRuleEvidenceBackfillRepository repository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    IEventPublisher events,
    ILogger<RuleEvidenceBackfillService> logger)
{
    /// <summary>Kuru çalıştırma. <b>Hiçbir kayıt değişmez.</b></summary>
    public Task<RuleEvidenceBackfillReport> PlanAsync(
        RuleEvidenceBackfillRequest request,
        CancellationToken cancellationToken = default) =>
        RunAsync(request, apply: false, cancellationToken);

    /// <summary>Planı uygular. Yalnızca kanıtı bulunan kurallara bağlantı ekler.</summary>
    public Task<RuleEvidenceBackfillReport> ApplyAsync(
        RuleEvidenceBackfillRequest request,
        CancellationToken cancellationToken = default) =>
        RunAsync(request, apply: true, cancellationToken);

    private async Task<RuleEvidenceBackfillReport> RunAsync(
        RuleEvidenceBackfillRequest request,
        bool apply,
        CancellationToken cancellationToken)
    {
        var adaylar = await repository.ListCandidateIdsAsync(
            request.AfterOpportunityId, request.SafeBatchSize, cancellationToken);

        var sonuclar = new List<RuleEvidenceBackfillItem>(adaylar.Count);
        var yenidenAyristirilacak = new List<(Source Source, SourceDocument Document)>();

        foreach (var opportunityId in adaylar)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var (sonuc, reparse) = await IncelAsync(opportunityId, apply, cancellationToken);
                sonuclar.Add(sonuc);

                if (reparse is not null)
                {
                    yenidenAyristirilacak.Add(reparse.Value);
                }
            }
            catch (Exception ex)
            {
                // Tek kaydın hatası turu durdurmaz: kalan kayıtlar işlenmeye devam eder
                // ve rapor hangisinin düştüğünü gösterir.
                logger.LogError(
                    ex,
                    "Kanıt bağlama sırasında hata. OpportunityId={OpportunityId}",
                    opportunityId);

                sonuclar.Add(new RuleEvidenceBackfillItem(
                    opportunityId, string.Empty, RuleEvidenceBackfillOutcome.Failed,
                    "Kayıt işlenirken beklenmeyen hata oluştu; kayda dokunulmadı.",
                    0, 0, 0, 0, null, null, null));
            }
        }

        if (apply)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // Yeniden ayrıştırma mesajı YAZMA İŞLEMİNDEN SONRA yayımlanır: mesaj
            // gidip kayıt kaydedilmezse worker olmayan bir duruma göre çalışırdı.
            foreach (var (source, document) in yenidenAyristirilacak)
            {
                await events.PublishAsync(
                    QueueNames.DocumentParseRequested,
                    SourceService.ReparsePayload(source, document),
                    cancellationToken);
            }
        }

        var sonImlec = adaylar.Count == 0 ? request.AfterOpportunityId : adaylar[^1];

        var devamVar = sonImlec is not null
            && adaylar.Count == request.SafeBatchSize
            && await repository.HasMoreAsync(sonImlec.Value, cancellationToken);

        return new RuleEvidenceBackfillReport(
            Applied: apply,
            TotalExamined: sonuclar.Count,
            BoundCount: Say(sonuclar, RuleEvidenceBackfillOutcome.Bound),
            AlreadyBoundCount: Say(sonuclar, RuleEvidenceBackfillOutcome.AlreadyBound),
            NoEvidenceCount: Say(sonuclar, RuleEvidenceBackfillOutcome.NoEvidenceFound),
            NeedsReparseCount: Say(sonuclar, RuleEvidenceBackfillOutcome.NeedsReparse),
            NeedsRedownloadCount: Say(sonuclar, RuleEvidenceBackfillOutcome.NeedsRedownload),
            SkippedQuarantinedCount: Say(sonuclar, RuleEvidenceBackfillOutcome.SkippedQuarantined),
            SkippedUnverifiedSourceCount: Say(sonuclar, RuleEvidenceBackfillOutcome.SkippedUnverifiedSource),
            NoSourceDocumentCount: Say(sonuclar, RuleEvidenceBackfillOutcome.NoSourceDocument),
            FailedCount: Say(sonuclar, RuleEvidenceBackfillOutcome.Failed),
            EvidenceLinksCreated: sonuclar.Sum(s => s.RulesBound),
            NextCursor: sonImlec,
            HasMore: devamVar,
            Items: sonuclar);
    }

    private static int Say(List<RuleEvidenceBackfillItem> sonuclar, RuleEvidenceBackfillOutcome outcome) =>
        sonuclar.Count(s => s.Outcome == outcome);

    /// <summary>
    /// Tek bir fırsatı inceler; <paramref name="apply"/> <c>false</c> ise hiçbir şey
    /// değiştirmez. İkinci değer, yeniden ayrıştırma istenecek belgedir.
    /// </summary>
    private async Task<(RuleEvidenceBackfillItem Item, (Source, SourceDocument)? Reparse)> IncelAsync(
        Guid opportunityId,
        bool apply,
        CancellationToken cancellationToken)
    {
        var context = await repository.LoadContextAsync(opportunityId, cancellationToken);

        if (context is null)
        {
            return (Sonuc(opportunityId, string.Empty, RuleEvidenceBackfillOutcome.Failed,
                "Fırsat kaydı okunamadı.", 0, 0), null);
        }

        var opportunity = context.Opportunity;
        var kurallar = opportunity.Rules.ToList();
        var zatenBagli = kurallar.Count(r => r.Evidence.Count > 0);

        // Karantinadaki kaydın içeriği doğrulanmamıştır; kanıt olarak kullanılamaz.
        if (!opportunity.IsPublishable)
        {
            return (Sonuc(opportunityId, opportunity.Title, RuleEvidenceBackfillOutcome.SkippedQuarantined,
                $"Fırsat karantinada ({opportunity.QuarantineReason}); kanıt bağlanmaz.",
                kurallar.Count, zatenBagli), null);
        }

        if (kurallar.Count == 0)
        {
            return (Sonuc(opportunityId, opportunity.Title, RuleEvidenceBackfillOutcome.NoRules,
                "Kayıtta kural yok; bağlanacak bir koşul bulunmuyor.", 0, 0), null);
        }

        if (context.Document is null)
        {
            return (Sonuc(opportunityId, opportunity.Title, RuleEvidenceBackfillOutcome.NoSourceDocument,
                "Kayıt elle açılmış; dayandığı resmî belge yok.", kurallar.Count, zatenBagli), null);
        }

        if (context.Document.IsQuarantined)
        {
            return (Sonuc(opportunityId, opportunity.Title, RuleEvidenceBackfillOutcome.SkippedQuarantined,
                $"Kaynak belge karantinada ({context.Document.QuarantineReason}); parçaları kanıt sayılmaz.",
                kurallar.Count, zatenBagli), null);
        }

        // Kaynağın yapılandırması doğrulanmamışsa parçanın hangi kurumdan geldiği
        // güvenilir değildir; resmî kanıt olarak sunulamaz.
        if (context.Source is null || !context.Source.ConfigurationVerified)
        {
            return (Sonuc(opportunityId, opportunity.Title, RuleEvidenceBackfillOutcome.SkippedUnverifiedSource,
                "Kaynağın yapılandırması doğrulanmamış; içeriği resmî kanıt olarak bağlanamaz.",
                kurallar.Count, zatenBagli), null);
        }

        var version = context.LatestVersion;

        if (version is null || string.IsNullOrWhiteSpace(version.RawContent))
        {
            // Ham içerik yoksa kayda DOKUNULMAZ. Yeniden indirme bu işlemin işi değil.
            return (Sonuc(opportunityId, opportunity.Title, RuleEvidenceBackfillOutcome.NeedsRedownload,
                "Belgenin ham içeriği veritabanında yok; kanıt üretilemez, yeniden indirme gerekli.",
                kurallar.Count, zatenBagli), null);
        }

        var link = OfficialLink.Verify(version.CanonicalUrl, context.Source.Profile.OfficialDomain);

        if (version.Chunks.Count == 0)
        {
            // Ham içerik duruyor: internete çıkmadan, saklanan içerikten yeniden
            // ayrıştırılabilir. Mesaj ham içeriği taşır (bkz. ReparsePayload).
            var item = Sonuc(opportunityId, opportunity.Title, RuleEvidenceBackfillOutcome.NeedsReparse,
                "Ham içerik var ama kanıt parçası üretilmemiş; saklanan içerikten yeniden ayrıştırılacak.",
                kurallar.Count, zatenBagli, version, link.Url);

            return (item, apply ? (context.Source, context.Document) : null);
        }

        // Yalnızca HİÇ kanıtı olmayan kurallar işlenir.
        //
        // Kanıtı olan kurala yeni sürümden ikinci bir bağ eklemek her koşuda yeni satır
        // üretirdi ve işlem idempotent olmaktan çıkardı. Eski sürüme giden bağlantılar
        // da bu sayede olduğu gibi korunur.
        var bagsizlar = kurallar.Where(r => r.Evidence.Count == 0).ToList();

        if (bagsizlar.Count == 0)
        {
            return (Sonuc(opportunityId, opportunity.Title, RuleEvidenceBackfillOutcome.AlreadyBound,
                "Tüm kuralların kanıt bağlantısı zaten var; değişiklik yapılmadı.",
                kurallar.Count, zatenBagli, version, link.Url), null);
        }

        var now = clock.UtcNow;
        var baglanan = 0;

        foreach (var kural in bagsizlar)
        {
            var eslesen = RuleEvidenceBinder.Match(
                version.Chunks, kural.SourceExcerpt, startOffset: null, endOffset: null);

            if (eslesen.Count == 0)
            {
                continue;
            }

            baglanan++;

            if (apply)
            {
                RuleEvidenceBinder.Attach(kural, version, eslesen, now);
            }
        }

        var bagsizKalan = bagsizlar.Count - baglanan;

        if (baglanan == 0)
        {
            return (Sonuc(opportunityId, opportunity.Title, RuleEvidenceBackfillOutcome.NoEvidenceFound,
                $"{bagsizlar.Count} kuralın hiçbiri belge metninde güvenilir biçimde bulunamadı; "
                + "kayda dokunulmadı.",
                kurallar.Count, zatenBagli, version, link.Url, rulesWithoutEvidence: bagsizKalan), null);
        }

        return (Sonuc(opportunityId, opportunity.Title, RuleEvidenceBackfillOutcome.Bound,
            $"{baglanan} kural, belge sürüm {version.VersionNumber} içindeki parçalara bağlandı"
            + (bagsizKalan > 0 ? $"; {bagsizKalan} kural kanıtsız kaldı ve dokunulmadı." : "."),
            kurallar.Count, zatenBagli, version, link.Url,
            rulesBound: baglanan, rulesWithoutEvidence: bagsizKalan), null);
    }

    private static RuleEvidenceBackfillItem Sonuc(
        Guid opportunityId,
        string title,
        RuleEvidenceBackfillOutcome outcome,
        string explanation,
        int ruleCount,
        int alreadyBound,
        SourceDocumentVersion? version = null,
        string? officialUrl = null,
        int rulesBound = 0,
        int rulesWithoutEvidence = 0) => new(
        opportunityId,
        title,
        outcome,
        explanation,
        ruleCount,
        alreadyBound,
        rulesBound,
        rulesWithoutEvidence,
        version?.VersionNumber,
        version?.RawContentHash,
        officialUrl);
}
