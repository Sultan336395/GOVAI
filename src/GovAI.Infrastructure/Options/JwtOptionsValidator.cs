using Microsoft.Extensions.Options;

namespace GovAI.Infrastructure.Options;

/// <summary>
/// JWT imza anahtarının açılışta reddedilme kuralları.
///
/// Uzunluk doğrulaması tek başına yetmiyordu: depodaki örnek anahtar
/// (<c>CHANGE_ME_AT_LEAST_32_CHARACTERS_LONG_SECRET</c>) 45 karakter olduğu için
/// <c>[MinLength(32)]</c> kontrolünü geçiyor ve üretimde fark edilmeden
/// kullanılabiliyordu (Faz 0 / D4). Veritabanı parolası unutulduğunda uygulama
/// hemen patlar; bu ise sessizce çalışmaya devam ederdi.
///
/// Anahtar kod içinde <b>üretilmez ve saklanmaz</b>. Ortam değişkeninden
/// (<c>Jwt__SigningKey</c>) veya güvenli bir secret deposundan gelmelidir —
/// bkz. <c>deploy/.env.example</c> ve <c>docs/security.md</c>.
/// </summary>
public sealed class JwtOptionsValidator(string environmentName) : IValidateOptions<JwtOptions>
{
    /// <summary>Anahtarın taşıması gereken en az karakter sayısı (HMAC-SHA256 için 256 bit).</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>Her ortamda reddedilen yer tutucu izleri.</summary>
    private static readonly string[] ForbiddenEverywhere =
    [
        "CHANGE_ME",
        "changeme",
        "your-secret",
        "secret-key-here"
    ];

    /// <summary>
    /// Yalnızca Development ortamında kabul edilen, depoda açıkça duran geliştirme anahtarları.
    /// Başka bir ortamda kullanılırlarsa uygulama açılmaz.
    /// </summary>
    private static readonly string[] DevelopmentOnlyMarkers =
    [
        "LOCAL-DEV-ONLY",
        "local-dev-only"
    ];

    private bool IsDevelopment =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var key = options.SigningKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            return Fail("Jwt:SigningKey tanımlı değil.");
        }

        if (key.Trim().Length < MinimumKeyLength)
        {
            return Fail($"Jwt:SigningKey en az {MinimumKeyLength} karakter olmalıdır (şu an {key.Trim().Length}).");
        }

        foreach (var marker in ForbiddenEverywhere)
        {
            if (key.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return Fail(
                    $"Jwt:SigningKey hâlâ örnek değeri içeriyor ('{marker}'). " +
                    "Gerçek anahtar Jwt__SigningKey ortam değişkeninden verilmelidir.");
            }
        }

        if (!IsDevelopment)
        {
            foreach (var marker in DevelopmentOnlyMarkers)
            {
                if (key.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return Fail(
                        $"Jwt:SigningKey geliştirme anahtarıdır ('{marker}') ve '{environmentName}' " +
                        "ortamında kullanılamaz. Bu anahtar herkese açık depoda durmaktadır.");
                }
            }
        }

        return ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult Fail(string message) =>
        ValidateOptionsResult.Fail($"{message} Uygulama güvenli bir imza anahtarı olmadan başlatılamaz.");
}
