using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Application.Abstractions.Services;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Sektör ile NACE kodunun birbirini tutması ve profil değişince skorun tazelenmesi.
///
/// <para>
/// Sahada görüldü: bir firmanın sektörü "İnşaat ve taahhüt" seçildi, NACE kodu ise
/// 23.61 (beton ürünleri imalatı) kaydedildi. İki alan da katalogda geçerliydi ama farklı
/// faaliyetleri anlatıyordu; motor NACE'ye baktığı için firma kendi sektöründeki
/// ihalelerde "sektör uyumsuz" göründü — profilinde sektörü doğru yazdığı hâlde.
/// </para>
///
/// <para>
/// İkinci kusur da aynı gün çıktı: kullanıcı sektörünü düzeltti, skorlar eski sektörle
/// hesaplanmış hâlde kaldı. Panelden yapılan düzenleme yeniden skorlamayı tetiklemiyordu.
/// </para>
/// </summary>
public sealed class SectorConsistencyTests : IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = new();

    private HttpClient _owner = null!;
    private SahteOlayYayinci _olaylar = null!;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _owner = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _olaylar = _factory.Services.GetRequiredService<SahteOlayYayinci>();
    }

    public Task DisposeAsync()
    {
        _owner.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static object Sirket(
        string taxNumber,
        string mainSector,
        string primaryNaceCode,
        string[]? secondaryNaceCodes = null,
        string[]? subSectors = null,
        string? legalName = null) => new
        {
            legalName = legalName ?? $"Tutarlılık Testi {taxNumber} A.Ş.",
            taxNumber,
            country = "TR",
            mainSector,
            primaryNaceCode,
            secondaryNaceCodes = secondaryNaceCodes ?? [],
            subSectors = subSectors ?? [],
            legalType = "LimitedCompany",
        };

    private async Task<List<JsonElement>> ListeAsync(string yol)
    {
        var response = await _owner.GetAsync(yol);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    // ═══════════════════ 1. Öneri listesi sektörle sınırlanır ═══════════════════

    [Fact(DisplayName = "SC1. NACE önerileri seçilen sektörle sınırlanır")]
    public async Task Oneriler_sektorle_sinirlanir()
    {
        var liste = await ListeAsync(
            $"/api/reference/nace?q=tesisat&sector={Uri.EscapeDataString("İnşaat ve taahhüt")}");

        Assert.NotEmpty(liste);
        Assert.All(liste, n => Assert.Equal("İnşaat ve taahhüt", n.GetProperty("sector").GetString()));
    }

    [Fact(DisplayName = "SC2. Sektör dışı kod öneri listesinde hiç çıkmaz")]
    public async Task Sektor_disi_kod_onerilmez()
    {
        // Kullanıcı tutmayan kodu göremezse seçemez de: asıl güvence budur.
        var liste = await ListeAsync(
            $"/api/reference/nace?q=2361&sector={Uri.EscapeDataString("İnşaat ve taahhüt")}");

        Assert.Empty(liste);
    }

    [Fact(DisplayName = "SC3. Birden çok sektör verilince kodları birleşir")]
    public async Task Birden_cok_sektor_birlesir()
    {
        var liste = await ListeAsync(
            "/api/reference/nace?q=imalat"
            + $"&sector={Uri.EscapeDataString("İnşaat ve taahhüt")}"
            + $"&sector={Uri.EscapeDataString("Yapı malzemeleri ve cam")}");

        var sektorler = liste.Select(n => n.GetProperty("sector").GetString()).Distinct().ToList();

        Assert.Contains("Yapı malzemeleri ve cam", sektorler);
        Assert.DoesNotContain("Bilişim ve yazılım", sektorler);
    }

    [Fact(DisplayName = "SC3b. Sorgu olmadan sektörün tüm kodları listelenir")]
    public async Task Sorgusuz_sektor_listesi_doner()
    {
        // Kullanıcı alana tıkladığı anda listeyi görmeli; üç harf yazmak zorunda
        // bırakmak, listeyi sektörle sınırlamanın kazandırdığı kolaylığı geri alırdı.
        var liste = await ListeAsync(
            $"/api/reference/nace?sector={Uri.EscapeDataString("İnşaat ve taahhüt")}");

        Assert.NotEmpty(liste);
        Assert.All(liste, n => Assert.Equal("İnşaat ve taahhüt", n.GetProperty("sector").GetString()));
        Assert.Contains(liste, n => n.GetProperty("code").GetString() == "41.20");
    }

    [Fact(DisplayName = "SC3c. Sektörsüz ve sorgusuz istek tüm kataloğu dökmez")]
    public async Task Sektorsuz_sorgusuz_istek_bos_doner()
    {
        Assert.Empty(await ListeAsync("/api/reference/nace"));
    }

    [Fact(DisplayName = "SC3d. Bir iki harf yazılınca da sektör listesi kaybolmaz")]
    public async Task Kisa_yazimda_liste_kaybolmaz()
    {
        // Üç harfin altında arama yapılmaz ama liste boşalmamalı: kullanıcı yazmaya
        // başlayınca ekranın boşalması "sonuç yok" gibi görünürdü.
        var liste = await ListeAsync(
            $"/api/reference/nace?q=in&sector={Uri.EscapeDataString("İnşaat ve taahhüt")}");

        Assert.NotEmpty(liste);
    }

    // ═══════════════════ 2. Kayıt doğrulaması ═══════════════════

    [Fact(DisplayName = "SC4. Sektörle tutmayan ana NACE kodu reddedilir")]
    public async Task Tutmayan_ana_kod_reddedilir()
    {
        // ERON'da gerçekleşen çelişkinin birebir kendisi.
        var response = await _owner.PostAsJsonAsync(
            "/api/companies", Sirket("6660001111", "İnşaat ve taahhüt", "23.61"));

        var govde = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Yapı malzemeleri ve cam", govde, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SC5. Hata mesajı kodun gerçek sektörünü söyler")]
    public async Task Hata_mesaji_gercek_sektoru_soyler()
    {
        // "Geçersiz" demek yetmez; kullanıcı hangi alanı düzelteceğini bilmelidir.
        var response = await _owner.PostAsJsonAsync(
            "/api/companies", Sirket("6660002222", "Bilişim ve yazılım", "25.62"));

        var govde = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Metal sanayi ve fabrikasyon", govde, StringComparison.Ordinal);
        Assert.Contains("Bilişim ve yazılım", govde, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SC6. Tutarlı çift kabul edilir")]
    public async Task Tutarli_cift_kabul_edilir()
    {
        var response = await _owner.PostAsJsonAsync(
            "/api/companies", Sirket("6660003333", "İnşaat ve taahhüt", "41.20"));

        Assert.True(
            response.IsSuccessStatusCode,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact(DisplayName = "SC7. Beyan edilmemiş alandaki ikincil kod reddedilir")]
    public async Task Beyan_edilmemis_ikincil_kod_reddedilir()
    {
        var response = await _owner.PostAsJsonAsync(
            "/api/companies",
            Sirket("6660004444", "İnşaat ve taahhüt", "41.20", secondaryNaceCodes: ["23.61"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "alt sektör olarak ekleyin",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SC8. Alt sektör beyan edilirse o alanın kodu kabul edilir")]
    public async Task Alt_sektor_beyan_edilirse_kod_kabul_edilir()
    {
        // Bir firma birden fazla alanda faaliyet gösterebilir; şart bunu beyan etmesidir.
        var response = await _owner.PostAsJsonAsync(
            "/api/companies",
            Sirket(
                "6660005555",
                "İnşaat ve taahhüt",
                "41.20",
                secondaryNaceCodes: ["23.61"],
                subSectors: ["Yapı malzemeleri ve cam"]));

        Assert.True(
            response.IsSuccessStatusCode,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact(DisplayName = "SC9. Öneri listesindeki her kod gerçekten kaydedilebilir")]
    public async Task Onerilen_her_kod_kaydedilebilir()
    {
        // Sözleşmenin özü: arayüzün gösterdiği hiçbir seçim sunucuda reddedilmemeli.
        const string sektor = "Bilişim ve yazılım";

        var kodlar = await ListeAsync(
            $"/api/reference/nace?q=bilgisayar&sector={Uri.EscapeDataString(sektor)}");

        Assert.NotEmpty(kodlar);

        var numara = 6660100000L;

        foreach (var kod in kodlar.Take(4).Select(n => n.GetProperty("code").GetString()!))
        {
            var response = await _owner.PostAsJsonAsync(
                "/api/companies", Sirket((numara++).ToString(), sektor, kod));

            Assert.True(
                response.IsSuccessStatusCode,
                $"{sektor} / {kod}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }

    // ═══════════════════ 3. Düzenleme yeniden skorlamayı tetikler ═══════════════════

    [Fact(DisplayName = "SC10. Firma düzenlemesi yeniden skorlamayı kuyruğa bırakır")]
    public async Task Duzenleme_yeniden_skorlamayi_tetikler()
    {
        // Sahada olan: kullanıcı sektörünü düzeltti, "fırsatlarım" listesi eski sektörle
        // hesaplanmış hâlde kaldı ve düzeltmenin işe yaramadığını sandı.
        var companyId = _factory.TenantA.CompanyId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
            var company = await db.Companies.IgnoreQueryFilters().SingleAsync(c => c.Id == companyId);

            Assert.NotNull(company);
        }

        _olaylar.Temizle();

        var response = await _owner.PutAsJsonAsync(
            $"/api/companies/{companyId}",
            Sirket(
                _factory.TenantA.TaxNumber,
                "Metal sanayi ve fabrikasyon",
                "25.62",
                legalName: $"{_factory.TenantA.Name} Sanayi A.Ş."));

        Assert.True(
            response.IsSuccessStatusCode,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        Assert.Contains(
            _olaylar.Olaylar,
            olay => olay.RoutingKey == QueueNames.ScoringRequested);
    }

    [Fact(DisplayName = "SC11. Reddedilen düzenleme yeniden skorlama TETİKLEMEZ")]
    public async Task Reddedilen_duzenleme_tetiklemez()
    {
        // Kaydedilmeyen bir değişiklik için skor yenilemek boşuna iş üretir.
        _olaylar.Temizle();

        var response = await _owner.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}",
            Sirket(_factory.TenantA.TaxNumber, "İnşaat ve taahhüt", "23.61"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(
            _olaylar.Olaylar,
            olay => olay.RoutingKey == QueueNames.ScoringRequested);
    }
}
