using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Mevcut kayıtlara geriye dönük kanıt bağlamanın uçtan uca sözleşmesi (Faz 3).
///
/// <para>
/// Kurgu bilerek <b>eski kayıt</b> gibidir: kural karakter aralığı olmadan yazılır, tıpkı
/// üretimdeki kurallar gibi. Bağlantı yalnızca alıntı metniyle kurulabilir ve bağın
/// gerçekten kurulup kurulmadığı veritabanındaki satırdan doğrulanır.
/// </para>
///
/// <para>
/// Gerçek PostgreSQL koşusunda tekil indeks de sınanır: ikinci <c>apply</c> mükerrer
/// satır yazmaya kalkışsa veritabanı reddederdi.
/// </para>
/// </summary>
public sealed class RuleEvidenceBackfillTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _catalog = null!;
    private HttpClient _ingest = null!;
    private HttpClient _tenantA = null!;

    private const string KobiDijital =
        "KOBİ Dijital Dönüşüm Destek Programı. Başvuru Şartları: Başvuru yapacak "
        + "işletmenin en az 10 çalışanı olmalıdır ve KOSGEB Veri Tabanında kayıtlı "
        + "olması gerekmektedir. Destek Unsurları: Yazılım ve donanım giderleri için "
        + "geri ödemesiz destek üst limiti 1.500.000 TL'dir.";

    private const string Kosul = "en az 10 çalışanı olmalıdır ve KOSGEB Veri Tabanında kayıtlı";

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _catalog = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformCatalogEmail);
        _ingest = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);
        _tenantA = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> QueryAsync<T>(Func<GovAiDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        return await query(db);
    }

    /// <summary>
    /// Kaynağı taranabilir ve doğrulanmış hâle getirir.
    ///
    /// <para>
    /// Doğrudan veritabanından yapılır: sınanan davranış kanıt bağlamadır, kaynak
    /// doğrulama akışı değil. Üretimde bu durum zaten sağlanır — doğrulanmamış kaynak
    /// hiç taranmaz (<c>Source.IsCrawlable</c>).
    /// </para>
    /// </summary>
    private async Task KaynagiDogrulaAsync(Guid sourceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();

        var source = await db.Sources.FirstAsync(s => s.Id == sourceId);

        source.Describe(
            SourceCategory.Grant,
            new SourceProfile("KOSGEB", "TR", "kosgeb.gov.tr", "tr"));

        source.PlanCrawl(new SourceCrawlPlan(
            "https://www.kosgeb.gov.tr/site/tr/genel/destekler",
            "a.destek-link", ".icerik", null, 1, null, "text/html"));

        source.MarkVerified(DateTimeOffset.UtcNow.AddDays(-1));

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Eski bir kaydı kurar: belge, parçalar ve <b>karakter aralığı olmayan</b> kural.
    /// </summary>
    private async Task<(Guid OpportunityId, Guid SourceId)> EskiKayitKurAsync(
        string url = "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/geri-donuk",
        string? alinti = Kosul)
    {
        var kaynak = await _catalog.PostAsJsonAsync("/api/sources", new
        {
            name = "KOSGEB Geriye Dönük " + Guid.CreateVersion7().ToString("N")[..8],
            type = "KosgebOrSimilar",
            baseUrl = "https://www.kosgeb.gov.tr",
            cronExpression = "0 6 * * *"
        });

        kaynak.EnsureSuccessStatusCode();
        var sourceId = (await kaynak.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await KaynagiDogrulaAsync(sourceId);

        var belge = await _ingest.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url,
            title = "KOBİ Dijital Dönüşüm Destek Programı",
            rawContent = KobiDijital,
            mediaType = "text/html",
            canonicalUrl = url,
            charset = "utf-8",
            httpStatusCode = 200
        });

        belge.EnsureSuccessStatusCode();
        var documentId = (await belge.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("documentId").GetGuid();

        var cumleler = KobiDijital.Split(". ", StringSplitOptions.RemoveEmptyEntries);
        var parcalar = new List<object>();
        var konum = 0;

        for (var i = 0; i < cumleler.Length; i++)
        {
            var baslangic = KobiDijital.IndexOf(cumleler[i], konum, StringComparison.Ordinal);
            konum = baslangic + cumleler[i].Length;

            parcalar.Add(new
            {
                sequenceNumber = i,
                text = cumleler[i],
                startOffset = baslangic,
                endOffset = konum,
                pageNumber = (int?)1,
                sectionTitle = (string?)"Başvuru Şartları",
                paragraphNumber = (int?)(i + 1)
            });
        }

        var parse = await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new
            {
                status = "Parsed",
                normalizedText = KobiDijital,
                title = "KOBİ Dijital Dönüşüm Destek Programı",
                language = "tr",
                pageCount = 1,
                chunks = parcalar
            });

        parse.EnsureSuccessStatusCode();

        // KURALDA ARALIK YOK: üretimdeki eski kayıtlar da böyledir.
        var firsat = await _catalog.PostAsJsonAsync("/api/opportunities", new
        {
            sourceId,
            sourceDocumentId = documentId,
            sourceType = "KosgebOrSimilar",
            supportCategory = "Grant",
            title = "KOBİ Dijital Dönüşüm Destek Programı",
            publisher = "KOSGEB",
            summary = KobiDijital[..200],
            publishedAt = DateTimeOffset.UtcNow.AddDays(-5),
            deadline = DateTimeOffset.UtcNow.AddDays(45),
            sourceUrl = url,
            officialDocumentUrl = url,
            ruleExtractionConfidence = 0.9m,
            rules = new[]
            {
                new
                {
                    field = "Workforce.EmployeeCount",
                    @operator = "GreaterThanOrEqual",
                    value = "10",
                    dimension = "Employment",
                    severity = "Major",
                    humanReadable = "En az 10 çalışan gereklidir.",
                    sourceExcerpt = alinti,
                    confidence = 1.0m
                }
            }
        });

        firsat.EnsureSuccessStatusCode();
        var opportunityId = (await firsat.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await KanitlariTemizleAsync(opportunityId);

        return (opportunityId, sourceId);
    }

    /// <summary>
    /// Kaydı, kanıt bağlama özelliği <b>eklenmeden önceki</b> hâline döndürür.
    ///
    /// <para>
    /// Kayıt uçtan uca gerçek akışla kuruldu ve upsert kanıtı zaten bağladı. Sınanmak
    /// istenen ise üretimdeki durum: özellik gelmeden önce yazılmış, hiç kanıtı olmayan
    /// kayıtlar. Bağları silmek o durumu birebir üretir — kural, belge, sürüm ve parçalar
    /// olduğu gibi kalır.
    /// </para>
    /// </summary>
    private async Task KanitlariTemizleAsync(Guid opportunityId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();

        var baglar = await (
            from e in db.OpportunityRuleEvidence
            join r in db.Set<OpportunityRule>() on e.OpportunityRuleId equals r.Id
            where r.OpportunityId == opportunityId
            select e).ToListAsync();

        db.OpportunityRuleEvidence.RemoveRange(baglar);
        await db.SaveChangesAsync();
    }

    private Task<int> BagSayisiAsync(Guid opportunityId) =>
        QueryAsync(db =>
            (from e in db.OpportunityRuleEvidence
             join r in db.Set<OpportunityRule>() on e.OpportunityRuleId equals r.Id
             where r.OpportunityId == opportunityId
             select e.Id).CountAsync());

    private static JsonElement Satir(JsonElement rapor, Guid opportunityId) =>
        rapor.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("opportunityId").GetGuid() == opportunityId);

    // ── Testler ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "GY1. Kiracı kullanıcısı toplu işlemi başlatamaz")]
    public async Task Tenant_baslatamaz()
    {
        var plan = await _tenantA.GetAsync("/api/opportunities/rule-evidence/backfill/plan");
        var apply = await _tenantA.PostAsync("/api/opportunities/rule-evidence/backfill/apply", null);

        Assert.Equal(HttpStatusCode.Forbidden, plan.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, apply.StatusCode);
    }

    [Fact(DisplayName = "GY2. Kuru çalıştırma veritabanına hiçbir bağ yazmaz")]
    public async Task Plan_veritabanina_yazmaz()
    {
        var (opportunityId, _) = await EskiKayitKurAsync(
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/plan-testi");

        Assert.Equal(0, await BagSayisiAsync(opportunityId));

        var rapor = await _catalog.GetFromJsonAsync<JsonElement>(
            "/api/opportunities/rule-evidence/backfill/plan?batchSize=500");

        Assert.False(rapor.GetProperty("applied").GetBoolean());
        Assert.Equal("Bound", Satir(rapor, opportunityId).GetProperty("outcome").GetString());

        // Rapor "bağlanacak" dedi ama veritabanı hâlâ boş.
        Assert.Equal(0, await BagSayisiAsync(opportunityId));
    }

    [Fact(DisplayName = "GY3. Mevcut içerikten bağ kurulur ve ikinci koşu mükerrer üretmez")]
    public async Task Apply_baglar_ve_idempotenttir()
    {
        var (opportunityId, _) = await EskiKayitKurAsync(
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/apply-testi");

        var ilk = await _catalog.PostAsync(
            "/api/opportunities/rule-evidence/backfill/apply?batchSize=500", null);

        ilk.EnsureSuccessStatusCode();
        var ilkRapor = await ilk.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(ilkRapor.GetProperty("applied").GetBoolean());
        Assert.Equal("Bound", Satir(ilkRapor, opportunityId).GetProperty("outcome").GetString());

        var sayi = await BagSayisiAsync(opportunityId);
        Assert.True(sayi > 0);

        var ikinci = await _catalog.PostAsync(
            "/api/opportunities/rule-evidence/backfill/apply?batchSize=500", null);

        ikinci.EnsureSuccessStatusCode();
        var ikinciRapor = await ikinci.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("AlreadyBound", Satir(ikinciRapor, opportunityId).GetProperty("outcome").GetString());
        Assert.Equal(sayi, await BagSayisiAsync(opportunityId));
    }

    [Fact(DisplayName = "GY4. Bağlanan kural için kanıt zinciri API çıktısında görünür")]
    public async Task Baglanan_kural_ekranda_gorunur()
    {
        var url = "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/zincir-testi";
        var (opportunityId, _) = await EskiKayitKurAsync(url);

        // Bağlanmadan önce: kural var, yapay zekâ ondan bahsedemez.
        var once = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{opportunityId}");
        var kuralOnce = once.GetProperty("rules").EnumerateArray().Single();

        Assert.False(kuralOnce.GetProperty("supportsAiClaims").GetBoolean());
        Assert.Empty(kuralOnce.GetProperty("evidence").EnumerateArray());

        (await _catalog.PostAsync("/api/opportunities/rule-evidence/backfill/apply?batchSize=500", null))
            .EnsureSuccessStatusCode();

        var sonra = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{opportunityId}");
        var kural = sonra.GetProperty("rules").EnumerateArray().Single();

        Assert.True(kural.GetProperty("supportsAiClaims").GetBoolean());

        var kanit = kural.GetProperty("evidence").EnumerateArray().First();

        Assert.NotEqual(Guid.Empty, kanit.GetProperty("evidenceChunkId").GetGuid());
        Assert.NotEqual(Guid.Empty, kanit.GetProperty("documentVersionId").GetGuid());
        Assert.Equal(64, kanit.GetProperty("textHash").GetString()!.Length);
        Assert.Equal(url, kanit.GetProperty("officialUrl").GetString());
        Assert.True(kanit.GetProperty("endOffset").GetInt32() > kanit.GetProperty("startOffset").GetInt32());
    }

    [Fact(DisplayName = "GY5. Kanıtı bulunamayan kural bağlanmaz ve silinmez")]
    public async Task Kaniti_bulunamayan_kural_korunur()
    {
        // Alıntı belgede geçmiyor: metni değişmiş eski bir kayıt.
        var (opportunityId, _) = await EskiKayitKurAsync(
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/kanitsiz-testi",
            "yıllık cirosu 50 milyon TL üzerinde olan işletmeler başvurabilir");

        var apply = await _catalog.PostAsync(
            "/api/opportunities/rule-evidence/backfill/apply?batchSize=500", null);

        apply.EnsureSuccessStatusCode();
        var rapor = await apply.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("NoEvidenceFound", Satir(rapor, opportunityId).GetProperty("outcome").GetString());
        Assert.Equal(0, await BagSayisiAsync(opportunityId));

        // Kural DURUYOR: deterministik motor onu kullanmaya devam eder.
        var detay = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{opportunityId}");
        var kural = detay.GetProperty("rules").EnumerateArray().Single();

        Assert.Equal("En az 10 çalışan gereklidir.", kural.GetProperty("humanReadable").GetString());
        Assert.False(kural.GetProperty("supportsAiClaims").GetBoolean());
    }

    [Fact(DisplayName = "GY6. Karantinadaki fırsat atlanır ve kanıt bağlanmaz")]
    public async Task Karantinadaki_kayit_atlanir()
    {
        var (opportunityId, _) = await EskiKayitKurAsync(
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/karantina-testi");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
            var firsat = await db.Opportunities.IgnoreQueryFilters().FirstAsync(o => o.Id == opportunityId);

            firsat.Quarantine(QuarantineReason.InvalidSourcePage, "Liste sayfası.");
            await db.SaveChangesAsync();
        }

        var apply = await _catalog.PostAsync(
            "/api/opportunities/rule-evidence/backfill/apply?batchSize=500", null);

        apply.EnsureSuccessStatusCode();
        var rapor = await apply.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("SkippedQuarantined", Satir(rapor, opportunityId).GetProperty("outcome").GetString());
        Assert.Equal(0, await BagSayisiAsync(opportunityId));
    }
}
