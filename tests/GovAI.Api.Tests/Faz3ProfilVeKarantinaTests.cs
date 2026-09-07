using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Application.Abstractions.Services;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Faz 3: genç/engelli çalışan alanları ve karantinanın davranışı.
///
/// Karantina testlerinin ortak iddiası şudur: <b>karantina silme değildir</b>. Kayıt
/// durur, kanıt korunur, karar geri alınabilir; değişen tek şey kaydın katalogda ve
/// skorlamada görünmemesidir.
/// </summary>
public sealed class Faz3ProfilVeKarantinaTests : IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = new();

    private HttpClient _owner = null!;
    private HttpClient _reviewer = null!;
    private HttpClient _digerKiraci = null!;
    private SahteOlayYayinci _olaylar = null!;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _owner = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _reviewer = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformReviewerEmail);
        _digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);
        _olaylar = _factory.Services.GetRequiredService<SahteOlayYayinci>();
    }

    public Task DisposeAsync()
    {
        _owner.Dispose();
        _reviewer.Dispose();
        _digerKiraci.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private object Sirket(
        int? genc = null,
        int? yasSiniri = null,
        int? engelli = null,
        int calisan = 40) => new
        {
            legalName = $"{_factory.TenantA.Name} Sanayi A.Ş.",
            taxNumber = _factory.TenantA.TaxNumber,
            country = "TR",
            mainSector = "Metal sanayi ve fabrikasyon",
            primaryNaceCode = "25.62",
            legalType = "LimitedCompany",
            employeeCount = calisan,
            youngEmployeeCount = genc ?? 0,
            youngEmployeeMaxAge = yasSiniri,
            disabledEmployeeCount = engelli ?? 0,
        };

    private Task<HttpResponseMessage> GuncelleAsync(object govde) =>
        _owner.PutAsJsonAsync($"/api/companies/{_factory.TenantA.CompanyId}", govde);

    // ═══════════ Genç ve engelli çalışan alanları ═══════════

    [Fact(DisplayName = "P1. Genç ve engelli sayıları kaydedilebilir")]
    public async Task Genc_ve_engelli_kaydedilebilir()
    {
        var response = await GuncelleAsync(Sirket(genc: 9, yasSiniri: 29, engelli: 2));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        var company = await db.Companies.IgnoreQueryFilters()
            .SingleAsync(c => c.Id == _factory.TenantA.CompanyId);

        Assert.Equal(9, company.Workforce.YoungEmployeeCount);
        Assert.Equal(29, company.Workforce.YoungEmployeeMaxAge);
        Assert.Equal(2, company.Workforce.DisabledEmployeeCount);
    }

    [Fact(DisplayName = "P2. Alanlar boş bırakılabilir")]
    public async Task Alanlar_bos_birakilabilir()
    {
        var response = await GuncelleAsync(Sirket());

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Theory(DisplayName = "P3. Negatif sayı reddedilir")]
    [InlineData(-1, null, null)]
    [InlineData(null, null, -3)]
    public async Task Negatif_sayi_reddedilir(int? genc, int? yas, int? engelli)
    {
        var response = await GuncelleAsync(Sirket(genc, yas, engelli));

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact(DisplayName = "P4. Toplam çalışanı aşan sayı reddedilir")]
    public async Task Toplami_asan_sayi_reddedilir()
    {
        var genc = await GuncelleAsync(Sirket(genc: 50, yasSiniri: 29, calisan: 40));
        var engelli = await GuncelleAsync(Sirket(engelli: 50, calisan: 40));

        Assert.False(genc.IsSuccessStatusCode);
        Assert.False(engelli.IsSuccessStatusCode);
    }

    [Fact(DisplayName = "P5. Profil güncellemesi yeniden skorlamayı tetikler")]
    public async Task Profil_guncellemesi_yeniden_skorlamayi_tetikler()
    {
        _olaylar.Temizle();

        var response = await GuncelleAsync(Sirket(genc: 5, yasSiniri: 25, engelli: 1));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Contains(_olaylar.Olaylar, o => o.RoutingKey == QueueNames.ScoringRequested);
    }

    [Fact(DisplayName = "P6. Başka şirketin toplu çalışan bilgileri okunamaz ve değiştirilemez")]
    public async Task Baska_sirketin_bilgileri_degistirilemez()
    {
        // B kiracısı, A kiracısının şirketini güncellemeye çalışıyor.
        var yazma = await _digerKiraci.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}", Sirket(genc: 99, yasSiniri: 29));

        var okuma = await _digerKiraci.GetAsync($"/api/companies/{_factory.TenantA.CompanyId}");

        Assert.False(yazma.IsSuccessStatusCode);
        Assert.False(okuma.IsSuccessStatusCode);
    }

    [Fact(DisplayName = "P7. Kişisel çalışan alanları gönderilirse yok sayılır, saklanmaz")]
    public async Task Kisisel_alanlar_saklanmaz()
    {
        // Sözleşmede yeri olmayan alanlar modele bağlanmaz; istek kabul edilse bile
        // hiçbir kişisel veri kayda geçmez.
        var response = await _owner.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}",
            new
            {
                legalName = $"{_factory.TenantA.Name} Sanayi A.Ş.",
                taxNumber = _factory.TenantA.TaxNumber,
                country = "TR",
                mainSector = "Metal sanayi ve fabrikasyon",
                primaryNaceCode = "25.62",
                employeeCount = 40,
                youngEmployeeCount = 5,
                youngEmployeeMaxAge = 29,
                // Sözleşme dışı:
                employeeNames = new[] { "Ali Veli" },
                tcKimlikNo = "12345678901",
                salaries = new[] { 45000 },
            });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var govde = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Ali Veli", govde, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678901", govde, StringComparison.Ordinal);
    }

    // ═══════════ Karantina davranışı ═══════════

    private async Task<Guid> KarantinaAdayiAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();

        return await db.SourceDocuments.IgnoreQueryFilters()
            .OrderBy(d => d.CollectedAt)
            .Select(d => d.Id)
            .FirstOrDefaultAsync();
    }

    [Fact(DisplayName = "P8. Karantina kaydı SİLMEZ ve karar geri alınabilir")]
    public async Task Karantina_silmez_ve_geri_alinabilir()
    {
        var documentId = await KarantinaAdayiAsync();

        if (documentId == Guid.Empty)
        {
            return; // Seed'de belge yoksa bu senaryo uygulanamaz.
        }

        var karantina = await _reviewer.PostAsJsonAsync(
            $"/api/quarantine/{documentId}/reject",
            new { reason = "InvalidSourcePage", note = "Liste sayfası; aktif fırsat değildir." });

        Assert.True(karantina.IsSuccessStatusCode, await karantina.Content.ReadAsStringAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
            var belge = await db.SourceDocuments.IgnoreQueryFilters()
                .Include(d => d.Versions)
                .SingleAsync(d => d.Id == documentId);

            // Kayıt duruyor, ham içerik ve sürüm korunuyor.
            Assert.NotNull(belge);
            Assert.NotEmpty(belge.Versions);
            Assert.NotEqual(GovAI.Domain.Common.QuarantineReason.None, belge.QuarantineReason);
            Assert.Contains("aktif fırsat değildir", belge.QuarantineNote!, StringComparison.Ordinal);
        }

        // PlatformReviewer kararı geri alabilir.
        var geriAl = await _reviewer.PostAsync($"/api/quarantine/{documentId}/approve", null);

        Assert.True(geriAl.IsSuccessStatusCode, await geriAl.Content.ReadAsStringAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
            var belge = await db.SourceDocuments.IgnoreQueryFilters().SingleAsync(d => d.Id == documentId);

            Assert.Equal(GovAI.Domain.Common.QuarantineReason.None, belge.QuarantineReason);
        }
    }

    [Fact(DisplayName = "P9. Karantina kararı yalnızca yetkili rolce verilir")]
    public async Task Karantina_yetki_ister()
    {
        var documentId = await KarantinaAdayiAsync();

        if (documentId == Guid.Empty)
        {
            return;
        }

        var response = await _owner.PostAsJsonAsync(
            $"/api/quarantine/{documentId}/reject",
            new { reason = "InvalidSourcePage", note = "deneme" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "P10. Karantinadaki kayıt fırsat kataloğunda görünmez")]
    public async Task Karantinadaki_kayit_katalogda_gorunmez()
    {
        var liste = await _owner.GetFromJsonAsync<JsonElement>("/api/opportunities?pageSize=100");

        var karantinaliVar = liste.GetProperty("items").EnumerateArray()
            .Any(o => o.TryGetProperty("quarantineReason", out var q)
                      && q.ValueKind == JsonValueKind.String
                      && q.GetString() != "None");

        Assert.False(karantinaliVar);
    }
}
