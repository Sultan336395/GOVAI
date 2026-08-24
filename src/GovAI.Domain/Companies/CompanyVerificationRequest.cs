using GovAI.Domain.Common;

namespace GovAI.Domain.Companies;

/// <summary>
/// Başka bir kiracıda kayıtlı vergi numarası için açılan bağlantı/doğrulama talebi.
///
/// Bir kullanıcı, başka bir çalışma alanında zaten kayıtlı olan bir şirketi eklemeye
/// çalıştığında sistem <b>hiçbir bilgi sızdırmaz</b>: diğer kiracının veya şirketin adı,
/// kimliği, hatta var olup olmadığı bile açıklanmaz. Yalnızca bu talep kaydı oluşur ve
/// kullanıcıya genel bir mesaj gösterilir.
///
/// Bu yüzden kayıtta karşı tarafa ait <b>hiçbir yabancı anahtar tutulmaz</b> —
/// yalnızca talebi açan tarafın kiracısı ve aranan vergi numarası saklanır.
/// Eşleştirme, platform onayı sırasında sunucu tarafında yapılır.
/// </summary>
public class CompanyVerificationRequest : AggregateRoot, IAuditable, ITenantScoped
{
    private CompanyVerificationRequest()
    {
    }

    public CompanyVerificationRequest(
        Guid tenantId,
        string taxNumber,
        string requestedLegalName,
        Guid requestedByUserId,
        DateTimeOffset requestedAt)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(taxNumber), "Vergi numarası zorunludur.");

        TenantId = tenantId;
        TaxNumber = taxNumber.Trim();
        RequestedLegalName = requestedLegalName.Trim();
        RequestedByUserId = requestedByUserId;
        RequestedAt = requestedAt;
        Status = VerificationRequestStatus.Pending;
    }

    /// <summary>Talebi açan çalışma alanı.</summary>
    public Guid TenantId { get; set; }

    public string TaxNumber { get; private set; } = string.Empty;

    /// <summary>Talebi açanın girdiği unvan; karşı taraftan alınmaz.</summary>
    public string RequestedLegalName { get; private set; } = string.Empty;

    public Guid RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public VerificationRequestStatus Status { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>Platform tarafından yazılan karar notu; kullanıcıya gösterilebilir.</summary>
    public string? ResolutionNote { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public void Approve(DateTimeOffset at, string? note)
    {
        DomainException.ThrowIf(Status != VerificationRequestStatus.Pending, "Talep zaten sonuçlanmış.");
        Status = VerificationRequestStatus.Approved;
        ResolvedAt = at;
        ResolutionNote = note;
    }

    public void Reject(DateTimeOffset at, string? note)
    {
        DomainException.ThrowIf(Status != VerificationRequestStatus.Pending, "Talep zaten sonuçlanmış.");
        Status = VerificationRequestStatus.Rejected;
        ResolvedAt = at;
        ResolutionNote = note;
    }

    public void Cancel(DateTimeOffset at)
    {
        DomainException.ThrowIf(Status != VerificationRequestStatus.Pending, "Talep zaten sonuçlanmış.");
        Status = VerificationRequestStatus.Cancelled;
        ResolvedAt = at;
    }
}
