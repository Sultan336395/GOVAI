using System.Net;
using System.Net.Http.Json;

namespace GovAI.Api.Tests;

/// <summary>
/// Faz 0B / 4. kontrol: ortak katalog (Sources + Opportunities) kiracıya özel değildir —
/// bir kiracının yaptığı değişikliği <b>tüm</b> kiracılar görür. Bu yüzden sıradan bir
/// kiracı kullanıcısı kataloğu okuyabilmeli ama <b>değiştirememelidir</b>.
///
/// Faz 0B / 3. kontrol: worker kimliği anonim değildir ve sıradan müşteri yetkisiyle
/// sınırsız erişemez. Worker, platform işletim hesabıyla (SuperAdmin) giriş yapar ve
/// ihtiyaç duyduğu uçlara erişir; kiracı sınırına o da tabidir.
/// </summary>
public sealed class CatalogAndWorkerAccessTests : IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = new();

    /// <summary>Kiracı A'nın SuperAdmin olmayan operasyon kullanıcısı.</summary>
    private HttpClient _operator = null!;

    /// <summary>Worker'ın gerçekten kullandığı sınırlı veri toplama kimliği.</summary>
    private HttpClient _platform = null!;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();
        _operator = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.OperatorEmail);
        _platform = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _platform.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ═══════════ Katalog: okuma serbest ═══════════

    [Fact(DisplayName = "P. Kiracı kullanıcısı ortak kataloğu okuyabilir")]
    public async Task Kiraci_kullanicisi_katalogu_okuyabilir()
    {
        var opportunities = await _operator.GetAsync("/api/opportunities?onlyOpen=false");
        var sources = await _operator.GetAsync("/api/sources");
        var detail = await _operator.GetAsync($"/api/opportunities/{_factory.TenantA.OpportunityId}");

        Assert.Equal(HttpStatusCode.OK, opportunities.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sources.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        Assert.Contains("Ortak Katalog Çağrısı", await opportunities.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ═══════════ Katalog: yazma kapalı ═══════════

    [Fact(DisplayName = "Q. Kiracı kullanıcısı ortak kataloğa çağrı ekleyemez")]
    public async Task Kiraci_kullanicisi_cagri_ekleyemez()
    {
        var response = await _operator.PostAsJsonAsync("/api/opportunities", new
        {
            sourceId = _factory.TenantA.SourceId,
            sourceType = "Ministry",
            supportCategory = "Grant",
            title = "Kiracı tarafından eklenmeye çalışılan çağrı",
            publisher = "Sahte Kurum",
            publishedAt = DateTimeOffset.UtcNow,
            rules = Array.Empty<object>(),
            documentChecklist = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Q2. Kiracı kullanıcısı çağrı kuralını değiştiremez")]
    public async Task Kiraci_kullanicisi_kural_degistiremez()
    {
        var response = await _operator.PutAsJsonAsync(
            $"/api/opportunities/{_factory.TenantA.OpportunityId}/rules/{Guid.CreateVersion7()}",
            new { field = "Workforce.EmployeeCount", @operator = "GreaterThanOrEqual", value = "1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Q3. Kiracı kullanıcısı çağrıyı danışman onaylı işaretleyemez")]
    public async Task Kiraci_kullanicisi_cagriyi_onaylayamaz()
    {
        var response = await _operator.PostAsync(
            $"/api/opportunities/{_factory.TenantA.OpportunityId}/review", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "R. Kiracı kullanıcısı kaynak tarama tetikleyemez")]
    public async Task Kiraci_kullanicisi_tarama_tetikleyemez()
    {
        var response = await _operator.PostAsync($"/api/sources/{_factory.TenantA.SourceId}/crawl", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "R2. Kiracı kullanıcısı kataloğa ham doküman enjekte edemez")]
    public async Task Kiraci_kullanicisi_dokuman_enjekte_edemez()
    {
        var response = await _operator.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId = _factory.TenantA.SourceId,
            url = "https://sahte.local/enjekte",
            title = "Sahte doküman",
            rawContent = "<html>sahte</html>",
            mediaType = "text/html"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "R3. Kiracı kullanıcısı kaynak tanımını değiştiremez")]
    public async Task Kiraci_kullanicisi_kaynak_tanimini_degistiremez()
    {
        var update = await _operator.PutAsJsonAsync($"/api/sources/{_factory.TenantA.SourceId}", new
        {
            name = "Ele geçirilmiş kaynak",
            type = "Ministry",
            baseUrl = "https://saldirgan.local",
            cronExpression = "0 9 * * *",
            configurationJson = (string?)null
        });

        var toggle = await _operator.PostAsync($"/api/sources/{_factory.TenantA.SourceId}/enabled?enabled=false", null);
        var create = await _operator.PostAsJsonAsync("/api/sources", new
        {
            name = "Sahte kaynak",
            type = "Ministry",
            baseUrl = "https://sahte.local",
            cronExpression = "0 9 * * *",
            configurationJson = (string?)null
        });

        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, toggle.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact(DisplayName = "S. Reddedilen yazma denemesi kataloğu gerçekten değiştirmez")]
    public async Task Reddedilen_yazma_katalogu_degistirmez()
    {
        var before = await _operator.GetStringAsync("/api/opportunities?onlyOpen=false");

        await _operator.PostAsJsonAsync("/api/opportunities", new
        {
            sourceId = _factory.TenantA.SourceId,
            sourceType = "Ministry",
            supportCategory = "Grant",
            title = "ASLA-KAYDEDILMEMELI",
            publisher = "Sahte Kurum",
            publishedAt = DateTimeOffset.UtcNow,
            rules = Array.Empty<object>(),
            documentChecklist = Array.Empty<object>()
        });

        var after = await _operator.GetStringAsync("/api/opportunities?onlyOpen=false");

        Assert.DoesNotContain("ASLA-KAYDEDILMEMELI", after, StringComparison.Ordinal);
        Assert.Equal(before, after);
    }

    // ═══════════ Worker kimliği ═══════════

    [Fact(DisplayName = "T. Worker'ın kullandığı uçlar kimlik doğrulaması olmadan kapalıdır")]
    public async Task Worker_uclari_kimliksiz_kapalidir()
    {
        using var anonymous = _factory.CreateClient();

        var crawl = await anonymous.PostAsync($"/api/sources/{_factory.TenantA.SourceId}/crawl", null);
        var documents = await anonymous.PostAsJsonAsync("/api/sources/documents", new { });
        var sources = await anonymous.GetAsync("/api/sources");
        var upsert = await anonymous.PostAsJsonAsync("/api/opportunities", new { });
        var rescore = await anonymous.PostAsync(
            $"/api/eligibility/companies/{_factory.TenantA.CompanyId}/rescore", null);

        Assert.Equal(HttpStatusCode.Unauthorized, crawl.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, documents.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sources.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, upsert.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rescore.StatusCode);
    }

    [Fact(DisplayName = "T2. Worker hesabı ihtiyaç duyduğu uçlara erişebilir")]
    public async Task Worker_hesabi_ihtiyac_duydugu_uclara_erisebilir()
    {
        // Collector: kaynak listesi ve tarama çalışması bildirimi
        var sources = await _platform.GetAsync("/api/sources");
        var run = await _platform.PostAsJsonAsync($"/api/sources/{_factory.TenantA.SourceId}/runs", new
        {
            status = "Succeeded",
            message = "test taraması",
            documentCount = 0
        });

        // Scheduler: firma listesi ve yeniden skorlama
        var companies = await _platform.GetAsync("/api/company-profile");
        var rescore = await _platform.PostAsync(
            $"/api/eligibility/companies/{_factory.TenantA.CompanyId}/rescore", null);

        Assert.Equal(HttpStatusCode.OK, sources.StatusCode);
        Assert.True(run.IsSuccessStatusCode, $"runs ucu başarısız: {run.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, companies.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rescore.StatusCode);
    }

    [Fact(DisplayName = "T3. Worker hesabı da kiracı sınırına tabidir")]
    public async Task Worker_hesabi_kiraci_sinirina_tabidir()
    {
        // Worker platform hesabıyla çalışsa bile başka kiracının verisine ulaşamaz.
        var otherCompany = await _platform.GetAsync($"/api/company-profile/{_factory.TenantB.CompanyId}");
        var otherRescore = await _platform.PostAsync(
            $"/api/eligibility/companies/{_factory.TenantB.CompanyId}/rescore", null);

        Assert.Equal(HttpStatusCode.NotFound, otherCompany.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherRescore.StatusCode);
    }
}
