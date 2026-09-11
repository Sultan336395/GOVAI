using GovAI.Domain.Common;

namespace GovAI.Domain.Notifications;

/// <summary>
/// Bildirim ve Hatırlatma Modülü (Modül 10) kaydı.
/// <see cref="DeduplicationKey"/> aynı olayın tekrar tekrar bildirilmesini engeller.
/// </summary>
public class Notification : AggregateRoot, ITenantScoped
{
    private Notification()
    {
    }

    public Notification(
        Guid tenantId,
        Guid? companyId,
        NotificationKind kind,
        string title,
        string body,
        DateTimeOffset createdAt,
        string deduplicationKey,
        Guid? opportunityId = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), "Bildirim başlığı zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(deduplicationKey), "Tekilleştirme anahtarı zorunludur.");

        TenantId = tenantId;
        CompanyId = companyId;
        OpportunityId = opportunityId;
        Kind = kind;
        Title = title.Trim();
        Body = body;
        CreatedAt = createdAt;
        DeduplicationKey = deduplicationKey;
    }

    public Guid TenantId { get; set; }

    public Guid? CompanyId { get; private set; }

    public Guid? OpportunityId { get; private set; }

    public NotificationKind Kind { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    /// <summary>Ör. <c>deadline:{opportunityId}:{companyId}:7d</c> — aynı hatırlatma iki kez gönderilmez.</summary>
    public string DeduplicationKey { get; private set; } = string.Empty;

    public NotificationChannel Channel { get; private set; } = NotificationChannel.InApp;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public string? DeliveryError { get; private set; }

    public int DeliveryAttemptCount { get; private set; }

    /// <summary>
    /// Teslimin son durumu.
    ///
    /// <para>
    /// <see cref="SentAt"/> tek başına yetmiyordu: "henüz denenmedi", "denendi ve
    /// başarısız oldu" ve "alıcısı tanımlı değil" üçü de boş <c>SentAt</c> ile aynı
    /// görünüyordu. Ayrımı kaydetmeden "neden e-posta gelmedi" sorusuna cevap
    /// verilemez.
    /// </para>
    /// </summary>
    public NotificationDeliveryStatus DeliveryStatus { get; private set; }
        = NotificationDeliveryStatus.Pending;

    public bool IsRead => ReadAt is not null;

    public void SetChannel(NotificationChannel channel) => Channel = channel;

    public void MarkSent(DateTimeOffset sentAt)
    {
        SentAt = sentAt;
        DeliveryError = null;
        DeliveryStatus = NotificationDeliveryStatus.Sent;
        DeliveryAttemptCount++;
    }

    public void MarkFailed(string error)
    {
        DeliveryError = error;
        DeliveryStatus = NotificationDeliveryStatus.Failed;
        DeliveryAttemptCount++;
    }

    /// <summary>
    /// Şirkette tanımlı bildirim alıcısı yok.
    ///
    /// <para>
    /// Deneme sayacı <b>artmaz</b>. Artsaydı, sorumlusu bir hafta sonra ERP'de
    /// tanımlanan bir firmanın birikmiş uyarıları üç denemeyi çoktan doldurmuş olur ve
    /// hiç gitmezdi. Bildirim kaybolmaz: ERP modülünde görünmeye devam eder ve alıcı
    /// tanımlandığında gönderilir.
    /// </para>
    /// </summary>
    public void MarkRecipientMissing()
    {
        DeliveryStatus = NotificationDeliveryStatus.RecipientMissing;
        DeliveryError = "Şirkette tanımlı bildirim alıcısı yok.";
    }

    public void MarkRead(DateTimeOffset readAt) => ReadAt ??= readAt;
}

/// <summary>
/// Bildirimin dış kanala teslim durumu.
///
/// <para>
/// Panelde/ERP modülünde görünürlüğü <b>etkilemez</b>: hiçbir durum bildirimi
/// listeden düşürmez. Yalnızca "dışarı çıktı mı, çıkmadıysa neden" sorusunu cevaplar.
/// </para>
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>Henüz denenmedi ya da kanal yapılandırılmamış olduğu için bekliyor.</summary>
    Pending = 0,

    Sent = 1,

    /// <summary>Denendi, gönderilemedi. Sebep <c>DeliveryError</c> alanındadır.</summary>
    Failed = 2,

    /// <summary>Şirkette tanımlı alıcı yok. Deneme hakkı harcanmaz.</summary>
    RecipientMissing = 3
}
