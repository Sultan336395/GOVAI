namespace GovAI.Application.Abstractions.Services;

/// <summary>Gönderilecek e-posta. Alıcılar çözülmüş hâlde gelir.</summary>
public sealed record EmailMessage
{
    public required IReadOnlyList<string> To { get; init; }

    public required string Subject { get; init; }

    /// <summary>Düz metin gövde. HTML gövde ayrıca verilebilir.</summary>
    public required string Body { get; init; }

    public string? HtmlBody { get; init; }
}

/// <summary>
/// Gönderim sonucu.
///
/// <para>
/// Beklenen başarısızlıklar (sunucuya ulaşılamadı, kimlik reddedildi, adres geçersiz)
/// <b>istisna değil sonuçtur</b>: bildirim hattı tek bir hatalı adres yüzünden durmaz
/// ve hata kaydın üzerine yazılıp tekrar denenebilir.
/// </para>
///
/// <para>
/// <see cref="Error"/> kullanıcıya değil <b>kayda</b> yazılır ve asla kimlik bilgisi
/// taşımaz; SMTP sunucusunun döndürdüğü gövde kullanıcı adı içerebilir.
/// </para>
/// </summary>
public sealed record EmailSendResult(bool Sent, string? Error)
{
    public static EmailSendResult Success() => new(true, null);

    public static EmailSendResult Failure(string error) => new(false, error);
}

/// <summary>
/// E-posta gönderim adaptörü.
///
/// <para>
/// <see cref="IsConfigured"/> yanlışsa sistem <b>çalışmaya devam eder</b>: bildirim
/// panelde görünür, yalnızca e-posta gitmez. Yapılandırma eksikken gönderilmiş gibi
/// işaretlemek, kullanıcının hiç almadığı bir hatırlatmayı "gönderildi" sanmasına yol
/// açardı.
/// </para>
/// </summary>
public interface IEmailSender
{
    /// <summary>SMTP ayarları eksiksiz mi? Eksikse gönderim denenmez.</summary>
    bool IsConfigured { get; }

    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
