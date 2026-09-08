using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Opportunities;
using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovAI.Application.Tests;

/// <summary>
/// Mevcut fırsat kurallarına geriye dönük kanıt bağlamanın davranış sözleşmesi
/// (Faz 3 — yayın öncesi hazırlık).
///
/// <para>
/// Kanıt bağlaması yalnızca yeni ayrıştırmalarda kuruluyordu; üretimdeki eski kurallar
/// kanıtsız kalıyordu. Bu işlem o boşluğu <b>veritabanında zaten duran</b> içerikten
/// kapatır ve şu garantileri verir:
/// </para>
///
/// <list type="number">
///   <item><c>plan</c> hiçbir kaydı değiştirmez.</item>
///   <item>Ham içeriği olmayan kayda dokunulmaz; "yeniden indirme gerekli" denir.</item>
///   <item>İkinci koşu mükerrer bağlantı üretmez.</item>
///   <item>Karantinadaki fırsat ve belge atlanır.</item>
///   <item>Doğrulanmamış kaynağın içeriği kanıt olarak bağlanmaz.</item>
///   <item>Kanıtı bulunamayan kural silinmez, dokunulmaz ve raporda sayılır.</item>
/// </list>
/// </summary>
public class RuleEvidenceBackfillServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private const string KobiDijital =
        "KOBİ Dijital Dönüşüm Destek Programı. Başvuru Şartları: Programa başvuracak "
        + "işletmelerin en az 10 çalışanı olmalıdır. Destek üst limiti 1.500.000 TL'dir. "
        + "Başvurular 31.12.2026 tarihine kadar alınır.";

    private const string CalisanKosulu = "işletmelerin en az 10 çalışanı olmalıdır";

    private static (RuleEvidenceBackfillService Service, FakeUnitOfWork Uow, FakeEventPublisher Events)
        Build(params RuleEvidenceBackfillContext[] kayitlar)
    {
        var uow = new FakeUnitOfWork();
        var events = new FakeEventPublisher();

        var service = new RuleEvidenceBackfillService(
            new FakeBackfillRepository(kayitlar), uow, new FixedClock(Now), events,
            NullLogger<RuleEvidenceBackfillService>.Instance);

        return (service, uow, events);
    }

    // ── Kurgu yardımcıları ────────────────────────────────────────────────

    private static Source Kaynak(bool dogrulanmis = true)
    {
        var source = new Source("KOSGEB", SourceType.KosgebOrSimilar, "https://www.kosgeb.gov.tr", "0 6 * * *");

        source.Describe(
            SourceCategory.Grant,
            new SourceProfile("KOSGEB", "TR", "kosgeb.gov.tr", "tr"));

        if (dogrulanmis)
        {
            // Doğrulama, taranabilir bir plan olmadan yapılamaz: kaynak gerçekten
            // taranabildiği için doğrulanmış sayılır.
            source.PlanCrawl(new SourceCrawlPlan(
                "https://www.kosgeb.gov.tr/site/tr/genel/destekler",
                "a.destek-link", ".icerik", null, 1, null, "text/html"));

            source.MarkVerified(Now.AddDays(-1));
        }

        return source;
    }

    /// <summary>
    /// Parçalar gerçek metinden, gerçek karakter aralıklarıyla kesilir. Elle uydurulmuş
    /// bir parça, aralığın metinle tuttuğunu doğrulamazdı.
    /// </summary>
    private static SourceDocumentVersion Surum(
        SourceDocument document,
        string metin,
        bool parcaliMi = true)
    {
        var version = document.AddVersion(
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/kobi-dijital",
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/kobi-dijital",
            200, "text/html", "utf-8", metin, Now.AddDays(-2));

        version.RecordParse(metin, "KOBİ Dijital Dönüşüm Destek Programı", "tr", 1, Now.AddDays(-2));

        if (!parcaliMi)
        {
            return version;
        }

        var cumleler = metin.Split(". ", StringSplitOptions.RemoveEmptyEntries);
        var parcalar = new List<DocumentEvidenceChunk>();
        var konum = 0;

        for (var i = 0; i < cumleler.Length; i++)
        {
            var baslangic = metin.IndexOf(cumleler[i], konum, StringComparison.Ordinal);
            var bitis = baslangic + cumleler[i].Length;
            konum = bitis;

            parcalar.Add(new DocumentEvidenceChunk(
                version.Id, i, cumleler[i], baslangic, bitis, 1, "Başvuru Şartları"));
        }

        version.ReplaceChunks(parcalar);
        return version;
    }

    private static (Opportunity Opportunity, SourceDocument Document, SourceDocumentVersion? Version, Source Source)
        Kurgu(
            string? alinti = CalisanKosulu,
            bool parcaliMi = true,
            bool hamIcerikVar = true,
            bool firsatKarantinada = false,
            bool belgeKarantinada = false,
            bool kaynakDogrulanmis = true,
            bool kuralVar = true)
    {
        var source = Kaynak(kaynakDogrulanmis);

        var document = new SourceDocument(
            source.Id,
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/kobi-dijital",
            "KOBİ Dijital Dönüşüm Destek Programı",
            KobiDijital, "text/html", Now.AddDays(-2));

        var version = hamIcerikVar ? Surum(document, KobiDijital, parcaliMi) : null;

        if (belgeKarantinada)
        {
            document.Quarantine(QuarantineReason.InvalidSourcePage, "Liste sayfası.");
        }

        var opportunity = new Opportunity(
            source.Id, SourceType.KosgebOrSimilar, SupportCategory.DigitalTransformation,
            "KOBİ Dijital Dönüşüm Destek Programı", "KOSGEB", Now.AddDays(-2));

        opportunity.Describe("Dijital dönüşüm hibesi.", document.Url, document.Id);

        if (kuralVar)
        {
            opportunity.ReplaceRules(
                [
                    new OpportunityRule(
                        "Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
                        RuleDimension.Employment, RuleSeverity.Blocking,
                        "En az 10 çalışan gereklidir.", alinti)
                ],
                0.9m);
        }

        if (firsatKarantinada)
        {
            opportunity.Quarantine(QuarantineReason.InvalidSourcePage, "Liste sayfası.");
        }

        return (opportunity, document, version, source);
    }

    private static RuleEvidenceBackfillContext Baglam(
        (Opportunity Opportunity, SourceDocument Document, SourceDocumentVersion? Version, Source Source) kurgu,
        bool belgeVar = true) =>
        new(kurgu.Opportunity,
            belgeVar ? kurgu.Document : null,
            belgeVar ? kurgu.Version : null,
            belgeVar ? kurgu.Source : null);

    // ── Testler ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "GB1. Kuru çalıştırma hiçbir kaydı değiştirmez")]
    public async Task Plan_kaydi_degistirmez()
    {
        var kurgu = Kurgu();
        var (service, uow, events) = Build(Baglam(kurgu));

        var rapor = await service.PlanAsync(new RuleEvidenceBackfillRequest());

        Assert.False(rapor.Applied);
        Assert.Equal(1, rapor.BoundCount);

        // Rapor "bağlanacak" diyor ama kayıtta hâlâ hiçbir kanıt yok.
        Assert.Empty(kurgu.Opportunity.Rules.Single().Evidence);
        Assert.Equal(0, uow.SaveCount);
        Assert.Empty(events.Published);
    }

    [Fact(DisplayName = "GB2. Mevcut ham içerikten kanıt bağlantısı kurulur")]
    public async Task Apply_mevcut_icerikten_baglar()
    {
        var kurgu = Kurgu();
        var (service, _, _) = Build(Baglam(kurgu));

        var rapor = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.True(rapor.Applied);
        Assert.Equal(1, rapor.BoundCount);
        Assert.Equal(1, rapor.EvidenceLinksCreated);

        var kanit = kurgu.Opportunity.Rules.Single().Evidence.Single();

        // Zincirin halkaları: parça → belge sürümü → karakter aralığı.
        Assert.Equal(kurgu.Version!.Id, kanit.DocumentVersionId);
        Assert.Contains(kurgu.Version.Chunks, c => c.Id == kanit.EvidenceChunkId);
        Assert.True(kanit.EndOffset > kanit.StartOffset);

        // Künye raporda da görünür: hangi sürüm, hangi hash, hangi resmî adres.
        var satir = rapor.Items.Single();
        Assert.Equal(kurgu.Version.VersionNumber, satir.DocumentVersionNumber);
        Assert.Equal(kurgu.Version.RawContentHash, satir.DocumentVersionHash);
        Assert.StartsWith("https://www.kosgeb.gov.tr/", satir.OfficialUrl);
    }

    [Fact(DisplayName = "GB3. İkinci koşu mükerrer bağlantı üretmez")]
    public async Task Ikinci_kosu_mukerrer_uretmez()
    {
        var kurgu = Kurgu();
        var (service, _, _) = Build(Baglam(kurgu));

        await service.ApplyAsync(new RuleEvidenceBackfillRequest());
        var ikinci = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.Single(kurgu.Opportunity.Rules.Single().Evidence);
        Assert.Equal(0, ikinci.EvidenceLinksCreated);
        Assert.Equal(1, ikinci.AlreadyBoundCount);
        Assert.Equal(0, ikinci.BoundCount);
    }

    [Fact(DisplayName = "GB4. Ham içerik yoksa kayıt değiştirilmez; yeniden indirme gerekli denir")]
    public async Task Ham_icerik_yoksa_dokunulmaz()
    {
        var kurgu = Kurgu(hamIcerikVar: false);
        var (service, _, events) = Build(Baglam(kurgu));

        var rapor = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.Equal(1, rapor.NeedsRedownloadCount);
        Assert.Empty(kurgu.Opportunity.Rules.Single().Evidence);

        // İnternete çıkma kararı bu işlemin işi değil: mesaj da yayımlanmaz.
        Assert.Empty(events.Published);
        Assert.Contains("yeniden indirme", rapor.Items.Single().Explanation);
    }

    [Fact(DisplayName = "GB5. Ham içerik varken parça yoksa saklanan içerikten yeniden ayrıştırılır")]
    public async Task Parca_yoksa_yerel_yeniden_ayristirma()
    {
        var kurgu = Kurgu(parcaliMi: false);
        var (service, _, events) = Build(Baglam(kurgu));

        var plan = await service.PlanAsync(new RuleEvidenceBackfillRequest());
        Assert.Equal(1, plan.NeedsReparseCount);
        Assert.Empty(events.Published);

        var rapor = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.Equal(1, rapor.NeedsReparseCount);

        var (kuyruk, mesaj) = Assert.Single(events.Published);
        Assert.Equal(QueueNames.DocumentParseRequested, kuyruk);

        // Mesaj HAM İÇERİĞİ taşır: worker kaynağa yeniden gitmez.
        var ham = mesaj.GetType().GetProperty("RawContent")?.GetValue(mesaj) as string;
        Assert.Equal(KobiDijital, ham);
    }

    [Fact(DisplayName = "GB6. Karantinadaki fırsat atlanır")]
    public async Task Karantinadaki_firsat_atlanir()
    {
        var kurgu = Kurgu(firsatKarantinada: true);
        var (service, _, _) = Build(Baglam(kurgu));

        var rapor = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.Equal(1, rapor.SkippedQuarantinedCount);
        Assert.Empty(kurgu.Opportunity.Rules.Single().Evidence);
    }

    [Fact(DisplayName = "GB7. Karantinadaki belgenin parçası kanıt olarak bağlanmaz")]
    public async Task Karantinadaki_belge_atlanir()
    {
        var kurgu = Kurgu(belgeKarantinada: true);
        var (service, _, _) = Build(Baglam(kurgu));

        var rapor = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.Equal(1, rapor.SkippedQuarantinedCount);
        Assert.Empty(kurgu.Opportunity.Rules.Single().Evidence);
    }

    [Fact(DisplayName = "GB8. Doğrulanmamış kaynağın içeriği kanıt olarak kullanılmaz")]
    public async Task Dogrulanmamis_kaynak_kullanilmaz()
    {
        var kurgu = Kurgu(kaynakDogrulanmis: false);
        var (service, _, _) = Build(Baglam(kurgu));

        var rapor = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.Equal(1, rapor.SkippedUnverifiedSourceCount);
        Assert.Empty(kurgu.Opportunity.Rules.Single().Evidence);
    }

    [Fact(DisplayName = "GB9. Kanıtı bulunamayan kural tahminle bağlanmaz ve silinmez")]
    public async Task Kaniti_bulunamayan_kural_korunur()
    {
        // Alıntı belgede geçmiyor: eski bir kayıt, metni değişmiş bir belge.
        var kurgu = Kurgu(alinti: "yıllık cirosu 50 milyon TL üzerinde olan işletmeler");
        var (service, _, _) = Build(Baglam(kurgu));

        var rapor = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.Equal(1, rapor.NoEvidenceCount);
        Assert.Equal(0, rapor.EvidenceLinksCreated);

        // Kural DURUYOR: deterministik motor onu kullanmaya devam eder.
        var kural = kurgu.Opportunity.Rules.Single();
        Assert.Empty(kural.Evidence);
        Assert.False(kural.SupportsAiClaims);
        Assert.Equal("En az 10 çalışan gereklidir.", kural.HumanReadable);
    }

    [Fact(DisplayName = "GB10. Kısa alıntı yanlış parçaya bağlanmaz")]
    public async Task Kisa_alinti_baglanmaz()
    {
        // "10 çalışan" belgenin birçok yerinde geçebilir; hangisinin kaynak olduğu
        // bilinmiyorsa bağ kurmak uydurmaktır.
        var kurgu = Kurgu(alinti: "10 çalışan");
        var (service, _, _) = Build(Baglam(kurgu));

        var rapor = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.Equal(1, rapor.NoEvidenceCount);
        Assert.Empty(kurgu.Opportunity.Rules.Single().Evidence);
    }

    [Fact(DisplayName = "GB11. Elle açılmış kayıt (belgesiz) atlanır")]
    public async Task Belgesiz_kayit_atlanir()
    {
        var kurgu = Kurgu();
        var (service, _, _) = Build(Baglam(kurgu, belgeVar: false));

        var rapor = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.Equal(1, rapor.NoSourceDocumentCount);
        Assert.Empty(kurgu.Opportunity.Rules.Single().Evidence);
    }

    [Fact(DisplayName = "GB12. Kuralı olmayan kayıt sessizce geçilir")]
    public async Task Kuralsiz_kayit_gecilir()
    {
        var kurgu = Kurgu(kuralVar: false);
        var (service, _, _) = Build(Baglam(kurgu));

        var rapor = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.Equal(0, rapor.BoundCount);
        Assert.Equal(0, rapor.FailedCount);
        Assert.Equal(1, rapor.TotalExamined);
    }

    [Fact(DisplayName = "GB13. İşlem imleçle kaldığı yerden devam eder")]
    public async Task Imlecle_devam_eder()
    {
        var kayitlar = Enumerable.Range(0, 3).Select(_ => Baglam(Kurgu())).ToArray();
        var (service, _, _) = Build(kayitlar);

        var ilk = await service.ApplyAsync(new RuleEvidenceBackfillRequest(BatchSize: 2));

        Assert.Equal(2, ilk.TotalExamined);
        Assert.True(ilk.HasMore);
        Assert.NotNull(ilk.NextCursor);

        var ikinci = await service.ApplyAsync(
            new RuleEvidenceBackfillRequest(ilk.NextCursor, BatchSize: 2));

        Assert.Equal(1, ikinci.TotalExamined);
        Assert.False(ikinci.HasMore);

        // Üç kaydın tamamı bağlandı; hiçbiri iki kez işlenmedi.
        Assert.All(kayitlar, k => Assert.Single(k.Opportunity.Rules.Single().Evidence));
    }

    [Fact(DisplayName = "GB14. Tek kaydın hatası turu durdurmaz")]
    public async Task Hata_turu_durdurmaz()
    {
        var saglam = Baglam(Kurgu());
        var repo = new FakeBackfillRepository([saglam], patlayanKimlik: Guid.CreateVersion7());

        var service = new RuleEvidenceBackfillService(
            repo, new FakeUnitOfWork(), new FixedClock(Now), new FakeEventPublisher(),
            NullLogger<RuleEvidenceBackfillService>.Instance);

        var rapor = await service.ApplyAsync(new RuleEvidenceBackfillRequest());

        Assert.Equal(1, rapor.FailedCount);
        Assert.Equal(1, rapor.BoundCount);
        Assert.Single(saglam.Opportunity.Rules.Single().Evidence);
    }

    [Fact(DisplayName = "GB15. Tur büyüklüğü sınırların dışına çıkamaz")]
    public void Tur_buyuklugu_sinirli()
    {
        Assert.Equal(1, new RuleEvidenceBackfillRequest(BatchSize: 0).SafeBatchSize);
        Assert.Equal(1, new RuleEvidenceBackfillRequest(BatchSize: -5).SafeBatchSize);
        Assert.Equal(
            RuleEvidenceBackfillRequest.MaximumBatchSize,
            new RuleEvidenceBackfillRequest(BatchSize: 100_000).SafeBatchSize);
    }
}

