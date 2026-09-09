using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Domain.Analysis;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// DeepTech analiz uçlarının API sınırındaki garantileri (Faz 3 — Aşama 2).
///
/// <para>
/// Bu testler dört şeyi sabitler: kiracı yalıtımı, mükerrer mesaj koruması, sürüm
/// künyesinin gerçekten kaydedilmesi ve model bağlı değilken sonucun bunu açıkça
/// söylemesi. Dördü de yalnızca gerçek veritabanı ve gerçek yetkilendirme zinciriyle
/// doğrulanabilir; bellek içi sahte nesneler kiracı filtresini atlar.
/// </para>
/// </summary>
public sealed class AnalysisEndpointTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _tenantA = null!;
    private HttpClient _tenantB = null!;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _tenantA = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _tenantB = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string Adres(Guid companyId, Guid opportunityId) =>
        $"/api/analysis/companies/{companyId}/opportunities/{opportunityId}";

    private async Task<T> QueryAsync<T>(Func<GovAiDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        return await query(db);
    }

    [Fact(DisplayName = "AN1. Analiz sonucu kriter, puan kırılımı ve güven ile döner")]
    public async Task Analiz_tam_doner()
    {
        var response = await _tenantA.PostAsync(
            Adres(_factory.TenantA.CompanyId, _factory.TenantA.OpportunityId), null);

        response.EnsureSuccessStatusCode();

        var govde = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(govde.GetProperty("criteria").GetArrayLength() > 0);
        Assert.True(govde.GetProperty("score").GetProperty("components").GetArrayLength() > 0);
        Assert.False(string.IsNullOrWhiteSpace(
            govde.GetProperty("confidence").GetProperty("levelLabel").GetString()));
    }

    [Fact(DisplayName = "AN2. Puan 'uygunluk puanı' olarak etiketlenir")]
    public async Task Puan_uygunluk_puani_etiketli()
    {
        var response = await _tenantA.PostAsync(
            Adres(_factory.TenantA.CompanyId, _factory.TenantA.OpportunityId), null);

        var ham = await response.Content.ReadAsStringAsync();

        Assert.Contains("Uygunluk Puanı", ham);
        Assert.DoesNotContain("kazanma ihtimali", ham, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kazanma olasılığı", ham, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "AN3. Başka kiracının firması analiz edilemez")]
    public async Task Baska_kiracinin_firmasi_analiz_edilemez()
    {
        var response = await _tenantB.PostAsync(
            Adres(_factory.TenantA.CompanyId, _factory.TenantA.OpportunityId), null);

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"Beklenen 403/404, gelen {(int)response.StatusCode}.");
    }

    [Fact(DisplayName = "AN4. Analiz kaydı sürüm künyesiyle veritabanına yazılır")]
    public async Task Analiz_kaydi_yazilir()
    {
        var response = await _tenantA.PostAsync(
            Adres(_factory.TenantA.CompanyId, _factory.TenantA.OpportunityId), null);

        response.EnsureSuccessStatusCode();

        var govde = await response.Content.ReadFromJsonAsync<JsonElement>();
        var runId = govde.GetProperty("version").GetProperty("analysisRunId").GetGuid();

        var kayit = await QueryAsync(db => db.AnalysisRuns
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == runId));

        Assert.NotNull(kayit);
        Assert.Equal(AnalysisRuleSet.Current.Version, kayit.RuleSetVersion);
        Assert.False(string.IsNullOrWhiteSpace(kayit.IdempotencyKey));
        Assert.False(string.IsNullOrWhiteSpace(kayit.CorrelationId));
        Assert.NotNull(kayit.CompletedAt);
    }

    [Fact(DisplayName = "AN5. Aynı sürümlerle ikinci çağrı ikinci analiz kaydı oluşturmaz")]
    public async Task Mukerrer_cagri_ikinci_kayit_uretmez()
    {
        var adres = Adres(_factory.TenantA.SecondCompanyId, _factory.TenantA.OpportunityId);

        await _tenantA.PostAsync(adres, null);
        await _tenantA.PostAsync(adres, null);

        var adet = await QueryAsync(db => db.AnalysisRuns
            .IgnoreQueryFilters()
            .CountAsync(r => r.CompanyId == _factory.TenantA.SecondCompanyId
                             && r.TargetId == _factory.TenantA.OpportunityId));

        Assert.Equal(1, adet);
    }

    [Fact(DisplayName = "AN6. Model yapılandırılmamışken sonuç bunu açıkça söyler")]
    public async Task Model_yoksa_uyari_gosterilir()
    {
        // Test ortamında OpenAI anahtarı yok; sistem "hibrit çalışıyor" DEMEMELİ.
        var response = await _tenantA.PostAsync(
            Adres(_factory.TenantA.CompanyId, _factory.TenantA.OpportunityId), null);

        var govde = await response.Content.ReadFromJsonAsync<JsonElement>();
        var katki = govde.GetProperty("contribution");

        Assert.False(katki.GetProperty("hasAiContribution").GetBoolean());
        Assert.Contains("yalnızca resmî belgedeki kurallara göre",
            katki.GetProperty("warning").GetString());
    }

    [Fact(DisplayName = "AN7. Bulunmayan fırsat 404 döner")]
    public async Task Bulunmayan_firsat_404()
    {
        var response = await _tenantA.PostAsync(
            Adres(_factory.TenantA.CompanyId, Guid.CreateVersion7()), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "AN8. Kimliksiz istek reddedilir")]
    public async Task Kimliksiz_istek_reddedilir()
    {
        using var anonim = _factory.CreateClient();

        var response = await anonim.PostAsync(
            Adres(_factory.TenantA.CompanyId, _factory.TenantA.OpportunityId), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
