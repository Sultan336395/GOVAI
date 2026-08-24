using GovAI.Api.Infrastructure;
using GovAI.Application.Companies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/companies</c> — çoklu şirket yönetimi (Faz 1).
///
/// Mevcut <c>/api/company-profile</c> uçları korunur; bunlar tek şirketli akışın
/// devamıdır. Buradaki uçlar üyelik tabanlı çoklu şirket akışını sağlar.
///
/// Yetki her uçta <c>CompanyAccessGuard</c> üzerinden üyelikten doğrulanır;
/// controller seviyesindeki politika yalnızca platform rollerini dışarıda tutar.
/// </summary>
[ApiController]
[Route("api/companies")]
[Authorize(Policy = Policies.CompanyData)]
[Produces("application/json")]
public sealed class CompaniesController(CompanyRegistryService registry) : ControllerBase
{
    /// <summary>Kullanıcının aktif üyeliği bulunan şirketler ("Şirketlerim").</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MyCompanyDto>>> ListMine(CancellationToken cancellationToken) =>
        Ok(await registry.ListMyCompaniesAsync(cancellationToken));

    /// <summary>
    /// Manuel şirket ekleme. Mükerrer durumlar hata değil, anlamlı sonuç olarak döner:
    /// <c>409</c> aynı çalışma alanında, <c>202</c> doğrulama gerekiyor.
    /// </summary>
    [HttpPost]
    [Audited("Company.Created", "Company")]
    public async Task<ActionResult<CreateCompanyResult>> Create(
        [FromBody] CreateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await registry.CreateAsync(request, cancellationToken);

        return result.Outcome switch
        {
            CreateCompanyOutcome.Created => Created($"/api/companies/{result.CompanyId}", result),
            CreateCompanyOutcome.AlreadyInWorkspace => Conflict(result),
            CreateCompanyOutcome.VerificationRequired => Accepted(result),
            _ => Ok(result)
        };
    }

    /// <summary>Şirket profilini günceller. Vergi numarası değiştirilemez.</summary>
    [HttpPut("{companyId:guid}")]
    [Audited("Company.Updated", "Company", RouteKey = "companyId")]
    public async Task<ActionResult<MyCompanyDto>> Update(
        Guid companyId,
        [FromBody] CreateCompanyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await registry.UpdateAsync(companyId, request, cancellationToken));

    /// <summary>Şirketin grup ve ana–bağlı şirket bağını değiştirir.</summary>
    [HttpPut("{companyId:guid}/hierarchy")]
    [Audited("Company.HierarchyChanged", "Company", RouteKey = "companyId")]
    public async Task<ActionResult<MyCompanyDto>> UpdateHierarchy(
        Guid companyId,
        [FromBody] UpdateCompanyHierarchyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await registry.UpdateHierarchyAsync(companyId, request, cancellationToken));

    // ══════════════════════ Şirket grupları ══════════════════════

    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyList<CompanyGroupDto>>> ListGroups(CancellationToken cancellationToken) =>
        Ok(await registry.ListGroupsAsync(cancellationToken));

    [HttpPost("groups")]
    [Audited("CompanyGroup.Created", "CompanyGroup")]
    public async Task<ActionResult<CompanyGroupDto>> CreateGroup(
        [FromBody] UpsertCompanyGroupRequest request,
        CancellationToken cancellationToken) =>
        Ok(await registry.CreateGroupAsync(request, cancellationToken));

    [HttpPut("groups/{groupId:guid}")]
    [Audited("CompanyGroup.Updated", "CompanyGroup", RouteKey = "groupId")]
    public async Task<ActionResult<CompanyGroupDto>> UpdateGroup(
        Guid groupId,
        [FromBody] UpsertCompanyGroupRequest request,
        CancellationToken cancellationToken) =>
        Ok(await registry.UpdateGroupAsync(groupId, request, cancellationToken));

    // ══════════════════════ Doğrulama talepleri ══════════════════════

