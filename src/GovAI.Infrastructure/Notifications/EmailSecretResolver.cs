using GovAI.Infrastructure.Options;

namespace GovAI.Infrastructure.Notifications;

/// <summary>
/// SMTP parolasını secret dosyasından çözer.
///
/// <para>
/// Dosya yolu verilmişse ortam değişkenindeki değerin <b>önüne geçer</b>. Sebep
/// güvenliktir: ortam değişkenindeki parola <c>docker inspect</c> çıktısında ve
/// <c>/proc/&lt;pid&gt;/environ</c> üzerinden okunabilir; secret dosyası yalnızca
/// kapsayıcının içinde ve dosya izinleriyle durur.
/// </para>
///
/// <para>
/// Dosya okunamazsa <b>sessizce geçilmez ama patlanmaz da</b>: parola boş kalır,
/// <c>IsConfigured</c> yine de doğru olabilir ve gönderim kimlik doğrulaması
/// aşamasında açık bir hatayla başarısız olur. Başlangıçta çökmek, e-posta yüzünden
/// bütün API'yi indirirdi.
/// </para>
/// </summary>
public static class EmailSecretResolver
{
    /// <summary>Dosya varsa içeriğini parola olarak yazar ve yolu geri döner.</summary>
    public static void Apply(EmailOptions options, Func<string, string?> readFile)
    {
        if (string.IsNullOrWhiteSpace(options.PasswordFile))
        {
            return;
        }

        var icerik = readFile(options.PasswordFile);

        if (icerik is null)
        {
            return;
        }

        // Dosya sonundaki satır sonu parolanın parçası değildir; `echo` ile yazılan
        // her secret dosyası bir `\n` taşır ve kimlik doğrulaması sessizce reddedilirdi.
        options.Password = icerik.Trim('\r', '\n', ' ', '\t');
    }

    /// <summary>Diskten okur; dosya yoksa ya da okunamıyorsa <c>null</c>.</summary>
    public static string? ReadFromDisk(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
