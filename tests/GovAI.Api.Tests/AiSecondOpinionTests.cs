using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Domain.Calibration;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Yapay zekâdan bağımsız ikinci görüş.
///
/// <para>
/// Bu modülün iki sözü var ve testler ikisini de sabitliyor:
/// </para>
///
/// <para>
/// 1. <b>İkinci görüş hiçbir skoru değiştirmez.</b> Motor deterministik kalır; görüş
///    yalnızca ayrışan vakaları insana işaret eder.
/// </para>
///
/// <para>
/// 2. <b>Anahtar yoksa görüş uydurulmaz.</b> Sahte bir ikinci görüş, ölçümü olduğundan
///    iyi ya da kötü gösterir ve kalibrasyonu bozar. Test ortamında OpenAI anahtarı
///    yoktur; bu yüzden burada beklenen davranış "hiç kayıt açılmaması"dır.
/// </para>
/// </summary>
public sealed class AiSecondOpinionTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _tenantAdmin = null!;
    private Guid _companyId;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);

        var liste = await _tenantAdmin.GetFromJsonAsync<JsonElement>("/api/companies");
        _companyId = (liste.ValueKind == JsonValueKind.Array ? liste : liste.GetProperty("items"))
            .EnumerateArray().First().GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> SorguAsync<T>(Func<GovAiDbContext, Task<T>> sorgu)
    {
        using var kapsam = _factory.Services.CreateScope();

        return await sorgu(kapsam.ServiceProvider.GetRequiredService<GovAiDbContext>());
    }

    private async Task SkorlaAsync()
    {
        var cevap = await _tenantAdmin.PostAsync(
            $"/api/eligibility/companies/{_companyId}/rescore", null);

        cevap.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "İG1. Anahtar yoksa görüş UYDURULMAZ")]
    public async Task Anahtarsiz_gorus_uydurulmaz()
    {
        // Sahte bir ikinci görüş, ölçümü olduğundan iyi ya da kötü gösterir. Hiç görüş
        // vermemek dürüst olandır.
        await SkorlaAsync();

        var cevap = await _tenantAdmin.PostAsync(
            $"/api/calibration/companies/{_companyId}/ai-review", null);

        cevap.EnsureSuccessStatusCode();

        var sonuc = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(sonuc.GetProperty("aiEnabled").GetBoolean());
        Assert.Equal(0, sonuc.GetProperty("recordedCount").GetInt32());

        var kayit = await SorguAsync(db => db.ExpertVerdicts.IgnoreQueryFilters()
            .CountAsync(v => v.Source == VerdictSource.Ai));

        Assert.Equal(0, kayit);
    }

    [Fact(DisplayName = "İG2. İkinci görüş turu SKORU DEĞİŞTİRMEZ")]
    public async Task Ikinci_gorus_skoru_degistirmez()
    {
        // Motorun deterministikliği ürünün ilk iddiasıdır.
        await SkorlaAsync();

        var once = await SorguAsync(db => db.Assessments.IgnoreQueryFilters()
            .Where(a => a.CompanyId == _companyId && a.IsLatest)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.FinalScore, a.Verdict })
            .ToListAsync());

        Assert.NotEmpty(once);

        (await _tenantAdmin.PostAsync(
            $"/api/calibration/companies/{_companyId}/ai-review", null)).EnsureSuccessStatusCode();

        var sonra = await SorguAsync(db => db.Assessments.IgnoreQueryFilters()
            .Where(a => a.CompanyId == _companyId && a.IsLatest)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.FinalScore, a.Verdict })
            .ToListAsync());

        Assert.Equal(once.Count, sonra.Count);

        for (var i = 0; i < once.Count; i++)
        {
            Assert.Equal(once[i].Id, sonra[i].Id);
            Assert.Equal(once[i].FinalScore, sonra[i].FinalScore);
            Assert.Equal(once[i].Verdict, sonra[i].Verdict);
        }
    }

    [Fact(DisplayName = "İG3. Rapor insan ve yapay zekâ ölçümünü AYRI döner")]
    public async Task Rapor_iki_olcumu_ayri_doner()
    {
        // Tek bir "uyum oranı" üretmek, modelin görüşünü danışmanınkiyle eşdeğer saymak
        // olurdu; oysa yalnızca biri ağırlık kalibrasyonunun ölçütüdür.
        var rapor = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/calibration/report?companyId={_companyId}");

        Assert.True(rapor.TryGetProperty("human", out var insan));
        Assert.True(rapor.TryGetProperty("ai", out var yapayZeka));

        // İkisi ayrı ölçümdür; ortak bir toplam alanı BULUNMAMALIDIR.
        Assert.False(rapor.TryGetProperty("summary", out _));

        Assert.True(insan.TryGetProperty("agreementRate", out _));
        Assert.True(yapayZeka.TryGetProperty("agreementRate", out _));
    }

    [Fact(DisplayName = "İG4. Danışman kaydı insan ölçümüne girer, yapay zekâ ölçümüne GİRMEZ")]
    public async Task Insan_kaydi_ai_olcumune_girmez()
    {
        await SkorlaAsync();

        var assessmentId = await SorguAsync(db => db.Assessments.IgnoreQueryFilters()
            .Where(a => a.CompanyId == _companyId && a.IsLatest)
            .Select(a => a.Id)
            .FirstAsync());

        var kaydet = await _tenantAdmin.PostAsJsonAsync("/api/calibration/verdicts", new
        {
            assessmentId,
            expertOpinion = "NotEligible",
            disagreementReason = "RuleExtraction",
            note = "Danışman notu.",
        });

        kaydet.EnsureSuccessStatusCode();

        var kayit = await kaydet.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Human", kayit.GetProperty("source").GetString());

        var rapor = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/calibration/report?companyId={_companyId}");

        Assert.True(rapor.GetProperty("human").GetProperty("totalVerdicts").GetInt32() >= 1);
        Assert.Equal(0, rapor.GetProperty("ai").GetProperty("totalVerdicts").GetInt32());
    }

    [Fact(DisplayName = "İG5. Toplu turu yalnızca zamanlayıcı kimliği çağırabilir")]
    public async Task Toplu_tur_korunur()
    {
        var worker = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);

        var workerCevap = await worker.PostAsync("/api/calibration/ai-review-batch", null);
        var kiraciCevap = await _tenantAdmin.PostAsync("/api/calibration/ai-review-batch", null);

        workerCevap.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, kiraciCevap.StatusCode);
    }

    [Fact(DisplayName = "İG6. Başka kiracı ikinci görüş turunu tetikleyemez")]
    public async Task Baska_kiraci_tetikleyemez()
    {
        var digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        var cevap = await digerKiraci.PostAsync(
            $"/api/calibration/companies/{_companyId}/ai-review", null);

        Assert.NotEqual(HttpStatusCode.OK, cevap.StatusCode);
    }

    [Fact(DisplayName = "İG7. Platform rolleri ikinci görüş turuna GİREMEZ")]
    public async Task Platform_rolleri_giremez()
    {
        foreach (var eposta in new[]
                 {
                     GovAiApiFactory.PlatformCatalogEmail,
                     GovAiApiFactory.PlatformReviewerEmail,
                 })
        {
            var client = await _factory.CreateAuthenticatedClientAsync(eposta);

            var cevap = await client.PostAsync(
                $"/api/calibration/companies/{_companyId}/ai-review", null);

            Assert.Equal(HttpStatusCode.Forbidden, cevap.StatusCode);
        }
    }
}
