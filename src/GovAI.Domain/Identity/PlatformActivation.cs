using GovAI.Domain.Common;

namespace GovAI.Domain.Identity;

/// <summary>
/// Platform hesabının kendi parolasını belirlemesi için üretilen tek kullanımlık
/// aktivasyon kaydı (Faz 2).
///
/// <para>
/// Platform hesaplarına <b>parola atanmaz</b>. Varsayılan ya da sabit bir parola,
/// kurulumdan sonra kimsenin değiştirmediği bir arka kapıya dönüşür; kodda, seed
/// verisinde ya da logda görünen bir parola ise zaten sır değildir. Bunun yerine
/// hesap parolasız açılır ve <b>pasif</b> kalır; parolayı yalnızca aktivasyon
/// bağlantısını açan kişi belirler.
/// </para>
///
/// <para>
/// Jeton <b>açık metin saklanmaz</b>: yalnızca SHA-256 özeti tutulur. Veritabanı
/// sızsa bile aktivasyon bağlantıları üretilemez. Açık metin yalnızca üretildiği
/// anda, bir kez çağırana döner ve hiçbir yere yazılmaz.
/// </para>
///
/// <para>
/// Tek kullanımlıktır: başarılı aktivasyondan sonra damgalanır ve aynı bağlantı bir
/// daha çalışmaz. Süresi dolan bağlantı da geçersizdir.
/// </para>
/// </summary>
public class PlatformActivation : AggregateRoot, IAuditable
{
    /// <summary>Bağlantının geçerlilik süresi.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private PlatformActivation()
    {
    }

    public PlatformActivation(
        Guid userId,
        string email,
        UserRole role,
        string tokenHash,
        DateTimeOffset createdAt)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(email), "E-posta zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(tokenHash), "Aktivasyon jetonu özeti zorunludur.");

        // Bu mekanizma YALNIZCA platform rolleri içindir. Kiracı rolleri normal
        // kullanıcı yönetiminden geçer; buradan geçmesi yetki sınırını delerdi.
        DomainException.ThrowIf(
            (int)role < 10,
            "Aktivasyon bağlantısı yalnızca platform rolleri için üretilir.");

        UserId = userId;
        Email = email.Trim().ToLowerInvariant();
        Role = role;
        TokenHash = tokenHash;
        ExpiresAt = createdAt.Add(Lifetime);
    }

    public Guid UserId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    /// <summary>Aktivasyon sonunda hesabın sahip olacağı rol.</summary>
    public UserRole Role { get; private set; }

    /// <summary>Jetonun SHA-256 özeti. Açık metin hiçbir zaman saklanmaz.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Tek kullanımlık: kullanıldığında damgalanır ve tekrar açılamaz.</summary>
    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Kullanılabilir mi: süresi dolmamış, kullanılmamış ve iptal edilmemiş.</summary>
    public bool IsRedeemable(DateTimeOffset asOf) =>
        ActivatedAt is null && RevokedAt is null && ExpiresAt > asOf;

    public void Activate(DateTimeOffset at)
    {
        DomainException.ThrowIf(
            !IsRedeemable(at),
            "Aktivasyon bağlantısı geçersiz, süresi dolmuş veya daha önce kullanılmış.");

        ActivatedAt = at;
    }

    /// <summary>
    /// Yeni bir bağlantı üretildiğinde eskisi iptal edilir: aynı anda birden çok
    /// geçerli bağlantı dolaşımda kalmamalıdır.
    /// </summary>
    public void Revoke(DateTimeOffset at)
    {
        if (ActivatedAt is null && RevokedAt is null)
        {
            RevokedAt = at;
        }
    }
}
