using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// ERP bağlantısı: kurulum, yetki ve kimlik gizliliği.
///
/// <para>
/// Bu modül müşterinin kendi ERP'sine bağlanmak için kimlik bilgisi saklar. İki sınır
/// mutlak: <b>kimlik hiçbir yanıtta dönmez</b> ve <b>açık hâliyle saklanmaz</b>.
/// </para>
/// </summary>
public sealed class ErpConnectionTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _tenantAdmin = null!;
    private Guid _companyId;

    private const string Gizli = "cok-gizli-erp-anahtari-12345";

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

    private async Task<HttpResponseMessage> KurAsync(
        HttpClient client,
        Guid companyId,
        string? gizli = Gizli,
        string adres = "https://erp.ornek.com/api/govai",
        bool kurumIci = false) =>
        await client.PutAsJsonAsync($"/api/erp/companies/{companyId}/connection", new
        {
            vendor = "Logo",
            baseUrl = adres,
            authMode = "ApiKeyHeader",
            secret = gizli,
            isOnPremise = kurumIci,
        });

    [Fact(DisplayName = "EA1. Bağlantı kurulur ve kimlik YANITTA DÖNMEZ")]
    public async Task Kimlik_yanitta_donmez()
    {
        var cevap = await KurAsync(_tenantAdmin, _companyId);
        cevap.EnsureSuccessStatusCode();

        var govde = await cevap.Content.ReadAsStringAsync();

        // Yanıtın tamamı taranır: hiçbir alanda, hiçbir biçimde geçmemeli.
        Assert.DoesNotContain(Gizli, govde, StringComparison.Ordinal);

        var kayit = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        // Kimliğin kendisi değil, VARLIĞI bildirilir.
        Assert.True(kayit.GetProperty("hasSecret").GetBoolean());
        Assert.False(kayit.TryGetProperty("secret", out _));
        Assert.False(kayit.TryGetProperty("protectedSecret", out _));
    }

    [Fact(DisplayName = "EA2. Kimlik veritabanında AÇIK HÂLDE saklanmaz")]
    public async Task Kimlik_acik_saklanmaz()
    {
        (await KurAsync(_tenantAdmin, _companyId)).EnsureSuccessStatusCode();

        var saklanan = await SorguAsync(db => db.ErpConnections.IgnoreQueryFilters()
            .Where(c => c.CompanyId == _companyId)
            .Select(c => c.ProtectedSecret)
            .SingleAsync());

        // Veritabanı yedeği sızsa bile kimlik anahtar olmadan okunamamalı.
        Assert.DoesNotContain(Gizli, saklanan, StringComparison.Ordinal);
        Assert.NotEmpty(saklanan);
    }

    [Fact(DisplayName = "EA3. Güncellemede boş kimlik MEVCUDU SİLMEZ")]
    public async Task Bos_kimlik_mevcudu_silmez()
    {
        // Ekranın kayıtlı sırrı geri göstermesi gerekmesin diye; gösterilen bir sır
        // ekran görüntüsüne ve tarayıcı geçmişine düşer.
        (await KurAsync(_tenantAdmin, _companyId)).EnsureSuccessStatusCode();

        var once = await SorguAsync(db => db.ErpConnections.IgnoreQueryFilters()
            .Where(c => c.CompanyId == _companyId).Select(c => c.ProtectedSecret).SingleAsync());

        var guncelle = await KurAsync(_tenantAdmin, _companyId, gizli: null,
            adres: "https://erp.ornek.com/api/v2");

        guncelle.EnsureSuccessStatusCode();

        var sonra = await SorguAsync(db => db.ErpConnections.IgnoreQueryFilters()
            .Where(c => c.CompanyId == _companyId).Select(c => c.ProtectedSecret).SingleAsync());

        Assert.Equal(once, sonra);

        var kayit = await guncelle.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://erp.ornek.com/api/v2", kayit.GetProperty("baseUrl").GetString());
    }

    [Fact(DisplayName = "EA4. Yeni bağlantı kimliksiz kurulamaz")]
    public async Task Yeni_baglanti_kimliksiz_kurulamaz()
    {
        // Testler aynı firmayı paylaşır; "yeni bağlantı" yolunu sınamak için önce
        // varsa mevcut bağlantı kaldırılır.
        await _tenantAdmin.DeleteAsync($"/api/erp/companies/{_companyId}/connection");

        var cevap = await KurAsync(_tenantAdmin, _companyId, gizli: null);

        Assert.NotEqual(HttpStatusCode.OK, cevap.StatusCode);
    }

    [Fact(DisplayName = "EA5. Bir firmanın TEK bağlantısı olur")]
    public async Task Firma_basina_tek_baglanti()
    {
        // İki bağlantı, aynı profile iki kaynaktan yazmak ve hangisinin kazandığının
        // belirsiz kalması demek olurdu.
        (await KurAsync(_tenantAdmin, _companyId)).EnsureSuccessStatusCode();
        (await KurAsync(_tenantAdmin, _companyId, adres: "https://erp2.ornek.com")).EnsureSuccessStatusCode();

        var sayi = await SorguAsync(db => db.ErpConnections.IgnoreQueryFilters()
            .CountAsync(c => c.CompanyId == _companyId));

        Assert.Equal(1, sayi);
    }

    [Fact(DisplayName = "EA6. Varsayılan alan eşlemesi kullanıcıya gösterilir")]
    public async Task Varsayilan_esleme_gosterilir()
    {
        // Kullanıcı hangi alanların arandığını görmeden eşlemeyi düzeltemez.
        var cevap = await KurAsync(_tenantAdmin, _companyId);
        cevap.EnsureSuccessStatusCode();

        var esleme = (await cevap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fieldMap");

        Assert.Equal("personel.kadin", esleme.GetProperty("womenEmployeeCount").GetString());
        Assert.Equal("mali.yillikCiro", esleme.GetProperty("annualRevenue").GetString());
    }

    [Fact(DisplayName = "EA13. Panelden düzenlenen eşleme SAKLANIR ve geri okunur")]
    public async Task Duzenlenen_esleme_saklanir()
    {
        // Varsayılan eşleme her kurulumda tutmaz; kullanıcının yazdığı alan adı kalıcı
        // olmalı, aksi hâlde her kaydetmede varsayılana dönerdi.
        var kur = await _tenantAdmin.PutAsJsonAsync($"/api/erp/companies/{_companyId}/connection", new
        {
            vendor = "Logo",
            baseUrl = "https://erp.ornek.com/api/govai",
            authMode = "ApiKeyHeader",
            secret = Gizli,
            isOnPremise = false,
            fieldMap = new
            {
                annualRevenue = "muhasebe.ciro2026",
                womenEmployeeCount = "ik.kadinSayisi",
                employeeCount = "ik.toplam",
            },
        });

        kur.EnsureSuccessStatusCode();

        var oku = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/erp/companies/{_companyId}/connection");

        var esleme = oku.GetProperty("fieldMap");

        Assert.Equal("muhasebe.ciro2026", esleme.GetProperty("annualRevenue").GetString());
        Assert.Equal("ik.kadinSayisi", esleme.GetProperty("womenEmployeeCount").GetString());

        // Verilmeyen alan UYDURULMAZ: varsayılana geri düşmez. API boş alanları
        // yanıttan çıkardığı için alan ya hiç yoktur ya da null'dır.
        var varMi = esleme.TryGetProperty("equity", out var ozkaynak);

        Assert.True(!varMi || ozkaynak.ValueKind == JsonValueKind.Null);
    }

    [Fact(DisplayName = "EA14. Ürün varsayılan eşlemesi sunucudan alınabilir")]
    public async Task Varsayilan_esleme_sunucudan_alinir()
    {
        // Varsayılanları istemciye kopyalamak, iki tarafın zamanla ayrışması demek
        // olurdu; alan adlarının tek kaynağı sunucudur.
        var logo = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            "/api/erp/field-map-defaults/Logo");

        var sap = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            "/api/erp/field-map-defaults/Sap");

        Assert.Equal("personel.kadin", logo.GetProperty("womenEmployeeCount").GetString());
        Assert.Equal("workforce.femaleHeadcount", sap.GetProperty("womenEmployeeCount").GetString());
    }

    [Fact(DisplayName = "EA7. Kurum içi adres BEYAN OLMADAN reddedilir")]
    public async Task Kurum_ici_adres_beyansiz_reddedilir()
    {
        // SSRF koruması: özel IP'lere ancak firma yöneticisinin açık beyanıyla gidilir.
        (await KurAsync(_tenantAdmin, _companyId, adres: "http://192.168.1.50:8080/api",
            kurumIci: false)).EnsureSuccessStatusCode();

        var cevap = await _tenantAdmin.PostAsync($"/api/erp/companies/{_companyId}/pull", null);
        cevap.EnsureSuccessStatusCode();

        var sonuc = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Failed", sonuc.GetProperty("status").GetString());
        Assert.Contains("kurum içi", sonuc.GetProperty("message").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "EA8. Bulut metadata adresine BEYANLA DAHİ gidilmez")]
    public async Task Metadata_adresine_gidilmez()
    {
        // Orası bir ERP değil, sunucunun kendi kimlik bilgilerinin durduğu yerdir.
        (await KurAsync(_tenantAdmin, _companyId, adres: "http://169.254.169.254/latest/meta-data",
            kurumIci: true)).EnsureSuccessStatusCode();

        var cevap = await _tenantAdmin.PostAsync($"/api/erp/companies/{_companyId}/pull", null);
        cevap.EnsureSuccessStatusCode();

        var sonuc = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Failed", sonuc.GetProperty("status").GetString());
        Assert.DoesNotContain("kurum içi", sonuc.GetProperty("message").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "EA9. Başka kiracı bağlantıyı göremez ve kuramaz")]
    public async Task Baska_kiraci_erisemez()
    {
        (await KurAsync(_tenantAdmin, _companyId)).EnsureSuccessStatusCode();

        var digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        var oku = await digerKiraci.GetAsync($"/api/erp/companies/{_companyId}/connection");
        var kur = await KurAsync(digerKiraci, _companyId);
        var cek = await digerKiraci.PostAsync($"/api/erp/companies/{_companyId}/pull", null);

        Assert.NotEqual(HttpStatusCode.OK, oku.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, kur.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, cek.StatusCode);
    }

    [Fact(DisplayName = "EA10. Platform rolleri ERP bağlantısına GİREMEZ")]
    public async Task Platform_rolleri_giremez()
    {
        foreach (var eposta in new[]
                 {
                     GovAiApiFactory.PlatformCatalogEmail,
                     GovAiApiFactory.PlatformReviewerEmail,
                 })
        {
            var client = await _factory.CreateAuthenticatedClientAsync(eposta);

            var oku = await client.GetAsync($"/api/erp/companies/{_companyId}/connection");

            Assert.Equal(HttpStatusCode.Forbidden, oku.StatusCode);
        }
    }

    [Fact(DisplayName = "EA11. Toplu çekmeyi yalnızca zamanlayıcı kimliği çağırabilir")]
    public async Task Toplu_cekme_korunur()
    {
        var worker = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);

        var workerCevap = await worker.PostAsync("/api/erp/pull-batch", null);
        var kiraciCevap = await _tenantAdmin.PostAsync("/api/erp/pull-batch", null);

        workerCevap.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, kiraciCevap.StatusCode);
    }

    [Fact(DisplayName = "EA12. Bağlantı silinince kimlik de gider")]
    public async Task Silinince_kimlik_gider()
    {
        // Kalibrasyon kayıtlarının aksine burada silme VARDIR: bu bir ölçüm kaydı değil,
        // müşterinin kimlik bilgisidir.
        (await KurAsync(_tenantAdmin, _companyId)).EnsureSuccessStatusCode();

        var sil = await _tenantAdmin.DeleteAsync($"/api/erp/companies/{_companyId}/connection");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);

        var kalan = await SorguAsync(db => db.ErpConnections.IgnoreQueryFilters()
            .CountAsync(c => c.CompanyId == _companyId));

        Assert.Equal(0, kalan);
    }
}
