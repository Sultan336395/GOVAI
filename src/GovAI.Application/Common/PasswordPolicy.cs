namespace GovAI.Application.Common;

/// <summary>
/// Parola gücü kuralı (Faz 2).
///
/// <para>
/// Platform hesapları ortak kataloğu ve karantina kararlarını yönetir; bir tanesinin
/// ele geçirilmesi tek bir müşteriyi değil <b>tüm kiracıları</b> etkiler. Bu yüzden
/// kiracı kullanıcılarından daha katı bir eşik uygulanır.
/// </para>
///
/// <para>
/// Kural karmaşık değil, <b>uzunluk ağırlıklı</b>: uzunluk parolanın gücüne
/// karakter çeşitliliğinden çok daha fazla katkı yapar. Yine de tek karakter
/// sınıfından oluşan uzun diziler ("aaaaaaaaaaaaaa") elenir.
/// </para>
/// </summary>
public static class PasswordPolicy
{
    /// <summary>Platform hesapları için asgari uzunluk.</summary>
    public const int MinimumLength = 12;

    /// <summary>En az kaç farklı karakter sınıfı bulunmalı (küçük, büyük, rakam, diğer).</summary>
    public const int MinimumCharacterClasses = 3;

    /// <summary>
    /// Parola kurala uyuyor mu? Uymuyorsa <paramref name="reason"/> kullanıcıya
    /// gösterilebilecek bir açıklama taşır.
    /// </summary>
    public static bool IsStrong(string? password, out string reason)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            reason = "Parola boş olamaz.";
            return false;
        }

        if (password.Length < MinimumLength)
        {
            reason = $"Parola en az {MinimumLength} karakter olmalıdır.";
            return false;
        }

        var kucuk = password.Any(char.IsLower);
        var buyuk = password.Any(char.IsUpper);
        var rakam = password.Any(char.IsDigit);
        var diger = password.Any(c => !char.IsLetterOrDigit(c));

        var sinif = new[] { kucuk, buyuk, rakam, diger }.Count(x => x);

        if (sinif < MinimumCharacterClasses)
        {
            reason =
                $"Parola en az {MinimumCharacterClasses} farklı karakter türü içermelidir: "
                + "küçük harf, büyük harf, rakam ve simgelerden en az üçü.";

            return false;
        }

        // Tek karakterin tekrarı uzunluk şartını sağlar ama parola değildir.
        if (password.Distinct().Count() < 6)
        {
            reason = "Parola çok az sayıda farklı karakterden oluşuyor.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
