using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GovAI.Api.Tests;

/// <summary>
/// Faz 1 sonrası iki yetki kararının testleri:
///
/// <list type="number">
///   <item>
///     <b>Şirket kaydetme</b> kiracı işlemidir. Kiracı yöneticisi her zaman, sıradan
///     kullanıcı ise yalnızca aynı kiracıda en az bir etkin CompanyOwner üyeliği varsa
///     ekleyebilir. Karar veritabanındaki üyelikten verilir, jetondaki claim'den değil.
///   </item>
///   <item>
///     <b>Şirket kullanıcı listesi</b> yalnızca sahip ve yöneticiye açıktır. Uzman ve
///     görüntüleyici listeyi sunucudan da okuyamaz.
///   </item>
/// </list>
///
/// Tümü gerçek HTTP hattı üzerinden koşar.
/// </summary>
public sealed class CompanyAuthorizationTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _tenantAdmin = null!;   // SuperAdmin + CompanyOwner
    private HttpClient _owner = null!;         // SuperAdmin DEĞİL, CompanyOwner
    private HttpClient _manager = null!;       // CompanyManager
    private HttpClient _expert = null!;        // CompanyExpert
    private HttpClient _viewer = null!;        // CompanyViewer
    private HttpClient _systemIngest = null!;  // veri toplama kimliği
    private HttpClient _otherTenant = null!;   // B kiracısı yöneticisi

    private HttpClient _emptyTenantAdmin = null!;  // şirketi olmayan kiracının yöneticisi
    private HttpClient _emptyTenantReader = null!; // şirketi olmayan kiracının okuyucusu

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _owner = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.OwnerEmail);
        _manager = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.OperatorEmail);
        _expert = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.ExpertEmail);
        _viewer = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.ViewerEmail);
        _systemIngest = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);
        _otherTenant = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        _emptyTenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantEmpty);
        _emptyTenantReader = await _factory.CreateAuthenticatedClientAsync(_factory.TenantEmptyDenied.ViewerEmail);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object NewCompany(string taxNumber, string legalName = "Yeni Tüzel Kişilik A.Ş.") => new
    {
        legalName,
        taxNumber,
        country = "TR",
        mainSector = "Metal sanayi ve fabrikasyon",
        primaryNaceCode = "2562"
    };

    private async Task<int> CompanyCountAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<JsonElement>>("/api/companies") ?? []).Count;

    // ═══════════════════ Şirket kaydetme ═══════════════════

    [Fact(DisplayName = "Yetki-A. Kiracı yöneticisi çalışma alanındaki ilk şirketi açabilir")]
    public async Task Kiraci_yoneticisi_ilk_sirketi_acabilir()
    {
        // Bu kiracıda hiç şirket yok.
        Assert.Empty(await _emptyTenantAdmin.GetFromJsonAsync<List<JsonElement>>("/api/companies") ?? []);

        var response = await _emptyTenantAdmin.PostAsJsonAsync("/api/companies", NewCompany("8100000001"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Created", result.GetProperty("outcome").GetString());

        // Ekleyen kişi şirketin sahibi olarak kaydedilir.
        var companies = await _emptyTenantAdmin.GetFromJsonAsync<List<JsonElement>>("/api/companies") ?? [];
        Assert.Single(companies);
        Assert.Equal("CompanyOwner", companies[0].GetProperty("companyRole").GetString());
    }

    [Fact(DisplayName = "Yetki-B. Şirketi olmayan çalışma alanında sıradan kullanıcı ilk şirketi açamaz")]
    public async Task Bos_calisma_alaninda_siradan_kullanici_sirket_acamaz()
    {
        var response = await _emptyTenantReader.PostAsJsonAsync("/api/companies", NewCompany("8100000002"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await _emptyTenantReader.GetFromJsonAsync<List<JsonElement>>("/api/companies") ?? []);
    }

    [Fact(DisplayName = "Yetki-C. CompanyOwner yeni şirket ekleyebilir (kiracı yöneticisi olmadan)")]
    public async Task CompanyOwner_yeni_sirket_ekleyebilir()
    {
        var before = await CompanyCountAsync(_owner);

        var response = await _owner.PostAsJsonAsync("/api/companies", NewCompany("8200000001"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(before + 1, await CompanyCountAsync(_owner));

        // Yeni şirkette de sahiptir.
        var companies = await _owner.GetFromJsonAsync<List<JsonElement>>("/api/companies") ?? [];
        var eklenen = companies.Single(c => c.GetProperty("taxNumber").GetString() == "8200000001");
        Assert.Equal("CompanyOwner", eklenen.GetProperty("companyRole").GetString());
    }

    [Fact(DisplayName = "Yetki-D. CompanyManager şirket ekleyemez")]
    public async Task CompanyManager_sirket_ekleyemez()
    {
        var response = await _manager.PostAsJsonAsync("/api/companies", NewCompany("8300000001"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Yetki-E. CompanyExpert şirket ekleyemez")]
    public async Task CompanyExpert_sirket_ekleyemez()
    {
        var response = await _expert.PostAsJsonAsync("/api/companies", NewCompany("8300000002"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Yetki-F. CompanyViewer şirket ekleyemez")]
    public async Task CompanyViewer_sirket_ekleyemez()
    {
        var response = await _viewer.PostAsJsonAsync("/api/companies", NewCompany("8300000003"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Yetki-G. SystemIngest şirket ekleyemez")]
    public async Task SystemIngest_sirket_ekleyemez()
    {
        var response = await _systemIngest.PostAsJsonAsync("/api/companies", NewCompany("8300000004"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Yetki-H. Reddedilen şirket ekleme isteği veritabanını değiştirmez")]
    public async Task Reddedilen_istek_veritabanini_degistirmez()
    {
        var before = await CompanyCountAsync(_tenantAdmin);

        // Reddedilen üç farklı rol; hiçbiri kayıt bırakmamalı.
        foreach (var (client, vergi) in new[]
                 {
                     (_manager, "8400000001"),
                     (_expert, "8400000002"),
                     (_viewer, "8400000003")
                 })
        {
            var response = await client.PostAsJsonAsync("/api/companies", NewCompany(vergi));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(before, await CompanyCountAsync(_tenantAdmin));

        // Çapraz çalışma alanı yolu doğrulama talebi yazar; yetkisiz istek oraya da
        // ulaşmamalıdır. B kiracısının vergi numarasıyla denenir.
        var crossTenant = await _viewer.PostAsJsonAsync(
            "/api/companies", NewCompany(_factory.TenantB.TaxNumber));

        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

        var talepler = await _tenantAdmin.GetFromJsonAsync<List<JsonElement>>(
            "/api/companies/verification-requests") ?? [];
        Assert.Empty(talepler);
    }

    // ═══════════════════ Kullanıcı listesi ═══════════════════

    [Fact(DisplayName = "Yetki-I. CompanyOwner üye listesini okuyabilir ve yönetebilir")]
    public async Task CompanyOwner_uye_listesini_yonetebilir()
    {
        // Kiracı yöneticiliğinden bağımsız olduğunu göstermek için, sahipliğin
        // üyelikten geldiği ikinci şirket kullanılır.
        var companyId = _factory.TenantA.SecondCompanyId;

        var list = await _owner.GetAsync($"/api/companies/{companyId}/members");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var members = await list.Content.ReadFromJsonAsync<List<JsonElement>>() ?? [];
        Assert.NotEmpty(members);

        // Yönetim yolu da açık: davet oluşturabilir.
        var invite = await _owner.PostAsJsonAsync(
            $"/api/companies/{companyId}/members/invitations",
            new { email = "davetli@govai.test", companyRole = "CompanyExpert" });

        Assert.Equal(HttpStatusCode.OK, invite.StatusCode);
    }

    [Fact(DisplayName = "Yetki-J. CompanyManager üye listesini okur ama değiştiremez")]
    public async Task CompanyManager_listeyi_okur_degistiremez()
    {
        var companyId = _factory.TenantA.CompanyId;

        var list = await _manager.GetAsync($"/api/companies/{companyId}/members");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var members = await list.Content.ReadFromJsonAsync<List<JsonElement>>() ?? [];
        Assert.NotEmpty(members);

        // Salt okunur alanlar listede gerçekten dönüyor.
        var ilk = members[0];
        Assert.False(string.IsNullOrWhiteSpace(ilk.GetProperty("companyRole").GetString()));
        Assert.True(ilk.TryGetProperty("isActive", out _));
        Assert.True(ilk.TryGetProperty("isDefault", out _));

        var viewerMembership = members.Single(
            m => m.GetProperty("userId").GetGuid() == _factory.TenantA.ViewerUserId);
        var membershipId = viewerMembership.GetProperty("membershipId").GetGuid();

        // Değiştirme yollarının tamamı kapalı.
        var rol = await _manager.PutAsJsonAsync(
            $"/api/companies/{companyId}/members/{membershipId}/role",
            new { companyRole = "CompanyExpert" });

        var kaldir = await _manager.DeleteAsync($"/api/companies/{companyId}/members/{membershipId}");

        var ekle = await _manager.PostAsJsonAsync(
            $"/api/companies/{companyId}/members",
            new { userId = _factory.TenantA.ExpertUserId, companyRole = "CompanyOwner" });

        var davet = await _manager.PostAsJsonAsync(
            $"/api/companies/{companyId}/members/invitations",
            new { email = "olmaz@govai.test", companyRole = "CompanyExpert" });

        Assert.Equal(HttpStatusCode.Forbidden, rol.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, kaldir.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ekle.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, davet.StatusCode);

        // Hiçbiri üyeliği bozmadı.
        var sonra = await _manager.GetFromJsonAsync<List<JsonElement>>(
            $"/api/companies/{companyId}/members") ?? [];
        Assert.Equal(members.Count, sonra.Count);
    }

    [Fact(DisplayName = "Yetki-K. CompanyExpert üye listesini okuyamaz")]
    public async Task CompanyExpert_uye_listesini_okuyamaz()
    {
        var response = await _expert.GetAsync($"/api/companies/{_factory.TenantA.CompanyId}/members");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Yetki-L. CompanyViewer üye listesini okuyamaz")]
    public async Task CompanyViewer_uye_listesini_okuyamaz()
    {
        var response = await _viewer.GetAsync($"/api/companies/{_factory.TenantA.CompanyId}/members");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ═══════════════════ Varsayılan aktif şirket ═══════════════════

    /// <summary>Giriş yanıtından hem jetonu hem sunucunun seçtiği aktif şirketi okur.</summary>
    private async Task<(string Token, Guid? ActiveCompanyId, JsonElement Claims)> LoginAsync(string email)
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/auth/login", new { email, password = GovAiApiFactory.Password });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString()!;

        Guid? active = body.TryGetProperty("activeCompanyId", out var raw)
                       && raw.ValueKind is not JsonValueKind.Null
            ? raw.GetGuid()
            : null;

        return (token, active, ReadClaims(token));
    }

    private static JsonElement ReadClaims(string jwt)
    {
        var payload = jwt.Split('.')[1];
        payload = payload.Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        return JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(payload));
    }

    [Fact(DisplayName = "Aktif-A. Giriş varsayılan aktif şirketi jetona ekler")]
    public async Task Giris_varsayilan_aktif_sirketi_jetona_ekler()
    {
        // B kiracısı bilerek seçilir: bu sınıftaki diğer testler A kiracısının
        // varsayılanını değiştirdiği için sıraya bağlı bir beklenti kurulmaz.
        var (_, active, claims) = await LoginAsync(_factory.TenantB.Email);

        // Fixture'da yöneticinin varsayılan şirketi birinci şirkettir.
        Assert.Equal(_factory.TenantB.CompanyId, active);
        Assert.Equal(_factory.TenantB.CompanyId, claims.GetProperty("active_company").GetGuid());

        // Kapsam da üyelikten kurulur; "tüm kiracı" varsayımına düşülmez.
        Assert.Equal("list", claims.GetProperty("company_scope").GetString());
    }

    [Fact(DisplayName = "Aktif-B. Üyeliği olmayan kullanıcıya aktif şirket verilmez")]
    public async Task Uyeligi_olmayan_kullaniciya_aktif_sirket_verilmez()
    {
        // Boş kiracının okuyucusunun hiç üyeliği yok.
        var (_, active, claims) = await LoginAsync(_factory.TenantEmptyDenied.ViewerEmail);

        Assert.Null(active);
        Assert.False(claims.TryGetProperty("active_company", out _));
    }

    [Fact(DisplayName = "Aktif-C. Varsayılan üyeliği olmayan kullanıcıya biri kalıcı olarak varsayılan yapılır")]
    public async Task Varsayilan_yoksa_ilk_uyelik_varsayilan_yapilir()
    {
        var companyId = _factory.TenantA.SecondCompanyId;

        // Sahip, ikinci şirketteki kendi üyeliğini varsayılan olmaktan çıkarır:
        // birinci şirketi varsayılan yaparak.
        var setDefault = await _owner.PostAsync($"/api/companies/{companyId}/members/default", null);
        setDefault.EnsureSuccessStatusCode();

        var (_, active, claims) = await LoginAsync(_factory.TenantA.OwnerEmail);

        Assert.Equal(companyId, active);
        Assert.Equal(companyId, claims.GetProperty("active_company").GetGuid());
    }

    [Fact(DisplayName = "Aktif-D. Şirket değiştirme yeni aktif şirketi jetona yazar")]
    public async Task Sirket_degistirme_yeni_aktif_sirketi_jetona_yazar()
    {
        var hedef = _factory.TenantA.SecondCompanyId;

        var response = await _tenantAdmin.PostAsJsonAsync(
            "/api/auth/active-company", new { companyId = hedef });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(hedef, result.GetProperty("companyId").GetGuid());

        var claims = ReadClaims(result.GetProperty("accessToken").GetString()!);
        Assert.Equal(hedef, claims.GetProperty("active_company").GetGuid());
    }

    [Fact(DisplayName = "Aktif-E. Üyeliği kaldırılan kullanıcının eski jetonu erişemez")]
    public async Task Uyeligi_kaldirilan_kullanicinin_eski_jetonu_erisemez()
    {
        var companyId = _factory.TenantA.CompanyId;

        // Uzman, üyeliği dururken şirket profilini okuyabiliyor.
        var (expertToken, _, _) = await LoginAsync(_factory.TenantA.ExpertEmail);

        using var expertClient = _factory.CreateClient();
        expertClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", expertToken);

        Assert.Equal(
            HttpStatusCode.OK,
            (await expertClient.GetAsync($"/api/company-profile/{companyId}")).StatusCode);

        // Sahip üyeliği kaldırır.
        var members = await _tenantAdmin.GetFromJsonAsync<List<JsonElement>>(
            $"/api/companies/{companyId}/members") ?? [];

        var membershipId = members
            .Single(m => m.GetProperty("userId").GetGuid() == _factory.TenantA.ExpertUserId)
            .GetProperty("membershipId").GetGuid();

        var remove = await _tenantAdmin.DeleteAsync($"/api/companies/{companyId}/members/{membershipId}");
        remove.EnsureSuccessStatusCode();

        // Aynı jeton hâlâ imza olarak geçerli ama artık erişim vermez.
        var after = await expertClient.GetAsync($"/api/company-profile/{companyId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact(DisplayName = "Yetki-M. Başka çalışma alanının üyeleri listelenemez")]
    public async Task Baska_kiracinin_uyeleri_listelenemez()
    {
        var response = await _otherTenant.GetAsync($"/api/companies/{_factory.TenantA.CompanyId}/members");

        // 403 değil 404: "yasak" cevabı o şirketin var olduğunu doğrular ve kimlik
        // sayımına izin verirdi.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(_factory.TenantA.Email, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.TenantA.OwnerEmail, body, StringComparison.OrdinalIgnoreCase);
    }
}
