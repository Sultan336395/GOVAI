using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GovAI.Api.Tests;

/// <summary>
/// Faz 1 çoklu şirket senaryoları (A–O). Faz 0'daki aynı harfli testlerden ayırmak için
/// görünen adlar "Faz1-" önekiyle yazılır.
///
/// Tümü gerçek HTTP hattı üzerinden koşar: kimlik doğrulama, politikalar, üyelik tabanlı
/// erişim kapısı ve EF sorgu filtreleri üretimdeki gibi devrededir.
/// </summary>
public sealed class MultiCompanyTests : IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = new();

    private HttpClient _owner = null!;    // A kiracısı, iki şirkette de CompanyOwner
    private HttpClient _manager = null!;  // birinci şirkette CompanyManager
    private HttpClient _expert = null!;   // birinci şirkette CompanyExpert
    private HttpClient _viewer = null!;   // birinci şirkette CompanyViewer
    private HttpClient _otherTenant = null!;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();
        _owner = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _manager = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.OperatorEmail);
        _expert = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.ExpertEmail);
        _viewer = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.ViewerEmail);
        _otherTenant = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);
    }

    public Task DisposeAsync()
    {
        _owner.Dispose();
        _manager.Dispose();
        _expert.Dispose();
        _viewer.Dispose();
        _otherTenant.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ═══════════════════ A ═══════════════════

    [Fact(DisplayName = "Faz1-A. Kullanıcı birden fazla şirkete üye olabilir")]
    public async Task Kullanici_birden_fazla_sirkete_uye_olabilir()
    {
        var response = await _owner.GetAsync("/api/companies");
        response.EnsureSuccessStatusCode();

        var companies = await response.Content.ReadFromJsonAsync<List<JsonElement>>() ?? [];
        var ids = companies.Select(c => c.GetProperty("id").GetGuid()).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Contains(_factory.TenantA.CompanyId, ids);
        Assert.Contains(_factory.TenantA.SecondCompanyId, ids);

        // Rol bilgisi de üyelikten gelir.
        Assert.All(companies, c => Assert.Equal("CompanyOwner", c.GetProperty("companyRole").GetString()));
    }

    [Fact(DisplayName = "Faz1-A2. Kullanıcı yalnızca üye olduğu şirketleri görür")]
    public async Task Kullanici_yalnizca_uye_oldugu_sirketleri_gorur()
    {
        var response = await _manager.GetAsync("/api/companies");
        response.EnsureSuccessStatusCode();

        var companies = await response.Content.ReadFromJsonAsync<List<JsonElement>>() ?? [];
        var ids = companies.Select(c => c.GetProperty("id").GetGuid()).ToList();

        Assert.Single(ids);
        Assert.Contains(_factory.TenantA.CompanyId, ids);
        Assert.DoesNotContain(_factory.TenantA.SecondCompanyId, ids);
    }

    // ═══════════════════ B ve C ═══════════════════

    [Fact(DisplayName = "Faz1-B. Kullanıcı şirketler arasında geçiş yapabilir")]
    public async Task Kullanici_sirketler_arasinda_gecis_yapabilir()
    {
        var response = await _owner.PostAsJsonAsync("/api/auth/active-company", new
        {
            companyId = _factory.TenantA.SecondCompanyId
        });

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_factory.TenantA.SecondCompanyId, result.GetProperty("companyId").GetGuid());
        Assert.Equal("CompanyOwner", result.GetProperty("companyRole").GetString());

        // Yeni jeton döner ve çalışır.
        var newToken = result.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newToken));

        using var switched = _factory.CreateClient();
        switched.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", newToken);

        var check = await switched.GetAsync($"/api/company-profile/{_factory.TenantA.SecondCompanyId}");
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
    }

    [Fact(DisplayName = "Faz1-C. Üye olmadığı şirkete geçemez")]
    public async Task Uye_olmadigi_sirkete_gecemez()
    {
        // Manager yalnızca birinci şirkete üye; ikinciye geçemez.
        var response = await _manager.PostAsJsonAsync("/api/auth/active-company", new
        {
            companyId = _factory.TenantA.SecondCompanyId
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "Faz1-C2. Başka kiracının şirketine geçemez")]
    public async Task Baska_kiracinin_sirketine_gecemez()
    {
        var response = await _owner.PostAsJsonAsync("/api/auth/active-company", new
        {
            companyId = _factory.TenantB.CompanyId
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(_factory.TenantB.Name, body, StringComparison.Ordinal);
    }

    // ═══════════════════ D ve E ═══════════════════

    [Fact(DisplayName = "Faz1-D. Aynı vergi numarası aynı çalışma alanında tekrar oluşturulamaz")]
    public async Task Ayni_vergi_numarasi_ayni_tenantta_tekrar_olusturulamaz()
    {
        var response = await _owner.PostAsJsonAsync("/api/companies", NewCompany(_factory.TenantA.TaxNumber));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AlreadyInWorkspace", result.GetProperty("outcome").GetString());
        Assert.Contains("çalışma alanınızda kayıtlı", result.GetProperty("message").GetString()!, StringComparison.Ordinal);

        // Yetkisi olduğu için mevcut şirkete gidebilmeli.
        Assert.True(result.GetProperty("canNavigateToExisting").GetBoolean());
        Assert.Equal(_factory.TenantA.CompanyId, result.GetProperty("companyId").GetGuid());
    }

    [Fact(DisplayName = "Faz1-E. Başka çalışma alanındaki vergi numarası bilgi sızdırmaz")]
    public async Task Baska_tenanttaki_vergi_numarasi_bilgi_sizdirmaz()
    {
        var response = await _owner.PostAsJsonAsync("/api/companies", NewCompany(_factory.TenantB.TaxNumber));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(body);

        Assert.Equal("VerificationRequired", result.GetProperty("outcome").GetString());
        Assert.Contains("doğrulama veya bağlantı talebi", result.GetProperty("message").GetString()!, StringComparison.Ordinal);

        // Şirket oluşturulmadı ve karşı taraf hakkında HİÇBİR bilgi dönmedi.
        // API null alanları hiç yazmaz (DefaultIgnoreCondition.WhenWritingNull);
        // sızıntı açısından "alan yok" ile "alan null" aynı güvencedir.
        Assert.True(!result.TryGetProperty("companyId", out var sizanId)
                    || sizanId.ValueKind == JsonValueKind.Null);
        Assert.DoesNotContain(_factory.TenantB.Name, body, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.TenantB.CompanyId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.TenantB.TenantId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sanayi", body, StringComparison.Ordinal);

        // Doğrulama talebi açıldı; içeriğinde de karşı taraf yok.
        var requests = await _owner.GetStringAsync("/api/companies/verification-requests");
        Assert.Contains(_factory.TenantB.TaxNumber, requests, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.TenantB.Name, requests, StringComparison.Ordinal);
    }

    // ═══════════════════ F ═══════════════════

    [Fact(DisplayName = "Faz1-F. CompanyOwner yeni şirket ekleyebilir")]
    public async Task CompanyOwner_sirket_ekleyebilir()
    {
        var response = await _owner.PostAsJsonAsync("/api/companies", NewCompany("7778889990"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Created", result.GetProperty("outcome").GetString());

        var newId = result.GetProperty("companyId").GetGuid();

        // Ekleyen kişi otomatik olarak sahibi olur; aksi hâlde kendi eklediğine erişemezdi.
        var mine = await _owner.GetFromJsonAsync<List<JsonElement>>("/api/companies") ?? [];
        var added = mine.Single(c => c.GetProperty("id").GetGuid() == newId);
        Assert.Equal("CompanyOwner", added.GetProperty("companyRole").GetString());
    }

    [Fact(DisplayName = "Faz1-F2. Zorunlu alanlar ve vergi numarası doğrulanır")]
    public async Task Zorunlu_alanlar_dogrulanir()
    {
        var harfli = await _owner.PostAsJsonAsync("/api/companies", NewCompany("ABC1234567"));
        var kisa = await _owner.PostAsJsonAsync("/api/companies", NewCompany("12345"));
        var sektorsuz = await _owner.PostAsJsonAsync("/api/companies", new
        {
            legalName = "Sektörsüz A.Ş.",
            taxNumber = "7778889991",
            country = "TR",
            mainSector = "",
            primaryNaceCode = "2562"
        });

        Assert.Equal(HttpStatusCode.BadRequest, harfli.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, kisa.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sektorsuz.StatusCode);

        Assert.Contains("yalnızca rakam", await harfli.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("10 hane", await kisa.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ═══════════════════ G, H, I ═══════════════════

    [Fact(DisplayName = "Faz1-G. CompanyManager profil alanlarını düzenleyebilir")]
    public async Task CompanyManager_profili_duzenleyebilir()
    {
        var response = await _manager.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}",
            NewCompany(_factory.TenantA.TaxNumber, "Kiracı A Sanayi A.Ş. (güncel)"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Kiracı A Sanayi A.Ş. (güncel)", updated.GetProperty("legalName").GetString());
    }

    [Fact(DisplayName = "Faz1-G2. CompanyManager üyelik yönetemez")]
    public async Task CompanyManager_uyelik_yonetemez()
    {
        var response = await _manager.PostAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members",
            new { userId = _factory.TenantA.ViewerUserId, companyRole = "CompanyExpert" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Faz1-H. CompanyExpert şirket üyeliği yönetemez")]
    public async Task CompanyExpert_uyelik_yonetemez()
    {
        var add = await _expert.PostAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members",
            new { userId = _factory.TenantA.ViewerUserId, companyRole = "CompanyManager" });

        var invite = await _expert.PostAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members/invitations",
            new { email = "davet@govai.test", companyRole = "CompanyViewer", validForDays = 7 });

        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);
    }

    [Fact(DisplayName = "Faz1-H2. CompanyExpert profil düzenleyemez ama analiz yapabilir")]
    public async Task CompanyExpert_profil_duzenleyemez_analiz_yapabilir()
    {
        var edit = await _expert.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}",
            NewCompany(_factory.TenantA.TaxNumber));

        var simulate = await _expert.PostAsJsonAsync(
            $"/api/scoring/companies/{_factory.TenantA.CompanyId}/simulate?persist=false",
            new { name = "uzman senaryosu", employeeCount = 55 });

        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, simulate.StatusCode);
    }

    [Fact(DisplayName = "Faz1-I. CompanyViewer veri değiştiremez")]
    public async Task CompanyViewer_veri_degistiremez()
    {
        var edit = await _viewer.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}",
            NewCompany(_factory.TenantA.TaxNumber));

        var simulate = await _viewer.PostAsJsonAsync(
            $"/api/scoring/companies/{_factory.TenantA.CompanyId}/simulate?persist=true",
            new { name = "okuyucu senaryosu", employeeCount = 55 });

        var addMember = await _viewer.PostAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members",
            new { userId = _factory.TenantA.ExpertUserId, companyRole = "CompanyViewer" });

        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, simulate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, addMember.StatusCode);

        // Okuma serbest.
        var read = await _viewer.GetAsync($"/api/company-profile/{_factory.TenantA.CompanyId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    // ═══════════════════ J ve K ═══════════════════

    [Fact(DisplayName = "Faz1-J. Son CompanyOwner kaldırılamaz")]
    public async Task Son_owner_kaldirilamaz()
    {
        var members = await _owner.GetFromJsonAsync<List<JsonElement>>(
            $"/api/companies/{_factory.TenantA.CompanyId}/members") ?? [];

        var ownerMembership = members.Single(m => m.GetProperty("companyRole").GetString() == "CompanyOwner");
        var membershipId = ownerMembership.GetProperty("membershipId").GetGuid();

        var remove = await _owner.DeleteAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members/{membershipId}");

        Assert.Equal(HttpStatusCode.BadRequest, remove.StatusCode);
        Assert.Contains("son sahibi", await remove.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Faz1-K. Kullanıcı kendi rolünü değiştiremez")]
    public async Task Kullanici_kendi_rolunu_degistiremez()
    {
        var members = await _owner.GetFromJsonAsync<List<JsonElement>>(
            $"/api/companies/{_factory.TenantA.CompanyId}/members") ?? [];

        var own = members.Single(m => m.GetProperty("userId").GetGuid() == _factory.TenantA.UserId);
        var membershipId = own.GetProperty("membershipId").GetGuid();

        var response = await _owner.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members/{membershipId}/role",
            new { companyRole = "CompanyViewer" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Kendi şirket rolünüzü", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Faz1-K2. Yetkisiz kullanıcı kendini yükseltemez")]
    public async Task Yetkisiz_kullanici_kendini_yukseltemez()
    {
        // Görüntüleyici üye listesini artık okuyamaz; kendi üyelik kimliğini bilse bile
        // rolünü yükseltemediği sınanır, bu yüzden kimlik sahip hesabından alınır.
        var listeDenemesi = await _viewer.GetAsync($"/api/companies/{_factory.TenantA.CompanyId}/members");
        Assert.Equal(HttpStatusCode.Forbidden, listeDenemesi.StatusCode);

        var members = await _owner.GetFromJsonAsync<List<JsonElement>>(
            $"/api/companies/{_factory.TenantA.CompanyId}/members") ?? [];

        var own = members.Single(m => m.GetProperty("userId").GetGuid() == _factory.TenantA.ViewerUserId);
        var membershipId = own.GetProperty("membershipId").GetGuid();

        var response = await _viewer.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members/{membershipId}/role",
            new { companyRole = "CompanyOwner" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ═══════════════════ L ve M ═══════════════════

    [Fact(DisplayName = "Faz1-L. Ana şirket başka kiracıdan seçilemez")]
    public async Task Ana_sirket_baska_kiracidan_secilemez()
    {
        var response = await _owner.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/hierarchy",
            new
            {
                groupId = (Guid?)null,
                parentCompanyId = _factory.TenantB.CompanyId,
                relationshipType = "Subsidiary",
                isHeadCompany = false
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(_factory.TenantB.Name, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Faz1-M. Döngüsel şirket hiyerarşisi oluşturulamaz")]
    public async Task Dongusel_hiyerarsi_olusturulamaz()
    {
        // İkinci şirketi birinciye bağla.
        var first = await _owner.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.SecondCompanyId}/hierarchy",
            new
            {
                groupId = (Guid?)null,
                parentCompanyId = _factory.TenantA.CompanyId,
                relationshipType = "Subsidiary",
                isHeadCompany = false
            });
        first.EnsureSuccessStatusCode();

        // Şimdi birinciyi ikinciye bağlamayı dene: döngü oluşurdu.
        var cycle = await _owner.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/hierarchy",
            new
            {
                groupId = (Guid?)null,
                parentCompanyId = _factory.TenantA.SecondCompanyId,
                relationshipType = "Subsidiary",
                isHeadCompany = false
            });

        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
        Assert.Contains("Döngüsel", await cycle.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Faz1-M2. Şirket kendisinin ana şirketi olamaz")]
    public async Task Sirket_kendisinin_ana_sirketi_olamaz()
    {
        var response = await _owner.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/hierarchy",
            new
            {
                groupId = (Guid?)null,
                parentCompanyId = _factory.TenantA.CompanyId,
                relationshipType = "Subsidiary",
                isHeadCompany = false
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "Faz1-M3. Şirket grubu oluşturulup şirkete bağlanabilir")]
    public async Task Sirket_grubu_olusturulup_baglanabilir()
    {
        var create = await _owner.PostAsJsonAsync("/api/companies/groups", new
        {
            name = "Test Holding",
            description = "Faz 1 grup testi"
        });
        create.EnsureSuccessStatusCode();

        var group = await create.Content.ReadFromJsonAsync<JsonElement>();
        var groupId = group.GetProperty("id").GetGuid();

        var attach = await _owner.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/hierarchy",
            new
            {
                groupId,
                parentCompanyId = (Guid?)null,
                relationshipType = "HeadCompany",
                isHeadCompany = true
            });
        attach.EnsureSuccessStatusCode();

        var mine = await _owner.GetFromJsonAsync<List<JsonElement>>("/api/companies") ?? [];
        var company = mine.Single(c => c.GetProperty("id").GetGuid() == _factory.TenantA.CompanyId);

        Assert.Equal(groupId, company.GetProperty("groupId").GetGuid());
        Assert.Equal("Test Holding", company.GetProperty("groupName").GetString());
        Assert.True(company.GetProperty("isHeadCompany").GetBoolean());
    }

    // ═══════════════════ N ve O ═══════════════════

    [Fact(DisplayName = "Faz1-N. Aktif şirket değişince diğer şirketin verisi dönmez")]
    public async Task Aktif_sirket_degisince_eski_veri_donmez()
    {
        var switchResponse = await _owner.PostAsJsonAsync("/api/auth/active-company", new
        {
            companyId = _factory.TenantA.SecondCompanyId
        });
        switchResponse.EnsureSuccessStatusCode();

        var token = (await switchResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        using var switched = _factory.CreateClient();
        switched.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Aktif şirketin panosu ikinci şirkete aittir ve birinci şirketin adını içermez.
        var dashboard = await switched.GetAsync(
            $"/api/reports/companies/{_factory.TenantA.SecondCompanyId}/dashboard");
        dashboard.EnsureSuccessStatusCode();

        var body = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("Lojistik", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Sanayi A.Ş.", body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Faz1-O. Üyelik kaldırılınca eski jeton erişim sağlayamaz")]
    public async Task Uyelik_kaldirilinca_eski_jeton_erisemez()
    {
        // Görüntüleyici geçerli bir jetonla şirkete erişebiliyor.
        var before = await _viewer.GetAsync($"/api/company-profile/{_factory.TenantA.CompanyId}");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Sahip, görüntüleyicinin üyeliğini kaldırıyor.
        var members = await _owner.GetFromJsonAsync<List<JsonElement>>(
            $"/api/companies/{_factory.TenantA.CompanyId}/members") ?? [];

        var viewerMembership = members
            .Single(m => m.GetProperty("userId").GetGuid() == _factory.TenantA.ViewerUserId)
            .GetProperty("membershipId").GetGuid();

        var remove = await _owner.DeleteAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members/{viewerMembership}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        // AYNI jetonla tekrar denendiğinde artık erişemez: karar veritabanından verilir.
        var after = await _viewer.GetAsync($"/api/company-profile/{_factory.TenantA.CompanyId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    // ═══════════════════ Davet altyapısı ═══════════════════

    [Fact(DisplayName = "Faz1-P. Davet jetonu açık metin saklanmaz ve tek kullanımlıktır")]
    public async Task Davet_jetonu_guvenli_saklanir()
    {
        var create = await _owner.PostAsJsonAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members/invitations",
            new { email = "yeni-uye@govai.test", companyRole = "CompanyExpert", validForDays = 7 });

        create.EnsureSuccessStatusCode();

        var result = await create.Content.ReadFromJsonAsync<JsonElement>();
        var token = result.GetProperty("token").GetString()!;
        var invitationId = result.GetProperty("invitationId").GetGuid();

        Assert.False(string.IsNullOrWhiteSpace(token));

        // Listede jeton HİÇBİR şekilde görünmez; yalnızca üretim anında dönmüştür.
        var listBody = await _owner.GetStringAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members/invitations");

        Assert.DoesNotContain(token, listBody, StringComparison.Ordinal);
        Assert.Contains("yeni-uye@govai.test", listBody, StringComparison.Ordinal);

        // İptal edilebilir.
        var revoke = await _owner.DeleteAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members/invitations/{invitationId}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var afterRevoke = await _owner.GetStringAsync(
            $"/api/companies/{_factory.TenantA.CompanyId}/members/invitations");
        Assert.Contains("\"isRedeemable\":false", afterRevoke.Replace(" ", string.Empty), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Faz1-Q. Sahip yeni üye ekleyip rolünü değiştirebilir")]
    public async Task Sahip_uye_ekleyip_rol_degistirebilir()
    {
        // Uzmanı ikinci şirkete ekle.
        var add = await _owner.PostAsJsonAsync(
            $"/api/companies/{_factory.TenantA.SecondCompanyId}/members",
            new { userId = _factory.TenantA.ExpertUserId, companyRole = "CompanyViewer" });

        add.EnsureSuccessStatusCode();
        var member = await add.Content.ReadFromJsonAsync<JsonElement>();
        var membershipId = member.GetProperty("membershipId").GetGuid();

        Assert.Equal("CompanyViewer", member.GetProperty("companyRole").GetString());

        // Rolünü yükselt.
        var change = await _owner.PutAsJsonAsync(
            $"/api/companies/{_factory.TenantA.SecondCompanyId}/members/{membershipId}/role",
            new { companyRole = "CompanyManager" });

        change.EnsureSuccessStatusCode();
        var updated = await change.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CompanyManager", updated.GetProperty("companyRole").GetString());

        // Aynı kullanıcı aynı şirkete ikinci kez eklenemez.
        var duplicate = await _owner.PostAsJsonAsync(
            $"/api/companies/{_factory.TenantA.SecondCompanyId}/members",
            new { userId = _factory.TenantA.ExpertUserId, companyRole = "CompanyExpert" });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("zaten bu şirkete bağlı", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static object NewCompany(string taxNumber, string? legalName = null) => new
    {
        legalName = legalName ?? $"Yeni Şirket {taxNumber} A.Ş.",
        taxNumber,
        country = "TR",
        mainSector = "Makine ve ekipman imalatı",
        primaryNaceCode = "2562",
        legalType = "LimitedCompany",
        city = "Mersin",
        employeeCount = 25,
        annualRevenue = 12_000_000m
    };
}