/// <summary>Bellek içi bağlam deposu; kayıt silme yeteneği <b>yoktur</b>.</summary>
internal sealed class FakeBackfillRepository : IRuleEvidenceBackfillRepository
{
    private readonly List<Guid> _sirali;
    private readonly Dictionary<Guid, RuleEvidenceBackfillContext> _kayitlar;
    private readonly Guid? _patlayanKimlik;

    public FakeBackfillRepository(
        IReadOnlyList<RuleEvidenceBackfillContext> kayitlar,
        Guid? patlayanKimlik = null)
    {
        _kayitlar = kayitlar.ToDictionary(k => k.Opportunity.Id);
        _patlayanKimlik = patlayanKimlik;

        _sirali = [.. _kayitlar.Keys];

        if (patlayanKimlik is { } kimlik)
        {
            _sirali.Add(kimlik);
        }

        _sirali.Sort();
    }

    public Task<IReadOnlyList<Guid>> ListCandidateIdsAsync(
        Guid? afterOpportunityId,
        int take,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. Sonrasi(afterOpportunityId).Take(take)]);

    public Task<bool> HasMoreAsync(Guid afterOpportunityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Sonrasi(afterOpportunityId).Any());

    public Task<RuleEvidenceBackfillContext?> LoadContextAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        if (opportunityId == _patlayanKimlik)
        {
            throw new InvalidOperationException("Kayıt okunamadı (test).");
        }

        return Task.FromResult(_kayitlar.GetValueOrDefault(opportunityId));
    }

    private IEnumerable<Guid> Sonrasi(Guid? imlec) =>
        imlec is { } deger ? _sirali.Where(id => id.CompareTo(deger) > 0) : _sirali;
}

/// <summary>Yayımlanan mesajları toplayan sahte yayıncı.</summary>
internal sealed class FakeEventPublisher : IEventPublisher
{
    public List<(string RoutingKey, object Payload)> Published { get; } = [];

    public Task PublishAsync<T>(string routingKey, T payload, CancellationToken cancellationToken = default)
        where T : class
    {
        Published.Add((routingKey, payload));
        return Task.CompletedTask;
    }
}
