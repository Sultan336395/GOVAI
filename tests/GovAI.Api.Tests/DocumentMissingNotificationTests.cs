using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Domain.Common;
using GovAI.Domain.Notifications;
using GovAI.Domain.Opportunities;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Eksik belge bildirimi ve kanal seçimi — gerçek skorlama hattı üzerinden.
///
/// <para>
/// <see cref="NotificationKind.DocumentMissing"/> türü enum'da uzun süre <b>vardı ama
/// hiç üretilmiyordu</b>; e-posta adaptörü kurulduğunda da bu yüzden hiç
/// ateşlenemiyordu. Bu testler türün gerçekten üretildiğini ve doğru kanala
/// düştüğünü, birim testiyle değil <b>çalışan skorlama hattıyla</b> doğrular.
/// </para>
/// </summary>
public sealed class DocumentMissingNotificationTests(GovAiApiFactory factory)
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
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> VeritabaniAsync<T>(Func<GovAiDbContext, Task<T>> islem)
    {
        using var kapsam = _factory.Services.CreateScope();

        return await islem(kapsam.ServiceProvider.GetRequiredService<GovAiDbContext>());
    }

    /// <summary>Zorunlu belge isteyen, açık ve firmaya uygun bir çağrı açar.</summary>
    private async Task<Guid> BelgeIsteyenCagriAsync(int kalanGun)
    {
        return await VeritabaniAsync(async db =>
        {
            var cagri = new Opportunity(
                _factory.TenantA.SourceId,
                SourceType.KosgebOrSimilar,
                SupportCategory.Grant,
                $"Belge İsteyen Çağrı {Guid.CreateVersion7():N}",
                "KOSGEB",
                DateTimeOffset.UtcNow.AddDays(-10));

            cagri.SetSchedule(
                DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(kalanGun));

            // Firmada olmayan bir belge: değerlendirme bunu eksik zorunlu belge sayar.
            cagri.ReplaceDocumentChecklist([
                new DocumentRequirement("ISO14001", "ISO 14001 Çevre Yönetim Sistemi", isMandatory: true),
            ]);

            db.Opportunities.Add(cagri);
            await db.SaveChangesAsync();

            return cagri.Id;
        });
    }

    private async Task SkorlaAsync()
    {
        var cevap = await _tenantAdmin.PostAsync(
            $"/api/eligibility/companies/{_companyId}/rescore", null);

        cevap.EnsureSuccessStatusCode();
    }

    private async Task<IReadOnlyList<Notification>> BildirimlerAsync(Guid opportunityId) =>
        await VeritabaniAsync(db => db.Notifications.IgnoreQueryFilters()
            .Where(n => n.OpportunityId == opportunityId)
            .ToListAsync());

    [Fact(DisplayName = "EB1. Eksik zorunlu belge bildirimi ÜRETİLİR ve e-posta kanalına düşer")]
    public async Task Eksik_belge_bildirimi_uretilir()
    {
        var cagriId = await BelgeIsteyenCagriAsync(kalanGun: 90);

        await SkorlaAsync();

        var bildirim = Assert.Single(
            await BildirimlerAsync(cagriId),
            n => n.Kind == NotificationKind.DocumentMissing);

        Assert.Equal(NotificationChannel.Email, bildirim.Channel);
        Assert.Contains("belge", bildirim.Title, StringComparison.OrdinalIgnoreCase);

        // Gönderilmeden "gönderildi" yazılmaz.
        Assert.Null(bildirim.SentAt);
    }

    [Fact(DisplayName = "EB2. Eksik belge uyarısı SON TARİH penceresini beklemez")]
    public async Task Eksik_belge_son_tarihi_beklemez()
    {
        // Belge temini haftalar alır; 15 güne inince haber vermek çoğu belge için geç.
        var cagriId = await BelgeIsteyenCagriAsync(kalanGun: 120);

        await SkorlaAsync();

        var bildirimler = await BildirimlerAsync(cagriId);

        Assert.Contains(bildirimler, n => n.Kind == NotificationKind.DocumentMissing);

        // Son tarih uyarısı bu kadar uzak bir çağrı için henüz çıkmamalı.
        Assert.DoesNotContain(bildirimler, n => n.Kind == NotificationKind.DeadlineApproaching);
    }

    [Fact(DisplayName = "EB3. Süresi GEÇMİŞ çağrı için belge uyarısı çıkmaz")]
    public async Task Suresi_gecmis_cagri_uyarmaz()
    {
        // Başvurulamayacak bir çağrı için belge toplatmak, kullanıcıyı boşa çalıştırır.
        var cagriId = await BelgeIsteyenCagriAsync(kalanGun: -5);

        await SkorlaAsync();

        Assert.DoesNotContain(
            await BildirimlerAsync(cagriId), n => n.Kind == NotificationKind.DocumentMissing);
    }

    [Fact(DisplayName = "EB4. Yeniden skorlama İKİNCİ bildirim açmaz")]
    public async Task Yeniden_skorlama_tekrar_uretmez()
    {
        // Gece turu her gün çalışır; her turda yeni uyarı üretmek kutuyu doldururdu.
        var cagriId = await BelgeIsteyenCagriAsync(kalanGun: 90);

        await SkorlaAsync();
        await SkorlaAsync();

        var belgeBildirimleri = (await BildirimlerAsync(cagriId))
            .Count(n => n.Kind == NotificationKind.DocumentMissing);

        Assert.Equal(1, belgeBildirimleri);
    }

    [Fact(DisplayName = "EB5. Yeni eşleşme bildirimi e-postaya GİTMEZ, panelde kalır")]
    public async Task Yeni_eslesme_panelde_kalir()
    {
        var cagriId = await BelgeIsteyenCagriAsync(kalanGun: 90);

        await SkorlaAsync();

        foreach (var bildirim in (await BildirimlerAsync(cagriId))
                 .Where(n => n.Kind is NotificationKind.NewMatch or NotificationKind.ScoreChanged))
        {
            Assert.Equal(NotificationChannel.InApp, bildirim.Channel);
        }
    }

    [Fact(DisplayName = "EB6. E-posta kanalındaki bildirim PANELDE de görünür")]
    public async Task Eposta_bildirimi_panelde_de_gorunur()
    {
        // Kanal, görünürlüğü değil iletimi belirler. Panelden düşseydi, SMTP
        // tanımlı olmayan bir kurulumda uyarı hiçbir yerde görünmezdi.
        var cagriId = await BelgeIsteyenCagriAsync(kalanGun: 90);

        await SkorlaAsync();

        var liste = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/notifications?companyId={_companyId}&pageSize=100");

        var kayitlar = liste.GetProperty("items").EnumerateArray()
            .Where(n => n.TryGetProperty("opportunityId", out var o)
                        && o.GetGuid() == cagriId)
            .ToList();

        Assert.Contains(kayitlar, n => n.GetProperty("kind").GetString() == "DocumentMissing");
    }
}
