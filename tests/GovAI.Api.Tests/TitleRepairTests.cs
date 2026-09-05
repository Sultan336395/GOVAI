using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Common;
using GovAI.Domain.Sources;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Bozuk başlıklı belgelerin yeniden ayrıştırılması (Faz 2).
///
/// <para>
/// Karakter kümesi tespiti düzeltildi, ama o düzeltmeden <b>önce</b> toplanmış
/// kayıtlar bozuk başlıklarla duruyor. Onarım üç şeyi birden sağlamak zorundadır:
/// başlık tahmin edilmez, hiçbir kayıt silinmez ve ikinci çalıştırma hiçbir şeyi
/// değiştirmez.
/// </para>
/// </summary>
public sealed class TitleRepairTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private SahteIndirici _indirici = null!;
    private HttpClient _reviewer = null!;
    private HttpClient _ingest = null!;
    private HttpClient _tenant = null!;

    /// <summary>Resmî Gazete başlığının windows-1254 gövdesi ISO-8859-1 sanılınca oluşan hâli.</summary>
    private const string BozukBaslik = "ARTIRMA, EKSÝLTME VE ÝHALE ÝLÂNLARI";

    private const string DogruBaslik =
        "Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğünden: TAŞINMAZ SATILACAKTIR";

    private const string Icerik =
        "Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğünden: TAŞINMAZ SATILACAKTIR. "
        + "İhale 15/09/2026 tarihinde saat 10.00'da İdare binasında yapılacaktır. "
        + "Muhammen bedel 4.250.000,00 TL, geçici teminat %3 oranındadır.";

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _indirici = _factory.Services.GetRequiredService<SahteIndirici>();
        _indirici.Temizle();

        _reviewer = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformReviewerEmail);
        _ingest = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);
        _tenant = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> QueryAsync<T>(Func<GovAiDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        return await query(db);
    }

    /// <summary>Resmî alan adı tanımlı bir kaynak ve bozuk başlıklı bir belge açar.</summary>
    private async Task<(Guid SourceId, Guid DocumentId, string Url)> BozukBelgeAsync(
        string sourceName,
        string? officialDomain = "resmigazete.gov.tr",
        string url = "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-1.htm")
    {
        var katalog = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformCatalogEmail);

        var response = await katalog.PostAsJsonAsync("/api/sources", new
        {
            name = sourceName,
            type = "OfficialGazette",
            baseUrl = "https://www.resmigazete.gov.tr",
            cronExpression = "0 6 * * *"
        });

        response.EnsureSuccessStatusCode();
        var sourceId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
            var source = await db.Sources.SingleAsync(s => s.Id == sourceId);
            source.Describe(
                SourceCategory.Tender,
                new SourceProfile("Resmî Gazete", "TR", officialDomain, "tr"));
            await db.SaveChangesAsync();
        }

        var belge = await _ingest.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url,
            title = BozukBaslik,
            rawContent = Icerik,
            mediaType = "text/html",
            canonicalUrl = url,
            charset = "iso-8859-1",
            httpStatusCode = 200
        });

        belge.EnsureSuccessStatusCode();
        var documentId = (await belge.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();

        return (sourceId, documentId, url);
    }

    // ═══════════════ Kuru çalıştırma ═══════════════

    [Fact(DisplayName = "TR-A. Plan hangi kayıtların değişeceğini söyler ve hiçbir şeyi değiştirmez")]
    public async Task Plan_hicbir_seyi_degistirmez()
    {
        var (_, documentId, _) = await BozukBelgeAsync("Plan Testi");

        var plan = await _reviewer.GetFromJsonAsync<JsonElement>("/api/sources/title-repair/plan");

        Assert.True(plan.GetProperty("corrupt").GetInt32() >= 1);

        var adaylar = plan.GetProperty("candidates").EnumerateArray()
            .Where(a => a.GetProperty("documentId").GetGuid() == documentId)
            .ToList();

        Assert.Single(adaylar);
        Assert.True(adaylar[0].GetProperty("canRepair").GetBoolean());
        Assert.Equal(BozukBaslik, adaylar[0].GetProperty("currentTitle").GetString());

        // Plan resmî kaynağa İSTEK GÖNDERMEZ.
        Assert.Equal(0, _indirici.CagriSayisi);

        // Ve hiçbir kaydı değiştirmez.
        var baslik = await QueryAsync(db => db.SourceDocuments
            .Where(d => d.Id == documentId).Select(d => d.Title).SingleAsync());

        Assert.Equal(BozukBaslik, baslik);
    }

    [Fact(DisplayName = "TR-B. Resmî adresi doğrulanamayan kayıt onarılamaz olarak işaretlenir")]
    public async Task Dogrulanamayan_kayit_atlanir()
    {
        // Kaynağın resmî alan adı tanımsız: bağlantı doğrulanamaz.
        var (_, documentId, _) = await BozukBelgeAsync("Alan Adsız Kaynak", officialDomain: null);

        var plan = await _reviewer.GetFromJsonAsync<JsonElement>("/api/sources/title-repair/plan");

        var aday = plan.GetProperty("candidates").EnumerateArray()
            .Single(a => a.GetProperty("documentId").GetGuid() == documentId);

        Assert.False(aday.GetProperty("canRepair").GetBoolean());
        Assert.Contains(
            "doğrulanamıyor",
            aday.GetProperty("skipReason").GetString()!,
            StringComparison.Ordinal);
    }

    // ═══════════════ Uygulama ═══════════════

    [Fact(DisplayName = "TR-C. Başlık resmî kaynaktan yeniden indirilerek düzeltilir")]
    public async Task Baslik_resmi_kaynaktan_duzeltilir()
    {
        var (_, documentId, url) = await BozukBelgeAsync("Onarım Testi");

        // Resmî kaynak doğru başlığı ve DEĞİŞMEMİŞ içeriği veriyor.
        _indirici.Ayarla(url, new DownloadedDocument(
            Icerik, "text/html", "utf-8", url, 200, DogruBaslik));

        var sonuc = await _reviewer.PostAsync("/api/sources/title-repair/apply", null);
        sonuc.EnsureSuccessStatusCode();

        var rapor = await sonuc.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(rapor.GetProperty("repaired").GetInt32() >= 1);

        var belge = await QueryAsync(db => db.SourceDocuments
            .Include(d => d.Versions)
            .SingleAsync(d => d.Id == documentId));

        // Başlık indirilen belgeden geldi; tahmin edilmedi.
        Assert.Equal(DogruBaslik, belge.Title);

        // İçerik aynı olduğu için YENİ SÜRÜM AÇILMADI.
        Assert.Single(belge.Versions);
        Assert.Equal(1, belge.Revision);
    }

    [Fact(DisplayName = "TR-D. İçerik gerçekten değiştiyse yeni sürüm açılır, eskisi silinmez")]
    public async Task Icerik_degistiyse_yeni_surum_acilir()
    {
        var (_, documentId, url) = await BozukBelgeAsync("Sürüm Testi");

        const string yeniIcerik = Icerik + " DÜZELTME: ihale tarihi 22/09/2026 olarak değiştirilmiştir.";

        _indirici.Ayarla(url, new DownloadedDocument(
            yeniIcerik, "text/html", "utf-8", url, 200, DogruBaslik));

        var sonuc = await _reviewer.PostAsync("/api/sources/title-repair/apply", null);
        sonuc.EnsureSuccessStatusCode();

        var belge = await QueryAsync(db => db.SourceDocuments
            .Include(d => d.Versions)
            .SingleAsync(d => d.Id == documentId));

        Assert.Equal(DogruBaslik, belge.Title);
        Assert.Equal(2, belge.Revision);

        // Eski sürüm YERİNDE: kanıt zinciri kopmaz.
        Assert.Equal(2, belge.Versions.Count);
        Assert.Contains(belge.Versions, v => v.RawContent == Icerik);
        Assert.Contains(belge.Versions, v => v.RawContent == yeniIcerik);
    }

    [Fact(DisplayName = "TR-E. İkinci çalıştırma hiçbir şeyi değiştirmez (idempotent)")]
    public async Task Ikinci_calistirma_degistirmez()
    {
        var (_, documentId, url) = await BozukBelgeAsync("İdempotentlik Testi");

        _indirici.Ayarla(url, new DownloadedDocument(
            Icerik, "text/html", "utf-8", url, 200, DogruBaslik));

        await (await _reviewer.PostAsync("/api/sources/title-repair/apply", null))
            .Content.ReadFromJsonAsync<JsonElement>();

        var ilkDurum = await QueryAsync(db => db.SourceDocuments
            .Where(d => d.Id == documentId)
            .Select(d => new { d.Title, d.Revision, Surum = d.Versions.Count })
            .SingleAsync());

        // İkinci çalıştırma: artık bozuk başlık yok, aday listesi boş.
        var ikinci = await _reviewer.PostAsync("/api/sources/title-repair/apply", null);
        ikinci.EnsureSuccessStatusCode();

        var sonDurum = await QueryAsync(db => db.SourceDocuments
            .Where(d => d.Id == documentId)
            .Select(d => new { d.Title, d.Revision, Surum = d.Versions.Count })
            .SingleAsync());

        Assert.Equal(ilkDurum.Title, sonDurum.Title);
        Assert.Equal(ilkDurum.Revision, sonDurum.Revision);
        Assert.Equal(ilkDurum.Surum, sonDurum.Surum);
    }

    [Fact(DisplayName = "TR-F. İndirilemeyen belgenin başlığı tahmin edilmez")]
    public async Task Indirilemeyen_baslik_tahmin_edilmez()
    {
        var (_, documentId, _) = await BozukBelgeAsync("Erişilemeyen Kaynak Testi");

        // İndirici bu adres için içerik tanımlamadı: null dönecek.
        var sonuc = await _reviewer.PostAsync("/api/sources/title-repair/apply", null);
        sonuc.EnsureSuccessStatusCode();

        var rapor = await sonuc.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(rapor.GetProperty("failed").GetInt32() >= 1);

        // Başlık OLDUĞU GİBİ kaldı; uydurulmuş bir değer yazılmadı.
        var belge = await QueryAsync(db => db.SourceDocuments
            .Where(d => d.Id == documentId)
            .Select(d => new { d.Title, d.Revision })
            .SingleAsync());

        Assert.Equal(BozukBaslik, belge.Title);
        Assert.Equal(1, belge.Revision);
    }

    // ═══════════════ Yetki ═══════════════

    [Fact(DisplayName = "TR-G. Kiracı kullanıcısı onarım uçlarına erişemez")]
    public async Task Kiraci_erisemez()
    {
        var plan = await _tenant.GetAsync("/api/sources/title-repair/plan");
        var apply = await _tenant.PostAsync("/api/sources/title-repair/apply", null);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, plan.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, apply.StatusCode);
    }
}
