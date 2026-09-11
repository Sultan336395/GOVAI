using GovAI.Infrastructure.Notifications;
using GovAI.Infrastructure.Options;

namespace GovAI.Application.Tests;

/// <summary>
/// SMTP parolasının secret dosyasından çözülmesi.
///
/// <para>
/// Ortam değişkenindeki parola <c>docker inspect</c> çıktısında ve
/// <c>/proc/&lt;pid&gt;/environ</c> üzerinden okunabilir; secret dosyası yalnızca
/// kapsayıcının içinde ve dosya izinleriyle durur. Dosya bu yüzden ortam değişkeninin
/// önüne geçer.
/// </para>
/// </summary>
public class EmailSecretResolverTests
{
    private static EmailOptions Ayar(string? dosyaYolu = null, string parola = "") =>
        new()
        {
            Enabled = true,
            Host = "smtp.ornek.test",
            FromAddress = "govai@ornek.test",
            Password = parola,
            PasswordFile = dosyaYolu ?? string.Empty,
        };

    [Fact(DisplayName = "SP1. Dosya verilmemişse ortam değişkenindeki parola korunur")]
    public void Dosya_yoksa_ortam_degiskeni_kalir()
    {
        var ayar = Ayar(parola: "ortamdan");

        EmailSecretResolver.Apply(ayar, _ => "dosyadan");

        Assert.Equal("ortamdan", ayar.Password);
    }

    [Fact(DisplayName = "SP2. Dosya verilmişse ortam değişkeninin ÖNÜNE geçer")]
    public void Dosya_ortam_degiskenini_ezer()
    {
        var ayar = Ayar("/run/secrets/smtp", "ortamdan");

        EmailSecretResolver.Apply(ayar, _ => "dosyadan");

        Assert.Equal("dosyadan", ayar.Password);
    }

    [Fact(DisplayName = "SP3. Dosya sonundaki satır sonu parolaya KARIŞMAZ")]
    public void Satir_sonu_kirpilir()
    {
        // `echo 'parola' > secret` her dosyaya bir \n ekler; kırpılmasaydı kimlik
        // doğrulaması sessizce reddedilir ve sebebi aranırdı.
        var ayar = Ayar("/run/secrets/smtp");

        EmailSecretResolver.Apply(ayar, _ => "gizli-parola\n");

        Assert.Equal("gizli-parola", ayar.Password);
    }

    [Fact(DisplayName = "SP4. Windows satır sonu da kırpılır")]
    public void Windows_satir_sonu_kirpilir()
    {
        var ayar = Ayar("/run/secrets/smtp");

        EmailSecretResolver.Apply(ayar, _ => "gizli\r\n");

        Assert.Equal("gizli", ayar.Password);
    }

    [Fact(DisplayName = "SP5. Dosya okunamazsa BAŞLANGIÇTA ÇÖKÜLMEZ")]
    public void Dosya_okunamazsa_cokme_olmaz()
    {
        // E-posta yüzünden bütün API'yi indirmek, bildirimden çok daha pahalıdır.
        // Parola boş kalır ve hata gönderim anında, kimlik doğrulamasında görünür.
        var ayar = Ayar("/run/secrets/yok", "ortamdan");

        var hata = Record.Exception(() => EmailSecretResolver.Apply(ayar, _ => null));

        Assert.Null(hata);
        Assert.Equal("ortamdan", ayar.Password);
    }

    [Fact(DisplayName = "SP6. Olmayan dosya diskten okunduğunda null döner")]
    public void Olmayan_dosya_null()
    {
        Assert.Null(EmailSecretResolver.ReadFromDisk(
            Path.Combine(Path.GetTempPath(), $"govai-yok-{Guid.NewGuid():N}")));
    }

    [Fact(DisplayName = "SP7. Gerçek dosyadan okunur")]
    public void Gercek_dosyadan_okunur()
    {
        var yol = Path.Combine(Path.GetTempPath(), $"govai-secret-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllText(yol, "dosya-parolasi\n");

            var ayar = Ayar(yol);

            EmailSecretResolver.Apply(ayar, EmailSecretResolver.ReadFromDisk);

            Assert.Equal("dosya-parolasi", ayar.Password);
        }
        finally
        {
            File.Delete(yol);
        }
    }

    [Fact(DisplayName = "SP8. Parola hiçbir metin temsilinde GÖRÜNMEZ")]
    public void Parola_metinde_gorunmez()
    {
        // Ayar nesnesi bir log satırına ya da hata mesajına düştüğünde parolayı
        // sızdırmamalı. Kayıtlar (record) varsayılan ToString'inde bütün alanları
        // basar; EmailOptions bilerek sınıftır.
        var ayar = Ayar("/run/secrets/smtp", "COK-GIZLI-PAROLA");

        Assert.DoesNotContain("COK-GIZLI-PAROLA", ayar.ToString());
    }
}
