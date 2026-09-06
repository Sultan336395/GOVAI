using System.Net;
using System.Net.Http.Json;

namespace GovAI.Api.Tests;

/// <summary>
/// Faz 1 / 1. madde: platform işletim yetkilerinin kiracı yetkilerinden ayrılması.
///
/// Senaryolar R, S, T. Temel iddia: <b>kiracı SuperAdmin'i platform yöneticisi değildir.</b>
/// Ortak katalog tüm müşterilerce paylaşıldığı için bir müşterinin yöneticisi orayı
/// değiştirememelidir.
/// </summary>
public sealed class PlatformRoleTests : IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = new();

    private HttpClient _tenantAdmin = null!;      // kiracı SuperAdmin
    private HttpClient _catalogManager = null!;   // PlatformCatalogManager
    private HttpClient _reviewer = null!;         // PlatformReviewer
    private HttpClient _ingest = null!;           // SystemIngest (worker)

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();
        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _catalogManager = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformCatalogEmail);
        _reviewer = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformReviewerEmail);
        _ingest = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);
    }

    public Task DisposeAsync()
    {
        _tenantAdmin.Dispose();
        _catalogManager.Dispose();
        _reviewer.Dispose();
        _ingest.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ═══════════════════ R ═══════════════════

    [Fact(DisplayName = "R. Kiracı SuperAdmin'i global kataloğu değiştiremez")]
    public async Task Kiraci_superadmini_katalogu_degistiremez()
    {
        var createSource = await _tenantAdmin.PostAsJsonAsync("/api/sources", new
        {
            name = "Kiracının eklemeye çalıştığı kaynak",
            type = "Ministry",
            baseUrl = "https://kiraci.local",
            cronExpression = "0 9 * * *",
            configurationJson = (string?)null
        });

        var updateSource = await _tenantAdmin.PutAsJsonAsync($"/api/sources/{_factory.TenantA.SourceId}", new
        {
            name = "Ele geçirilmiş",
            type = "Ministry",
            baseUrl = "https://kiraci.local",
            cronExpression = "0 9 * * *",
            configurationJson = (string?)null
        });

        var toggle = await _tenantAdmin.PostAsync(
            $"/api/sources/{_factory.TenantA.SourceId}/enabled?enabled=false", null);

        var review = await _tenantAdmin.PostAsync(
            $"/api/opportunities/{_factory.TenantA.OpportunityId}/review", null);

        Assert.Equal(HttpStatusCode.Forbidden, createSource.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, updateSource.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, toggle.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, review.StatusCode);
    }

    [Fact(DisplayName = "R2. Kiracı SuperAdmin'i kataloğu okumaya devam edebilir")]
    public async Task Kiraci_superadmini_katalogu_okuyabilir()
    {
        var sources = await _tenantAdmin.GetAsync("/api/sources");
        var opportunities = await _tenantAdmin.GetAsync("/api/opportunities?onlyOpen=false");

        Assert.Equal(HttpStatusCode.OK, sources.StatusCode);
        Assert.Equal(HttpStatusCode.OK, opportunities.StatusCode);
    }

    [Fact(DisplayName = "R3. Kiracı SuperAdmin'i platform rolü atayamaz")]
    public async Task Kiraci_superadmini_platform_rolu_atayamaz()
    {
        var create = await _tenantAdmin.PostAsJsonAsync("/api/admin/users", new
        {
            email = "sahte-platform@govai.test",
            fullName = "Yetki yükseltme denemesi",
            role = "PlatformCatalogManager",
            password = "YeterinceUzunParola!2026",
            scopedCompanyIds = (object?)null
        });

        var elevate = await _tenantAdmin.PutAsync(
            $"/api/admin/users/{_factory.TenantA.OperatorUserId}/role?role=SystemIngest", null);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, elevate.StatusCode);
    }

    // ═══════════════════ Platform katalog yöneticisi ═══════════════════

    [Fact(DisplayName = "R4. PlatformCatalogManager kaynak yönetebilir")]
    public async Task Platform_katalog_yoneticisi_kaynak_yonetebilir()
    {
        var create = await _catalogManager.PostAsJsonAsync("/api/sources", new
        {
            name = "Platform tarafından eklenen kaynak",
            type = "Ministry",
            baseUrl = "https://platform.local",
            cronExpression = "0 9 * * *",
            configurationJson = (string?)null
        });

        Assert.True(create.IsSuccessStatusCode, $"Beklenen başarı, alınan {create.StatusCode}");
    }

    // ═══════════════════ S ═══════════════════

    [Fact(DisplayName = "S. PlatformReviewer yalnızca inceleme yetkisini kullanabilir")]
    public async Task Platform_inceleyici_yalnizca_inceleme_yapabilir()
    {
        // İnceleme yetkisi VAR
        var review = await _reviewer.PostAsync(
            $"/api/opportunities/{_factory.TenantA.OpportunityId}/review", null);

        // Katalog tanımını değiştirme yetkisi YOK
        var createSource = await _reviewer.PostAsJsonAsync("/api/sources", new
        {
            name = "İnceleyicinin eklemeye çalıştığı kaynak",
            type = "Ministry",
            baseUrl = "https://inceleyici.local",
            cronExpression = "0 9 * * *",
            configurationJson = (string?)null
        });

        // Worker uçlarına da erişemez
        var ingestDoc = await _reviewer.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId = _factory.TenantA.SourceId,
            url = "https://inceleyici.local/x",
            title = "x",
            rawContent = "x",
            mediaType = "text/html"
        });

        Assert.True(review.IsSuccessStatusCode, $"İnceleme başarısız: {review.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, createSource.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ingestDoc.StatusCode);
    }

    // ═══════════════════ T ═══════════════════

    [Fact(DisplayName = "T. SystemIngest kullanıcı veya rol yönetemez")]
    public async Task SystemIngest_kullanici_yonetemez()
    {
        var listUsers = await _ingest.GetAsync("/api/admin/users");
        var createUser = await _ingest.PostAsJsonAsync("/api/admin/users", new
        {
            email = "ingest-denemesi@govai.test",
            fullName = "x",
            role = "CompanyManager",
            password = "YeterinceUzunParola!2026",
            scopedCompanyIds = (object?)null
        });
        var changeRole = await _ingest.PutAsync(
            $"/api/admin/users/{_factory.TenantA.UserId}/role?role=ReadOnly", null);
        var auditLog = await _ingest.GetAsync("/api/admin/audit-log");

        Assert.Equal(HttpStatusCode.Forbidden, listUsers.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createUser.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, changeRole.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, auditLog.StatusCode);
    }

    [Fact(DisplayName = "T2. SystemIngest şirket yönetemez")]
    public async Task SystemIngest_sirket_yonetemez()
    {
        var create = await _ingest.PostAsJsonAsync("/api/company-profile", new
        {
            legalName = "Ingest'in kurmaya çalıştığı şirket",
            taxNumber = "9998887776",
            legalType = "LimitedCompany"
        });

        var delete = await _ingest.DeleteAsync($"/api/company-profile/{_factory.TenantA.CompanyId}");

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact(DisplayName = "T3. SystemIngest şirket raporlarını okuyamaz")]
    public async Task SystemIngest_raporlari_okuyamaz()
    {
        var dashboard = await _ingest.GetAsync(
            $"/api/reports/companies/{_factory.TenantA.CompanyId}/dashboard");
        var excel = await _ingest.GetAsync(
            $"/api/reports/companies/{_factory.TenantA.CompanyId}/export/excel");
        var pdf = await _ingest.GetAsync(
            $"/api/reports/companies/{_factory.TenantA.CompanyId}/export/pdf");
        var ranking = await _ingest.GetAsync(
            $"/api/scoring/companies/{_factory.TenantA.CompanyId}/ranking");

        Assert.Equal(HttpStatusCode.Forbidden, dashboard.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, excel.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, pdf.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ranking.StatusCode);
    }

    [Fact(DisplayName = "T5. Platform hesapları mevzuat ekranını AÇABİLİR")]
    public async Task Platform_hesaplari_mevzuati_okuyabilir()
    {
        // Sol menü mevzuat ekranını platform rollerine gösteriyor ve mevzuatı asıl
        // inceleyecek kişi platform inceleyicisidir. Uç yanlışlıkla CompanyData
        // politikasındaydı; o politika platform rollerini dışarıda bıraktığı için
        // ekran 403 veriyordu. Mevzuat kaydı ortak kataloğa aittir ve içinde hiçbir
        // kiracı verisi yoktur.
        var reviewer = await _reviewer.GetAsync("/api/regulatory-changes");
        var catalog = await _catalogManager.GetAsync("/api/regulatory-changes");

        Assert.Equal(HttpStatusCode.OK, reviewer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
    }

    [Fact(DisplayName = "T6. Mevzuat kiracı kullanıcılarına da açık kalır")]
    public async Task Kiraci_kullanicisi_mevzuati_okuyabilir()
    {
        // Platform rolleri için açılırken kiracı erişimi KAPANMAMALIDIR.
        var response = await _tenantAdmin.GetAsync("/api/regulatory-changes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "T7. Mevzuat ucu oturum ister")]
    public async Task Mevzuat_ucu_oturum_ister()
    {
        using var anonim = _factory.CreateClient();

        var response = await anonim.GetAsync("/api/regulatory-changes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "T8. Mevzuat ucunun yazma yolu yoktur")]
    public async Task Mevzuat_ucunun_yazma_yolu_yoktur()
    {
        // Okuma yetkisi genişletildi; yazma yolu AÇILMADI. Kayıtlar ortak kataloğa
        // aittir ve panelden değiştirilemez.
        var post = await _catalogManager.PostAsJsonAsync("/api/regulatory-changes", new { title = "x" });
        var delete = await _catalogManager.DeleteAsync($"/api/regulatory-changes/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
    }

    [Fact(DisplayName = "T9. Platform hesaplarının menüsündeki her ekran açılır")]
    public async Task Platform_menusundeki_her_ekran_acilir()
    {
        // Sol menü ile sunucu politikaları ayrışırsa kullanıcı tıkladığı her yerde 403
        // görür ve bunu ancak canlıda fark ederiz — mevzuat ekranında olan buydu.
        // Menüdeki her satırın arkasındaki uç burada tek tek denenir.
        (string ekran, string uc)[] inceleyici =
        [
            ("Fon, Hibe ve İhale Kataloğu", "/api/opportunities"),
            ("Mevzuat Değişiklikleri", "/api/regulatory-changes"),
            ("Karantina İnceleme", "/api/quarantine"),
        ];

        foreach (var (ekran, uc) in inceleyici)
        {
            var response = await _reviewer.GetAsync(uc);
            Assert.True(response.IsSuccessStatusCode, $"PlatformReviewer / {ekran} ({uc}): {response.StatusCode}");
        }

        // Katalog yöneticisinde ek olarak kaynak yönetimi vardır.
        foreach (var (ekran, uc) in inceleyici.Append(("Veri Kaynakları", "/api/sources")))
        {
            var response = await _catalogManager.GetAsync(uc);
            Assert.True(response.IsSuccessStatusCode, $"PlatformCatalogManager / {ekran} ({uc}): {response.StatusCode}");
        }
    }

    [Fact(DisplayName = "T4. SystemIngest ihtiyaç duyduğu veri toplama uçlarına erişebilir")]
    public async Task SystemIngest_veri_toplama_uclarina_erisebilir()
    {
        var sources = await _ingest.GetAsync("/api/sources");
        var crawl = await _ingest.PostAsync($"/api/sources/{_factory.TenantA.SourceId}/crawl", null);
        var run = await _ingest.PostAsJsonAsync($"/api/sources/{_factory.TenantA.SourceId}/runs", new
        {
            status = "Succeeded",
            message = "ingest testi",
            documentCount = 0
        });
        var dispatch = await _ingest.PostAsync("/api/notifications/dispatch?take=10", null);

        Assert.Equal(HttpStatusCode.OK, sources.StatusCode);
        Assert.True(crawl.IsSuccessStatusCode, $"crawl: {crawl.StatusCode}");
        Assert.True(run.IsSuccessStatusCode, $"runs: {run.StatusCode}");
        Assert.True(dispatch.IsSuccessStatusCode, $"dispatch: {dispatch.StatusCode}");
    }
}
