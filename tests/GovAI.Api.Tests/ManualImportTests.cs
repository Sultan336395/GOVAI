using System.Net;
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
/// Faz 2 – kontrollü manuel içe aktarma.
///
/// <para>
/// Bu yol, otomatik taramaya uygun olmayan resmî kaynaklar içindir (EKAP gibi).
/// Testlerin koruduğu sözleşme şudur: <b>bu bir tarama değildir ve öyle gösterilmez</b>,
/// yalnızca kaynağın resmî alan adından kayıt alınabilir ve metin kullanıcıdan
/// yapıştırılmaz — indirilir.
/// </para>
/// </summary>
public sealed class ManualImportTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _reviewer = null!;
    private HttpClient _tenantAdmin = null!;
    private SahteIndirici _indirici = null!;

    private const string ResmiIcerik =
        "<html><head><title>2026/912345 İhale İlanı</title></head><body>"
        + "Kamu İhale Kurumu ilanı. İdarenin adı ve ihale kayıt numarası aşağıdadır. "
        + "İhale, 4734 sayılı Kamu İhale Kanununun 19 uncu maddesine göre açık ihale "
        + "usulü ile yapılacaktır. Teklifler elektronik ortamda sunulur ve son teklif "
        + "verme tarihi ilanda belirtilmiştir.</body></html>";

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _reviewer = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformReviewerEmail);
        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);

        _indirici = _factory.Services.GetRequiredService<SahteIndirici>();
        _indirici.Temizle();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Resmî alan adı tanımlı bir kaynak açar (EKAP'ın karşılığı).</summary>
    private async Task<Guid> ResmiKaynakAsync(string name, string officialDomain)
    {
        var katalog = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformCatalogEmail);

        var response = await katalog.PostAsJsonAsync("/api/sources", new
        {
            name,
            type = "TenderPortal",
            baseUrl = $"https://{officialDomain}",
            cronExpression = "0 6 * * *"
        });

        response.EnsureSuccessStatusCode();
        var sourceId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        var source = await db.Sources.SingleAsync(s => s.Id == sourceId);

        source.Describe(
            SourceCategory.Tender,
            new SourceProfile("Kamu İhale Kurumu", "TR", officialDomain, "tr"));

        await db.SaveChangesAsync();
        return sourceId;
    }

    private async Task<T> QueryAsync<T>(Func<GovAiDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        return await query(db);
    }

    // ═══════════════ Mutlu yol ═══════════════

    [Fact(DisplayName = "MI1. İnceleyici resmî adresten tek bir kaydı içe aktarır")]
    public async Task Inceleyici_resmi_adresten_kayit_alir()
    {
        var sourceId = await ResmiKaynakAsync("EKAP Manuel Testi", "ekap.kik.gov.tr");
        const string adres = "https://ekap.kik.gov.tr/EKAP/Ortak/KSP/ilan/2026-912345";

        _indirici.Ayarla(adres, new DownloadedDocument(
            ResmiIcerik, "text/html", "utf-8", adres, 200, "2026/912345 İhale İlanı"));

        var response = await _reviewer.PostAsJsonAsync(
            $"/api/sources/{sourceId}/manual-import", new { url = adres, title = (string?)null });

        response.EnsureSuccessStatusCode();

        var govde = await response.Content.ReadFromJsonAsync<JsonElement>();
        var documentId = govde.GetProperty("documentId").GetGuid();

        Assert.True(govde.GetProperty("isNew").GetBoolean());
        Assert.Equal(200, govde.GetProperty("httpStatusCode").GetInt32());
        Assert.Contains("elle içe aktarıldı", govde.GetProperty("note").GetString()!, StringComparison.Ordinal);

        var belge = await QueryAsync(db => db.SourceDocuments
            .Include(d => d.Versions)
            .SingleAsync(d => d.Id == documentId));

        // Kaynak adres, içerik ve kanıt zinciri korunur.
        Assert.Equal(adres, belge.Url);
        Assert.Equal("2026/912345 İhale İlanı", belge.Title);
        Assert.Equal(ResmiIcerik, belge.RawContent);
        Assert.Single(belge.Versions);
        Assert.Equal("utf-8", belge.Versions[0].Charset);
        Assert.Equal(200, belge.Versions[0].HttpStatusCode);
    }

    [Fact(DisplayName = "MI2. Elle aktarılan kayıt tarama gibi gösterilmez")]
    public async Task Elle_aktarilan_kayit_tarama_gibi_gosterilmez()
    {
        var sourceId = await ResmiKaynakAsync("EKAP Köken Testi", "ekap.kik.gov.tr");
        const string adres = "https://ekap.kik.gov.tr/EKAP/Ortak/KSP/ilan/2026-912346";

        _indirici.Ayarla(adres, new DownloadedDocument(
            ResmiIcerik, "text/html", "utf-8", adres, 200, "İlan"));

        var response = await _reviewer.PostAsJsonAsync(
            $"/api/sources/{sourceId}/manual-import", new { url = adres, title = (string?)null });

        response.EnsureSuccessStatusCode();

        var documentId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("documentId").GetGuid();

        var belge = await QueryAsync(db => db.SourceDocuments.SingleAsync(d => d.Id == documentId));
        Assert.Equal(DocumentOrigin.ManualImport, belge.Origin);

        // Kaynak DOĞRULANMIŞ sayılmaz, sağlığı değişmez, taranabilir olmaz.
        var kaynak = await QueryAsync(db => db.Sources.SingleAsync(s => s.Id == sourceId));

        Assert.False(kaynak.ConfigurationVerified);
        Assert.Equal(SourceHealth.Unverified, kaynak.Health);
        Assert.False(kaynak.IsCrawlable);
        Assert.Null(kaynak.LastSuccessfulRunAt);
    }

    // ═══════════════ Alan adı sınırı ═══════════════

    [Theory(DisplayName = "MI3. Kaynağın resmî alan adı dışından kayıt alınamaz")]
    [InlineData("https://baska-site.example/ilan/1")]
    [InlineData("https://ekap.kik.gov.tr.saldirgan.example/ilan/1")]
    [InlineData("https://sahte-ekap.kik.gov.tr.example/ilan/1")]
    [InlineData("http://localhost:8080/api/companies")]
    [InlineData("file:///etc/passwd")]
    public async Task Resmi_alan_adi_disindan_kayit_alinamaz(string adres)
    {
        var sourceId = await ResmiKaynakAsync($"Alan Adı Testi {adres.GetHashCode()}", "ekap.kik.gov.tr");

        var oncekiCagri = _indirici.CagriSayisi;

        var response = await _reviewer.PostAsJsonAsync(
            $"/api/sources/{sourceId}/manual-import", new { url = adres, title = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Reddedilen adres HİÇ indirilmemeli: denetim indirmeden önce yapılır.
        Assert.Equal(oncekiCagri, _indirici.CagriSayisi);

        var acildiMi = await QueryAsync(db => db.SourceDocuments.AnyAsync(d => d.SourceId == sourceId));
        Assert.False(acildiMi);
    }

    [Fact(DisplayName = "MI4. Resmî alan adının alt alan adları kabul edilir")]
    public async Task Alt_alan_adi_kabul_edilir()
    {
        var sourceId = await ResmiKaynakAsync("Alt Alan Testi", "kik.gov.tr");
        const string adres = "https://ekapv2.kik.gov.tr/ekap/ihale/2026-912347";

        _indirici.Ayarla(adres, new DownloadedDocument(
            ResmiIcerik, "text/html", "utf-8", adres, 200, "İlan"));

        var response = await _reviewer.PostAsJsonAsync(
            $"/api/sources/{sourceId}/manual-import", new { url = adres, title = (string?)null });

        response.EnsureSuccessStatusCode();
    }

    // ═══════════════ İçerik uydurulmaz ═══════════════

    [Fact(DisplayName = "MI5. İçerik indirilemezse kayıt açılmaz")]
    public async Task Icerik_indirilemezse_kayit_acilmaz()
    {
        var sourceId = await ResmiKaynakAsync("İndirilemeyen Testi", "ekap.kik.gov.tr");

        // Sahte indirici bu adres için içerik tanımlamadı → null döner.
        var response = await _reviewer.PostAsJsonAsync(
            $"/api/sources/{sourceId}/manual-import",
            new { url = "https://ekap.kik.gov.tr/EKAP/erisilemez", title = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var govde = await response.Content.ReadAsStringAsync();
        Assert.Contains("indirilemedi", govde, StringComparison.Ordinal);

        var acildiMi = await QueryAsync(db => db.SourceDocuments.AnyAsync(d => d.SourceId == sourceId));
        Assert.False(acildiMi);
    }

    // ═══════════════ Yetki ═══════════════

    [Fact(DisplayName = "MI6. Kiracı kullanıcısı elle içe aktarma yapamaz")]
    public async Task Kiraci_kullanicisi_ice_aktaramaz()
    {
        var sourceId = await ResmiKaynakAsync("Yetki Testi MI", "ekap.kik.gov.tr");
        const string adres = "https://ekap.kik.gov.tr/EKAP/ilan/2026-912348";

        _indirici.Ayarla(adres, new DownloadedDocument(
            ResmiIcerik, "text/html", "utf-8", adres, 200, "İlan"));

        var response = await _tenantAdmin.PostAsJsonAsync(
            $"/api/sources/{sourceId}/manual-import", new { url = adres, title = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var acildiMi = await QueryAsync(db => db.SourceDocuments.AnyAsync(d => d.SourceId == sourceId));
        Assert.False(acildiMi);
    }
}
