using GovAI.Domain.Common;

namespace GovAI.Domain.Identity;

/// <summary>
/// Bir şirkete kullanıcı daveti. Bu aşamada e-posta gönderimi yoktur; veri modeli ve
/// güvenli jeton yapısı kurulur, gönderim adaptörü sonraki fazda eklenir.
///
/// Jeton <b>açık metin saklanmaz</b>: yalnızca özeti tutulur. Veritabanı sızsa bile
/// davet bağlantıları kullanılamaz. Aynı sebeple jeton yalnızca üretildiği anda,
/// bir kez çağırana döner.
/// </summary>
public class CompanyInvitation : AggregateRoot, IAuditable, ITenantScoped
{
    private CompanyInvitation()
    {
    }

    public CompanyInvitation(
        Guid tenantId,
        Guid companyId,
        string email,
        CompanyRole companyRole,
        string tokenHash,
        DateTimeOffset expiresAt,
        Guid invitedByUserId)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(email), "E-posta zorunludur.");
        DomainException.ThrowIf(!email.Contains('@'), "Geçerli bir e-posta adresi giriniz.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(tokenHash), "Davet jetonu özeti zorunludur.");

        TenantId = tenantId;
        CompanyId = companyId;
        Email = email.Trim().ToLowerInvariant();
        CompanyRole = companyRole;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        InvitedByUserId = invitedByUserId;
    }

    public Guid TenantId { get; set; }

    /// <summary>Davetin hangi şirket için üretildiği.</summary>
    public Guid CompanyId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    /// <summary>Kabul edildiğinde verilecek şirket rolü.</summary>
    public CompanyRole CompanyRole { get; private set; }

    /// <summary>Jetonun SHA-256 özeti. Açık metin hiçbir zaman saklanmaz.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Tek kullanımlık: kabul edildiğinde damgalanır ve tekrar kullanılamaz.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Kabul edilebilir durumda mı: süresi dolmamış, kullanılmamış ve iptal edilmemiş.</summary>
    public bool IsRedeemable(DateTimeOffset asOf) =>
        AcceptedAt is null && RevokedAt is null && ExpiresAt > asOf;

    public void Accept(DateTimeOffset at)
    {
        DomainException.ThrowIf(!IsRedeemable(at), "Davet geçersiz, süresi dolmuş veya daha önce kullanılmış.");
        AcceptedAt = at;
    }

    public void Revoke(DateTimeOffset at)
    {
        DomainException.ThrowIf(AcceptedAt is not null, "Kabul edilmiş davet iptal edilemez.");
        RevokedAt = at;
    }
}
