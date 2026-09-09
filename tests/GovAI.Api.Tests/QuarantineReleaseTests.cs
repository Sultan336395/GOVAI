using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Common;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Karantinadan çıkarmanın sonuçları (Faz 3).
///
/// <para>
/// İnceleyicinin en sık yaptığı iş bu ve sonucu görünür olmalı: kayıt katalogdaki
/// yerine döner, <b>yeniden puanlanır</b> ve inceleyici karar vermeden önce kaydın ne
/// olduğunu görebilir.
/// </para>
///
/// <para>
/// Puanlama tetiklemesi eskiden yoktu: kayıt katalogda anında görünüyor ama firmaların
/// "Fırsat Eşleşmelerim" listesine ancak gece 03:30 toplu turundan sonra düşüyordu.
/// İnceleyici sabah bir kaydı geri alıyor, danışman gün boyu onu göremiyordu.
/// </para>
/// </summary>
public sealed class QuarantineReleaseTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _catalog = null!;
    private HttpClient _ingest = null!;
    private HttpClient _reviewer = null!;
    private SahteOlayYayinci _olaylar = null!;

    private const string Ilan =
        "MERSİN TEKNOPARK YÖNETİCİ ŞİRKETİ'NDEN: Yazılım geliştirme hizmeti alımı "
        + "ihalesi yapılacaktır. İhaleye katılacak isteklilerin en az 5 çalışanı "
        + "olması gerekmektedir. İhale tarihi 30.11.2026'dır.";

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _catalog = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformCatalogEmail);
        _ingest = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);
        _reviewer = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformReviewerEmail);
        _olaylar = _factory.Services.GetRequiredService<SahteOlayYayinci>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> QueryAsync<T>(Func<GovAiDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        return await query(db);
    }

    /// <summary>Karantinaya alınmış bir belge ve ondan türeyen fırsat kurar.</summary>
    private async Task<(Guid DocumentId, Guid OpportunityId)> KarantinaKayitKurAsync(string url)
    {
        var kaynak = await _catalog.PostAsJsonAsync("/api/sources", new
        {
            name = "Teknopark İhale " + Guid.CreateVersion7().ToString("N")[..8],
            type = "TenderPortal",
            baseUrl = "https://www.kosgeb.gov.tr",
            cronExpression = "0 6 * * *"
        });

        kaynak.EnsureSuccessStatusCode();
        var sourceId = (await kaynak.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var belge = await _ingest.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url,
            title = "Yazılım Geliştirme Hizmeti Alımı İhalesi",
            rawContent = Ilan,
            mediaType = "text/html",
            canonicalUrl = url,
            charset = "utf-8",
            httpStatusCode = 200
        });

        belge.EnsureSuccessStatusCode();
        var documentId = (await belge.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("documentId").GetGuid();

        var firsat = await _catalog.PostAsJsonAsync("/api/opportunities", new
        {
            sourceId,
            sourceDocumentId = documentId,
            sourceType = "TenderPortal",
            supportCategory = "Tender",
            title = "Yazılım Geliştirme Hizmeti Alımı İhalesi",
            publisher = "Mersin Teknopark",
            summary = Ilan,
            publishedAt = DateTimeOffset.UtcNow.AddDays(-3),
            deadline = DateTimeOffset.UtcNow.AddDays(60),
            sourceUrl = url,
            officialDocumentUrl = url,
            ruleExtractionConfidence = 0.8m,
            rules = Array.Empty<object>()
        });

        firsat.EnsureSuccessStatusCode();
        var opportunityId = (await firsat.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // Belgeyi karantinaya al; fırsat da birlikte katalogdan çıkar.
        (await _catalog.PostAsJsonAsync(
            $"/api/quarantine/{documentId}/reject",
            new { reason = "NeedsManualReview", note = "İnceleme için ayrıldı." }))
            .EnsureSuccessStatusCode();

        return (documentId, opportunityId);
    }

    [Fact(DisplayName = "KR1. Karantina listesi, belgeden türeyen fırsatın kimliğini taşır")]
    public async Task Liste_firsat_kimligini_tasir()
    {
        var (documentId, opportunityId) = await KarantinaKayitKurAsync(
            "https://www.kosgeb.gov.tr/ihale/kimlik-testi");

        var liste = await _reviewer.GetFromJsonAsync<JsonElement>("/api/quarantine");

        var satir = liste.EnumerateArray()
            .Single(x => x.GetProperty("documentId").GetGuid() == documentId);

        // Kimlik olmadan ekran detaya bağlantı veremez; inceleyicinin elinde yalnızca
        // başlık ve adres kalırdı.
        Assert.Equal(opportunityId, satir.GetProperty("opportunityId").GetGuid());
    }

    [Fact(DisplayName = "KR2. Fırsatı olmayan belgede kimlik boş döner")]
    public async Task Firsatsiz_belgede_kimlik_bos()
    {
        var kaynak = await _catalog.PostAsJsonAsync("/api/sources", new
        {
            name = "Mevzuat " + Guid.CreateVersion7().ToString("N")[..8],
            type = "OfficialGazette",
            baseUrl = "https://www.resmigazete.gov.tr",
            cronExpression = "0 6 * * *"
        });

        var sourceId = (await kaynak.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var belge = await _ingest.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url = "https://www.resmigazete.gov.tr/eskiler/menu",
            title = "İletişim",
            rawContent = "Kurumsal iletişim sayfası.",
            mediaType = "text/html",
            httpStatusCode = 200
        });

        var documentId = (await belge.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("documentId").GetGuid();

        var liste = await _reviewer.GetFromJsonAsync<JsonElement>("/api/quarantine");

        var satir = liste.EnumerateArray()
            .SingleOrDefault(x => x.GetProperty("documentId").GetGuid() == documentId);

        if (satir.ValueKind is JsonValueKind.Undefined)
        {
            return; // Kayıt karantinaya girmediyse bu testin konusu yok.
        }

        // Çalışmayan bir bağlantı göstermemek için kimlik boş kalmalı.
        Assert.True(
            !satir.TryGetProperty("opportunityId", out var kimlik)
            || kimlik.ValueKind is JsonValueKind.Null);
    }

    [Fact(DisplayName = "KR3. Karantinadan çıkan fırsat katalogdaki yerine döner")]
    public async Task Serbest_birakilan_katalogda_gorunur()
    {
        var (documentId, opportunityId) = await KarantinaKayitKurAsync(
            "https://www.kosgeb.gov.tr/ihale/serbest-testi");

        Assert.NotEqual(
            QuarantineReason.None,
            await QueryAsync(db => db.Opportunities.IgnoreQueryFilters()
                .Where(o => o.Id == opportunityId)
                .Select(o => o.QuarantineReason)
                .FirstAsync()));

        (await _reviewer.PostAsync($"/api/quarantine/{documentId}/approve", null))
            .EnsureSuccessStatusCode();

        Assert.Equal(
            QuarantineReason.None,
            await QueryAsync(db => db.Opportunities.IgnoreQueryFilters()
                .Where(o => o.Id == opportunityId)
                .Select(o => o.QuarantineReason)
                .FirstAsync()));
    }

    [Fact(DisplayName = "KR4. Karantinadan çıkan fırsat için yeniden puanlama istenir")]
    public async Task Serbest_birakilan_puanlanir()
    {
        var (documentId, opportunityId) = await KarantinaKayitKurAsync(
            "https://www.kosgeb.gov.tr/ihale/puanlama-testi");

        _olaylar.Temizle();

        (await _reviewer.PostAsync($"/api/quarantine/{documentId}/approve", null))
            .EnsureSuccessStatusCode();

        // Kayıt katalogda görünür ama puanlanmazsa firmaların eşleşme listesine
        // gece turuna kadar düşmez.
        var puanlama = _olaylar.Olaylar
            .Where(e => e.RoutingKey == QueueNames.ScoringRequested)
            .ToList();

        Assert.NotEmpty(puanlama);

        var govde = JsonSerializer.Serialize(puanlama[0].Payload);

        Assert.Contains(opportunityId.ToString(), govde, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QuarantineReleased", govde, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "KR6. Karantinadaki fırsatın detayı KİRACIYA kapalı kalır")]
    public async Task Karantinadaki_firsat_kiraciya_kapali()
    {
        var (_, opportunityId) = await KarantinaKayitKurAsync(
            "https://www.kosgeb.gov.tr/ihale/kiraci-testi");

        var kiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);

        var yanit = await kiraci.GetAsync($"/api/opportunities/{opportunityId}");

        // İnceleyiciye açmak bir GEVŞETME DEĞİLDİR: listeden gizlenen kaydın detayını
        // kiracıya açık bırakmak, korumayı işe yaramaz kılardı — bağlantı elden ele
        // dolaşabilir.
        Assert.Equal(System.Net.HttpStatusCode.NotFound, yanit.StatusCode);
    }

    [Fact(DisplayName = "KR7. Worker kimliği karantinadaki kaydın içeriğini okuyamaz")]
    public async Task Karantinadaki_firsat_workera_kapali()
    {
        var (_, opportunityId) = await KarantinaKayitKurAsync(
            "https://www.kosgeb.gov.tr/ihale/worker-testi");

        var yanit = await _ingest.GetAsync($"/api/opportunities/{opportunityId}");

        // Worker belge bırakır, karantina incelemez. Ele geçirilen bir worker
        // kimliğinin elenmiş kayıtları okuyabilmesi için sebep yok.
        Assert.Equal(System.Net.HttpStatusCode.NotFound, yanit.StatusCode);
    }

    [Fact(DisplayName = "KR5. Karantinadaki fırsatın detayı inceleyiciye açıktır")]
    public async Task Karantinadaki_firsat_detayi_okunur()
    {
        var (_, opportunityId) = await KarantinaKayitKurAsync(
            "https://www.kosgeb.gov.tr/ihale/detay-testi");

        var detay = await _reviewer.GetFromJsonAsync<JsonElement>($"/api/opportunities/{opportunityId}");

        // İnceleyici karar vermeden önce kaydın ne olduğunu görebilmeli; karantinada
        // olması detayı okumasını engellememeli.
        Assert.Equal("Yazılım Geliştirme Hizmeti Alımı İhalesi", detay.GetProperty("title").GetString());

        // Ekran "bu kayıt karantinada" diyebilmek için durumu bilmeli.
        Assert.NotEqual("None", detay.GetProperty("quarantineReason").GetString());
    }
}
