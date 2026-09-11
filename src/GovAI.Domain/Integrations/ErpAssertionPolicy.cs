using GovAI.Domain.Common;

namespace GovAI.Domain.Integrations;

/// <summary>Beyanı kimin adına sunulduğu.</summary>
public enum ErpPrincipalKind
{
    /// <summary>ERP'nin kendisi (arka plan işi). <c>sub == iss</c>.</summary>
    Service = 1,

    /// <summary>ERP'de oturum açmış bir çalışan. <c>sub</c> o kişinin ERP kimliğidir.</summary>
    User = 2
}

/// <summary>Doğrulanmış bir beyanın taşıdığı bilgi.</summary>
public sealed record ErpAssertionClaims
{
    public required string Issuer { get; init; }

    public required string Subject { get; init; }

    public required string Audience { get; init; }

    public required string TokenId { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>ERP kullanıcısının görünen adı. Zorunlu değildir; denetim izinde kullanılır.</summary>
    public string? SubjectName { get; init; }

    public ErpPrincipalKind Kind =>
        string.Equals(Subject, Issuer, StringComparison.Ordinal)
            ? ErpPrincipalKind.Service
            : ErpPrincipalKind.User;
}

/// <summary>Beyan reddedilme sebebi. Dışarı <b>ayrıntısız</b> döner; kayda ayrıntılı yazılır.</summary>
public enum ErpAssertionRejection
{
    None = 0,
    MissingClaim = 1,
    AudienceMismatch = 2,
    IssuerMismatch = 3,
    Expired = 4,
    NotYetValid = 5,
    LifetimeTooLong = 6,
    Replayed = 7,
    IdentityDisabled = 8,
    UnknownKey = 9,
    UnsupportedAlgorithm = 10,
    BadSignature = 11
}

public sealed record ErpAssertionResult(bool IsValid, ErpAssertionRejection Rejection, string? Detail)
{
    public static ErpAssertionResult Ok() => new(true, ErpAssertionRejection.None, null);

    public static ErpAssertionResult Fail(ErpAssertionRejection reason, string detail) =>
        new(false, reason, detail);
}

/// <summary>
/// İmzası doğrulanmış bir beyanın <b>içeriğinin</b> kabul edilebilirliği.
///
/// <para>
/// İmza doğruluğu tek başına yetmez: geçerli imzalı ama süresi dolmuş, çok uzun ömürlü,
/// başka bir alıcıya yazılmış ya da daha önce kullanılmış bir beyan da reddedilmelidir.
/// Bu kurallar saf tutulur; kriptografi olmadan, saat verilerek sınanabilirler.
/// </para>
/// </summary>
public static class ErpAssertionPolicy
{
    /// <summary>
    /// Beyanı değerlendirir.
    /// </summary>
    /// <param name="claims">Beyandan okunan alanlar.</param>
    /// <param name="expectedIssuer">Kimlik kaydındaki <c>ClientId</c>.</param>
    /// <param name="expectedAudience">GOVAI'nin jeton ucu için beklediği alıcı.</param>
    /// <param name="identityEnabled">Kimlik etkin mi?</param>
    /// <param name="alreadySeen">Bu <c>jti</c> daha önce kullanıldı mı?</param>
    /// <param name="now">Sunucu saati.</param>
    public static ErpAssertionResult Evaluate(
        ErpAssertionClaims claims,
        string expectedIssuer,
        string expectedAudience,
        bool identityEnabled,
        bool alreadySeen,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(claims.TokenId))
        {
            // jti olmadan tekrar saldırısı tespit edilemez; beyan sınırsız kez kullanılabilirdi.
            return ErpAssertionResult.Fail(
                ErpAssertionRejection.MissingClaim, "Beyanda jti yok.");
        }

        if (string.IsNullOrWhiteSpace(claims.Subject))
        {
            return ErpAssertionResult.Fail(
                ErpAssertionRejection.MissingClaim, "Beyanda sub yok.");
        }

        if (!identityEnabled)
        {
            return ErpAssertionResult.Fail(
                ErpAssertionRejection.IdentityDisabled, "Servis kimliği devre dışı.");
        }

        if (!string.Equals(claims.Issuer, expectedIssuer, StringComparison.Ordinal))
        {
            return ErpAssertionResult.Fail(
                ErpAssertionRejection.IssuerMismatch, "Beyanın iss alanı kimliğe ait değil.");
        }

        // Alıcı TAM eşleşir. Gevşetilirse bir ERP'nin başka bir servis için ürettiği
        // geçerli imzalı beyan GOVAI'de de kabul edilirdi.
        if (!string.Equals(claims.Audience, expectedAudience, StringComparison.Ordinal))
        {
            return ErpAssertionResult.Fail(
                ErpAssertionRejection.AudienceMismatch, "Beyanın aud alanı bu uç için değil.");
        }

        var skew = TimeSpan.FromSeconds(ErpServiceIdentity.ClockSkewSeconds);

        if (claims.ExpiresAt <= now - skew)
        {
            return ErpAssertionResult.Fail(ErpAssertionRejection.Expired, "Beyanın süresi dolmuş.");
        }

        if (claims.IssuedAt > now + skew)
        {
            // Gelecek tarihli beyan, saat kayması değilse ömrü uzatma girişimidir.
            return ErpAssertionResult.Fail(
                ErpAssertionRejection.NotYetValid, "Beyan gelecek tarihli.");
        }

        var omur = claims.ExpiresAt - claims.IssuedAt;

        if (omur > TimeSpan.FromSeconds(ErpServiceIdentity.MaximumAssertionLifetimeSeconds))
        {
            // İmza geçerli olsa bile uzun ömre izin verilmez: çalınan beyan o süre
            // boyunca kullanılabilir olurdu.
            return ErpAssertionResult.Fail(
                ErpAssertionRejection.LifetimeTooLong,
                $"Beyan ömrü {ErpServiceIdentity.MaximumAssertionLifetimeSeconds} saniyeyi aşıyor.");
        }

        if (omur <= TimeSpan.Zero)
        {
            return ErpAssertionResult.Fail(
                ErpAssertionRejection.Expired, "Beyanın bitişi başlangıcından önce.");
        }

        if (alreadySeen)
        {
            return ErpAssertionResult.Fail(
                ErpAssertionRejection.Replayed, "Bu beyan daha önce kullanıldı.");
        }

        return ErpAssertionResult.Ok();
    }

    /// <summary>
    /// Tekrar önleme kaydının ne kadar tutulacağı.
    ///
    /// <para>
    /// Beyanın azami ömrü artı saat kayması payı kadar. Daha kısa tutmak, ömrü dolmamış
    /// bir beyanın yeniden kullanılmasına kapı açardı.
    /// </para>
    /// </summary>
    public static TimeSpan ReplayWindow { get; } = TimeSpan.FromSeconds(
        ErpServiceIdentity.MaximumAssertionLifetimeSeconds + (ErpServiceIdentity.ClockSkewSeconds * 2));
}
