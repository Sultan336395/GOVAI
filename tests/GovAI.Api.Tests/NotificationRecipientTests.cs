using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Domain.Notifications;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Bildirim sorumluları: yetki, kiracı sınırı ve denetim izi.
///
/// <para>
/// Alıcılar <b>GOVAI kullanıcısı değildir</b> — hesapları ve parolaları yoktur.
/// Müşteri çalışanları kendi ERP'lerindeki GOVAI modülünü kullanır. Bu testler o
/// sınırı korur: alıcı listesi kullanıcı tablosundan türetilmemeli, kiracılar arası
/// karışmamalı ve her değişiklik denetim izine düşmelidir.
/// </para>
/// </summary>
public sealed class NotificationRecipientTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _tenantAdmin = null!;
    private Guid _companyId;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _companyId = _factory.TenantA.CompanyId;

        await TemizleAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> VeritabaniAsync<T>(Func<GovAiDbContext, Task<T>> islem)
    {
        using var kapsam = _factory.Services.CreateScope();

        return await islem(kapsam.ServiceProvider.GetRequiredService<GovAiDbContext>());
    }

    /// <summary>Testler arası sızıntı olmasın diye alıcılar sıfırlanır.</summary>
    private async Task TemizleAsync() =>
        await VeritabaniAsync(async db =>
        {
            var hepsi = await db.NotificationRecipients.IgnoreQueryFilters()
                .Where(r => r.CompanyId == _companyId).ToListAsync();

            db.NotificationRecipients.RemoveRange(hepsi);

            return await db.SaveChangesAsync();
        });

    private string Yol(Guid? companyId = null) =>
        $"/api/erp/companies/{companyId ?? _companyId}/notification-recipients";

    private async Task<HttpResponseMessage> YazAsync(
        object[] alicilar, HttpClient? client = null, Guid? companyId = null) =>
        await (client ?? _tenantAdmin).PutAsJsonAsync(
            Yol(companyId), new { recipients = alicilar });

    [Fact(DisplayName = "BA1. Birden fazla alıcı tanımlanabilir ve hesap GEREKTİRMEZ")]
    public async Task Birden_fazla_alici_tanimlanir()
    {
        var cevap = await YazAsync([
            new { email = "mali@musteri.test", fullName = "Mali İşler", role = "Müdür" },
            new { email = "hukuk@musteri.test", fullName = "Hukuk", role = "Uzman" },
        ]);

        cevap.EnsureSuccessStatusCode();

        var liste = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, liste.GetArrayLength());

        // Bu adreslerin hiçbirinin GOVAI kullanıcısı yok; olması da gerekmiyor.
        var kullaniciSayisi = await VeritabaniAsync(db => db.Users.IgnoreQueryFilters()
            .CountAsync(u => u.Email == "mali@musteri.test" || u.Email == "hukuk@musteri.test"));

        Assert.Equal(0, kullaniciSayisi);
    }

    [Fact(DisplayName = "BA2. Alıcı kaydı ŞİRKETE ve KİRACIYA bağlıdır")]
    public async Task Alici_sirkete_baglidir()
    {
        (await YazAsync([new { email = "a@musteri.test" }])).EnsureSuccessStatusCode();

        var kayit = await VeritabaniAsync(db => db.NotificationRecipients.IgnoreQueryFilters()
            .SingleAsync(r => r.CompanyId == _companyId));

        Assert.Equal(_factory.TenantA.TenantId, kayit.TenantId);
        Assert.Equal(_companyId, kayit.CompanyId);
        Assert.Equal(RecipientSource.Manual, kayit.Source);
        Assert.True(kayit.IsActive);
    }

    [Fact(DisplayName = "BA3. Geçersiz adres REDDEDİLİR ve hiçbiri kaydedilmez")]
    public async Task Gecersiz_adres_reddedilir()
    {
        // Kısmi kayıt, kullanıcıyı "ikisini de girdim" sanırken tekini göndermeye bırakır.
        var cevap = await YazAsync([
            new { email = "gecerli@musteri.test" },
            new { email = "bozuk" },
        ]);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, cevap.StatusCode);

        var sayi = await VeritabaniAsync(db => db.NotificationRecipients.IgnoreQueryFilters()
            .CountAsync(r => r.CompanyId == _companyId));

        Assert.Equal(0, sayi);
    }

    [Fact(DisplayName = "BA4. Listeden çıkarılan alıcı PASİFLEŞİR, silinmez")]
    public async Task Cikarilan_alici_pasiflesir()
    {
        (await YazAsync([
            new { email = "kalan@musteri.test" },
            new { email = "cikan@musteri.test" },
        ])).EnsureSuccessStatusCode();

        (await YazAsync([new { email = "kalan@musteri.test" }])).EnsureSuccessStatusCode();

        var kayitlar = await VeritabaniAsync(db => db.NotificationRecipients.IgnoreQueryFilters()
            .Where(r => r.CompanyId == _companyId).ToListAsync());

        Assert.Equal(2, kayitlar.Count);
        Assert.True(kayitlar.Single(r => r.Email == "kalan@musteri.test").IsActive);
        Assert.False(kayitlar.Single(r => r.Email == "cikan@musteri.test").IsActive);
    }

    [Fact(DisplayName = "BA5. Değişiklik DENETİM İZİNE düşer")]
    public async Task Degisiklik_denetime_duser()
    {
        var oncesi = await VeritabaniAsync(db => db.AuditLog.IgnoreQueryFilters()
            .CountAsync(a => a.Action == "NotificationRecipients.Replaced"));

        (await YazAsync([new { email = "izlenen@musteri.test" }])).EnsureSuccessStatusCode();

        var kayitlar = await VeritabaniAsync(db => db.AuditLog.IgnoreQueryFilters()
            .Where(a => a.Action == "NotificationRecipients.Replaced")
            .ToListAsync());

        Assert.Equal(oncesi + 1, kayitlar.Count);

        var son = kayitlar.OrderByDescending(a => a.OccurredAt).First();

        // Kim, ne zaman, hangi firma: üçü de kayıtta olmalı.
        Assert.Equal(_companyId.ToString(), son.EntityId);
        Assert.False(string.IsNullOrWhiteSpace(son.UserEmail));
        Assert.NotEqual(default, son.OccurredAt);
    }

    [Fact(DisplayName = "BA6. Görüntüleyici alıcıları OKUR ama değiştiremez")]
    public async Task Goruntuleyici_degistiremez()
    {
        var okuyucu = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.ViewerEmail);

        var oku = await okuyucu.GetAsync(Yol());
        var yaz = await YazAsync([new { email = "x@musteri.test" }], okuyucu);

        Assert.Equal(HttpStatusCode.OK, oku.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, yaz.StatusCode);
    }

    [Fact(DisplayName = "BA7. Başka kiracı alıcıları GÖREMEZ ve yazamaz")]
    public async Task Baska_kiraci_erisemez()
    {
        (await YazAsync([new { email = "gizli@musteri.test" }])).EnsureSuccessStatusCode();

        var digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        var oku = await digerKiraci.GetAsync(Yol());
        var yaz = await YazAsync([new { email = "sizan@musteri.test" }], digerKiraci);

        Assert.NotEqual(HttpStatusCode.OK, oku.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, yaz.StatusCode);
    }

    [Fact(DisplayName = "BA8. Bir kiracının alıcısı diğerinin listesine KARIŞMAZ")]
    public async Task Kiracilar_karismaz()
    {
        (await YazAsync([new { email = "kiracia@musteri.test" }])).EnsureSuccessStatusCode();

        var digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        var digerListe = await digerKiraci.GetFromJsonAsync<JsonElement>(
            Yol(_factory.TenantB.CompanyId));

        Assert.DoesNotContain(
            digerListe.EnumerateArray(),
            r => r.GetProperty("email").GetString() == "kiracia@musteri.test");
    }

    [Fact(DisplayName = "BA9. Platform rolleri alıcı listesine GİREMEZ")]
    public async Task Platform_rolleri_giremez()
    {
        foreach (var eposta in new[]
                 {
                     GovAiApiFactory.PlatformCatalogEmail,
                     GovAiApiFactory.PlatformReviewerEmail,
                 })
        {
            var client = await _factory.CreateAuthenticatedClientAsync(eposta);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Yol())).StatusCode);
        }
    }

    [Fact(DisplayName = "BA10. Aynı adres iki kez yazılsa TEK kayıt olur")]
    public async Task Ayni_adres_tek_kayit()
    {
        (await YazAsync([
            new { email = "ayni@musteri.test" },
            new { email = "AYNI@Musteri.Test" },
        ])).EnsureSuccessStatusCode();

        var sayi = await VeritabaniAsync(db => db.NotificationRecipients.IgnoreQueryFilters()
            .CountAsync(r => r.CompanyId == _companyId));

        Assert.Equal(1, sayi);
    }
}
