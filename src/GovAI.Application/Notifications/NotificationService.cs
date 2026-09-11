using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    NotificationKind Kind,
    string Title,
    string Body,
    Guid? CompanyId,
    Guid? OpportunityId,
    NotificationChannel Channel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    bool IsRead);

/// <summary>Bir gönderim turunun sonucu.</summary>
/// <param name="Processed">İşlenen kayıt sayısı.</param>
/// <param name="Sent">Gerçekten gönderilen.</param>
/// <param name="Failed">Denendi ve başarısız oldu; sebebi kayda yazıldı.</param>
/// <param name="Skipped">Kanalı yapılandırılmadığı için hiç denenmedi; deneme hakkı harcanmadı.</param>
/// <summary>
/// Bir gönderim turunun sonucu.
///
/// <para>
/// Dört sonuç ayrı sayılır. Tek bir "işlendi" sayısı, hiç ulaşmayan bildirimleri
/// başarı gibi gösteriyordu; <see cref="RecipientMissing"/> ise bir arıza değil
/// <b>eksik kurulum</b> işaretidir ve başarısızlıkla aynı kefeye konursa gerçek SMTP
/// hataları görünmez olur.
/// </para>
/// </summary>
public sealed record NotificationDispatchResult(
    int Processed,
    int Sent,
    int Failed,
    /// <summary>Kanalı yapılandırılmadığı için bekleyenler. Deneme hakkı harcanmadı.</summary>
    int Skipped,
    /// <summary>Şirkette tanımlı alıcı olmadığı için gönderilemeyenler.</summary>
    int RecipientMissing);

