using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Domain.Common;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Ayrıştırıcının bilerek elediği belgeler.
///
/// <para>
/// Sahada görülen arıza: worker liste sayfasını eleyip <c>status="Skipped"</c> bildiriyor,
/// API ise bu değeri tanımadığı için 400 dönüyordu. Mesaj ölü kuyruğa düşüyor, eleme
/// kaydedilmiyor ve her liste sayfası günlüğe yığın izi bırakıyordu. Metin daha önceki
/// çağrıda kaydedildiği için veri kaybı yoktu — ama hata sessiz de değildi, anlamsızdı.
/// </para>
///
/// <para>
/// Asıl ders <c>Skipped</c> ile <c>Failed</c>'ın aynı şey OLMADIĞIdır: elenen belge
/// karantinaya alınmaz. Karantina inceleyiciye "buna bak" demektir; düzgün çalışan
/// sistemin ürününü oraya koymak gerçek arızaları görünmez yapar.
/// </para>
/// </summary>
public sealed class ElenenBelgeTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _ingest = null!;

    private const string ListeSayfasi =
        "<html><head><title>Destekler Listesi - KOSGEB</title></head><body>" +
        "<p>KOSGEB tarafından sunulan destek programlarının tam listesi aşağıdadır. " +
        "Her programın başvuru koşulları kendi sayfasında yer alır.</p></body></html>";

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();
        _ingest = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> KaynakAcAsync(string ad)
    {
        var katalog = await _factory.CreateAuthenticatedClientAsync(
            GovAiApiFactory.PlatformCatalogEmail);

        var cevap = await katalog.PostAsJsonAsync("/api/sources", new
        {
            name = ad,
            type = "KosgebOrSimilar",
            baseUrl = "https://www.kosgeb.gov.tr",
            cronExpression = "0 6 * * *",
            category = "Incentive",
            authority = "KOSGEB",
            jurisdiction = "TR",
            configurationJson = "{\"linkSelector\":\"a.destek\"}",
        });

        cevap.EnsureSuccessStatusCode();

        return (await cevap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> BelgeAlAsync(Guid sourceId, string url)
    {
        var cevap = await _ingest.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url,
            canonicalUrl = url,
            title = "Destekler Listesi",
            mediaType = "text/html",
            rawContent = ListeSayfasi,
            httpStatusCode = 200,
            charset = "utf-8",
        });

        cevap.EnsureSuccessStatusCode();

        return (await cevap.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("documentId").GetGuid();
    }

    private async Task<T> SorguAsync<T>(Func<GovAiDbContext, Task<T>> sorgu)
    {
        using var kapsam = _factory.Services.CreateScope();

        return await sorgu(kapsam.ServiceProvider.GetRequiredService<GovAiDbContext>());
    }

    [Fact(DisplayName = "EB1. Worker'ın bildirdiği Skipped durumu kabul edilir")]
    public async Task Skipped_durumu_kabul_edilir()
    {
        // Bu testin tek işi, worker'ın gönderdiği değerin API tarafında TANINDIĞINI
        // kanıtlamak. Arıza tam buradaydı: 400 "The JSON value could not be converted".
        var sourceId = await KaynakAcAsync("Eleme Testi");
        var documentId = await BelgeAlAsync(sourceId, "https://www.kosgeb.gov.tr/destekler");

        var cevap = await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new { status = "Skipped", error = "Liste sayfası; tek bir çağrı değildir." });

        cevap.EnsureSuccessStatusCode();

        var sonuc = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Skipped", sonuc.GetProperty("status").GetString());
    }

    [Fact(DisplayName = "EB2. Elenen belge KARANTİNAYA ALINMAZ")]
    public async Task Elenen_belge_karantinaya_alinmaz()
    {
        var sourceId = await KaynakAcAsync("Eleme Karantina Testi");
        var documentId = await BelgeAlAsync(sourceId, "https://www.kosgeb.gov.tr/destekler/2");

        await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new { status = "Skipped", error = "Liste sayfası; tek bir çağrı değildir." });

        var belge = await SorguAsync(db => db.SourceDocuments.SingleAsync(d => d.Id == documentId));

        Assert.False(belge.IsQuarantined);
        Assert.Equal(QuarantineReason.None, belge.QuarantineReason);
    }

    [Fact(DisplayName = "EB3. Eleme gerekçesi kaydedilir ve ezilmez")]
    public async Task Eleme_gerekcesi_kaydedilir()
    {
        var sourceId = await KaynakAcAsync("Eleme Gerekçe Testi");
        var documentId = await BelgeAlAsync(sourceId, "https://www.kosgeb.gov.tr/destekler/3");

        await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new { status = "Skipped", error = "Başlık kurumsal sayfa başlığı; çağrı kaydı açılmadı." });

        var surum = await SorguAsync(db => db.SourceDocumentVersions
            .SingleAsync(v => v.SourceDocumentId == documentId));

        Assert.Equal(DocumentParseStatus.Skipped, surum.ParseStatus);
        Assert.Equal("Başlık kurumsal sayfa başlığı; çağrı kaydı açılmadı.", surum.ParseError);
        Assert.False(surum.RequiresOcr);
    }

    [Fact(DisplayName = "EB4. Eleme önce kaydedilmiş metni ve kanıt parçalarını silmez")]
    public async Task Eleme_kaniti_silmez()
    {
        // Worker gerçek sırayı şöyle işletir: metni ve parçaları kaydeder (Parsed),
        // SONRA başlığa bakıp kayıt açmamaya karar verir. İkinci çağrı birincinin
        // ürününü götürürse kanıt zinciri kopar ve belge boş görünür.
        var sourceId = await KaynakAcAsync("Eleme Kanıt Testi");
        var documentId = await BelgeAlAsync(sourceId, "https://www.kosgeb.gov.tr/destekler/4");

        var metin = "KOSGEB tarafından sunulan destek programlarının tam listesi aşağıdadır. " +
                    "Her programın başvuru koşulları kendi sayfasında yer alır.";

        var ayristir = await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new
            {
                status = "Parsed",
                normalizedText = metin,
                title = "Destekler Listesi",
                language = "tr",
                chunks = new[]
                {
                    new
                    {
                        sequenceNumber = 1,
                        text = metin,
                        startOffset = 0,
                        endOffset = metin.Length,
                    },
                },
            });

        ayristir.EnsureSuccessStatusCode();

        var elendi = await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new { status = "Skipped", error = "Liste sayfası; tek bir çağrı değildir." });

        elendi.EnsureSuccessStatusCode();

        var surum = await SorguAsync(db => db.SourceDocumentVersions
            .Include(v => v.Chunks)
            .SingleAsync(v => v.SourceDocumentId == documentId));

        Assert.Equal(DocumentParseStatus.Skipped, surum.ParseStatus);
        Assert.Equal(metin, surum.NormalizedText);
        Assert.NotEmpty(surum.Chunks);

        // Dönen sayı da kaybolan bir şey olmadığını söyler.
        var sonuc = await elendi.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(surum.Chunks.Count, sonuc.GetProperty("chunkCount").GetInt32());
    }

    /// <summary>Belgeden gerçek bir çağrı kaydı açtırır; sonrasını testler kurar.</summary>
    private async Task<Guid> FirsatAcAsync(Guid sourceId, Guid documentId, string baslik)
    {
        var cevap = await _ingest.PostAsJsonAsync("/api/opportunities", new
        {
            sourceId,
            sourceDocumentId = documentId,
            sourceType = "KosgebOrSimilar",
            supportCategory = "Grant",
            title = baslik,
            publisher = "KOSGEB",
            summary = "KOSGEB tarafından açılan örnek destek çağrısının özeti.",
            sourceUrl = "https://www.kosgeb.gov.tr/destekler/ornek",
            publishedAt = DateTimeOffset.UtcNow.AddDays(-1),
            deadline = DateTimeOffset.UtcNow.AddDays(30),
        });

        cevap.EnsureSuccessStatusCode();

        return (await cevap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact(DisplayName = "EB6. Belge çağrı sayfası olmaktan çıkınca kayıt katalogdan çekilir")]
    public async Task Elenen_belgenin_eski_kaydi_katalogdan_cekilir()
    {
        // Sahadaki boşluk: ayrıştırıcı liste sayfasından artık kayıt AÇMIYOR, ama daha
        // önce açılmış kayıt katalogda kalıyordu. Kurum sayfasının içeriğini değiştirince
        // aynısı yeniden olurdu.
        var sourceId = await KaynakAcAsync("Geri Çekme Testi");
        var documentId = await BelgeAlAsync(sourceId, "https://www.kosgeb.gov.tr/destekler/6");
        var opportunityId = await FirsatAcAsync(sourceId, documentId, "Örnek Destek Çağrısı");

        var once = await SorguAsync(db => db.Opportunities.SingleAsync(o => o.Id == opportunityId));
        Assert.True(once.IsPublishable);

        await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new { status = "Skipped", error = "Liste sayfası; tek bir çağrı değildir." });

        var sonra = await SorguAsync(db => db.Opportunities.SingleAsync(o => o.Id == opportunityId));

        Assert.False(sonra.IsPublishable);
        Assert.Equal(QuarantineReason.InvalidSourcePage, sonra.QuarantineReason);
        Assert.Equal("Liste sayfası; tek bir çağrı değildir.", sonra.QuarantineNote);
    }

    [Fact(DisplayName = "EB7. Geri çekilen kayıt SİLİNMEZ")]
    public async Task Geri_cekilen_kayit_silinmez()
    {
        var sourceId = await KaynakAcAsync("Geri Çekme Silme Testi");
        var documentId = await BelgeAlAsync(sourceId, "https://www.kosgeb.gov.tr/destekler/7");
        var opportunityId = await FirsatAcAsync(sourceId, documentId, "Silinmeyecek Çağrı");

        await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new { status = "Skipped", error = "Liste sayfası; tek bir çağrı değildir." });

        var kayit = await SorguAsync(db => db.Opportunities
            .IgnoreQueryFilters()
            .SingleAsync(o => o.Id == opportunityId));

        Assert.False(kayit.IsDeleted);
        Assert.Equal("Silinmeyecek Çağrı", kayit.Title);
    }

    [Fact(DisplayName = "EB8. İnceleyicinin kendi kararı EZİLMEZ")]
    public async Task Inceleyicinin_karari_ezilmez()
    {
        // Kayıt zaten karantinadaysa oradaki gerekçe insanın verdiği karardır.
        // Onu makine gerekçesiyle değiştirmek, inceleyicinin niçin öyle karar verdiğini
        // silmek olurdu.
        var sourceId = await KaynakAcAsync("Karar Ezilmez Testi");
        var documentId = await BelgeAlAsync(sourceId, "https://www.kosgeb.gov.tr/destekler/8");
        var opportunityId = await FirsatAcAsync(sourceId, documentId, "İnceleyici Kararı Testi");

        // İnceleyicinin kararı doğrudan kurulur: bu testin konusu kararın nasıl
        // verildiği değil, verilmiş bir kararın korunması.
        using (var kapsam = _factory.Services.CreateScope())
        {
            var db = kapsam.ServiceProvider.GetRequiredService<GovAiDbContext>();
            var oncekiKarar = await db.Opportunities.SingleAsync(o => o.Id == opportunityId);

            oncekiKarar.Quarantine(QuarantineReason.Duplicate, "Aynı ihale başka kayıtta duruyor.");
            await db.SaveChangesAsync();
        }

        await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new { status = "Skipped", error = "Liste sayfası; tek bir çağrı değildir." });

        var kayit = await SorguAsync(db => db.Opportunities.SingleAsync(o => o.Id == opportunityId));

        Assert.Equal(QuarantineReason.Duplicate, kayit.QuarantineReason);
        Assert.Equal("Aynı ihale başka kayıtta duruyor.", kayit.QuarantineNote);
    }

    [Fact(DisplayName = "EB5. Elenen belgeden fırsat kaydı açılmaz")]
    public async Task Elenen_belgeden_firsat_acilmaz()
    {
        var sourceId = await KaynakAcAsync("Eleme Fırsat Testi");
        var documentId = await BelgeAlAsync(sourceId, "https://www.kosgeb.gov.tr/destekler/5");

        await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new { status = "Skipped", error = "Liste sayfası; tek bir çağrı değildir." });

        var firsatVar = await SorguAsync(db => db.Opportunities
            .AnyAsync(o => o.SourceDocumentId == documentId));

        Assert.False(firsatVar);
    }
}
