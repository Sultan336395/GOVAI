using GovAI.Api.Infrastructure;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Application.Companies;
using GovAI.Application.Identity;
using GovAI.Domain.Auditing;
using GovAI.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/admin</c> — kullanıcı yönetimi, rol bazlı yetkilendirme, audit log ve sistem ayarları.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = Policies.SuperAdmin)]
[Produces("application/json")]
public sealed class AdminController(
    AuthenticationService authentication,
    IAuditLogRepository auditLog,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> ListUsers(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        return Ok(await authentication.ListAsync(tenantId, cancellationToken));
    }

    [HttpPost("users")]
    [Audited("Admin.UserCreated", "AppUser")]
    public async Task<ActionResult<UserDto>> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        return Ok(await authentication.CreateUserAsync(tenantId, request, cancellationToken));
    }

    [HttpPut("users/{id:guid}/role")]
    [Audited("Admin.UserRoleChanged", "AppUser")]
    public async Task<ActionResult<UserDto>> ChangeRole(
        Guid id,
        [FromQuery] UserRole role,
        CancellationToken cancellationToken) =>
        Ok(await authentication.ChangeRoleAsync(id, role, cancellationToken));

    [HttpPut("users/{id:guid}/active")]
    [Audited("Admin.UserActiveChanged", "AppUser")]
    public async Task<ActionResult<UserDto>> SetActive(
        Guid id,
        [FromQuery] bool isActive,
        CancellationToken cancellationToken) =>
        Ok(await authentication.SetActiveAsync(id, isActive, cancellationToken));

    /// <summary>Denetim kayıtlarında arama; geriye dönük izlenebilirlik için.</summary>
    [HttpGet("audit-log")]
    public async Task<ActionResult<PagedResult<AuditLogEntry>>> SearchAuditLog(
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? userEmail,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = new AuditLogQuery
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            UserEmail = userEmail,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await auditLog.SearchAsync(query, cancellationToken));
    }

    private Guid RequireTenant() =>
        currentUser.TenantId ?? throw new ForbiddenException("İstek bir kiracıya bağlı değil.");
}

/// <summary>
/// <c>/api/auth</c> — oturum açma. Kurumsal SSO devreye alındığında bu uç yalnızca yerel hesaplar için kalır.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(AuthenticationService authentication, CompanyMembershipService membership) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken) =>
        Ok(await authentication.LoginAsync(request, cancellationToken));

    /// <summary>
    /// Aktif şirketi değiştirir.
    ///
    /// İstemcinin gönderdiği şirket kimliğine güvenilmez: üyelik veritabanından
    /// doğrulanır, şirket başka kiracıdaysa veya üyelik pasifse istek reddedilir.
    /// Doğrulama geçerse aktif şirket bilgisini taşıyan <b>yeni bir jeton</b> döner.
    /// </summary>
    [HttpPost("active-company")]
    [Authorize(Policy = Policies.CompanyData)]
    [Audited("Auth.ActiveCompanyChanged", "Company")]
    public async Task<ActionResult<ActiveCompanyResult>> SetActiveCompany(
        [FromBody] SetActiveCompanyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await membership.SetActiveCompanyAsync(request, cancellationToken));

    /// <summary>Oturum açmış kullanıcının kendi bilgileri.</summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.Read)]
    public ActionResult<CurrentUserDto> Me([FromServices] ICurrentUser currentUser) =>
        Ok(new CurrentUserDto(
            currentUser.UserId,
            currentUser.TenantId,
            currentUser.Email,
            currentUser.Role));

    public sealed record CurrentUserDto(Guid? UserId, Guid? TenantId, string? Email, UserRole? Role);
}
