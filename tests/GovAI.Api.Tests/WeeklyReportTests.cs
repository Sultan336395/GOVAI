using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GovAI.Api.Tests;

/// <summary>
/// Otonom haftalık rapor: yetki, geçmiş ve çıktılar.
///
/// <para>
/// Rapor müşteri verisidir. İki sınır birden korunmalıdır: başka kiracının raporu
/// görünmemeli ve platform rolleri (katalog yöneticisi, inceleyici) müşteri verisine
/// hiç girememelidir.
/// </para>
/// </summary>
public sealed class WeeklyReportTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _tenantAdmin = null!;
    private Guid _companyId;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _companyId = await IlkFirmaAsync(_tenantAdmin);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> IlkFirmaAsync(HttpClient client)
    {
        var cevap = await client.GetAsync("/api/companies");
        cevap.EnsureSuccessStatusCode();

        var govde = await cevap.Content.ReadFromJsonAsync<JsonElement>();
        var liste = govde.ValueKind == JsonValueKind.Array ? govde : govde.GetProperty("items");

        return liste.EnumerateArray().First().GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> UretAsync(HttpClient client, Guid companyId)
    {
        var cevap = await client.PostAsJsonAsync(
            $"/api/reports/weekly/companies/{companyId}", new { });

        cevap.EnsureSuccessStatusCode();

        return await cevap.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact(DisplayName = "HRA1. Rapor üretilir ve yedi bölümün tamamını taşır")]
    public async Task Rapor_yedi_bolum_tasir()
    {
        var rapor = await UretAsync(_tenantAdmin, _companyId);
        var icerik = rapor.GetProperty("content");

        // Bölüm listesi sözleşmedir: biri kaybolursa arayüz o bölümü hiç göstermez.
        foreach (var bolum in new[]
                 {
                     "header", "supports", "technologyTenders", "regulatoryChanges",
                     "risks", "deadlines", "todos", "notes",
                 })
        {
            Assert.True(icerik.TryGetProperty(bolum, out _), $"Bölüm eksik: {bolum}");
        }

        var baslik = icerik.GetProperty("header");

        Assert.Equal(_companyId, baslik.GetProperty("companyId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(baslik.GetProperty("companyName").GetString()));
    }

    [Fact(DisplayName = "HRA2. Aynı haftanın raporu İKİNCİ kayıt açmaz")]
    public async Task Ayni_hafta_tek_kayit()
    {
        // Otomatik üretimle elle üretim aynı haftada çakışabilir; her çakışmada yeni
        // satır açmak geçmişi aynı haftanın kopyalarıyla doldururdu.
        var bir = await UretAsync(_tenantAdmin, _companyId);
        var iki = await UretAsync(_tenantAdmin, _companyId);

        Assert.Equal(bir.GetProperty("id").GetGuid(), iki.GetProperty("id").GetGuid());

        var gecmis = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/reports/weekly/companies/{_companyId}");

        Assert.Single(gecmis.EnumerateArray());
    }

    [Fact(DisplayName = "HRA3. Geçmiş rapor SAKLANIR, yeniden hesaplanmaz")]
    public async Task Gecmis_rapor_saklanir()
    {
        // Geçmiş raporun anlamı, o hafta ne görüldüğüdür. Açıldığında bugünün verisiyle
        // yeniden hesaplanırsa "geçen hafta bunu neden görmedim" sorusu cevaplanamaz.
        var uretilen = await UretAsync(_tenantAdmin, _companyId);
        var reportId = uretilen.GetProperty("id").GetGuid();

        var okunan = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/reports/weekly/{reportId}");

        Assert.Equal(
            uretilen.GetProperty("content").ToString(),
            okunan.GetProperty("content").ToString());
    }

    [Fact(DisplayName = "HRA4. Farklı haftalar ayrı kayıt olur ve geçmişte yeni başta gelir")]
    public async Task Haftalar_ayri_kayit_olur()
    {
        var oncekiHafta = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-14);

        var eski = await _tenantAdmin.PostAsJsonAsync(
            $"/api/reports/weekly/companies/{_companyId}",
            new { weekOf = oncekiHafta.ToString("yyyy-MM-dd") });

        eski.EnsureSuccessStatusCode();
        await UretAsync(_tenantAdmin, _companyId);

        var gecmis = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/reports/weekly/companies/{_companyId}");

        var donemler = gecmis.EnumerateArray()
            .Select(r => r.GetProperty("periodStart").GetString()!)
            .ToList();

        Assert.Equal(2, donemler.Count);
        Assert.True(string.CompareOrdinal(donemler[0], donemler[1]) > 0, "En yeni hafta başta olmalı.");
    }

    [Fact(DisplayName = "HRA5. PDF gerçek bir PDF dosyasıdır")]
    public async Task Pdf_gercek_dosyadir()
    {
        var reportId = (await UretAsync(_tenantAdmin, _companyId)).GetProperty("id").GetGuid();

        var cevap = await _tenantAdmin.GetAsync($"/api/reports/weekly/{reportId}/pdf");
        cevap.EnsureSuccessStatusCode();

        var bytes = await cevap.Content.ReadAsByteArrayAsync();

        Assert.Equal("application/pdf", cevap.Content.Headers.ContentType?.MediaType);
        Assert.True(bytes.Length > 1000, "PDF boş görünüyor.");

        // %PDF imzası: dosyanın gerçekten üretildiğini kanıtlar.
        Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
    }

    [Fact(DisplayName = "HRA6. Excel gerçek bir Excel dosyasıdır")]
    public async Task Excel_gercek_dosyadir()
    {
        var reportId = (await UretAsync(_tenantAdmin, _companyId)).GetProperty("id").GetGuid();

        var cevap = await _tenantAdmin.GetAsync($"/api/reports/weekly/{reportId}/excel");
        cevap.EnsureSuccessStatusCode();

        var bytes = await cevap.Content.ReadAsByteArrayAsync();

        Assert.True(bytes.Length > 1000, "Excel boş görünüyor.");

        // XLSX bir ZIP arşividir; "PK" imzası taşır.
        Assert.Equal("PK"u8.ToArray(), bytes.Take(2).ToArray());
    }

    [Fact(DisplayName = "HRA7. Dosya adı raporun dönemini taşır")]
    public async Task Dosya_adi_donemi_tasir()
    {
        // İndirilen üç haftalık rapor birbirine karışmamalı.
        var rapor = await UretAsync(_tenantAdmin, _companyId);
        var reportId = rapor.GetProperty("id").GetGuid();
        var donem = rapor.GetProperty("content").GetProperty("header")
            .GetProperty("periodStart").GetString();

        var cevap = await _tenantAdmin.GetAsync($"/api/reports/weekly/{reportId}/pdf");

        Assert.Contains(donem!, cevap.Content.Headers.ContentDisposition?.FileNameStar
                                ?? cevap.Content.Headers.ContentDisposition?.FileName
                                ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "HRA8. Başka kiracı raporu göremez")]
    public async Task Baska_kiraci_goremez()
    {
        var reportId = (await UretAsync(_tenantAdmin, _companyId)).GetProperty("id").GetGuid();

        var digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        var detay = await digerKiraci.GetAsync($"/api/reports/weekly/{reportId}");
        var liste = await digerKiraci.GetAsync($"/api/reports/weekly/companies/{_companyId}");
        var pdf = await digerKiraci.GetAsync($"/api/reports/weekly/{reportId}/pdf");

        Assert.NotEqual(HttpStatusCode.OK, detay.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, liste.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, pdf.StatusCode);
    }

    [Fact(DisplayName = "HRA9. Platform rolleri müşteri raporuna GİREMEZ")]
    public async Task Platform_rolleri_giremez()
    {
        var reportId = (await UretAsync(_tenantAdmin, _companyId)).GetProperty("id").GetGuid();

        foreach (var eposta in new[]
                 {
                     GovAiApiFactory.PlatformCatalogEmail,
                     GovAiApiFactory.PlatformReviewerEmail,
                     GovAiApiFactory.SystemIngestEmail,
                 })
        {
            var client = await _factory.CreateAuthenticatedClientAsync(eposta);

            var detay = await client.GetAsync($"/api/reports/weekly/{reportId}");
            var uret = await client.PostAsJsonAsync(
                $"/api/reports/weekly/companies/{_companyId}", new { });

            Assert.Equal(HttpStatusCode.Forbidden, detay.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, uret.StatusCode);
        }
    }

    [Fact(DisplayName = "HRA10. Toplu üretimi yalnızca zamanlayıcı kimliği çağırabilir")]
    public async Task Toplu_uretim_korunur()
    {
        var worker = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);

        var workerCevap = await worker.PostAsync("/api/reports/weekly-batch", null);
        var kiraciCevap = await _tenantAdmin.PostAsync("/api/reports/weekly-batch", null);

        workerCevap.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, kiraciCevap.StatusCode);

        var sonuc = await workerCevap.Content.ReadFromJsonAsync<JsonElement>();

        // Yanıt yalnızca sayı taşır; worker müşteri verisi görmez.
        Assert.True(sonuc.GetProperty("companyCount").GetInt32() >= 1);
        Assert.False(sonuc.TryGetProperty("companyName", out _));
    }

    [Fact(DisplayName = "HRA11. Kimliksiz istek reddedilir")]
    public async Task Kimliksiz_istek_reddedilir()
    {
        var anonim = _factory.CreateClient();

        var liste = await anonim.GetAsync($"/api/reports/weekly/companies/{_companyId}");
        var toplu = await anonim.PostAsync("/api/reports/weekly-batch", null);

        Assert.Equal(HttpStatusCode.Unauthorized, liste.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, toplu.StatusCode);
    }
}
