using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Uzman değerlendirmesi ve kalibrasyon uçları.
///
/// <para>
/// Kalibrasyon kaydı müşteri verisidir: başka kiracı göremez, platform rolleri
/// giremez. Ayrıca <b>uzman görüşü skoru değiştirmez</b> — motorun deterministikliği
/// bu modülle bozulmamalıdır.
/// </para>
/// </summary>
public sealed class CalibrationTests(GovAiApiFactory factory)
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

    /// <summary>Firmanın güncel bir değerlendirmesini bulur; yoksa skorlamayı tetikler.</summary>
    private async Task<Guid> DegerlendirmeAsync()
    {
        var mevcut = await SorguAsync(db => db.Assessments
            .IgnoreQueryFilters()
            .Where(a => a.CompanyId == _companyId && a.IsLatest)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync());

        if (mevcut is { } id)
        {
            return id;
        }

        var skorla = await _tenantAdmin.PostAsync(
            $"/api/eligibility/companies/{_companyId}/rescore", null);

        skorla.EnsureSuccessStatusCode();

        return await SorguAsync(db => db.Assessments
            .IgnoreQueryFilters()
            .Where(a => a.CompanyId == _companyId && a.IsLatest)
            .Select(a => a.Id)
            .FirstAsync());
    }

    private async Task<HttpResponseMessage> KaydetAsync(
        HttpClient client,
        Guid assessmentId,
        string karar,
        string sebep = "RuleExtraction") =>
        await client.PostAsJsonAsync("/api/calibration/verdicts", new
        {
            assessmentId,
            expertOpinion = karar,
            disagreementReason = sebep,
            note = "Danışman notu.",
        });

    [Fact(DisplayName = "KA1. Uzman kararı kaydedilir ve sistem kararıyla birlikte saklanır")]
    public async Task Uzman_karari_kaydedilir()
    {
        var assessmentId = await DegerlendirmeAsync();

        var cevap = await KaydetAsync(_tenantAdmin, assessmentId, "NotEligible");
        cevap.EnsureSuccessStatusCode();

        var kayit = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(assessmentId, kayit.GetProperty("assessmentId").GetGuid());
        Assert.Equal("NotEligible", kayit.GetProperty("expertOpinion").GetString());

        // Sistem kararı KOPYALANARAK saklanır: sonradan yeniden hesaplanırsa
        // karşılaştırma anlamını yitirirdi.
        Assert.True(kayit.TryGetProperty("systemVerdict", out _));
        Assert.True(kayit.TryGetProperty("systemScore", out _));
        Assert.False(string.IsNullOrWhiteSpace(kayit.GetProperty("opportunityTitle").GetString()));
    }

    [Fact(DisplayName = "KA2. Uzman görüşü SKORU DEĞİŞTİRMEZ")]
    public async Task Uzman_gorusu_skoru_degistirmez()
    {
        // Motorun deterministikliği ürünün ilk iddiasıdır. Kalibrasyon verisi karar
        // mekanizmasının girdisi değil, denetçisidir.
        var assessmentId = await DegerlendirmeAsync();

        var once = await SorguAsync(db => db.Assessments.IgnoreQueryFilters()
            .Where(a => a.Id == assessmentId)
            .Select(a => new { a.FinalScore, a.Verdict })
            .SingleAsync());

        (await KaydetAsync(_tenantAdmin, assessmentId, "NotEligible")).EnsureSuccessStatusCode();

        var sonra = await SorguAsync(db => db.Assessments.IgnoreQueryFilters()
            .Where(a => a.Id == assessmentId)
            .Select(a => new { a.FinalScore, a.Verdict })
            .SingleAsync());

        Assert.Equal(once.FinalScore, sonra.FinalScore);
        Assert.Equal(once.Verdict, sonra.Verdict);
    }

    [Fact(DisplayName = "KA3. Aynı değerlendirme için İKİNCİ kayıt açılmaz")]
    public async Task Ayni_degerlendirme_tek_kayit()
    {
        // Tek vaka kalibrasyon sayımına iki kez girerse oranlar bozulur.
        var assessmentId = await DegerlendirmeAsync();

        var bir = await KaydetAsync(_tenantAdmin, assessmentId, "NotEligible");
        var iki = await KaydetAsync(_tenantAdmin, assessmentId, "Eligible", "CompanyData");

        bir.EnsureSuccessStatusCode();
        iki.EnsureSuccessStatusCode();

        var birId = (await bir.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var ikiId = (await iki.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(birId, ikiId);

        var sayi = await SorguAsync(db => db.ExpertVerdicts.IgnoreQueryFilters()
            .CountAsync(v => v.AssessmentId == assessmentId));

        Assert.Equal(1, sayi);
    }

    [Fact(DisplayName = "KA4. Sistemden farklı karar verilirken sebepsiz istek REDDEDİLİR")]
    public async Task Sebepsiz_ayrisma_reddedilir()
    {
        var assessmentId = await DegerlendirmeAsync();

        var sistemKarari = await SorguAsync(db => db.Assessments.IgnoreQueryFilters()
            .Where(a => a.Id == assessmentId)
            .Select(a => a.Verdict)
            .SingleAsync());

        // Sistemin kararından KESİNLİKLE farklı bir değer seç.
        var farkli = sistemKarari == Domain.Common.EligibilityVerdict.NotEligible
            ? "Eligible"
            : "NotEligible";

        var cevap = await KaydetAsync(_tenantAdmin, assessmentId, farkli, sebep: "None");

        Assert.NotEqual(HttpStatusCode.OK, cevap.StatusCode);
    }

    [Fact(DisplayName = "KA5. Rapor uyum oranını ve hata sayılarını döner")]
    public async Task Rapor_olculeri_doner()
    {
        var assessmentId = await DegerlendirmeAsync();
        (await KaydetAsync(_tenantAdmin, assessmentId, "NotEligible")).EnsureSuccessStatusCode();

        var rapor = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/calibration/report?companyId={_companyId}");

        var ozet = rapor.GetProperty("summary");

        Assert.True(ozet.GetProperty("totalVerdicts").GetInt32() >= 1);

        foreach (var alan in new[]
                 {
                     "agreementRate", "falsePositiveCount", "falseNegativeCount",
                     "dataGapDisagreementCount", "isSampleSufficient", "matrix",
                     "reasons", "scoreBands",
                 })
        {
            Assert.True(ozet.TryGetProperty(alan, out _), $"Ölçü eksik: {alan}");
        }

        // Kural çıkarım kalitesi var olan veriden ölçülür; ayrı veri girişi gerektirmez.
        var kural = rapor.GetProperty("ruleExtraction");
        Assert.True(kural.GetProperty("totalRules").GetInt32() >= 0);
    }

    [Fact(DisplayName = "KA6. Tek kayıtla örneklem YETERSİZ işaretlenir")]
    public async Task Az_kayitta_orneklem_yetersizdir()
    {
        var assessmentId = await DegerlendirmeAsync();
        (await KaydetAsync(_tenantAdmin, assessmentId, "NotEligible")).EnsureSuccessStatusCode();

        var rapor = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/calibration/report?companyId={_companyId}");

        Assert.False(rapor.GetProperty("summary").GetProperty("isSampleSufficient").GetBoolean());
    }

    [Fact(DisplayName = "KA7. Başka kiracı kalibrasyon verisini göremez")]
    public async Task Baska_kiraci_goremez()
    {
        var assessmentId = await DegerlendirmeAsync();
        (await KaydetAsync(_tenantAdmin, assessmentId, "NotEligible")).EnsureSuccessStatusCode();

        var digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        var liste = await digerKiraci.GetAsync($"/api/calibration/companies/{_companyId}/verdicts");
        var kayit = await KaydetAsync(digerKiraci, assessmentId, "Eligible", "CompanyData");

        Assert.NotEqual(HttpStatusCode.OK, liste.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, kayit.StatusCode);
    }

    [Fact(DisplayName = "KA8. Platform rolleri kalibrasyon uçlarına GİREMEZ")]
    public async Task Platform_rolleri_giremez()
    {
        var assessmentId = await DegerlendirmeAsync();

        foreach (var eposta in new[]
                 {
                     GovAiApiFactory.PlatformCatalogEmail,
                     GovAiApiFactory.PlatformReviewerEmail,
                     GovAiApiFactory.SystemIngestEmail,
                 })
        {
            var client = await _factory.CreateAuthenticatedClientAsync(eposta);

            var rapor = await client.GetAsync("/api/calibration/report");
            var kayit = await KaydetAsync(client, assessmentId, "Eligible", "CompanyData");

            Assert.Equal(HttpStatusCode.Forbidden, rapor.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, kayit.StatusCode);
        }
    }

    [Fact(DisplayName = "KA9. Kimliksiz istek reddedilir")]
    public async Task Kimliksiz_istek_reddedilir()
    {
        var anonim = _factory.CreateClient();

        var rapor = await anonim.GetAsync("/api/calibration/report");

        Assert.Equal(HttpStatusCode.Unauthorized, rapor.StatusCode);
    }
}
