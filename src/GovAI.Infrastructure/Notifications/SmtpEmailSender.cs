using GovAI.Application.Abstractions.Services;
using GovAI.Infrastructure.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GovAI.Infrastructure.Notifications;

/// <summary>
/// Gerçek SMTP gönderimi.
///
/// <para>
/// Üç karar bilinçlidir ve gevşetilmemelidir:
/// </para>
///
/// <list type="bullet">
///   <item>
///     <b>Şifresiz bağlantı yok.</b> Bağlantı ya STARTTLS ile yükseltilir ya da baştan
///     TLS'tir. Bildirim gövdesi firmanın hangi çağrıya hangi eksikle bakıldığını
///     taşır; kimlik bilgisiyle birlikte açık ağdan geçmemelidir.
///   </item>
///   <item>
///     <b>Hata gövdesi kullanıcıya yansıtılmaz.</b> SMTP sunucusunun döndürdüğü metin
///     kullanıcı adını ve iç sunucu adlarını içerebilir; kayda yalnızca sınıflandırılmış
///     bir sebep yazılır.
///   </item>
///   <item>
///     <b>Parola loglanmaz.</b> Hiçbir log satırında kimlik bilgisi geçmez; bağlantı
///     hatasında yalnızca sunucu adı ve portu yazılır.
///   </item>
/// </list>
///
/// <para>
/// Yapılandırma eksikse <see cref="IsConfigured"/> yanlıştır ve gönderim hiç denenmez;
/// sistem bildirimleri panelde göstermeye devam eder.
/// </para>
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<EmailSendResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return EmailSendResult.Failure("E-posta gönderimi yapılandırılmadı.");
        }

        var alicilar = message.To
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(_options.MaxRecipients)
            .ToList();

        if (alicilar.Count == 0)
        {
            return EmailSendResult.Failure("Gönderilecek adres yok.");
        }

        var eposta = new MimeMessage();

        eposta.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        eposta.Subject = message.Subject;

        foreach (var adres in alicilar)
        {
            // Geçersiz bir adres tüm gönderimi düşürmemeli; o adres atlanır.
            if (MailboxAddress.TryParse(adres, out var kutu))
            {
                eposta.Bcc.Add(kutu);
            }
        }

        if (eposta.Bcc.Count == 0)
        {
            return EmailSendResult.Failure("Adreslerin hiçbiri geçerli değil.");
        }

        // Alıcılar Bcc'dedir: aynı firmadaki kişilerin adresleri birbirine açılmaz.
        // To boş bir zarf bazı sunucularda reddedilir, bu yüzden gönderen yazılır.
        eposta.To.Add(new MailboxAddress(_options.FromName, _options.FromAddress));

        eposta.Body = new BodyBuilder
        {
            TextBody = message.Body,
            HtmlBody = message.HtmlBody,
        }.ToMessageBody();

        using var istemci = new SmtpClient
        {
            Timeout = _options.TimeoutSeconds * 1000,
        };

        try
        {
            await istemci.ConnectAsync(
                _options.Host,
                _options.Port,
                _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.UserName))
            {
                await istemci.AuthenticateAsync(_options.UserName, _options.Password, cancellationToken);
            }

            await istemci.SendAsync(eposta, cancellationToken);
            await istemci.DisconnectAsync(quit: true, cancellationToken);

            logger.LogInformation(
                "E-posta gönderildi. Alıcı sayısı={Count} Sunucu={Host}:{Port}",
                eposta.Bcc.Count, _options.Host, _options.Port);

            return EmailSendResult.Success();
        }
        catch (AuthenticationException)
        {
            // Sunucunun mesajı kullanıcı adını taşıyabilir; sınıflandırma yeter.
            logger.LogError(
                "SMTP kimlik doğrulaması reddedildi. Sunucu={Host}:{Port}",
                _options.Host, _options.Port);

            return EmailSendResult.Failure("SMTP kimlik doğrulaması reddedildi.");
        }
        catch (SmtpCommandException ex)
        {
            logger.LogError(
                "SMTP komutu reddedildi. Sunucu={Host}:{Port} Durum={Status}",
                _options.Host, _options.Port, ex.StatusCode);

            return EmailSendResult.Failure($"SMTP sunucusu isteği reddetti ({ex.StatusCode}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "E-posta gönderilemedi. Sunucu={Host}:{Port}", _options.Host, _options.Port);

            return EmailSendResult.Failure("SMTP sunucusuna ulaşılamadı.");
        }
    }
}

/// <summary>
/// SMTP yapılandırılmadığında kullanılan adaptör.
///
/// <para>
/// Gönderilmiş <b>gibi davranmaz</b>: her çağrıda başarısızlık döner ve
/// <see cref="IsConfigured"/> yanlıştır. Bildirim hattı bunu görüp gönderimi hiç
/// denemez, kaydı "gönderildi" işaretlemez.
/// </para>
/// </summary>
public sealed class DisabledEmailSender : IEmailSender
{
    public bool IsConfigured => false;

    public Task<EmailSendResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(EmailSendResult.Failure("E-posta gönderimi yapılandırılmadı."));
}
