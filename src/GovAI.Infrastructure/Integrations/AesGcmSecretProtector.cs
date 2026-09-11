using System.Security.Cryptography;
using System.Text;
using GovAI.Application.Integrations;
using GovAI.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace GovAI.Infrastructure.Integrations;

/// <summary>
/// ERP kimlik bilgilerini AES-GCM ile şifreler.
///
/// <para>
/// Parola gibi özetlenemez (hash): kimlik ERP'ye <b>gönderilmek</b> zorundadır, bu yüzden
/// geri çevrilebilir olmalıdır. Geri çevrilebilir olması, saklamanın gevşek olabileceği
/// anlamına gelmez — veritabanı yedeği sızsa bile kimlik bilgileri anahtar olmadan
/// okunamaz.
/// </para>
///
/// <para>
/// AES-GCM seçilmesi bilinçlidir: yalnızca şifrelemekle kalmaz, <b>bütünlüğü de</b>
/// doğrular. Şifreli metin kurcalanırsa çözme hata verir; sessizce bozuk bir kimlik
/// üretip ERP'ye göndermez.
/// </para>
///
/// <para>
/// Anahtar JWT imzalama anahtarından türetilir. Ayrı bir anahtar daha iyi olurdu, ama
/// yapılandırmaya ikinci bir zorunlu sır eklemek kurulumu karmaşıklaştırır ve sahada
/// "geçici olarak" zayıf bir değerle doldurulmasına yol açar. Türetme HKDF ile ve ayrı
/// bir bağlam etiketiyle yapılır: imzalama anahtarı ile şifreleme anahtarı aynı değer
/// olmaz.
/// </para>
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    /// <summary>Türetmenin bağlamı; aynı ana anahtardan başka amaçla üretilen anahtarla çakışmasın.</summary>
    private static readonly byte[] Context = "GOVAI.ErpSecret.v1"u8.ToArray();

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmSecretProtector(IOptions<JwtOptions> jwtOptions)
    {
        ArgumentNullException.ThrowIfNull(jwtOptions);

        var ana = Encoding.UTF8.GetBytes(jwtOptions.Value.SigningKey);

        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, ana, outputLength: 32, info: Context);
    }

    public string Protect(string plainText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plainText);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var acik = Encoding.UTF8.GetBytes(plainText);
        var sifreli = new byte[acik.Length];
        var etiket = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, acik, sifreli, etiket);

        // nonce | etiket | şifreli metin — tek dizede saklanır.
        var paket = new byte[NonceSize + TagSize + sifreli.Length];
        nonce.CopyTo(paket, 0);
        etiket.CopyTo(paket, NonceSize);
        sifreli.CopyTo(paket, NonceSize + TagSize);

        return Convert.ToBase64String(paket);
    }

    public string Unprotect(string protectedText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedText);

        byte[] paket;

        try
        {
            paket = Convert.FromBase64String(protectedText);
        }
        catch (FormatException)
        {
            throw new ErpFetchException("Kayıtlı kimlik bilgisi okunamadı; bağlantıyı yeniden kurun.");
        }

        if (paket.Length <= NonceSize + TagSize)
        {
            throw new ErpFetchException("Kayıtlı kimlik bilgisi eksik; bağlantıyı yeniden kurun.");
        }

        var nonce = paket.AsSpan(0, NonceSize);
        var etiket = paket.AsSpan(NonceSize, TagSize);
        var sifreli = paket.AsSpan(NonceSize + TagSize);
        var acik = new byte[sifreli.Length];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, sifreli, etiket, acik);
        }
        catch (CryptographicException)
        {
            // Anahtar değişmiş ya da kayıt kurcalanmış olabilir. Hangi ihtimal olduğunu
            // söylemek saldırgana bilgi verir; kullanıcıya yapılacak iş söylenir.
            throw new ErpFetchException(
                "Kayıtlı kimlik bilgisi çözülemedi. Bağlantıyı yeniden kurmanız gerekiyor.");
        }

        return Encoding.UTF8.GetString(acik);
    }
}