    /// <summary>
    /// Başka çalışma alanında kayıtlı vergi numaraları için açılmış talepler.
    /// Karşı tarafa ait hiçbir bilgi içermez.
    /// </summary>
    [HttpGet("verification-requests")]
    public async Task<ActionResult<IReadOnlyList<VerificationRequestDto>>> ListVerificationRequests(
        CancellationToken cancellationToken) =>
        Ok(await registry.ListVerificationRequestsAsync(cancellationToken));
}

/// <summary>
/// <c>/api/companies/{companyId}/members</c> — şirket kullanıcıları ve yetkileri.
/// Üyelik yönetimi yalnızca <c>CompanyOwner</c> rolündedir; kural servis katmanında uygulanır.
/// </summary>
[ApiController]
[Route("api/companies/{companyId:guid}/members")]
[Authorize(Policy = Policies.CompanyData)]
[Produces("application/json")]
public sealed class CompanyMembersController(CompanyMembershipService members) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CompanyMemberDto>>> List(
        Guid companyId,
        CancellationToken cancellationToken) =>
        Ok(await members.ListMembersAsync(companyId, cancellationToken));

    [HttpPost]
    [Audited("CompanyMember.Added", "UserCompany", RouteKey = "companyId")]
    public async Task<ActionResult<CompanyMemberDto>> Add(
        Guid companyId,
        [FromBody] AddCompanyMemberRequest request,
        CancellationToken cancellationToken) =>
        Ok(await members.AddMemberAsync(companyId, request, cancellationToken));

    [HttpPut("{membershipId:guid}/role")]
    [Audited("CompanyMember.RoleChanged", "UserCompany", RouteKey = "companyId")]
    public async Task<ActionResult<CompanyMemberDto>> ChangeRole(
        Guid companyId,
        Guid membershipId,
        [FromBody] ChangeCompanyRoleRequest request,
        CancellationToken cancellationToken) =>
        Ok(await members.ChangeRoleAsync(companyId, membershipId, request, cancellationToken));

    [HttpDelete("{membershipId:guid}")]
    [Audited("CompanyMember.Removed", "UserCompany", RouteKey = "companyId")]
    public async Task<IActionResult> Remove(
        Guid companyId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        await members.RemoveMemberAsync(companyId, membershipId, cancellationToken);
        return NoContent();
    }

    /// <summary>Kullanıcının kendi varsayılan şirketini belirlemesi.</summary>
    [HttpPost("default")]
    [Audited("CompanyMember.DefaultChanged", "UserCompany", RouteKey = "companyId")]
    public async Task<IActionResult> SetDefault(Guid companyId, CancellationToken cancellationToken)
    {
        await members.SetDefaultAsync(companyId, cancellationToken);
        return NoContent();
    }

    // ══════════════════════ Davetler ══════════════════════

    [HttpGet("invitations")]
    public async Task<ActionResult<IReadOnlyList<CompanyInvitationDto>>> ListInvitations(
        Guid companyId,
        CancellationToken cancellationToken) =>
        Ok(await members.ListInvitationsAsync(companyId, cancellationToken));

    /// <summary>
    /// Davet oluşturur. Jetonun açık metni yanıtta <b>bir kez</b> döner ve bir daha
    /// alınamaz; veritabanında yalnızca özeti saklanır.
    /// </summary>
    [HttpPost("invitations")]
    [Audited("CompanyInvitation.Created", "CompanyInvitation", RouteKey = "companyId")]
    public async Task<ActionResult<CompanyInvitationResult>> CreateInvitation(
        Guid companyId,
        [FromBody] CreateInvitationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await members.CreateInvitationAsync(companyId, request, cancellationToken));

    [HttpDelete("invitations/{invitationId:guid}")]
    [Audited("CompanyInvitation.Revoked", "CompanyInvitation", RouteKey = "companyId")]
    public async Task<IActionResult> RevokeInvitation(
        Guid companyId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        await members.RevokeInvitationAsync(companyId, invitationId, cancellationToken);
        return NoContent();
    }
}