/// <summary>
/// Bildirim ve Hatırlatma Modülü (Modül 10) use-case servisi.
/// Bildirimlerin üretimi değerlendirme akışında yapılır; burada okuma ve gönderim yönetilir.
/// </summary>
public sealed class NotificationService(
    INotificationRepository notifications,
    INotificationRecipientRepository recipients,
    IEmailSender email,
    IUnitOfWork unitOfWork,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    IEventPublisher events,
    ILogger<NotificationService> logger)
{
    public async Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery query, CancellationToken cancellationToken = default)
    {
        // Kiracı her zaman oturumdan yeniden yazılır; çağıranın gönderdiği değere güvenilmez.
        var scoped = query with { TenantId = access.RequireTenant() };

        // Firma verildiyse ayrıca o firmanın bu kiracıya ait olduğu ve kullanıcının
        // erişebildiği doğrulanır. Firma verilmediyse kiracı sınırı tek başına yeterlidir.
        if (scoped.CompanyId is not null)
        {
            await access.EnsureAccessAsync(scoped.CompanyId.Value, CompanyPermission.Read, cancellationToken);
        }

        var page = await notifications.ListAsync(scoped, cancellationToken);

        return new PagedResult<NotificationDto>(
            page.Items.Select(ToDto).ToList(),
            page.TotalCount,
            page.Page,
            page.PageSize);
    }

    public async Task<NotificationDto> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();

        var notification = await notifications.GetAsync(notificationId, cancellationToken)
                           ?? throw new NotFoundException("Bildirim", notificationId);

        // Başka kiracının bildirimi "yok" sayılır; varlığı doğrulanmaz.
        if (notification.TenantId != tenantId)
        {
            throw new NotFoundException("Bildirim", notificationId);
        }

        if (notification.CompanyId is not null)
        {
            await access.EnsureAccessAsync(notification.CompanyId.Value, CompanyPermission.Read, cancellationToken);
        }

        notification.MarkRead(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(notification);
    }

    /// <summary>
    /// Gönderilmemiş bildirimleri kanallarına aktarır. Zamanlayıcı çağırır.
    ///
    /// <para>
    /// "Gönderildi" damgası <b>gerçekten gönderilince</b> basılır. Eskiden e-posta
    /// kuyruğa bırakılır bırakılmaz gönderilmiş sayılıyordu; kuyruğun ucunda kimse
    /// olmadığı için hiç ulaşmayan hatırlatmalar panelde "gönderildi" görünüyordu.
    /// Artık başarısız gönderim sebebiyle birlikte kayda yazılır ve tekrar denenir
    /// (<c>ListUnsentAsync</c> deneme sayısını sınırlar).
    /// </para>
    /// </summary>
    public async Task<NotificationDispatchResult> DispatchPendingAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var pending = await notifications.ListUnsentAsync(batchSize, cancellationToken);

        var gonderilen = 0;
        var basarisiz = 0;
        var atlanan = 0;
        var alicisiz = 0;

        foreach (var notification in pending)
        {
            switch (notification.Channel)
            {
                case NotificationChannel.InApp:
                    notification.MarkSent(clock.UtcNow);
                    gonderilen++;
                    break;

                case NotificationChannel.Email when !email.IsConfigured:
                    // Yapılandırma eksik: deneme hakkı HARCANMAZ. Harcansaydı SMTP
                    // tanımlandığında birikmiş hatırlatmalar üç denemeyi doldurmuş
                    // olur ve hiç gitmezdi.
                    atlanan++;
                    break;

                case NotificationChannel.Email:
                    switch (await EpostaGonderAsync(notification, cancellationToken))
                    {
                        case EmailDispatchOutcome.Sent:
                            gonderilen++;
                            break;

                        case EmailDispatchOutcome.RecipientMissing:
                            alicisiz++;
                            break;

                        default:
                            basarisiz++;
                            break;
                    }

                    break;

                default:
                    // Webhook ve sonraki kanallar hâlâ kuyruk üzerinden yürür ve
                    // kuyruğun ucunda HENÜZ TÜKETİCİ YOK. Bu yüzden "gönderildi"
                    // damgalanmaz: damgalansaydı hiç ulaşmayan bir bildirim panelde
                    // gönderilmiş görünürdü — §2.2.7'nin düzelttiği hatanın ta kendisi.
                    // Kayıt bekler; tüketici yazıldığında kaldığı yerden alınır.
                    await events.PublishAsync(
                        QueueNames.NotificationDispatchRequested,
                        new
                        {
                            NotificationId = notification.Id,
                            notification.Channel,
                            notification.Title,
                            notification.Body,
                            notification.TenantId
                        },
                        cancellationToken);

                    atlanan++;
                    break;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (pending.Count > 0)
        {
            logger.LogInformation(
                "Bildirim gönderimi: {Sent} gönderildi, {Failed} başarısız, "
                + "{RecipientMissing} alıcısız, {Skipped} beklemede.",
                gonderilen, basarisiz, alicisiz, atlanan);
        }

        return new NotificationDispatchResult(
            pending.Count, gonderilen, basarisiz, atlanan, alicisiz);
    }

    /// <summary>Tek bir e-posta gönderiminin sonucu.</summary>
    private enum EmailDispatchOutcome
    {
        Sent,

        /// <summary>Denendi, gönderilemedi. Deneme hakkı harcandı.</summary>
        Failed,

        /// <summary>Şirkette tanımlı alıcı yok. Deneme hakkı harcanmadı.</summary>
        RecipientMissing
    }

    /// <summary>Tek bir bildirimi e-posta ile gönderir; sonucu kayda yazar.</summary>
    private async Task<EmailDispatchOutcome> EpostaGonderAsync(
        Domain.Notifications.Notification notification,
        CancellationToken cancellationToken)
    {
        var alicilar = await recipients.ListForNotificationAsync(
            notification.TenantId, notification.CompanyId, cancellationToken);

        if (alicilar.Count == 0)
        {
            // Alıcı tanımlı değil: bu bir gönderim HATASI DEĞİLDİR, eksik bir kurulumdur.
            // Ayrı durum olarak kaydedilir ve deneme hakkı HARCANMAZ — sorumlusu bir
            // hafta sonra ERP'de tanımlanan firmanın birikmiş uyarıları yoksa üç
            // denemeyi çoktan doldurmuş olur ve hiç gitmezdi.
            //
            // Bildirim kaybolmaz: ERP modülünde görünmeye devam eder.
            notification.MarkRecipientMissing();
            return EmailDispatchOutcome.RecipientMissing;
        }

        var sonuc = await email.SendAsync(
            new EmailMessage
            {
                To = alicilar.Select(a => a.Email).ToList(),
                Subject = notification.Title,
                Body = notification.Body,
            },
            cancellationToken);

        if (sonuc.Sent)
        {
            notification.MarkSent(clock.UtcNow);
            return EmailDispatchOutcome.Sent;
        }

        notification.MarkFailed(sonuc.Error ?? "E-posta gönderilemedi.");
        return EmailDispatchOutcome.Failed;
    }

    private static NotificationDto ToDto(Domain.Notifications.Notification notification) => new(
        notification.Id,
        notification.Kind,
        notification.Title,
        notification.Body,
        notification.CompanyId,
        notification.OpportunityId,
        notification.Channel,
        notification.CreatedAt,
        notification.SentAt,
        notification.IsRead);
}
