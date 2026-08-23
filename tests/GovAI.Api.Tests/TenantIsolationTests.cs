using System.Net;
using System.Net.Http.Json;

namespace GovAI.Api.Tests;

/// <summary>
/// Kiracılar arası veri sızıntısına karşı negatif testler (Faz 0 / D1–D3).
///
/// Her test <b>Kiracı A</b> olarak giriş yapar ve <b>Kiracı B</b>'nin kimliklerini
/// kullanarak veri okumaya/yazmaya çalışır. Beklenen cevap her zaman
/// <see cref="HttpStatusCode.NotFound"/>'dur — <c>403 Forbidden</c> değil: "yasak"
/// cevabı o kimliğin var olduğunu doğrular ve kimlik sayımına izin verirdi.
///
/// Testler yanıt kodunun yanı sıra <b>gövdede B'ye ait hiçbir metnin geçmediğini</b>
/// de doğrular; bir sızıntı yalnızca durum kodundan anlaşılmayabilir.
/// </summary>
public sealed class TenantIsolationTests : IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = new();
    private HttpClient _tenantA = null!;
    private HttpClient _tenantB = null!;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();
        _tenantA = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _tenantB = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);
    }

    public Task DisposeAsync()
    {
        _tenantA.Dispose();
        _tenantB.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ─────────────────────────── A ───────────────────────────

    [Fact(DisplayName = "A. Kiracı A, kiracı B'nin firma profilini okuyamaz")]
    public async Task Kiraci_A_kiraci_B_firma_profilini_okuyamaz()
    {
        var response = await _tenantA.GetAsync($"/api/company-profile/{_factory.TenantB.CompanyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoTenantBLeakAsync(response);
    }

    [Fact(DisplayName = "A2. Firma listesi yalnızca kendi kiracısının firmalarını döndürür")]
    public async Task Firma_listesi_yalnizca_kendi_kiracisini_dondurur()
    {
        var response = await _tenantA.GetAsync("/api/company-profile");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(_factory.TenantA.CompanyId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.TenantB.CompanyId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.TenantB.TaxNumber, body, StringComparison.Ordinal);
    }

    // ─────────────────────────── B ───────────────────────────

    [Fact(DisplayName = "B. Kiracı A, kiracı B'nin uygunluk eşleşmelerini okuyamaz")]
    public async Task Kiraci_A_kiraci_B_eslesmelerini_okuyamaz()
    {
        var response = await _tenantA.GetAsync($"/api/eligibility/companies/{_factory.TenantB.CompanyId}/matches");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoTenantBLeakAsync(response);
    }

    [Fact(DisplayName = "B2. Kiracı A, kiracı B'nin değerlendirme detayını okuyamaz")]
    public async Task Kiraci_A_kiraci_B_degerlendirme_detayini_okuyamaz()
    {
        var response = await _tenantA.GetAsync($"/api/eligibility/{_factory.TenantB.AssessmentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoTenantBLeakAsync(response);
    }

    // ─────────────────────────── C ───────────────────────────

    [Fact(DisplayName = "C. Kiracı A, kiracı B için değerlendirme başlatamaz")]
    public async Task Kiraci_A_kiraci_B_icin_degerlendirme_baslatamaz()
    {
        var response = await _tenantA.PostAsJsonAsync("/api/eligibility/evaluate", new
        {
            companyId = _factory.TenantB.CompanyId,
            opportunityId = _factory.TenantB.OpportunityId,
            generateSummary = false
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoTenantBLeakAsync(response);
    }

    [Fact(DisplayName = "C2. Kiracı A, kiracı B'yi yeniden skorlayamaz")]
    public async Task Kiraci_A_kiraci_B_yi_yeniden_skorlayamaz()
    {
        var response = await _tenantA.PostAsync(
            $"/api/eligibility/companies/{_factory.TenantB.CompanyId}/rescore", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─────────────────────────── D ve E ───────────────────────────

    [Fact(DisplayName = "D. Kiracı A, kiracı B'nin dashboard raporunu göremez")]
    public async Task Kiraci_A_kiraci_B_dashboardunu_goremez()
    {
        var response = await _tenantA.GetAsync($"/api/reports/companies/{_factory.TenantB.CompanyId}/dashboard");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoTenantBLeakAsync(response);
    }

    [Fact(DisplayName = "E. Kiracı A, kiracı B'nin Excel raporunu indiremez")]
    public async Task Kiraci_A_kiraci_B_excel_raporunu_indiremez()
    {
        var response = await _tenantA.GetAsync($"/api/reports/companies/{_factory.TenantB.CompanyId}/export/excel");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoTenantBLeakAsync(response);
    }

    [Fact(DisplayName = "E2. Kiracı A, kiracı B'nin PDF raporunu indiremez")]
    public async Task Kiraci_A_kiraci_B_pdf_raporunu_indiremez()
    {
        var response = await _tenantA.GetAsync($"/api/reports/companies/{_factory.TenantB.CompanyId}/export/pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoTenantBLeakAsync(response);
    }

    // ─────────────────────────── F ───────────────────────────

    [Fact(DisplayName = "F. Kiracı A, kiracı B'nin senaryolarını listeleyemez")]
    public async Task Kiraci_A_kiraci_B_senaryolarini_listeleyemez()
    {
        var response = await _tenantA.GetAsync($"/api/scoring/companies/{_factory.TenantB.CompanyId}/simulations");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoTenantBLeakAsync(response);
    }

    [Fact(DisplayName = "F2. Kiracı A, kiracı B için senaryo çalıştıramaz")]
    public async Task Kiraci_A_kiraci_B_icin_senaryo_calistiramaz()
    {
        var response = await _tenantA.PostAsJsonAsync(
            $"/api/scoring/companies/{_factory.TenantB.CompanyId}/simulate?persist=true",
            new { name = "izinsiz senaryo", employeeCount = 99 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoTenantBLeakAsync(response);
    }

    [Fact(DisplayName = "F3. Kiracı A, kiracı B'nin sıralamasını okuyamaz")]
    public async Task Kiraci_A_kiraci_B_siralamasini_okuyamaz()
    {
        var response = await _tenantA.GetAsync($"/api/scoring/companies/{_factory.TenantB.CompanyId}/ranking");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─────────────────────────── G ve H ───────────────────────────

    [Fact(DisplayName = "G. Kiracı A, kiracı B'nin bildirimlerini firma filtresiyle listeleyemez")]
    public async Task Kiraci_A_kiraci_B_bildirimlerini_listeleyemez()
    {
        var response = await _tenantA.GetAsync($"/api/notifications?companyId={_factory.TenantB.CompanyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoTenantBLeakAsync(response);
    }

    [Fact(DisplayName = "H. Parametresiz bildirim listesi yalnızca kendi kiracısını döndürür")]
    public async Task Parametresiz_bildirim_listesi_yalnizca_kendi_kiracisini_dondurur()
    {
        var response = await _tenantA.GetAsync("/api/notifications");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        // Kendi bildirimi görünmeli...
        Assert.Contains(_factory.TenantA.Name, body, StringComparison.Ordinal);

        // ...diğer kiracınınki hiçbir biçimde görünmemeli.
        Assert.DoesNotContain(_factory.TenantB.Name, body, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.TenantB.NotificationId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.TenantB.CompanyId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "H2. Kiracı A, kiracı B'nin bildirimini okundu işaretleyemez")]
    public async Task Kiraci_A_kiraci_B_bildirimini_okundu_isaretleyemez()
    {
        var response = await _tenantA.PostAsync($"/api/notifications/{_factory.TenantB.NotificationId}/read", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoTenantBLeakAsync(response);
    }

    // ─────────────────────────── I ve J ───────────────────────────

    [Fact(DisplayName = "I. Kiracı A yöneticisi, kiracı B kullanıcısının rolünü değiştiremez")]
    public async Task Kiraci_A_yoneticisi_kiraci_B_kullanicisinin_rolunu_degistiremez()
    {
        var response = await _tenantA.PutAsync(
            $"/api/admin/users/{_factory.TenantB.UserId}/role?role=ReadOnly", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Rol gerçekten değişmemiş olmalı: B hâlâ giriş yapıp yönetici işlemi görebilmeli.
        var stillAdmin = await _tenantB.GetAsync("/api/admin/users");
        stillAdmin.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "J. Kiracı A yöneticisi, kiracı B kullanıcısını pasif yapamaz")]
    public async Task Kiraci_A_yoneticisi_kiraci_B_kullanicisini_pasif_yapamaz()
    {
        var response = await _tenantA.PutAsync(
            $"/api/admin/users/{_factory.TenantB.UserId}/active?isActive=false", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // B hesabı hâlâ çalışıyor olmalı.
        using var freshClient = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);
        var me = await freshClient.GetAsync("/api/auth/me");
        me.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "J2. Kullanıcı listesi yalnızca kendi kiracısını döndürür")]
    public async Task Kullanici_listesi_yalnizca_kendi_kiracisini_dondurur()
    {
        var response = await _tenantA.GetAsync("/api/admin/users");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(_factory.TenantA.Email, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.TenantB.Email, body, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────── L ve M ───────────────────────────

    [Fact(DisplayName = "L. Geçerli kullanıcı kendi kiracısındaki firmaya erişebilir")]
    public async Task Gecerli_kullanici_kendi_firmasina_erisebilir()
    {
        var profile = await _tenantA.GetAsync($"/api/company-profile/{_factory.TenantA.CompanyId}");
        var dashboard = await _tenantA.GetAsync($"/api/reports/companies/{_factory.TenantA.CompanyId}/dashboard");
        var matches = await _tenantA.GetAsync($"/api/eligibility/companies/{_factory.TenantA.CompanyId}/matches");
        var simulations = await _tenantA.GetAsync($"/api/scoring/companies/{_factory.TenantA.CompanyId}/simulations");

        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Equal(HttpStatusCode.OK, matches.StatusCode);
        Assert.Equal(HttpStatusCode.OK, simulations.StatusCode);
    }

    [Fact(DisplayName = "M. Global fırsat kataloğu kiracı filtresinden etkilenmez")]
    public async Task Global_firsat_katalogu_kiraci_filtresinden_etkilenmez()
    {
        var fromA = await _tenantA.GetAsync("/api/opportunities?onlyOpen=false");
        var fromB = await _tenantB.GetAsync("/api/opportunities?onlyOpen=false");

        fromA.EnsureSuccessStatusCode();
        fromB.EnsureSuccessStatusCode();

        var bodyA = await fromA.Content.ReadAsStringAsync();
        var bodyB = await fromB.Content.ReadAsStringAsync();

        // Aynı ortak çağrı her iki kiracıya da görünmeli; katalog kiracıya özel değildir.
        Assert.Contains("Ortak Katalog Çağrısı", bodyA, StringComparison.Ordinal);
        Assert.Contains("Ortak Katalog Çağrısı", bodyB, StringComparison.Ordinal);

        var detailA = await _tenantA.GetAsync($"/api/opportunities/{_factory.TenantA.OpportunityId}");
        Assert.Equal(HttpStatusCode.OK, detailA.StatusCode);
    }

    [Fact(DisplayName = "M2. Kaynak kataloğu her iki kiracıya da açıktır")]
    public async Task Kaynak_katalogu_her_iki_kiraciya_acik()
    {
        var fromA = await _tenantA.GetAsync("/api/sources");
        var fromB = await _tenantB.GetAsync("/api/sources");

        fromA.EnsureSuccessStatusCode();
        fromB.EnsureSuccessStatusCode();

        Assert.Contains("Test Kaynağı", await fromA.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Test Kaynağı", await fromB.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Yanıt gövdesinde kiracı B'ye ait hiçbir tanımlayıcı veya metin bulunmadığını doğrular.
    /// Durum kodu doğru olsa bile hata gövdesi veri sızdırabilir.
    /// </summary>
    private async Task AssertNoTenantBLeakAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(_factory.TenantB.Name, body, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.TenantB.TaxNumber, body, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.TenantB.Email, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.TenantB.TenantId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }
}
