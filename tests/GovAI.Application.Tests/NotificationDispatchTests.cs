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
        IReadOnlyList<RecipientAddress>? alicilar = null,
        params Notification[] bildirimler)
    {
        var depo = new FakeNotificationRepository(bildirimler);
        var kullanici = new FakeCurrentUser { TenantId = TenantId };
        var uyelikler = new FakeUserCompanyRepository();

        uyelikler.Seed(TenantId, kullanici.UserId!.Value, CompanyId);

        var servis = new NotificationService(
            depo,
            new FakeRecipientRepository(alicilar ?? [new RecipientAddress("u@firma.test", "Ad Soyad", null)]),
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

    [Fact(DisplayName = "BG4. Alıcı tanımlı değilse RecipientMissing yazılır ve HAK HARCANMAZ")]
    public async Task Alicisi_yoksa_recipient_missing()
    {
        // Alıcı eksikliği bir gönderim hatası değil, eksik kurulumdur. Hak harcansaydı
        // sorumlusu bir hafta sonra ERP'de tanımlanan firmanın birikmiş uyarıları üç
        // denemeyi çoktan doldurmuş olur ve hiç gitmezdi.
        var bildirim = Bildirim(NotificationChannel.Email);
        var eposta = new FakeEmailSender(configured: true, basarili: true);

        var (servis, _) = Kur(eposta, alicilar: [], bildirimler: bildirim);

        var sonuc = await servis.DispatchPendingAsync();

        Assert.Equal(1, sonuc.RecipientMissing);
        Assert.Equal(0, sonuc.Failed);
        Assert.Equal(NotificationDeliveryStatus.RecipientMissing, bildirim.DeliveryStatus);
        Assert.Null(bildirim.SentAt);
        Assert.Equal(0, bildirim.DeliveryAttemptCount);
        Assert.Empty(eposta.Gonderilenler);
    }

    [Fact(DisplayName = "BG4b. Alıcısız bildirim KAYBOLMAZ; alıcı tanımlanınca gider")]
    public async Task Alici_tanimlaninca_gider()
    {
        var bildirim = Bildirim(NotificationChannel.Email);
        var eposta = new FakeEmailSender(configured: true, basarili: true);

        // Önce alıcısız tur.
        var (alicisiz, _) = Kur(eposta, alicilar: [], bildirimler: bildirim);
        await alicisiz.DispatchPendingAsync();

        Assert.Null(bildirim.SentAt);

        // Sonra alıcı tanımlanmış tur: aynı kayıt gönderilir.
        var (tanimli, _) = Kur(
            eposta,
            alicilar: [new RecipientAddress("sorumlu@firma.test", "Sorumlu", "Mali İşler")],
            bildirimler: bildirim);

        var sonuc = await tanimli.DispatchPendingAsync();

        Assert.Equal(1, sonuc.Sent);
        Assert.Equal(NotificationDeliveryStatus.Sent, bildirim.DeliveryStatus);
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

    [Fact(DisplayName = "BG8. Başarısız gönderim SONRAKİ turda yeniden denenir")]
    public async Task Basarisiz_gonderim_yeniden_denenir()
    {
        var bildirim = Bildirim(NotificationChannel.Email);

        // İlk tur: SMTP ulaşılamıyor.
        var (basarisizServis, _) = Kur(
            new FakeEmailSender(configured: true, basarili: false), bildirimler: bildirim);

        await basarisizServis.DispatchPendingAsync();

        Assert.Equal(1, bildirim.DeliveryAttemptCount);
        Assert.Equal(NotificationDeliveryStatus.Failed, bildirim.DeliveryStatus);

        // İkinci tur: sunucu geri geldi. Geçici arıza kalıcı kayba dönüşmemeli.
        var iyilesen = new FakeEmailSender(configured: true, basarili: true);
        var (iyilesenServis, _) = Kur(iyilesen, bildirimler: bildirim);

        var sonuc = await iyilesenServis.DispatchPendingAsync();

        Assert.Equal(1, sonuc.Sent);
        Assert.Equal(NotificationDeliveryStatus.Sent, bildirim.DeliveryStatus);
        Assert.Null(bildirim.DeliveryError);
        Assert.Single(iyilesen.Gonderilenler);
    }

    [Fact(DisplayName = "BG9. Gönderilmiş bildirim İKİNCİ KEZ gönderilmez")]
    public async Task Mukerrer_gonderim_engellenir()
    {
        // Zamanlayıcı her beş dakikada bir çalışır; gönderilmiş kaydı yeniden
        // göndermek aynı uyarıyı gün içinde onlarca kez postalardı.
        var bildirim = Bildirim(NotificationChannel.Email);
        var eposta = new FakeEmailSender(configured: true, basarili: true);

        var (servis, _) = Kur(eposta, bildirimler: bildirim);

        await servis.DispatchPendingAsync();
        var ikinciTur = await servis.DispatchPendingAsync();

        Assert.Single(eposta.Gonderilenler);
        Assert.Equal(0, ikinciTur.Processed);
        Assert.Equal(1, bildirim.DeliveryAttemptCount);
    }

    [Fact(DisplayName = "BG10. Üç başarısız denemeden sonra kayıt turdan DÜŞER")]
    public async Task Uc_denemeden_sonra_durur()
    {
        // Sonsuz yeniden deneme, kalıcı olarak geçersiz bir adres yüzünden her turu
        // aynı kayda harcardı.
        var bildirim = Bildirim(NotificationChannel.Email);
        var eposta = new FakeEmailSender(configured: true, basarili: false);

        var (servis, _) = Kur(eposta, bildirimler: bildirim);

        for (var i = 0; i < 4; i++)
        {
            await servis.DispatchPendingAsync();
        }

        Assert.Equal(3, bildirim.DeliveryAttemptCount);
    }

    [Fact(DisplayName = "BG11. Webhook kuyruğa bırakılır ama GÖNDERİLDİ yazılmaz")]
    public async Task Webhook_gonderildi_yazilmaz()
    {
        // Kuyruğun ucunda henüz tüketici yok. "Gönderildi" damgalansaydı hiç ulaşmayan
        // bildirim panelde gönderilmiş görünürdü.
        var bildirim = Bildirim(NotificationChannel.Webhook);
        var eposta = new FakeEmailSender(configured: true, basarili: true);

        var (servis, _) = Kur(eposta, bildirimler: bildirim);

        var sonuc = await servis.DispatchPendingAsync();

        Assert.Equal(0, sonuc.Sent);
        Assert.Equal(1, sonuc.Skipped);
        Assert.Null(bildirim.SentAt);
        Assert.Equal(NotificationDeliveryStatus.Pending, bildirim.DeliveryStatus);
    }

    [Fact(DisplayName = "BG12. Alıcılar bildirimin KENDİ kiracısı ve firmasıyla sorulur")]
    public async Task Alicilar_bildirimin_kiracisiyla_sorulur()
    {
        // Kiracı oturumdan alınsaydı, zamanlayıcının kiracısı bildirimin kiracısından
        // farklı olduğunda bir firmanın uyarısı başka kiracının sorumlusuna giderdi.
        var bildirim = Bildirim(NotificationChannel.Email);

        var depo = new FakeNotificationRepository([bildirim]);
        var sahteAlicilar = new FakeRecipientRepository(
            [new RecipientAddress("u@firma.test", "Ad", null)]);

        // Oturumun kiracısı BİLEREK farklı.
        var kullanici = new FakeCurrentUser { TenantId = Guid.NewGuid() };
        var uyelikler = new FakeUserCompanyRepository();

        var servis = new NotificationService(
            depo,
            sahteAlicilar,
            new FakeEmailSender(configured: true, basarili: true),
            new FakeUnitOfWork(),
            new CompanyAccessGuard(new FakeCompanyRepository(), uyelikler, kullanici),
            new FixedClock(AsOf),
            new FakeEventPublisher(),
            NullLogger<NotificationService>.Instance);

        await servis.DispatchPendingAsync();

        var sorgu = Assert.Single(sahteAlicilar.Sorgular);

        Assert.Equal(TenantId, sorgu.TenantId);
        Assert.Equal(CompanyId, sorgu.CompanyId);
        Assert.NotEqual(kullanici.TenantId, sorgu.TenantId);
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
                new RecipientAddress("bir@firma.test", "Bir", null),
                new RecipientAddress("iki@firma.test", "İki", null),
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

internal sealed class FakeRecipientRepository(IReadOnlyList<RecipientAddress> alicilar)
    : INotificationRecipientRepository
{
    /// <summary>Hangi kiracı ve firma için sorulduğu; sınır testleri bunu okur.</summary>
    public List<(Guid TenantId, Guid? CompanyId)> Sorgular { get; } = [];

    public Task<IReadOnlyList<RecipientAddress>> ListForNotificationAsync(
        Guid tenantId,
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        Sorgular.Add((tenantId, companyId));

        return Task.FromResult(alicilar);
    }

    public Task<IReadOnlyList<NotificationRecipient>> ListForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NotificationRecipient>>([]);

    public Task AddAsync(NotificationRecipient recipient, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
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
