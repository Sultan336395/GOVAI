using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Api.Controllers;
using GovAI.Application.Identity;
using GovAI.Domain.Common;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Platform hesabının güvenli aktivasyonu (Faz 2).
///
/// <para>
/// Platform hesabına parola atanmaz: hesap parolasız ve pasif açılır, parolayı
/// yalnızca aktivasyon bağlantısını açan kişi belirler. Bu testler dört güvenlik
/// garantisini sabitler: jeton veritabanında açık durmaz, bağlantı tek kullanımlıktır,
/// 24 saat sonra geçersizleşir ve yalnızca PlatformReviewer rolü verilir.
/// </para>
/// </summary>
public sealed class PlatformActivationTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private const string GucluParola = "Kaynak-Denetim-2026";

    /// <summary>
    /// Her testin kendi e-postası olur.
    ///
    /// Fabrika sınıf düzeyinde paylaşılır ve veritabanı testler arasında yaşar; ortak
    /// bir e-posta kullanılsaydı bir testin açtığı hesap diğerini etkiler ve testler
    /// çalışma sırasına bağlı hâle gelirdi.
    /// </summary>
    private static string Eposta(string ek) => $"inceleyici-{ek}@govai.local";

    private HttpClient _anonim = null!;
    private HttpClient _tenantAdmin = null!;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _anonim = _factory.CreateClient();
        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> QueryAsync<T>(Func<GovAiDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        return await query(db);
    }

    /// <summary>Bootstrap sırrıyla aktivasyon üretir.</summary>
    private async Task<JsonElement> UretAsync(string email)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "/api/platform/activations")
        {
            Content = JsonContent.Create(new { email, fullName = "Platform İnceleyicisi" }),
        };

        istek.Headers.Add(
            PlatformActivationController.BootstrapHeader, GovAiApiFactory.BootstrapSecret);

        var response = await _anonim.SendAsync(istek);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Aktivasyonu tamamlar ve yanıtı gövdesiyle birlikte döner.</summary>
    private async Task<(HttpStatusCode Status, string Body)> TamamlaAsync(
        string jeton,
        string parola,
        string? tekrar = null)
    {
        var response = await _anonim.PostAsJsonAsync("/api/platform/activations/complete", new
        {
            token = jeton,
            password = parola,
            passwordConfirmation = tekrar ?? parola,
        });

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // ═══════════════ Jeton güvenliği ═══════════════

    [Fact(DisplayName = "PA1. Jeton veritabanında açık metin olarak durmaz")]
    public async Task Jeton_acik_saklanmaz()
    {
        var sonuc = await UretAsync(Eposta("jeton"));
        var jeton = sonuc.GetProperty("token").GetString()!;

        Assert.NotEmpty(jeton);

        var kayitlar = await QueryAsync(db => db.PlatformActivations.ToListAsync());

        // Hiçbir kayıtta açık jeton bulunmamalı; yalnızca SHA-256 özeti durur.
        Assert.DoesNotContain(kayitlar, k => k.TokenHash.Contains(jeton, StringComparison.Ordinal));
        Assert.Contains(kayitlar, k => k.TokenHash == PlatformActivationService.HashToken(jeton));

        // Özet, jetonun kendisinden farklı ve SHA-256 uzunluğunda (64 hex karakter).
        var kayit = kayitlar.Single(k => k.Email == Eposta("jeton"));
        Assert.Equal(64, kayit.TokenHash.Length);
        Assert.NotEqual(jeton, kayit.TokenHash);
    }

    [Fact(DisplayName = "PA2. Bağlantı 24 saat sonra geçersizleşir")]
    public async Task Baglanti_24_saat_gecerli()
    {
        var sonuc = await UretAsync(Eposta("sure"));
        var bitis = sonuc.GetProperty("expiresAt").GetDateTimeOffset();

        var fark = bitis - DateTimeOffset.UtcNow;

        Assert.True(fark > TimeSpan.FromHours(23), $"Geçerlilik çok kısa: {fark}");
        Assert.True(fark <= TimeSpan.FromHours(24), $"Geçerlilik 24 saati aşıyor: {fark}");
    }

    [Fact(DisplayName = "PA3. Süresi dolmuş bağlantı kullanılamaz")]
    public async Task Suresi_dolmus_baglanti_reddedilir()
    {
        var sonuc = await UretAsync(Eposta("dolmus"));
        var jeton = sonuc.GetProperty("token").GetString()!;

        // Süreyi geriye çekmek için kaydı doğrudan geçmişe taşı.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
            var kayit = await db.PlatformActivations.SingleAsync(
                a => a.TokenHash == PlatformActivationService.HashToken(jeton));

            db.Entry(kayit).Property("ExpiresAt").CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var durum = await _anonim.GetFromJsonAsync<JsonElement>($"/api/platform/activations/{jeton}");
        Assert.False(durum.GetProperty("isRedeemable").GetBoolean());

        var tamamla = await TamamlaAsync(jeton, GucluParola);
        Assert.Equal(HttpStatusCode.BadRequest, tamamla.Status);
    }

    // ═══════════════ Tek kullanımlık ═══════════════

    [Fact(DisplayName = "PA4. Bağlantı ikinci kez kullanılamaz")]
    public async Task Baglanti_tek_kullanimlik()
    {
        var sonuc = await UretAsync(Eposta("tek"));
        var jeton = sonuc.GetProperty("token").GetString()!;

        var ilk = await TamamlaAsync(jeton, GucluParola);
        Assert.True(ilk.Status is HttpStatusCode.OK, $"İlk aktivasyon başarısız: {ilk.Status} {ilk.Body}");

        var ikinci = await TamamlaAsync(jeton, "Baska-Parola-2026");
        Assert.Equal(HttpStatusCode.BadRequest, ikinci.Status);

        // Durum sorgusu da artık kullanılamaz diyor.
        var durum = await _anonim.GetFromJsonAsync<JsonElement>($"/api/platform/activations/{jeton}");
        Assert.False(durum.GetProperty("isRedeemable").GetBoolean());
    }

    [Fact(DisplayName = "PA5. Yeni bağlantı üretilince eskisi geçersizleşir")]
    public async Task Yeni_baglanti_eskisini_iptal_eder()
    {
        var eski = (await UretAsync(Eposta("iptal"))).GetProperty("token").GetString()!;
        var yeni = (await UretAsync(Eposta("iptal"))).GetProperty("token").GetString()!;

        Assert.NotEqual(eski, yeni);

        var eskiDurum = await _anonim.GetFromJsonAsync<JsonElement>($"/api/platform/activations/{eski}");
        var yeniDurum = await _anonim.GetFromJsonAsync<JsonElement>($"/api/platform/activations/{yeni}");

        Assert.False(eskiDurum.GetProperty("isRedeemable").GetBoolean());
        Assert.True(yeniDurum.GetProperty("isRedeemable").GetBoolean());
    }

    // ═══════════════ Hesap ve rol ═══════════════

    [Fact(DisplayName = "PA6. Hesap parolasız ve pasif açılır; aktivasyondan önce giriş yapılamaz")]
    public async Task Hesap_pasif_acilir()
    {
        await UretAsync(Eposta("pasif"));

        var kullanici = await QueryAsync(db => db.Users
            .IgnoreQueryFilters()
            .SingleAsync(u => u.Email == Eposta("pasif")));

        Assert.Null(kullanici.PasswordHash);
        Assert.False(kullanici.IsActive);
        Assert.Equal(UserRole.PlatformReviewer, kullanici.Role);

        var giris = await _anonim.PostAsJsonAsync("/api/auth/login", new
        {
            email = Eposta("pasif"), password = GucluParola,
        });

        Assert.NotEqual(HttpStatusCode.OK, giris.StatusCode);
    }

    [Fact(DisplayName = "PA7. Aktivasyondan sonra yalnızca PlatformReviewer rolüyle giriş yapılır")]
    public async Task Aktivasyon_sonrasi_giris()
    {
        var jeton = (await UretAsync(Eposta("giris"))).GetProperty("token").GetString()!;

        var tamamla = await TamamlaAsync(jeton, GucluParola);
        Assert.True(tamamla.Status is HttpStatusCode.OK, $"Aktivasyon başarısız: {tamamla.Status} {tamamla.Body}");

        var giris = await _anonim.PostAsJsonAsync("/api/auth/login", new
        {
            email = Eposta("giris"), password = GucluParola,
        });

        giris.EnsureSuccessStatusCode();
        var govde = await giris.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("PlatformReviewer", govde.GetProperty("user").GetProperty("role").GetString());
        Assert.NotEmpty(govde.GetProperty("accessToken").GetString()!);
    }

    [Fact(DisplayName = "PA8. Aynı e-posta için mükerrer hesap açılmaz")]
    public async Task Mukerrer_hesap_acilmaz()
    {
        await UretAsync(Eposta("mukerrer"));
        await UretAsync(Eposta("mukerrer"));
        await UretAsync(Eposta("mukerrer"));

        var sayi = await QueryAsync(db => db.Users
            .IgnoreQueryFilters()
            .CountAsync(u => u.Email == Eposta("mukerrer")));

        Assert.Equal(1, sayi);
    }

    // ═══════════════ Parola kuralı ═══════════════

    [Theory(DisplayName = "PA9. Zayıf parola reddedilir ve bağlantı tükenmez")]
    [InlineData("kisa1!")]
    [InlineData("abcdefghijklmnop")]
    [InlineData("aA1aA1aA1aA1aA1")]
    public async Task Zayif_parola_reddedilir(string zayif)
    {
        var jeton = (await UretAsync(Eposta($"zayif-{zayif.Length}"))).GetProperty("token").GetString()!;

        var response = await TamamlaAsync(jeton, zayif);
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);

        // Başarısız deneme bağlantıyı TÜKETMEZ; kullanıcı yeniden deneyebilir.
        var durum = await _anonim.GetFromJsonAsync<JsonElement>($"/api/platform/activations/{jeton}");
        Assert.True(durum.GetProperty("isRedeemable").GetBoolean());
    }

    [Fact(DisplayName = "PA10. Doğrulama alanı eşleşmezse reddedilir")]
    public async Task Parola_tekrari_eslesmeli()
    {
        var jeton = (await UretAsync(Eposta("tekrar"))).GetProperty("token").GetString()!;

        var response = await TamamlaAsync(jeton, GucluParola, "Baska-Parola-2026");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Contains("eşleşmiyor", response.Body, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════ Yetki ═══════════════

    [Fact(DisplayName = "PA11. Bootstrap sırrı olmadan aktivasyon üretilemez")]
    public async Task Sirsiz_uretilemez()
    {
        var response = await _anonim.PostAsJsonAsync("/api/platform/activations", new
        {
            email = Eposta("sirsiz"), fullName = "Deneme",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "PA12. Yanlış bootstrap sırrı reddedilir")]
    public async Task Yanlis_sir_reddedilir()
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "/api/platform/activations")
        {
            Content = JsonContent.Create(new { email = Eposta("yanlissir"), fullName = "Deneme" }),
        };

        istek.Headers.Add(PlatformActivationController.BootstrapHeader, "yanlis-sir");

        var response = await _anonim.SendAsync(istek);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "PA13. Kiracı yöneticisi platform rolü veremez")]
    public async Task Kiraci_yoneticisi_platform_rolu_veremez()
    {
        var response = await _tenantAdmin.PostAsJsonAsync("/api/admin/users", new
        {
            email = "sahte-inceleyici@govai.local",
            fullName = "Sahte İnceleyici",
            role = "PlatformReviewer",
            password = "Kaynak-Denetim-2026",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ═══════════════ Aktivasyon sonrası karantina yetkileri ═══════════════

    [Fact(DisplayName = "PA15. Etkinleşen inceleyici karantina işlemlerinin tamamını yapabilir")]
    public async Task Inceleyici_karantina_islemlerini_yapabilir()
    {
        // Faz 2'nin karantina inceleme özelliği bu hesap olmadan fiilen çalışmıyordu:
        // uçlar PlatformReview yetkisi ister ve temiz kurulumda o yetkiye sahip
        // kimse yoktur.
        var jeton = (await UretAsync(Eposta("yetki"))).GetProperty("token").GetString()!;

        var tamamla = await TamamlaAsync(jeton, GucluParola);
        Assert.True(tamamla.Status is HttpStatusCode.OK, tamamla.Body);

        var inceleyici = await _factory.CreateAuthenticatedClientAsync(Eposta("yetki"), GucluParola);

        // 1) Karantina listesini görüntüleyebilir.
        var liste = await inceleyici.GetAsync("/api/quarantine");
        Assert.Equal(HttpStatusCode.OK, liste.StatusCode);

        // 2) Triyaj raporunu (belge ve kanıt ayrıntıları) açabilir.
        var triyaj = await inceleyici.PostAsync("/api/quarantine/triage?apply=false", null);
        Assert.Equal(HttpStatusCode.OK, triyaj.StatusCode);

        // Üzerinde işlem yapılacak gerçek bir belge hazırlanır.
        var ingest = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);
        var sourceId = await IhaleKaynagiAsync();

        var belge = await ingest.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url = "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/09/20260906-3-77.pdf",
            title = "TAŞINMAZ SATILACAKTIR",
            rawContent =
                "TAŞINMAZ SATILACAKTIR. Çay İşletmeleri Genel Müdürlüğünden: İhale "
                + "18/09/2026 tarihinde yapılacaktır. Şartname İdareden temin edilir.",
            mediaType = "text/html",
            canonicalUrl = "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/09/20260906-3-77.pdf",
            charset = "utf-8",
            httpStatusCode = 200,
        });

        belge.EnsureSuccessStatusCode();
        var documentId = (await belge.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("documentId").GetGuid();

        // 3) Kaydı karantinada tutabilir (reddedebilir).
        var reddet = await inceleyici.PostAsJsonAsync(
            $"/api/quarantine/{documentId}/reject",
            new { reason = "NeedsManualReview", note = "İnceleme bekliyor." });

        Assert.Equal(HttpStatusCode.NoContent, reddet.StatusCode);

        var karantinada = await QueryAsync(db => db.SourceDocuments
            .IgnoreQueryFilters().SingleAsync(d => d.Id == documentId));

        Assert.True(karantinada.IsQuarantined);

        // 4) Kararı geri alabilir: onaylar, karantinadan çıkarır ve yeniden
        //    ayrıştırmaya gönderir.
        var onayla = await inceleyici.PostAsync($"/api/quarantine/{documentId}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, onayla.StatusCode);

        var serbest = await QueryAsync(db => db.SourceDocuments
            .IgnoreQueryFilters().SingleAsync(d => d.Id == documentId));

        Assert.False(serbest.IsQuarantined);
    }

    /// <summary>Karantina testleri için resmî alan adı tanımlı bir ihale kaynağı.</summary>
    private async Task<Guid> IhaleKaynagiAsync()
    {
        var katalog = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformCatalogEmail);

        var response = await katalog.PostAsJsonAsync("/api/sources", new
        {
            name = "Karantina Yetki Testi Kaynağı",
            type = "TenderPortal",
            baseUrl = "https://www.resmigazete.gov.tr",
            cronExpression = "0 6 * * *",
        });

        response.EnsureSuccessStatusCode();
        var sourceId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        var source = await db.Sources.SingleAsync(s => s.Id == sourceId);

        source.Describe(
            GovAI.Domain.Common.SourceCategory.Tender,
            new GovAI.Domain.Sources.SourceProfile("Resmî Gazete", "TR", "resmigazete.gov.tr", "tr"));

        await db.SaveChangesAsync();
        return sourceId;
    }

    [Fact(DisplayName = "PA14. Geçersiz jeton hesap bilgisi sızdırmaz")]
    public async Task Gecersiz_jeton_bilgi_sizdirmaz()
    {
        var durum = await _anonim.GetFromJsonAsync<JsonElement>(
            "/api/platform/activations/boyle-bir-jeton-yok");

        Assert.False(durum.GetProperty("isRedeemable").GetBoolean());

        // E-posta sızdırılmaz. API null alanları gövdeye hiç yazmaz (WhenWritingNull),
        // bu yüzden "yok" ile "null" aynı anlama gelir.
        Assert.True(
            !durum.TryGetProperty("email", out var eposta) || eposta.ValueKind is JsonValueKind.Null,
            "Geçersiz jeton için e-posta sızdırıldı.");
    }
}
