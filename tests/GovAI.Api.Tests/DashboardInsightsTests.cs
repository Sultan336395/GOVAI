using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GovAI.Api.Tests;

/// <summary>
/// Dashboard'un dört ek bölümü: uç, yetki ve kiracı sınırı.
///
/// <para>
/// Bölümler müşteri verisidir. Ayrı bir uçtadır çünkü ana dashboard sayıları bu
/// bölümler hesaplanamasa da görünmelidir.
/// </para>
/// </summary>
public sealed class DashboardInsightsTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _tenantAdmin = null!;
    private Guid _companyId;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _companyId = _factory.TenantA.CompanyId;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string Yol => $"/api/reports/companies/{_companyId}/dashboard/insights";

    [Fact(DisplayName = "DA1. Dört bölüm de gelir")]
    public async Task Dort_bolum_gelir()
    {
        var govde = await _tenantAdmin.GetFromJsonAsync<JsonElement>(Yol);

        // Bölüm listesi sözleşmedir: biri kaybolursa arayüz o bölümü hiç göstermez.
        foreach (var bolum in new[] { "actions", "profile", "funnel", "trend", "activity", "notes" })
        {
            Assert.True(govde.TryGetProperty(bolum, out _), $"Bölüm eksik: {bolum}");
        }

        Assert.Equal(_companyId, govde.GetProperty("companyId").GetGuid());
    }

    [Fact(DisplayName = "DA2. Profil göstergesi yüzde ve eksik alan adlarını taşır")]
    public async Task Profil_gostergesi()
    {
        var profil = (await _tenantAdmin.GetFromJsonAsync<JsonElement>(Yol)).GetProperty("profile");

        var yuzde = profil.GetProperty("percentage").GetInt32();

        Assert.InRange(yuzde, 0, 100);
        Assert.True(profil.GetProperty("totalCount").GetInt32() > 0);

        // Eksik alan varsa kullanıcıya sistemin iç terimleri değil okunur ad gösterilir.
        if (profil.TryGetProperty("missingLabels", out var eksikler))
        {
            foreach (var etiket in eksikler.EnumerateArray())
            {
                Assert.DoesNotContain("Workforce.", etiket.GetString());
                Assert.DoesNotContain("Financials.", etiket.GetString());
            }
        }
    }

    [Fact(DisplayName = "DA3. Her aksiyonun gideceği bir yer vardır")]
    public async Task Aksiyonlarin_hedefi_var()
    {
        var govde = await _tenantAdmin.GetFromJsonAsync<JsonElement>(Yol);

        foreach (var aksiyon in govde.GetProperty("actions").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(aksiyon.GetProperty("target").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(aksiyon.GetProperty("title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(aksiyon.GetProperty("priorityLabel").GetString()));
        }
    }

    [Fact(DisplayName = "DA4. Boş bölümün sebebi yazılır")]
    public async Task Bos_bolumun_sebebi_yazilir()
    {
        // Görünmeyen ya da sessizce boş bir bölüm "bu konu incelenmedi" izlenimi verir.
        var govde = await _tenantAdmin.GetFromJsonAsync<JsonElement>(Yol);

        var egilimBos = govde.GetProperty("trend").GetArrayLength() == 0;

        if (egilimBos)
        {
            Assert.Contains(
                govde.GetProperty("notes").EnumerateArray(),
                n => n.GetString()!.Contains("Eğilim", StringComparison.Ordinal));
        }
    }

    [Fact(DisplayName = "DA5. İhale takibi huniye yansır")]
    public async Task Takip_huniye_yansir()
    {
        var oncesi = (await _tenantAdmin.GetFromJsonAsync<JsonElement>(Yol))
            .GetProperty("funnel").GetProperty("tracked").GetInt32();

        var takip = await _tenantAdmin.PostAsJsonAsync(
            $"/api/tenders/companies/{_companyId}",
            new { opportunityId = _factory.TenantA.OpportunityId });

        takip.EnsureSuccessStatusCode();

        var sonrasi = (await _tenantAdmin.GetFromJsonAsync<JsonElement>(Yol))
            .GetProperty("funnel").GetProperty("tracked").GetInt32();

        Assert.Equal(oncesi + 1, sonrasi);
    }

    [Fact(DisplayName = "DA6. Başka kiracı göremez")]
    public async Task Baska_kiraci_goremez()
    {
        var digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        var cevap = await digerKiraci.GetAsync(Yol);

        Assert.NotEqual(HttpStatusCode.OK, cevap.StatusCode);
    }

    [Fact(DisplayName = "DA7. Platform rolleri GİREMEZ")]
    public async Task Platform_rolleri_giremez()
    {
        foreach (var eposta in new[]
                 {
                     GovAiApiFactory.PlatformCatalogEmail,
                     GovAiApiFactory.PlatformReviewerEmail,
                 })
        {
            var client = await _factory.CreateAuthenticatedClientAsync(eposta);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Yol)).StatusCode);
        }
    }

    [Fact(DisplayName = "DA8. Görüntüleyici rolü okuyabilir")]
    public async Task Goruntuleyici_okuyabilir()
    {
        // Bölümler bir karar ekranıdır, bir işlem değil; okumak Read yetkisiyle olur.
        var okuyucu = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.ViewerEmail);

        Assert.Equal(HttpStatusCode.OK, (await okuyucu.GetAsync(Yol)).StatusCode);
    }
}
