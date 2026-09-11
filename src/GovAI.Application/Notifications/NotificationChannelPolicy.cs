using GovAI.Domain.Common;

namespace GovAI.Application.Notifications;

/// <summary>
/// Bir bildirimin hangi kanaldan iletileceği.
///
/// <para>
/// Kanal, bildirimin panelde görünüp görünmeyeceğini <b>belirlemez</b>: panel listesi
/// kanala bakmaz, her bildirim orada durur. Buradaki seçim yalnızca "ayrıca e-posta da
/// gitsin mi" sorusunu cevaplar.
/// </para>
///
/// <para>
/// E-posta listesi bilerek <b>kısadır</b>. Her tür e-postaya çevrilseydi kutu dolar,
/// insanlar gelen kutusunda kural yazıp klasöre atar ve zamana bağlı asıl iki uyarı da
/// onlarla birlikte görünmez olurdu. Bildirim sisteminin başarısızlık biçimi
/// "az bildirim" değil, "görmezden gelinen bildirim"dir.
/// </para>
///
/// <para>
/// Kural tek yerde durur ve saftır; hangi türün nereye gittiği servis koduna
/// bakmadan okunabilir ve sınanabilir.
/// </para>
/// </summary>
public static class NotificationChannelPolicy
{
    /// <summary>
    /// Panelin yanı sıra e-postayla da iletilen türler.
    ///
    /// <para>
    /// İkisi de <b>zamana bağlıdır</b> ve kaçırılırsa fırsat kapanır: son başvuru
    /// tarihi geçer, belge yetişmez. Panelde durup fark edilmemeleri, yapılabilecek
    /// bir başvurunun kaçması demektir.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<NotificationKind> EmailKinds =
        new HashSet<NotificationKind>
        {
            NotificationKind.DeadlineApproaching,
            NotificationKind.DocumentMissing,
        };

    /// <summary>
    /// Türün kanalı.
    ///
    /// <para>
    /// Varsayılan <see cref="NotificationChannel.InApp"/>'tir: yeni bir bildirim türü
    /// eklendiğinde kimseye sormadan e-posta göndermeye başlamaz. Listeye eklemek
    /// bilinçli bir karar olmalıdır.
    /// </para>
    /// </summary>
    public static NotificationChannel For(NotificationKind kind) =>
        EmailKinds.Contains(kind) ? NotificationChannel.Email : NotificationChannel.InApp;
}
