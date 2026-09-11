using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Application.Notifications;
using GovAI.Domain.Common;
using GovAI.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovAI.Application.Tests;

/// <summary>
/// Bildirim gönderimi.
///
/// <para>
/// Korunan tek kural şudur: <b>"gönderildi" damgası ancak gerçekten gönderilince
/// basılır.</b> Eskiden e-posta kuyruğa bırakılır bırakılmaz gönderilmiş sayılıyordu;
/// kuyruğun ucunda kimse olmadığı için hiç ulaşmayan hatırlatmalar panelde
/// "gönderildi" görünüyordu. Bu, kullanıcının almadığı bir uyarıyı almış sanması
/// demekti.
/// </para>
/// </summary>
public class NotificationDispatchTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 14, 8, 0, 0, TimeSpan.FromHours(3));

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static Notification Bildirim(NotificationChannel kanal)
    {
        var bildirim = new Notification(
            TenantId, CompanyId, NotificationKind.DeadlineApproaching,
            "Son başvuruya 7 gün kaldı", "KOSGEB Ar-Ge çağrısı 21.09.2026'da kapanıyor.",
            AsOf.AddDays(-1), $"deadline:{Guid.NewGuid()}");

        bildirim.SetChannel(kanal);

        return bildirim;
    }

    private static (NotificationService Servis, FakeNotificationRepository Depo) Kur(
        IEmailSender eposta,
        IReadOnlyList<NotificationRecipient>? alicilar = null,
        params Notification[] bildirimler)
    {
        var depo = new FakeNotificationRepository(bildirimler);
        var kullanici = new FakeCurrentUser { TenantId = TenantId };
        var uyelikler = new FakeUserCompanyRepository();

        uyelikler.Seed(TenantId, kullanici.UserId!.Value, CompanyId);

        var servis = new NotificationService(
            depo,
            new FakeRecipientRepository(alicilar ?? [new NotificationRecipient(Guid.NewGuid(), "u@firma.test", "Ad Soyad")]),
            eposta,
            new FakeUnitOfWork(),
            new CompanyAccessGuard(new FakeCompanyRepository(), uyelikler, kullanici),
            new FixedClock(AsOf),
            new FakeEventPublisher(),
            NullLogger<NotificationService>.Instance);

        return (servis, depo);
    }

    [Fact(DisplayName = "BG1. Başarılı gönderim GÖNDERİLDİ işaretler")]
    public async Task Basarili_gonderim_isaretlenir()
    {
        var bildirim = Bildirim(NotificationChannel.Email);
        var eposta = new FakeEmailSender(configured: true, basarili: true);

        var (servis, _) = Kur(eposta, bildirimler: bildirim);

        var sonuc = await servis.DispatchPendingAsync();

        Assert.Equal(1, sonuc.Sent);
        Assert.Equal(AsOf, bildirim.SentAt);
        Assert.Null(bildirim.DeliveryError);
        Assert.Single(eposta.Gonderilenler);
    }

    [Fact(DisplayName = "BG2. BAŞARISIZ gönderim 'gönderildi' YAZMAZ, sebebi kayda geçer")]
    public async Task Basarisiz_gonderim_isaretlenmez()
    {
        var bildirim = Bildirim(NotificationChannel.Email);
        var eposta = new FakeEmailSender(configured: true, basarili: false);

        var (servis, _) = Kur(eposta, bildirimler: bildirim);

        var sonuc = await servis.DispatchPendingAsync();

        Assert.Equal(0, sonuc.Sent);
        Assert.Equal(1, sonuc.Failed);
        Assert.Null(bildirim.SentAt);
        Assert.False(string.IsNullOrWhiteSpace(bildirim.DeliveryError));
        Assert.Equal(1, bildirim.DeliveryAttemptCount);
    }

    [Fact(DisplayName = "BG3. SMTP yapılandırılmamışsa gönderim DENENMEZ ve deneme hakkı harcanmaz")]
    public async Task Yapilandirilmamissa_hak_harcanmaz()
    {
        // Hak harcansaydı SMTP tanımlandığında birikmiş hatırlatmalar üç denemeyi
        // doldurmuş olur ve hiç gitmezdi.
        var bildirim = Bildirim(NotificationChannel.Email);
        var eposta = new FakeEmailSender(configured: false, basarili: false);

        var (servis, _) = Kur(eposta, bildirimler: bildirim);

        var sonuc = await servis.DispatchPendingAsync();

        Assert.Equal(1, sonuc.Skipped);
        Assert.Equal(0, sonuc.Sent);
        Assert.Null(bildirim.SentAt);
        Assert.Equal(0, bildirim.DeliveryAttemptCount);
        Assert.Empty(eposta.Gonderilenler);
    }

    [Fact(DisplayName = "BG4. Alıcısı olmayan bildirim gönderilmiş SAYILMAZ")]
    public async Task Alicisi_yoksa_gonderilmis_sayilmaz()
    {
        var bildirim = Bildirim(NotificationChannel.Email);
        var eposta = new FakeEmailSender(configured: true, basarili: true);

        var (servis, _) = Kur(eposta, alicilar: [], bildirimler: bildirim);

        var sonuc = await servis.DispatchPendingAsync();

        Assert.Equal(1, sonuc.Failed);
        Assert.Null(bildirim.SentAt);
        Assert.Contains("kullanıcı", bildirim.DeliveryError);
        Assert.Empty(eposta.Gonderilenler);
    }

    [Fact(DisplayName = "BG5. Panel bildirimi e-postaya BAĞLI DEĞİLDİR")]
    public async Task Panel_bildirimi_epostadan_bagimsiz()
    {
        // E-posta yapılandırılmamışken de panel bildirimi işlenmeye devam etmeli.
        var panel = Bildirim(NotificationChannel.InApp);
        var eposta = new FakeEmailSender(configured: false, basarili: false);

        var (servis, _) = Kur(eposta, bildirimler: panel);

        var sonuc = await servis.DispatchPendingAsync();

        Assert.Equal(1, sonuc.Sent);
        Assert.Equal(AsOf, panel.SentAt);
    }

    [Fact(DisplayName = "BG6. Bir bildirimin başarısızlığı diğerlerini DURDURMAZ")]
    public async Task Bir_hata_hatti_durdurmaz()
    {
        var basarisiz = Bildirim(NotificationChannel.Email);
        var panel = Bildirim(NotificationChannel.InApp);

        var eposta = new FakeEmailSender(configured: true, basarili: false);

        var (servis, _) = Kur(eposta, null, basarisiz, panel);

        var sonuc = await servis.DispatchPendingAsync();

        Assert.Equal(2, sonuc.Processed);
        Assert.Equal(1, sonuc.Sent);
        Assert.Equal(1, sonuc.Failed);
        Assert.NotNull(panel.SentAt);
    }

    [Fact(DisplayName = "BG7. Alıcı adresleri gönderime taşınır")]
    public async Task Adresler_tasinir()
    {
        var bildirim = Bildirim(NotificationChannel.Email);
        var eposta = new FakeEmailSender(configured: true, basarili: true);

        var (servis, _) = Kur(
            eposta,
            alicilar:
            [
                new NotificationRecipient(Guid.NewGuid(), "bir@firma.test", "Bir"),
                new NotificationRecipient(Guid.NewGuid(), "iki@firma.test", "İki"),
            ],
            bildirimler: bildirim);

        await servis.DispatchPendingAsync();

        var mesaj = Assert.Single(eposta.Gonderilenler);

        Assert.Equal(["bir@firma.test", "iki@firma.test"], mesaj.To);
        Assert.Equal(bildirim.Title, mesaj.Subject);
        Assert.Equal(bildirim.Body, mesaj.Body);
    }
}

