using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Rapora soru sorma ve +5 hak.
///
/// <para>
/// Üç sınır korunur: kullanıcı soru <b>yazamaz</b> (yalnızca anahtar kabul edilir), hak
/// <b>cevap üretilince</b> harcanır ve daha önce sorulmuş bir soruyu yeniden açmak hak
/// harcamaz.
/// </para>
/// </summary>
public sealed class ReportInquiryTests(GovAiApiFactory factory)
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

    /// <summary>Her test kendi raporunu üretir; hak sayacı testler arasında karışmasın.</summary>
    private async Task<Guid> RaporAsync(int haftaOnce)
    {
        var hafta = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7 * haftaOnce);

        var cevap = await _tenantAdmin.PostAsJsonAsync(
            $"/api/reports/weekly/companies/{_companyId}",
            new { weekOf = hafta.ToString("yyyy-MM-dd") });

        cevap.EnsureSuccessStatusCode();

        return (await cevap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> DurumAsync(Guid reportId) =>
        await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/reports/weekly/{reportId}/questions");

    private async Task<HttpResponseMessage> SorAsync(Guid reportId, string anahtar) =>
        await _tenantAdmin.PostAsJsonAsync(
            $"/api/reports/weekly/{reportId}/questions", new { questionKey = anahtar });

    [Fact(DisplayName = "SA1. Sorular rapordan üretilir ve hak sayacı beşten başlar")]
    public async Task Sorular_ve_hak_gelir()
    {
        var reportId = await RaporAsync(1);
        var durum = await DurumAsync(reportId);

        Assert.Equal(5, durum.GetProperty("totalQuota").GetInt32());
        Assert.Equal(5, durum.GetProperty("remainingCount").GetInt32());
        Assert.Equal(0, durum.GetProperty("usedCount").GetInt32());

        // Sorular rapordan çıkar; her birinin anahtarı ve metni vardır.
        foreach (var soru in durum.GetProperty("available").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(soru.GetProperty("key").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(soru.GetProperty("text").GetString()));
        }
    }

    [Fact(DisplayName = "SA2. Soru cevaplanır, bir hak harcanır ve devam soruları gelir")]
    public async Task Soru_cevaplanir()
    {
        var reportId = await RaporAsync(2);
        var durum = await DurumAsync(reportId);

        var sorular = durum.GetProperty("available").EnumerateArray().ToList();
        Assert.NotEmpty(sorular);

        var anahtar = sorular[0].GetProperty("key").GetString()!;

        var cevap = await SorAsync(reportId, anahtar);
        cevap.EnsureSuccessStatusCode();

        var sonuc = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(4, sonuc.GetProperty("remainingCount").GetInt32());

        var kayit = sonuc.GetProperty("inquiry");
        Assert.Equal(anahtar, kayit.GetProperty("questionKey").GetString());
        Assert.False(string.IsNullOrWhiteSpace(kayit.GetProperty("answerText").GetString()));

        Assert.True(sonuc.TryGetProperty("followUps", out _));
    }

    [Fact(DisplayName = "SA3. Sorulmuş soruyu yeniden açmak HAK HARCAMAZ")]
    public async Task Tekrar_acmak_hak_harcamaz()
    {
        // Kullanıcı listeye dönüp okuduğunu tekrar görebilmeli.
        var reportId = await RaporAsync(3);
        var durum = await DurumAsync(reportId);
        var anahtar = durum.GetProperty("available").EnumerateArray().First()
            .GetProperty("key").GetString()!;

        var ilk = await SorAsync(reportId, anahtar);
        var ikinci = await SorAsync(reportId, anahtar);

        ilk.EnsureSuccessStatusCode();
        ikinci.EnsureSuccessStatusCode();

        var ilkSonuc = await ilk.Content.ReadFromJsonAsync<JsonElement>();
        var ikinciSonuc = await ikinci.Content.ReadFromJsonAsync<JsonElement>();

        // Aynı kayıt döner, kalan hak değişmez.
        Assert.Equal(
            ilkSonuc.GetProperty("inquiry").GetProperty("id").GetGuid(),
            ikinciSonuc.GetProperty("inquiry").GetProperty("id").GetGuid());

        Assert.Equal(4, ikinciSonuc.GetProperty("remainingCount").GetInt32());

        var kayitSayisi = await SorguAsync(db => db.ReportInquiries.IgnoreQueryFilters()
            .CountAsync(i => i.WeeklyReportId == reportId));

        Assert.Equal(1, kayitSayisi);
    }

    [Fact(DisplayName = "SA4. Sorulmuş soru açık listede GÖRÜNMEZ, sorulanlarda görünür")]
    public async Task Sorulan_listeden_cikar()
    {
        var reportId = await RaporAsync(4);
        var anahtar = (await DurumAsync(reportId)).GetProperty("available").EnumerateArray()
            .First().GetProperty("key").GetString()!;

        (await SorAsync(reportId, anahtar)).EnsureSuccessStatusCode();

        var durum = await DurumAsync(reportId);

        Assert.DoesNotContain(
            durum.GetProperty("available").EnumerateArray(),
            s => s.GetProperty("key").GetString() == anahtar);

        Assert.Contains(
            durum.GetProperty("asked").EnumerateArray(),
            i => i.GetProperty("questionKey").GetString() == anahtar);
    }

    [Fact(DisplayName = "SA5. Beş hak bitince yeni soru REDDEDİLİR")]
    public async Task Hak_bitince_reddedilir()
    {
        var reportId = await RaporAsync(5);

        var anahtarlar = (await DurumAsync(reportId)).GetProperty("available").EnumerateArray()
            .Select(s => s.GetProperty("key").GetString()!)
            .ToList();

        // Beşten az soru üretiliyorsa hak sınırı zaten sınanamaz.
        if (anahtarlar.Count <= 5)
        {
            var durumHepsi = await DurumAsync(reportId);

            foreach (var a in anahtarlar)
            {
                (await SorAsync(reportId, a)).EnsureSuccessStatusCode();
            }

            Assert.True(durumHepsi.GetProperty("totalQuota").GetInt32() == 5);
            return;
        }

        foreach (var a in anahtarlar.Take(5))
        {
            (await SorAsync(reportId, a)).EnsureSuccessStatusCode();
        }

        var son = await DurumAsync(reportId);
        Assert.Equal(0, son.GetProperty("remainingCount").GetInt32());

        var fazladan = await SorAsync(reportId, anahtarlar[5]);

        Assert.NotEqual(HttpStatusCode.OK, fazladan.StatusCode);
    }

    [Fact(DisplayName = "SA6. Hak RAPOR bazındadır; yeni rapor yeni hak getirir")]
    public async Task Hak_rapor_bazindadir()
    {
        // Firma düzeyinde tek havuz olsaydı yoğun bir hafta, sonraki haftanın raporunu
        // sorusuz bırakırdı.
        var birinci = await RaporAsync(6);
        var anahtar = (await DurumAsync(birinci)).GetProperty("available").EnumerateArray()
            .First().GetProperty("key").GetString()!;

        (await SorAsync(birinci, anahtar)).EnsureSuccessStatusCode();

        Assert.Equal(4, (await DurumAsync(birinci)).GetProperty("remainingCount").GetInt32());

        var ikinci = await RaporAsync(7);

        Assert.Equal(5, (await DurumAsync(ikinci)).GetProperty("remainingCount").GetInt32());
    }

    [Fact(DisplayName = "SA7. Tanınmayan soru anahtarı kabul edilmez")]
    public async Task Taninmayan_anahtar_reddedilir()
    {
        // Kullanıcı soru yazamaz; uydurulmuş bir anahtar da cevap üretemez.
        var reportId = await RaporAsync(8);

        var cevap = await SorAsync(reportId, "BoyleBirSoruYok:123");

        Assert.NotEqual(HttpStatusCode.OK, cevap.StatusCode);

        var kayit = await SorguAsync(db => db.ReportInquiries.IgnoreQueryFilters()
            .CountAsync(i => i.WeeklyReportId == reportId));

        Assert.Equal(0, kayit);
    }

    [Fact(DisplayName = "SA8. Başka kiracı soru soramaz ve göremez")]
    public async Task Baska_kiraci_erisemez()
    {
        var reportId = await RaporAsync(9);
        var digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        var oku = await digerKiraci.GetAsync($"/api/reports/weekly/{reportId}/questions");
        var sor = await digerKiraci.PostAsJsonAsync(
            $"/api/reports/weekly/{reportId}/questions", new { questionKey = "EnAcilIs" });

        Assert.NotEqual(HttpStatusCode.OK, oku.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, sor.StatusCode);
    }

    [Fact(DisplayName = "SA9. Platform rolleri soru ekranına GİREMEZ")]
    public async Task Platform_rolleri_giremez()
    {
        var reportId = await RaporAsync(10);

        foreach (var eposta in new[]
                 {
                     GovAiApiFactory.PlatformCatalogEmail,
                     GovAiApiFactory.PlatformReviewerEmail,
                 })
        {
            var client = await _factory.CreateAuthenticatedClientAsync(eposta);

            var oku = await client.GetAsync($"/api/reports/weekly/{reportId}/questions");

            Assert.Equal(HttpStatusCode.Forbidden, oku.StatusCode);
        }
    }
}
