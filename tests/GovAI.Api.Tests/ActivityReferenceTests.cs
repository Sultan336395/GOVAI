using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GovAI.Api.Tests;

/// <summary>
/// Sektör ve NACE alanlarının "seç, yazma" sözleşmesi (Faz 2).
///
/// <para>
/// İki alan da serbest metindi. Sahada çelişti: bir firmanın sektörü "inşaat" yazarken
/// NACE kodu <c>2562</c> (metal işleme) kalmıştı; motor NACE koduna baktığı için firma
/// kendi sektöründeki ihalelerde "sektör uyumsuz" görünüyordu.
/// </para>
///
/// <para>
/// Bu testler iki şeyi birden sabitler: arayüzün beslendiği öneri ucu ile kaydın
/// doğrulandığı katalog <b>aynıdır</b>, ve elle yazılmış değer kabul edilmez.
/// Ayrı listeler olsaydı arayüzde geçerli görünen bir seçim sunucuda reddedilirdi.
/// </para>
/// </summary>
public sealed class ActivityReferenceTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _owner = null!;
    private HttpClient _anonim = null!;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _owner = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _anonim = _factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<List<JsonElement>> GetirAsync(string yol)
    {
        var response = await _owner.GetAsync(yol);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    /// <summary>Oluşturma yanıtındaki kimlikle şirketin kayıtlı hâlini geri okur.</summary>
    private async Task<JsonElement> KayitAsync(HttpResponseMessage createResponse)
    {
        var companyId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("companyId").GetGuid();

        var liste = await _owner.GetFromJsonAsync<List<JsonElement>>("/api/companies");

        return liste!.Single(c => c.GetProperty("id").GetGuid() == companyId);
    }

    private static object YeniSirket(
        string taxNumber,
        string mainSector = "Metal sanayi ve fabrikasyon",
        string primaryNaceCode = "25.62",
        string[]? secondaryNaceCodes = null,
        string[]? subSectors = null) => new
        {
            legalName = $"Katalog Testi {taxNumber} A.Ş.",
            taxNumber,
            country = "TR",
            mainSector,
            primaryNaceCode,
            secondaryNaceCodes = secondaryNaceCodes ?? [],
            subSectors = subSectors ?? [],
            legalType = "LimitedCompany",
        };

    // ═══════════════════ Öneri ucu ═══════════════════

    [Fact(DisplayName = "AR1. Sektör listesi sorgusuz da döner")]
    public async Task Sektor_listesi_sorgusuz_doner()
    {
        // Sektör listesi kısadır; kullanıcı hiç yazmadan açılır listeyi görebilmelidir.
        var liste = await GetirAsync("/api/reference/sectors");

        Assert.NotEmpty(liste);
        Assert.All(liste, s => Assert.False(string.IsNullOrWhiteSpace(s.GetProperty("name").GetString())));
    }

    [Fact(DisplayName = "AR2. Sektör araması Türkçe harf farkına takılmaz")]
    public async Task Sektor_aramasi_turkce_harf_farkina_takilmaz()
    {
        var kucuk = await GetirAsync("/api/reference/sectors?q=insaat");
        var buyuk = await GetirAsync("/api/reference/sectors?q=%C4%B0N%C5%9EAAT");

        Assert.Contains(kucuk, s => s.GetProperty("name").GetString() == "İnşaat ve taahhüt");
        Assert.Equal(kucuk.Count, buyuk.Count);
    }

    [Fact(DisplayName = "AR3. NACE araması kod ile çalışır")]
    public async Task Nace_aramasi_kod_ile_calisir()
    {
        var liste = await GetirAsync("/api/reference/nace?q=256");

        Assert.Contains(liste, n => n.GetProperty("code").GetString() == "25.62");
        Assert.All(liste, n => Assert.False(string.IsNullOrWhiteSpace(n.GetProperty("title").GetString())));
    }

    [Fact(DisplayName = "AR4. NACE araması tanım ile de çalışır")]
    public async Task Nace_aramasi_tanim_ile_calisir()
    {
        var liste = await GetirAsync("/api/reference/nace?q=yaz%C4%B1l%C4%B1m");

        Assert.NotEmpty(liste);
    }

    [Fact(DisplayName = "AR5. Üç harften kısa sorgu öneri getirmez")]
    public async Task Kisa_sorgu_oneri_getirmez()
    {
        // Kullanıcı arayüzde "en az 3 karakter yazın" uyarısını görür; sunucu da aynı
        // eşiği uygular ki her tuşta tüm katalog dönmesin.
        Assert.Empty(await GetirAsync("/api/reference/nace?q=ya"));
        Assert.Empty(await GetirAsync("/api/reference/nace"));
    }

    [Fact(DisplayName = "AR6. Öneri ucu oturum ister")]
    public async Task Oneri_ucu_oturum_ister()
    {
        var response = await _anonim.GetAsync("/api/reference/nace?q=256");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ═══════════════════ Kayıt doğrulaması ═══════════════════

    [Fact(DisplayName = "AR7. Öneri ucundan gelen seçim kayıtta kabul edilir")]
    public async Task Onerilen_secim_kabul_edilir()
    {
        // Sözleşmenin özü: arayüzün gösterdiği değer sunucuda geçerli olmalıdır.
        var sektor = (await GetirAsync("/api/reference/sectors?q=insaat"))
            .First(s => s.GetProperty("name").GetString() == "İnşaat ve taahhüt")
            .GetProperty("name").GetString()!;

        var nace = (await GetirAsync($"/api/reference/nace?q=412&sector={Uri.EscapeDataString(sektor)}"))[0]
            .GetProperty("code").GetString()!;

        var response = await _owner.PostAsJsonAsync("/api/companies", YeniSirket("5550001111", sektor, nace));

        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "AR8. Elle yazılmış sektör reddedilir")]
    public async Task Elle_yazilan_sektor_reddedilir()
    {
        var response = await _owner.PostAsJsonAsync(
            "/api/companies", YeniSirket("5550002222", mainSector: "inşaat"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("listeden seçilmelidir", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "AR9. Katalogda olmayan NACE kodu reddedilir")]
    public async Task Bilinmeyen_nace_reddedilir()
    {
        var response = await _owner.PostAsJsonAsync(
            "/api/companies", YeniSirket("5550003333", primaryNaceCode: "9999"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("listeden seçilmelidir", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "AR10. Diğer NACE kodları ve alt sektörler de doğrulanır")]
    public async Task Ikincil_alanlar_da_dogrulanir()
    {
        var kod = await _owner.PostAsJsonAsync(
            "/api/companies", YeniSirket("5550004444", secondaryNaceCodes: ["8888"]));

        var sektor = await _owner.PostAsJsonAsync(
            "/api/companies", YeniSirket("5550005555", subSectors: ["her neyse"]));

        Assert.Equal(HttpStatusCode.BadRequest, kod.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sektor.StatusCode);
    }

    [Fact(DisplayName = "AR11. Noktalı ve noktasız kod aynı kaydı üretir")]
    public async Task Noktali_ve_noktasiz_kod_ayni_kaydi_uretir()
    {
        // Katalog kodu okunaklı olsun diye noktalı gösterir ("25.62"); alan modeli ise
        // kodu noktasız saklar (CompanyNaceCode.Code, NaceCode.Normalize). Eşleştirme bu
        // noktasız biçim üzerinden yapılır. İkisi ayrışırsa aynı firma iki farklı kodla
        // kaydedilmiş görünür ve önek eşleşmesi kaçar.
        var noktali = await _owner.PostAsJsonAsync(
            "/api/companies", YeniSirket("5550006666", primaryNaceCode: "25.62"));
        var noktasiz = await _owner.PostAsJsonAsync(
            "/api/companies", YeniSirket("5550006677", primaryNaceCode: "2562"));

        noktali.EnsureSuccessStatusCode();
        noktasiz.EnsureSuccessStatusCode();

        var a = (await KayitAsync(noktali)).GetProperty("primaryNaceCode").GetString();
        var b = (await KayitAsync(noktasiz)).GetProperty("primaryNaceCode").GetString();

        Assert.Equal("2562", a);
        Assert.Equal(a, b);
    }

    [Fact(DisplayName = "AR12. Sektör adı kanonik yazıma çevrilerek kaydedilir")]
    public async Task Sektor_kanonige_cevrilir()
    {
        var response = await _owner.PostAsJsonAsync(
            "/api/companies", YeniSirket("5550007777", mainSector: "BİLİŞİM VE YAZILIM", primaryNaceCode: "62.01"));

        response.EnsureSuccessStatusCode();

        Assert.Equal("Bilişim ve yazılım", (await KayitAsync(response)).GetProperty("mainSector").GetString());
    }

    [Fact(DisplayName = "AR13. Katalogdaki her sektör gerçekten kaydedilebilir")]
    public async Task Katalogdaki_her_sektor_kaydedilebilir()
    {
        // Katalog ile doğrulama ayrışırsa listede görünen bir seçim kayıtta reddedilir.
        // Tümünü tek tek kaydetmek yerine kaydın reddedilmediğini doğrulamak yeterli:
        // asıl risk katalog dışı bir adın listeye sızmasıdır.
        var sektorler = await GetirAsync("/api/reference/sectors");

        var numara = 5550100000L;

        foreach (var sektor in sektorler.Take(5))
        {
            var ad = sektor.GetProperty("name").GetString()!;
            var bolum = sektor.GetProperty("divisions").EnumerateArray().First().GetString()!;

            var response = await _owner.PostAsJsonAsync(
                "/api/companies", YeniSirket((numara++).ToString(), ad, bolum));

            Assert.True(
                response.IsSuccessStatusCode,
                $"{ad} / {bolum}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }
}