// ── Sahteler ────────────────────────────────────────────────────────────────

internal sealed class FakeEmailSender(bool configured, bool basarili) : IEmailSender
{
    public List<EmailMessage> Gonderilenler { get; } = [];

    public bool IsConfigured => configured;

    public Task<EmailSendResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!basarili)
        {
            return Task.FromResult(EmailSendResult.Failure("SMTP sunucusuna ulaşılamadı."));
        }

        Gonderilenler.Add(message);

        return Task.FromResult(EmailSendResult.Success());
    }
}

internal sealed class FakeRecipientRepository(IReadOnlyList<NotificationRecipient> alicilar)
    : INotificationRecipientRepository
{
    public Task<IReadOnlyList<NotificationRecipient>> ListForNotificationAsync(
        Guid tenantId,
        Guid? companyId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(alicilar);
}

internal sealed class FakeNotificationRepository(IReadOnlyList<Notification> bildirimler)
    : INotificationRepository
{
    private readonly List<Notification> _bildirimler = [.. bildirimler];

    public Task<Notification?> GetAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bildirimler.FirstOrDefault(n => n.Id == notificationId));

    public Task<PagedResult<Notification>> ListAsync(
        NotificationQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<Notification>(_bildirimler, _bildirimler.Count, 1, 50));

    public Task<IReadOnlyList<Notification>> ListUnsentAsync(
        int take,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Notification>>(
            _bildirimler.Where(n => n.SentAt is null && n.DeliveryAttemptCount < 3).Take(take).ToList());

    public Task<bool> ExistsAsync(string deduplicationKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bildirimler.Any(n => n.DeduplicationKey == deduplicationKey));

    public Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        _bildirimler.Add(notification);
        return Task.CompletedTask;
    }
}
