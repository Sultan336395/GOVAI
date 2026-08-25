using System.Security.Cryptography;
using System.Text;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using GovAI.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Companies;

/// <summary>
/// Kullanıcı–şirket üyelikleri, şirket rolleri, davetler ve aktif şirket değiştirme.
///
/// Değişmez kurallar burada uygulanır:
/// <list type="bullet">
///   <item>Bir kullanıcı aynı şirkete iki kez bağlanamaz.</item>
///   <item>Bir kullanıcının en fazla bir varsayılan şirketi olabilir.</item>
///   <item>Her şirkette en az bir aktif CompanyOwner bulunmalıdır.</item>
///   <item>Kullanıcı kendi yetkisini yükseltemez.</item>
/// </list>
/// </summary>
public sealed class CompanyMembershipService(
    IUserCompanyRepository memberships,
    IUserRepository users,
    ICompanyInvitationRepository invitations,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITokenService tokenService,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    ILogger<CompanyMembershipService> logger)
{
    // ══════════════════════ Üyelik listesi ══════════════════════

    public async Task<IReadOnlyList<CompanyMemberDto>> ListMembersAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        // Üye listesini yalnızca sahip ve yönetici okuyabilir; yönetmek için sahiplik
        // gerekir. Uzman ve görüntüleyici listeyi hiç göremez — çalıştıkları şirketteki
        // diğer kişilerin adını ve e-postasını görmeleri için bir gerekçe yoktur.
        await access.EnsureAccessAsync(companyId, CompanyPermission.ViewMembers, cancellationToken);

        var items = await memberships.ListForCompanyAsync(companyId, cancellationToken);
        var result = new List<CompanyMemberDto>();

        foreach (var membership in items)
        {
            var user = await users.GetAsync(membership.UserId, cancellationToken);
            if (user is null)
            {
                continue;
            }

            result.Add(new CompanyMemberDto(
                membership.Id,
                user.Id,
                user.Email,
                user.FullName,
                membership.CompanyRole,
                membership.IsActive,
                membership.IsDefault,
                membership.CreatedAt));
        }

        return result;
    }

    // ══════════════════════ Üyelik ekleme ══════════════════════

    public async Task<CompanyMemberDto> AddMemberAsync(
        Guid companyId,
        AddCompanyMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();
        await access.EnsureAccessAsync(companyId, CompanyPermission.ManageMembers, cancellationToken);

        var user = await users.GetAsync(request.UserId, cancellationToken)
                   ?? throw new NotFoundException("Kullanıcı", request.UserId);

        if (user.TenantId != tenantId)
        {
            throw new NotFoundException("Kullanıcı", request.UserId);
        }

        var existing = await memberships.GetAsync(request.UserId, companyId, cancellationToken);
        if (existing is not null)
        {
            if (existing.GrantsAccess)
            {
                throw new ValidationException("UserId", "Bu kullanıcı zaten bu şirkete bağlı.");
            }

            // Pasif üyelik yeniden açılır; ikinci satır oluşturulmaz (benzersizlik kısıtı).
            existing.Activate();
            existing.ChangeRole(request.CompanyRole);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(existing, user);
        }

        var isFirst = (await memberships.ListForUserAsync(request.UserId, cancellationToken))
            .All(m => !m.GrantsAccess);

        var membership = new UserCompany(tenantId, request.UserId, companyId, request.CompanyRole, isDefault: isFirst);
        await memberships.AddAsync(membership, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Şirket üyeliği eklendi. CompanyId={CompanyId} UserId={UserId} Rol={Role}",
            companyId, request.UserId, request.CompanyRole);

        return ToDto(membership, user);
    }

    // ══════════════════════ Rol değiştirme ══════════════════════

    public async Task<CompanyMemberDto> ChangeRoleAsync(
        Guid companyId,
        Guid membershipId,
        ChangeCompanyRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureAccessAsync(companyId, CompanyPermission.ManageMembers, cancellationToken);

        var membership = await LoadMembershipAsync(companyId, membershipId, cancellationToken);
        var actorUserId = currentUser.UserId;

        // Kullanıcı kendi yetkisini değiştiremez: yükseltme de düşürme de kilitlenmeye yol açar.
        if (membership.UserId == actorUserId)
        {
            throw new ForbiddenException("Kendi şirket rolünüzü değiştiremezsiniz.");
        }

        // Son aktif sahibin rolü düşürülemez.
        if (membership.CompanyRole == CompanyRole.CompanyOwner
            && request.CompanyRole != CompanyRole.CompanyOwner
            && membership.IsActive)
        {
            await EnsureNotLastOwnerAsync(companyId, cancellationToken);
        }

        membership.ChangeRole(request.CompanyRole);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var user = await users.GetAsync(membership.UserId, cancellationToken)!;
        return ToDto(membership, user!);
    }

    // ══════════════════════ Üyelik kaldırma ══════════════════════

    public async Task RemoveMemberAsync(
        Guid companyId,
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureAccessAsync(companyId, CompanyPermission.ManageMembers, cancellationToken);

        var membership = await LoadMembershipAsync(companyId, membershipId, cancellationToken);

        if (membership.CompanyRole == CompanyRole.CompanyOwner && membership.IsActive)
        {
            await EnsureNotLastOwnerAsync(companyId, cancellationToken);
        }

        membership.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Şirket üyeliği kaldırıldı. CompanyId={CompanyId} MembershipId={MembershipId}", companyId, membershipId);
    }

    // ══════════════════════ Varsayılan şirket ══════════════════════

    public async Task SetDefaultAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        await access.EnsureAccessAsync(companyId, CompanyPermission.Read, cancellationToken);

        var all = await memberships.ListForUserAsync(userId, cancellationToken);

        // Tek varsayılan kuralını veritabanı da zorlar: user_companies üzerinde
        // is_default = true için kısmi tekil indeks vardır. Temizleme ile işaretlemeyi
        // aynı SaveChanges'e koymak yetmez — EF ifadeleri tek toplu işte gönderir ve
        // PostgreSQL indeksi ifade ifade denetler; işaretleme temizlemeden önce
        // gönderilirse istek 23505 ile 500 döner. Sıra bu yüzden iki adımda garanti
        // edilir ve tek işleme alınır: arada varsayılansız bir an kalmaz.
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var membership in all.Where(m => m.IsDefault && m.CompanyId != companyId))
            {
                membership.ClearDefault();
            }

            await unitOfWork.SaveChangesAsync(ct);

            var target = all.FirstOrDefault(m => m.CompanyId == companyId);
            if (target is not null && !target.IsDefault)
            {
                target.MarkDefault();
                await unitOfWork.SaveChangesAsync(ct);
            }
        }, cancellationToken);
    }

    // ══════════════════════ Aktif şirket ══════════════════════

    /// <summary>
    /// Aktif şirketi sunucuda doğrular ve yeni bir jetona yazar.
    ///
    /// İstemcinin gönderdiği şirket kimliğine güvenilmez: üyelik <b>veritabanından</b>
    /// doğrulanır. Üyelik pasifse veya şirket başka kiracıdaysa istek reddedilir.
    /// </summary>
    public async Task<ActiveCompanyResult> SetActiveCompanyAsync(
        SetActiveCompanyRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();
        var userId = RequireUser();

        // Kiracı + varlık + aktif üyelik kontrolü tek kapıdan.
        var company = await access.LoadAccessibleAsync(request.CompanyId, CompanyPermission.Read, cancellationToken);
        var membership = await memberships.GetAsync(userId, company.Id, cancellationToken);

        if (membership is null || !membership.GrantsAccess)
        {
            throw new NotFoundException("Firma", request.CompanyId);
        }

        var user = await users.GetAsync(userId, cancellationToken)
                   ?? throw new NotFoundException("Kullanıcı", userId);

        // Kullanıcının erişebildiği şirketler jetona yazılır; aktif şirket ilk sıradadır.
        var accessible = (await memberships.ListForUserAsync(userId, cancellationToken))
            .Where(m => m.GrantsAccess)
            .Select(m => m.CompanyId)
            .ToList();

        var (token, expiresAt) = tokenService.CreateAccessToken(
            user.Id, tenantId, user.Email, user.Role, accessible, company.Id);

        // Aktif şirket aynı zamanda varsayılan olur; kullanıcı bir daha girdiğinde açılır.
        await SetDefaultAsync(company.Id, cancellationToken);

        logger.LogInformation(
            "Aktif şirket değiştirildi. UserId={UserId} CompanyId={CompanyId}", userId, company.Id);

        return new ActiveCompanyResult(
            company.Id, company.LegalName, membership.CompanyRole, token, expiresAt);
    }

    // ══════════════════════ Davetler ══════════════════════

    /// <summary>
    /// Davet oluşturur. Jetonun açık metni <b>yalnızca bu dönüşte, bir kez</b> verilir;
    /// veritabanında SHA-256 özeti saklanır. Veritabanı sızsa bile davet bağlantıları
    /// kullanılamaz.
    /// </summary>
    public async Task<CompanyInvitationResult> CreateInvitationAsync(
        Guid companyId,
        CreateInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();
        await access.EnsureAccessAsync(companyId, CompanyPermission.ManageMembers, cancellationToken);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenHash = HashToken(token);

        var validFor = Math.Clamp(request.ValidForDays, 1, 30);

        var invitation = new CompanyInvitation(
            tenantId,
            companyId,
            request.Email,
            request.CompanyRole,
            tokenHash,
            clock.UtcNow.AddDays(validFor),
            RequireUser());

        await invitations.AddAsync(invitation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CompanyInvitationResult(
            invitation.Id, invitation.Email, invitation.CompanyRole, invitation.ExpiresAt, token);
    }

    public async Task<IReadOnlyList<CompanyInvitationDto>> ListInvitationsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureAccessAsync(companyId, CompanyPermission.ManageMembers, cancellationToken);

        var items = await invitations.ListForCompanyAsync(companyId, cancellationToken);
        var now = clock.UtcNow;

        return items
            .Select(i => new CompanyInvitationDto(
                i.Id, i.Email, i.CompanyRole, i.ExpiresAt, i.AcceptedAt, i.RevokedAt, i.IsRedeemable(now)))
            .ToList();
    }

    public async Task RevokeInvitationAsync(
        Guid companyId,
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureAccessAsync(companyId, CompanyPermission.ManageMembers, cancellationToken);

        var invitation = await invitations.GetAsync(invitationId, cancellationToken)
                         ?? throw new NotFoundException("Davet", invitationId);

        if (invitation.CompanyId != companyId)
        {
            throw new NotFoundException("Davet", invitationId);
        }

        invitation.Revoke(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Jeton özeti; açık metin hiçbir zaman saklanmaz.</summary>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // ══════════════════════ Yardımcılar ══════════════════════

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new ForbiddenException("İstek bir kullanıcıya bağlı değil.");

    private async Task<UserCompany> LoadMembershipAsync(
        Guid companyId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var membership = await memberships.GetByIdAsync(membershipId, cancellationToken)
                         ?? throw new NotFoundException("Üyelik", membershipId);

        if (membership.CompanyId != companyId)
        {
            throw new NotFoundException("Üyelik", membershipId);
        }

        return membership;
    }

    private async Task EnsureNotLastOwnerAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var owners = await memberships.CountActiveOwnersAsync(companyId, cancellationToken);

        if (owners <= 1)
        {
            throw new ValidationException(
                "CompanyRole",
                "Şirketin son sahibi kaldırılamaz veya rolü düşürülemez. Önce başka bir sahip atayın.");
        }
    }

    private static CompanyMemberDto ToDto(UserCompany membership, AppUser user) =>
        new(
            membership.Id,
            user.Id,
            user.Email,
            user.FullName,
            membership.CompanyRole,
            membership.IsActive,
            membership.IsDefault,
            membership.CreatedAt);
}
