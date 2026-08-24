using GovAI.Domain.Common;

namespace GovAI.Domain.Companies;

/// <summary>
/// Aynı kiracı altındaki tüzel şirketleri raporlama amacıyla bir araya getiren küme
/// (holding, aile şirketleri topluluğu vb.).
///
/// Gruba üyelik ile <b>ana/bağlı şirket ilişkisi ayrı kavramlardır</b>: grup bir
/// kümedir, ana/bağlı ise hukuki bir bağdır. Bir şirket gruba üye olmadan da
/// ana şirkete bağlanabilir.
/// </summary>
public class CompanyGroup : AggregateRoot, IAuditable, ISoftDeletable, ITenantScoped
{
    private CompanyGroup()
    {
    }

    public CompanyGroup(Guid tenantId, string name, string? description = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "Grup adı zorunludur.");

        TenantId = tenantId;
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = true;
    }

    public Guid TenantId { get; set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public void Rename(string name, string? description)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "Grup adı zorunludur.");

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
