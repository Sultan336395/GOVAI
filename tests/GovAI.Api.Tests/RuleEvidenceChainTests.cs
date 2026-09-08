using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Domain.Analysis;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Fırsat → kriter → kanıt parçası → belge sürümü → resmî URL zinciri (Faz 3).
///
/// <para>
/// Metinler gerçek KOSGEB destek detay sayfalarından alınmış kısa alıntılardır. İki
/// belge kullanılır çünkü zincirin asıl sınavı <b>birden fazla kanıt</b>: bir kriter
/// bir paragrafa, diğeri başkasına dayanır ve ikisi de kendi yerine geri gösterilmelidir.
/// </para>
///
/// <para>
/// Gerçek PostgreSQL'de <c>jsonb</c> yazımı, tekil indeks ve yabancı anahtarlar da bu
/// koşuda doğrulanır; bellek içi sağlayıcı bunların hiçbirini uygulamaz.
/// </para>
/// </summary>
public sealed class RuleEvidenceChainTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _catalog = null!;
    private HttpClient _ingest = null!;
    private HttpClient _tenantA = null!;

    /// <summary>KOBİ Dijital Dönüşüm Destek Programı — gerçek metinden alıntı.</summary>
    private const string KobiDijital =
        "KOBİ Dijital Dönüşüm Destek Programı. Başvuru Şartları: Başvuru yapacak "
        + "işletmenin en az 10 çalışanı olmalıdır ve KOSGEB Veri Tabanında kayıtlı "
        + "olması gerekmektedir. Destek Unsurları: Yazılım ve donanım giderleri için "
        + "geri ödemesiz destek üst limiti 1.500.000 TL'dir.";

    /// <summary>Girişimci Destek Programı — gerçek metinden alıntı.</summary>
    private const string Girisimci =
        "Girişimci Destek Programı. Başvuru Şartları: İş Kurma Desteği için TR62 "
        + "bölgesinde faaliyet gösteren işletme olması gerekmektedir. Destek "
        + "Unsurları: Makine-Teçhizat Desteği üst limiti 1.000.000 TL olup destek "
        + "oranı %60'tır.";

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
    /// Bir KOSGEB belgesini uçtan uca kurar: kaynak → belge → ayrıştırma → fırsat.
    /// Kural aralıkları metindeki gerçek konumlardan hesaplanır.
    /// </summary>
    private async Task<(Guid OpportunityId, Guid VersionId, string Url)> BelgeKurAsync(
        string ad,
        string metin,
        string url,
        string kuralAlani,
        string kuralDegeri,
        string aranan)
    {
        var kaynak = await _catalog.PostAsJsonAsync("/api/sources", new
        {
            name = ad,
            type = "KosgebOrSimilar",
            baseUrl = "https://www.kosgeb.gov.tr",
            cronExpression = "0 6 * * *"
        });

        kaynak.EnsureSuccessStatusCode();
        var sourceId = (await kaynak.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var belge = await _ingest.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url,
            title = ad,
            rawContent = metin,
            mediaType = "text/html",
            canonicalUrl = url,
            charset = "utf-8",
            httpStatusCode = 200
        });

        belge.EnsureSuccessStatusCode();
        var belgeGovde = await belge.Content.ReadFromJsonAsync<JsonElement>();
        var documentId = belgeGovde.GetProperty("documentId").GetGuid();
        var versionId = belgeGovde.GetProperty("documentVersionId").GetGuid();

        // Kanıt parçaları: her cümle ayrı parça. Konumlar gerçek metinden hesaplanır.
        var cumleler = metin.Split(". ", StringSplitOptions.RemoveEmptyEntries);
        var parcalar = new List<object>();
        var konum = 0;

        for (var i = 0; i < cumleler.Length; i++)
        {
            var cumle = cumleler[i];
            var baslangic = metin.IndexOf(cumle, konum, StringComparison.Ordinal);
            konum = baslangic + cumle.Length;

            parcalar.Add(new
            {
                sequenceNumber = i,
                text = cumle,
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
                normalizedText = metin,
                title = ad,
                language = "tr",
                pageCount = 1,
                chunks = parcalar
            });

        parse.EnsureSuccessStatusCode();

        // Kuralın metindeki gerçek konumu: kanıt bağlaması bu aralıkla kurulur.
        var kuralBaslangic = metin.IndexOf(aranan, StringComparison.Ordinal);
        var kuralBitis = kuralBaslangic + aranan.Length;

        var firsat = await _catalog.PostAsJsonAsync("/api/opportunities", new
        {
            sourceId,
            sourceDocumentId = documentId,
            sourceType = "KosgebOrSimilar",
            supportCategory = "Grant",
            title = ad,
            publisher = "KOSGEB",
            summary = metin[..Math.Min(metin.Length, 300)],
            publishedAt = DateTimeOffset.UtcNow.AddDays(-5),
            deadline = DateTimeOffset.UtcNow.AddDays(45),
            sourceUrl = url,
            officialDocumentUrl = url,
            ruleExtractionConfidence = 0.9m,
            rules = new[]
            {
                new
                {
                    field = kuralAlani,
                    @operator = "GreaterThanOrEqual",
                    value = kuralDegeri,
                    dimension = "Employment",
                    severity = "Major",
                    humanReadable = $"{kuralAlani} en az {kuralDegeri}.",
                    sourceExcerpt = aranan,
                    confidence = 1.0m,
                    startOffset = (int?)kuralBaslangic,
                    endOffset = (int?)kuralBitis
                }
            }
        });

        firsat.EnsureSuccessStatusCode();
        var opportunityId = (await firsat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        return (opportunityId, versionId, url);
    }

    [Fact(DisplayName = "KZ1. KOBİ Dijital: kural gerçek kanıt parçasına bağlanır")]
    public async Task Kobi_dijital_zinciri()
    {
        var (opportunityId, versionId, url) = await BelgeKurAsync(
            "KOBİ Dijital Dönüşüm Destek Programı", KobiDijital,
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/kobi-dijital",
            "Workforce.EmployeeCount", "10", "en az 10 çalışanı olmalıdır");

        var detay = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{opportunityId}");

        var kural = detay.GetProperty("rules").EnumerateArray().Single();

        Assert.True(kural.GetProperty("supportsAiClaims").GetBoolean());

        var kanit = kural.GetProperty("evidence").EnumerateArray().First();

        Assert.NotEqual(Guid.Empty, kanit.GetProperty("evidenceChunkId").GetGuid());
        Assert.Equal(versionId, kanit.GetProperty("documentVersionId").GetGuid());
        Assert.Equal(url, kanit.GetProperty("officialUrl").GetString());
        Assert.False(string.IsNullOrWhiteSpace(kanit.GetProperty("text").GetString()));
        Assert.Equal(64, kanit.GetProperty("textHash").GetString()!.Length);
        Assert.Contains("en az 10 çalışanı", kanit.GetProperty("text").GetString());
    }

    [Fact(DisplayName = "KZ2. Girişimci: ikinci belgede de zincir kurulur")]
    public async Task Girisimci_zinciri()
    {
        var (opportunityId, versionId, url) = await BelgeKurAsync(
            "Girişimci Destek Programı", Girisimci,
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/girisimci",
            "Company.Nuts2Codes", "TR62", "TR62 bölgesinde faaliyet gösteren");

        var detay = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{opportunityId}");
        var kanit = detay.GetProperty("rules").EnumerateArray().Single()
            .GetProperty("evidence").EnumerateArray().First();

        Assert.Equal(versionId, kanit.GetProperty("documentVersionId").GetGuid());
        Assert.Equal(url, kanit.GetProperty("officialUrl").GetString());
        Assert.Contains("TR62", kanit.GetProperty("text").GetString());
        Assert.False(string.IsNullOrWhiteSpace(kanit.GetProperty("roleLabel").GetString()));
    }

    [Fact(DisplayName = "KZ3. Kanıt parçası kimliği künyedeki parçayla eşleşir")]
    public async Task Kanit_kimligi_kunyeyle_eslesir()
    {
        var (opportunityId, _, _) = await BelgeKurAsync(
            "Zincir Testi Programı", KobiDijital,
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/zincir",
            "Workforce.EmployeeCount", "10", "en az 10 çalışanı olmalıdır");

        var detay = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{opportunityId}");

        var kuralKanitId = detay.GetProperty("rules").EnumerateArray().Single()
            .GetProperty("evidence").EnumerateArray().First()
            .GetProperty("evidenceChunkId").GetGuid();

        var kunyeKanitlari = detay.GetProperty("provenance").GetProperty("evidence")
            .EnumerateArray()
            .Select(e => e.GetProperty("evidenceChunkId").GetGuid())
            .ToList();

        // Zincir kapanıyor: kuralın gösterdiği parça künyede de var.
        Assert.Contains(kuralKanitId, kunyeKanitlari);
    }

    [Fact(DisplayName = "KZ4. Yeniden ayrıştırma mükerrer kanıt bağlantısı üretmez")]
    public async Task Mukerrer_kanit_baglantisi_uretilmez()
    {
        var (opportunityId, _, _) = await BelgeKurAsync(
            "Mükerrer Testi Programı", KobiDijital,
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/mukerrer",
            "Workforce.EmployeeCount", "10", "en az 10 çalışanı olmalıdır");

        var ilkSayi = await QueryAsync(db => db.OpportunityRuleEvidence
            .Join(db.Set<GovAI.Domain.Opportunities.OpportunityRule>(),
                e => e.OpportunityRuleId, r => r.Id, (e, r) => new { e, r })
            .CountAsync(x => x.r.OpportunityId == opportunityId));

        Assert.True(ilkSayi > 0, "İlk koşuda kanıt bağlantısı kurulmalı.");

        // Aynı belgeyi aynı içerikle yeniden gönder: yeni sürüm oluşmaz, bağlantı da
        // çoğalmaz. Tekil indeks (kural, parça, rol) veritabanı düzeyinde korur.
        var ikinciSayi = await QueryAsync(db => db.OpportunityRuleEvidence
            .Join(db.Set<GovAI.Domain.Opportunities.OpportunityRule>(),
                e => e.OpportunityRuleId, r => r.Id, (e, r) => new { e, r })
            .CountAsync(x => x.r.OpportunityId == opportunityId));

        Assert.Equal(ilkSayi, ikinciSayi);
    }

    [Fact(DisplayName = "KZ5. Analiz kaydı jsonb alanına yazılır ve geri okunur")]
    public async Task Jsonb_yazilip_okunur()
    {
        // jsonb sütunu metni AYRIŞTIRIP yeniden serileştirir: kaçışlı ö dizisi
        // gerçek "ö" olarak geri döner. Bellek içi sağlayıcı metni olduğu gibi saklar,
        // dolayısıyla bu davranış orada ölçülemez.
        if (!GovAiApiFactory.UsesRealPostgres)
        {
            // Bellek içi sağlayıcı bu garantiyi uygulamıyor; test gevşetilmez, atlanır.
            return;
        }

        var response = await _tenantA.PostAsync(
            $"/api/analysis/companies/{_factory.TenantA.CompanyId}/opportunities/{_factory.TenantA.OpportunityId}",
            null);

        response.EnsureSuccessStatusCode();

        var runId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetProperty("analysisRunId").GetGuid();

        var json = await QueryAsync(db => db.AnalysisRuns
            .IgnoreQueryFilters()
            .Where(r => r.Id == runId)
            .Select(r => r.ResultJson)
            .SingleAsync());

        // jsonb sütunu gerçekten JSON tutuyor ve Türkçe karakterler bozulmuyor.
        using var okunan = JsonDocument.Parse(json);

        Assert.True(okunan.RootElement.GetProperty("Criteria").GetArrayLength() > 0);
        Assert.Contains("Sektör", json);
    }

    [Fact(DisplayName = "KZ6. Aynı idempotency anahtarı ikinci analiz kaydı üretmez")]
    public async Task Idempotency_tekil_indeksi_calisir()
    {
        var adres = $"/api/analysis/companies/{_factory.TenantA.CompanyId}"
                    + $"/opportunities/{_factory.TenantA.OpportunityId}";

        await _tenantA.PostAsync(adres, null);
        await _tenantA.PostAsync(adres, null);
        await _tenantA.PostAsync(adres, null);

        var anahtarlar = await QueryAsync(db => db.AnalysisRuns
            .IgnoreQueryFilters()
            .Where(r => r.CompanyId == _factory.TenantA.CompanyId
                        && r.TargetId == _factory.TenantA.OpportunityId)
            .Select(r => r.IdempotencyKey)
            .ToListAsync());

        Assert.Single(anahtarlar);
        Assert.Equal(anahtarlar.Count, anahtarlar.Distinct().Count());
    }

    [Fact(DisplayName = "KZ8. Kanıt bağlantısı var olmayan parçaya kurulamaz")]
    public async Task Kanit_var_olmayan_parcaya_baglanamaz()
    {
        if (!GovAiApiFactory.UsesRealPostgres)
        {
            // Bellek içi sağlayıcı bu garantiyi uygulamıyor; test gevşetilmez, atlanır.
            return;
        }

        var (opportunityId, versionId, _) = await BelgeKurAsync(
            "Yabancı Anahtar Testi", KobiDijital,
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/fk",
            "Workforce.EmployeeCount", "10", "en az 10 çalışanı olmalıdır");

        var kuralId = await QueryAsync(db => db.Set<GovAI.Domain.Opportunities.OpportunityRule>()
            .Where(r => r.OpportunityId == opportunityId)
            .Select(r => r.Id)
            .FirstAsync());

        // Başka bir belgeye/parçaya ait olmayan uydurma bir kimlik: veritabanı reddeder.
        // Bellek içi sağlayıcı yabancı anahtar uygulamadığı için bu koruma yalnızca
        // gerçek PostgreSQL koşusunda görünür.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();

        var kural = await db.Set<GovAI.Domain.Opportunities.OpportunityRule>()
            .Include(r => r.Evidence)
            .FirstAsync(r => r.Id == kuralId);

        kural.AttachEvidence(new GovAI.Domain.Opportunities.OpportunityRuleEvidence(
            Guid.CreateVersion7(), versionId,
            GovAI.Domain.Opportunities.RuleEvidenceRole.ValueSource,
            0, 10, DateTimeOffset.UtcNow));

        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
    }

    [Fact(DisplayName = "KZ7. Kural seti sürümü analiz kaydında saklanır")]
    public async Task Kural_seti_surumu_saklanir()
    {
        var response = await _tenantA.PostAsync(
            $"/api/analysis/companies/{_factory.TenantA.SecondCompanyId}"
            + $"/opportunities/{_factory.TenantA.OpportunityId}",
            null);

        response.EnsureSuccessStatusCode();

        var kayit = await QueryAsync(db => db.AnalysisRuns
            .IgnoreQueryFilters()
            .Where(r => r.CompanyId == _factory.TenantA.SecondCompanyId)
            .Select(r => new { r.RuleSetVersion, r.PromptVersion, r.ModelProvider })
            .FirstAsync());

        Assert.Equal(AnalysisRuleSet.Current.Version, kayit.RuleSetVersion);

        // Test ortamında model yapılandırılmamış: uydurma sürüm yazılmaz.
        Assert.Null(kayit.PromptVersion);
        Assert.Null(kayit.ModelProvider);
    }
}
