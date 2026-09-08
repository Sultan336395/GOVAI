using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Platform İnceleme uçlarının yetki sınırı (Faz 3).
///
/// <para>
/// Bu uçlar ortak kataloğu <b>değiştirir</b>: bir müşterinin isteği tüm müşterilerin
/// gördüğü veriyi bozabilirdi. Bu yüzden erişim yalnızca platform rollerine açıktır ve
/// sınır hem plan hem uygulama hem geri alma için ayrı ayrı sınanır — birine açık
/// bırakılan bir kapı, diğerlerini kapatmayı anlamsız kılar.
/// </para>
///
/// <para>
/// Kiracı yöneticisi (SuperAdmin) de <b>dışarıdadır</b>: kendi çalışma alanının tamamına
/// yetkilidir ama ortak katalog onun alanı değildir.
/// </para>
/// </summary>
public sealed class PlatformReviewAccessTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    /// <summary>Korunan uçların tamamı: yolu ve yönteminin listesi.</summary>
    private static readonly (string Method, string Path)[] KorunanUclar =
    [
        ("GET", "/api/sources/catalog-repair/plan"),
        ("POST", "/api/sources/catalog-repair/apply"),
        ("POST", "/api/sources/catalog-repair/runs/01a05d9e-0000-7000-a000-00000000000a/undo"),
        ("GET", "/api/opportunities/rule-evidence/backfill/plan"),
        ("POST", "/api/opportunities/rule-evidence/backfill/apply"),
        ("POST", "/api/opportunities/rule-evidence/backfill/runs/01a05d9e-0000-7000-a000-00000000000a/undo"),
    ];

    public Task InitializeAsync() => _factory.SeedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<HttpResponseMessage> CagirAsync(HttpClient client, string method, string path) =>
        method == "GET"
            ? client.GetAsync(path)
            : client.PostAsJsonAsync(path, new { planHash = "deneme" });

    // ── Erişemeyecek olanlar ──────────────────────────────────────────────

    [Theory(DisplayName = "PY1. Kiracı yöneticisi bakım uçlarına erişemez")]
    [MemberData(nameof(Uclar))]
    public async Task Tenant_yoneticisi_erisemez(string method, string path)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);

        var yanit = await CagirAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }

    [Theory(DisplayName = "PY2. Şirket yöneticisi bakım uçlarına erişemez")]
    [MemberData(nameof(Uclar))]
    public async Task Sirket_yoneticisi_erisemez(string method, string path)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.OperatorEmail);

        var yanit = await CagirAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }

    [Theory(DisplayName = "PY3. Worker kimliği bakım uçlarına erişemez")]
    [MemberData(nameof(Uclar))]
    public async Task Worker_erisemez(string method, string path)
    {
        // Worker ham belge bırakabilir ama kataloğu TOPLUCA değiştiremez: ele geçirilen
        // bir worker kimliği tüm kataloğu karantinaya alabilirdi.
        var client = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);

        var yanit = await CagirAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }

    [Theory(DisplayName = "PY4. Oturumsuz istek bakım uçlarına erişemez")]
    [MemberData(nameof(Uclar))]
    public async Task Oturumsuz_erisemez(string method, string path)
    {
        var client = _factory.CreateClient();

        var yanit = await CagirAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    // ── Erişebilecek olanlar ──────────────────────────────────────────────

    [Fact(DisplayName = "PY5. Platform inceleyicisi planı görebilir")]
    public async Task Inceleyici_plani_gorur()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            GovAiApiFactory.PlatformReviewerEmail);

        var onarim = await client.GetAsync("/api/sources/catalog-repair/plan");
        var kanit = await client.GetAsync("/api/opportunities/rule-evidence/backfill/plan");

        onarim.EnsureSuccessStatusCode();
        kanit.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "PY6. Katalog yöneticisi de planı görebilir")]
    public async Task Katalog_yoneticisi_plani_gorur()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            GovAiApiFactory.PlatformCatalogEmail);

        var yanit = await client.GetAsync("/api/sources/catalog-repair/plan");

        yanit.EnsureSuccessStatusCode();
    }

    // ── Onay kapısı ───────────────────────────────────────────────────────

    [Fact(DisplayName = "PY7. Onay özeti olmadan uygulama reddedilir")]
    public async Task Onaysiz_uygulama_reddedilir()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            GovAiApiFactory.PlatformReviewerEmail);

        var yanit = await client.PostAsJsonAsync(
            "/api/sources/catalog-repair/apply", new { planHash = "" });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact(DisplayName = "PY8. Yanlış onay özetiyle uygulama reddedilir")]
    public async Task Yanlis_ozetle_uygulama_reddedilir()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            GovAiApiFactory.PlatformReviewerEmail);

        // Görülmemiş bir plan uygulanamaz: uydurulmuş bir özet kabul edilmemeli.
        var yanit = await client.PostAsJsonAsync(
            "/api/sources/catalog-repair/apply", new { planHash = new string('a', 64) });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);

        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.Contains("plan", govde, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "PY9. Kanıt bağlamada da onay özeti zorunludur")]
    public async Task Kanit_baglamada_onay_zorunlu()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            GovAiApiFactory.PlatformReviewerEmail);

        var yanit = await client.PostAsJsonAsync(
            "/api/opportunities/rule-evidence/backfill/apply",
            new { planHash = new string('b', 64) });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact(DisplayName = "PY10. Plan ucundan gelen özetle uygulama kabul edilir")]
    public async Task Dogru_ozetle_uygulama_kabul_edilir()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            GovAiApiFactory.PlatformReviewerEmail);

        var plan = await client.GetFromJsonAsync<JsonElement>("/api/sources/catalog-repair/plan");

        var yanit = await client.PostAsJsonAsync(
            "/api/sources/catalog-repair/apply",
            new { planHash = plan.GetProperty("planHash").GetString() });

        yanit.EnsureSuccessStatusCode();
    }

    // ── Denetim kaydı ─────────────────────────────────────────────────────

    [Fact(DisplayName = "PY11. Uygulanan onarım denetim kaydına geçer")]
    public async Task Onarim_denetim_kaydina_gecer()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            GovAiApiFactory.PlatformReviewerEmail);

        var once = await SayAsync("CatalogRepair.Applied");

        var plan = await client.GetFromJsonAsync<JsonElement>("/api/sources/catalog-repair/plan");

        (await client.PostAsJsonAsync(
            "/api/sources/catalog-repair/apply",
            new { planHash = plan.GetProperty("planHash").GetString() })).EnsureSuccessStatusCode();

        Assert.Equal(once + 1, await SayAsync("CatalogRepair.Applied"));
    }

    [Fact(DisplayName = "PY12. Reddedilen uygulama denetim kaydına GEÇMEZ")]
    public async Task Reddedilen_islem_kayda_gecmez()
    {
        // Gerçekleşmemiş bir işlemin kaydı, kaydın tamamını şüpheli yapar.
        var client = await _factory.CreateAuthenticatedClientAsync(
            GovAiApiFactory.PlatformReviewerEmail);

        var once = await SayAsync("CatalogRepair.Applied");

        var yanit = await client.PostAsJsonAsync(
            "/api/sources/catalog-repair/apply", new { planHash = new string('c', 64) });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Equal(once, await SayAsync("CatalogRepair.Applied"));
    }

    [Fact(DisplayName = "PY13. Yetkisiz denemenin kendisi de kayda geçmez")]
    public async Task Yetkisiz_deneme_kayda_gecmez()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);

        var once = await SayAsync("CatalogRepair.Applied");

        await client.PostAsJsonAsync(
            "/api/sources/catalog-repair/apply", new { planHash = "x" });

        Assert.Equal(once, await SayAsync("CatalogRepair.Applied"));
    }

    private async Task<int> SayAsync(string eylem)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();

        return await db.AuditLog.IgnoreQueryFilters().CountAsync(a => a.Action == eylem);
    }

    public static TheoryData<string, string> Uclar()
    {
        var data = new TheoryData<string, string>();

        foreach (var (method, path) in KorunanUclar)
        {
            data.Add(method, path);
        }

        return data;
    }
}
