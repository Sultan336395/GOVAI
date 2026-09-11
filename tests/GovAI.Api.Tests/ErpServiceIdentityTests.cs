using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GovAI.Domain.Integrations;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GovAI.Api.Tests;

/// <summary>
/// ERP–GOVAI kimlik doğrulaması: uçtan uca, gerçek imzayla.
///
/// <para>
/// Model düz makine anahtarı <b>değildir</b>: GOVAI hiçbir sır saklamaz, yalnızca
/// ERP'nin açık anahtarını tutar. ERP her istekte kısa ömürlü, tek kullanımlık bir
/// beyan imzalar ve onu kısa ömürlü bir jetona çevirir.
/// </para>
///
/// <para>
/// Bu testler kriptografiyi taklit etmez; gerçek ECDSA anahtarı üretip gerçek imza
/// atar ve HTTP hattının tamamından geçer.
/// </para>
/// </summary>
public sealed class ErpServiceIdentityTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _tenantAdmin = null!;
    private Guid _companyId;

    /// <summary>Beyanın <c>aud</c> alanı; sunucunun beklediğiyle birebir aynı olmalı.</summary>
    private const string Alici = "https://govai.yuppi.cloud/api/erp-auth/token";

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _companyId = _factory.TenantA.CompanyId;

        await TemizleAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Testler arası sızıntıyı önler.
    ///
    /// <para>
    /// Firma başına kimlik sayısı sınırlıdır; testler aynı firmayı paylaştığı için
    /// temizlenmezse beşincisinden sonrası "sınır aşıldı" ile düşerdi.
    /// </para>
    /// </summary>
    private async Task TemizleAsync()
    {
        using var kapsam = _factory.Services.CreateScope();

        var db = kapsam.ServiceProvider.GetRequiredService<GovAiDbContext>();

        db.ErpSigningKeys.RemoveRange(
            await db.ErpSigningKeys.IgnoreQueryFilters().ToListAsync());

        db.ErpServiceIdentities.RemoveRange(
            await db.ErpServiceIdentities.IgnoreQueryFilters()
                .Where(i => i.CompanyId == _companyId).ToListAsync());

        db.ErpAssertionUses.RemoveRange(await db.ErpAssertionUses.ToListAsync());

        await db.SaveChangesAsync();
    }

    // ── Kurulum ─────────────────────────────────────────────────────────────

    /// <summary>ERP tarafını temsil eder: özel anahtar ERP'de kalır, GOVAI'ye gitmez.</summary>
    private sealed record ErpTarafi(ECDsa Ozel, string ClientId, string KeyId);

    private async Task<ErpTarafi> KimlikKurAsync(string kid = "k1")
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var cevap = await _tenantAdmin.PostAsJsonAsync(
            $"/api/erp/companies/{_companyId}/service-identities",
            new
            {
                displayName = $"IKPROF {Guid.CreateVersion7():N}",
                keyId = kid,
                algorithm = "ES256",
                publicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem(),
            });

        cevap.EnsureSuccessStatusCode();

        var kayit = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        return new ErpTarafi(ecdsa, kayit.GetProperty("clientId").GetString()!, kid);
    }

    /// <summary>ERP'nin yaptığı iş: kısa ömürlü beyanı imzalamak.</summary>
    private static string Beyan(
        ErpTarafi erp,
        string? sub = "ikprof-user-42",
        string? aud = Alici,
        string? jti = null,
        int omurSaniye = 60,
        string? ad = null,
        int zamanKaymasi = 0)
    {
        var iat = DateTimeOffset.UtcNow.AddSeconds(zamanKaymasi).ToUnixTimeSeconds();

        var payload = new Dictionary<string, object>
        {
            ["iss"] = erp.ClientId,
            ["sub"] = sub!,
            ["aud"] = aud!,
            ["jti"] = jti ?? Guid.CreateVersion7().ToString(),
            ["iat"] = iat,
            ["exp"] = iat + omurSaniye,
        };

        if (ad is not null)
        {
            payload["name"] = ad;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = payload,
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(erp.Ozel) { KeyId = erp.KeyId }, SecurityAlgorithms.EcdsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private async Task<HttpResponseMessage> JetonAlAsync(string beyan)
    {
        using var anonim = _factory.CreateClient();

        return await anonim.PostAsJsonAsync("/api/erp-auth/token", new { assertion = beyan });
    }

    private async Task<HttpClient> ErpIstemcisiAsync(ErpTarafi erp, string? sub = "ikprof-user-42")
    {
        var cevap = await JetonAlAsync(Beyan(erp, sub: sub));
        cevap.EnsureSuccessStatusCode();

        var jeton = (await cevap.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);

        return client;
    }

    // ── Mutlu yol ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "EK1. İmzalı beyan KISA ÖMÜRLÜ jetona çevrilir")]
    public async Task Beyan_jetona_cevrilir()
    {
        var erp = await KimlikKurAsync();

        var cevap = await JetonAlAsync(Beyan(erp, ad: "Ayşe Yılmaz"));

        cevap.EnsureSuccessStatusCode();

        var jeton = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Bearer", jeton.GetProperty("tokenType").GetString());
        Assert.Equal(_companyId, jeton.GetProperty("companyId").GetGuid());

        // Ömür kısa olmalı: uzun ömür modeli düz makine anahtarına çevirir ve
        // kimliği iptal etmenin etkisini geciktirir.
        var omur = jeton.GetProperty("expiresInSeconds").GetInt32();

        Assert.InRange(omur, 1, 3600);
    }

    [Fact(DisplayName = "EK2. Jetonla ERP modülü okunur ve YALNIZCA kendi şirketini görür")]
    public async Task Jetonla_modul_okunur()
    {
        var erp = await KimlikKurAsync();
        var client = await ErpIstemcisiAsync(erp);

        var kim = await client.GetFromJsonAsync<JsonElement>("/api/erp-module/whoami");

        Assert.Equal(_companyId, kim.GetProperty("companyId").GetGuid());
        Assert.Equal(erp.ClientId, kim.GetProperty("clientId").GetString());
        Assert.Equal("User", kim.GetProperty("kind").GetString());

        var bildirimler = await client.GetFromJsonAsync<JsonElement>("/api/erp-module/notifications");

        Assert.Equal(_companyId, bildirimler.GetProperty("companyId").GetGuid());
        Assert.True(bildirimler.TryGetProperty("items", out _));
    }

    [Fact(DisplayName = "EK3. sub == iss ise SERVİS kimliği olarak jeton verilir")]
    public async Task Servis_jetonu()
    {
        var erp = await KimlikKurAsync();
        var client = await ErpIstemcisiAsync(erp, sub: erp.ClientId);

        var kim = await client.GetFromJsonAsync<JsonElement>("/api/erp-module/whoami");

        Assert.Equal("Service", kim.GetProperty("kind").GetString());
    }

    // ── Saldırı senaryoları ─────────────────────────────────────────────────

    [Fact(DisplayName = "EK4. TEKRAR oynatılan beyan İKİNCİ kez jeton ALMAZ")]
    public async Task Tekrar_oynatma_engellenir()
    {
        // Ağdan yakalanan bir beyan, ömrü dolana kadar sınırsız kez kullanılabilirdi.
        var erp = await KimlikKurAsync();
        var beyan = Beyan(erp);

        var ilk = await JetonAlAsync(beyan);
        var ikinci = await JetonAlAsync(beyan);

        ilk.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, ikinci.StatusCode);
    }

    [Fact(DisplayName = "EK5. BAŞKA anahtarla imzalanmış beyan reddedilir")]
    public async Task Baska_anahtar_reddedilir()
    {
        var erp = await KimlikKurAsync();

        var saldirgan = erp with { Ozel = ECDsa.Create(ECCurve.NamedCurves.nistP256) };

        var cevap = await JetonAlAsync(Beyan(saldirgan));

        Assert.Equal(HttpStatusCode.Unauthorized, cevap.StatusCode);
    }

    [Fact(DisplayName = "EK6. ÖMRÜ UZUN beyan reddedilir")]
    public async Task Uzun_omurlu_beyan_reddedilir()
    {
        // Bu, modeli düz makine anahtarına çevirmenin en kolay yolu olurdu.
        var erp = await KimlikKurAsync();

        var cevap = await JetonAlAsync(Beyan(erp, omurSaniye: 86_400));

        Assert.Equal(HttpStatusCode.Unauthorized, cevap.StatusCode);
    }

    [Fact(DisplayName = "EK7. BAŞKA ALICIYA yazılmış beyan reddedilir")]
    public async Task Yanlis_alici_reddedilir()
    {
        var erp = await KimlikKurAsync();

        var cevap = await JetonAlAsync(Beyan(erp, aud: "https://baska-servis.example/token"));

        Assert.Equal(HttpStatusCode.Unauthorized, cevap.StatusCode);
    }

    [Fact(DisplayName = "EK8. Kimlik KAPATILINCA yeni jeton verilmez")]
    public async Task Kapatilan_kimlik_jeton_almaz()
    {
        var erp = await KimlikKurAsync();

        var liste = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/erp/companies/{_companyId}/service-identities");

        var kimlikId = liste.EnumerateArray()
            .Single(k => k.GetProperty("clientId").GetString() == erp.ClientId)
            .GetProperty("id").GetGuid();

        var kapat = await _tenantAdmin.PostAsync(
            $"/api/erp/service-identities/{kimlikId}/enabled?enabled=false", null);

        kapat.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await JetonAlAsync(Beyan(erp))).StatusCode);
    }

    [Fact(DisplayName = "EK9. Anahtar İPTAL edilince o anahtarla jeton alınamaz")]
    public async Task Iptal_edilen_anahtar_calismaz()
    {
        var erp = await KimlikKurAsync();

        var liste = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/erp/companies/{_companyId}/service-identities");

        var kimlikId = liste.EnumerateArray()
            .Single(k => k.GetProperty("clientId").GetString() == erp.ClientId)
            .GetProperty("id").GetGuid();

        var iptal = await _tenantAdmin.DeleteAsync(
            $"/api/erp/service-identities/{kimlikId}/keys/{erp.KeyId}");

        iptal.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await JetonAlAsync(Beyan(erp))).StatusCode);
    }

    [Fact(DisplayName = "EK10. Reddedilme sebebi dışarıya AYRIŞTIRILMADAN döner")]
    public async Task Sebep_ayristirilmaz()
    {
        // Bilinmeyen istemci ile bozuk imza aynı cevabı almalı; ayrılsaydı saldırgan
        // hangi istemci kimliklerinin var olduğunu öğrenirdi.
        var erp = await KimlikKurAsync();

        var bilinmeyen = erp with { ClientId = "erp_hicboyleyok" };

        var a = await JetonAlAsync(Beyan(bilinmeyen));
        var b = await JetonAlAsync(Beyan(erp with { Ozel = ECDsa.Create(ECCurve.NamedCurves.nistP256) }));

        Assert.Equal(a.StatusCode, b.StatusCode);
        Assert.Equal(
            await a.Content.ReadAsStringAsync(),
            await b.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "EK11. ERP jetonu PANEL uçlarında kabul EDİLMEZ")]
    public async Task Erp_jetonu_panelde_gecmez()
    {
        // Jetonun alıcısı ayrıdır. Aynı olsaydı dar yetkili bir entegrasyon jetonu
        // bütün API'yi açardı.
        var erp = await KimlikKurAsync();
        var client = await ErpIstemcisiAsync(erp);

        foreach (var yol in new[]
                 {
                     $"/api/companies",
                     $"/api/reports/companies/{_companyId}/dashboard",
                     $"/api/tenders/companies/{_companyId}",
                 })
        {
            var cevap = await client.GetAsync(yol);

            Assert.True(
                cevap.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"{yol} → {cevap.StatusCode}");
        }
    }

    [Fact(DisplayName = "EK12. PANEL jetonu ERP modülü uçlarında kabul EDİLMEZ")]
    public async Task Panel_jetonu_erp_modulunde_gecmez()
    {
        var cevap = await _tenantAdmin.GetAsync("/api/erp-module/whoami");

        Assert.True(cevap.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "EK13. Jeton BAŞKA kiracının şirketine geçemez")]
    public async Task Kiracilar_arasi_gecis_yok()
    {
        // Şirket kimliği istek parametresi değil, jetonun claim'idir. Parametre olsaydı
        // ERP kendi jetonuyla başka şirketin verisini isteyebilirdi.
        var erp = await KimlikKurAsync();
        var client = await ErpIstemcisiAsync(erp);

        var kim = await client.GetFromJsonAsync<JsonElement>("/api/erp-module/whoami");

        Assert.Equal(_companyId, kim.GetProperty("companyId").GetGuid());
        Assert.NotEqual(_factory.TenantB.CompanyId, kim.GetProperty("companyId").GetGuid());
    }

    [Fact(DisplayName = "EK14. Kayıtta hiçbir SIR tutulmaz; yanıt da sır döndürmez")]
    public async Task Sir_tutulmaz()
    {
        var erp = await KimlikKurAsync();

        var govde = await (await _tenantAdmin.GetAsync(
            $"/api/erp/companies/{_companyId}/service-identities")).Content.ReadAsStringAsync();

        foreach (var yasak in new[] { "secret", "privateKey", "PRIVATE KEY", "password" })
        {
            Assert.DoesNotContain(yasak, govde, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(erp.ClientId, govde, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "EK15. ÖZEL anahtar yapıştırılırsa kayıt REDDEDİLİR")]
    public async Task Ozel_anahtar_reddedilir()
    {
        // En olası ve en pahalı hata; kabul edilseydi GOVAI veritabanında ERP'nin
        // imzalama anahtarı dururdu.
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var cevap = await _tenantAdmin.PostAsJsonAsync(
            $"/api/erp/companies/{_companyId}/service-identities",
            new
            {
                displayName = "Yanlış kurulum",
                keyId = "k1",
                algorithm = "ES256",
                publicKeyPem = ecdsa.ExportPkcs8PrivateKeyPem(),
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, cevap.StatusCode);
    }

    [Fact(DisplayName = "EK16. Anahtar DEĞİŞİMİ kesintisizdir")]
    public async Task Anahtar_degisimi_kesintisiz()
    {
        var erp = await KimlikKurAsync("eski");

        var liste = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/erp/companies/{_companyId}/service-identities");

        var kimlikId = liste.EnumerateArray()
            .Single(k => k.GetProperty("clientId").GetString() == erp.ClientId)
            .GetProperty("id").GetGuid();

        var yeniOzel = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var ekle = await _tenantAdmin.PostAsJsonAsync(
            $"/api/erp/service-identities/{kimlikId}/keys",
            new { keyId = "yeni", algorithm = "ES256", publicKeyPem = yeniOzel.ExportSubjectPublicKeyInfoPem() });

        ekle.EnsureSuccessStatusCode();

        // Geçiş sırasında İKİSİ de çalışır; tek anahtar zorunlu olsaydı her değişim
        // kesinti demek olurdu ve anahtarlar hiç değiştirilmezdi.
        var eskiyle = await JetonAlAsync(Beyan(erp));
        var yeniyle = await JetonAlAsync(Beyan(erp with { Ozel = yeniOzel, KeyId = "yeni" }));

        eskiyle.EnsureSuccessStatusCode();
        yeniyle.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "EK17. Kimlik tanımlamak ManageProfile ister")]
    public async Task Kimlik_tanimlamak_yetki_ister()
    {
        var okuyucu = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.ViewerEmail);

        var cevap = await okuyucu.PostAsJsonAsync(
            $"/api/erp/companies/{_companyId}/service-identities",
            new { displayName = "X", keyId = "k", algorithm = "ES256", publicKeyPem = "-----BEGIN PUBLIC KEY-----\nx\n-----END PUBLIC KEY-----" });

        Assert.Equal(HttpStatusCode.Forbidden, cevap.StatusCode);
    }

    [Fact(DisplayName = "EK18. Bozuk beyan çökmeye YOL AÇMAZ")]
    public async Task Bozuk_beyan_cokmez()
    {
        foreach (var bozuk in new[] { "", "abc", "a.b.c", "....", new string('x', 5000) })
        {
            var cevap = await JetonAlAsync(bozuk);

            Assert.True(
                cevap.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest,
                $"beklenmeyen: {cevap.StatusCode}");
        }
    }
}
