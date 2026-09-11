using System.Security.Cryptography;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using GovAI.Domain.Integrations;

namespace GovAI.Application.Integrations;

public sealed record ErpSigningKeyDto(
    string KeyId,
    ErpSigningAlgorithm Algorithm,
    DateTimeOffset AddedAt,
    DateTimeOffset? RevokedAt,
    bool IsActive);

public sealed record ErpServiceIdentityDto(
    Guid Id,
    Guid CompanyId,
    string ClientId,
    string DisplayName,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastTokenIssuedAt,
    IReadOnlyList<ErpSigningKeyDto> Keys);

public sealed record CreateErpIdentityRequest
{
    public required string DisplayName { get; init; }

    /// <summary>İlk anahtarın kimliği (ör. <c>2026-09</c>).</summary>
    public required string KeyId { get; init; }

    public required ErpSigningAlgorithm Algorithm { get; init; }

    /// <summary>ERP'nin <b>açık</b> anahtarı, PEM biçiminde.</summary>
    public required string PublicKeyPem { get; init; }
}

public sealed record AddErpKeyRequest
{
    public required string KeyId { get; init; }

    public required ErpSigningAlgorithm Algorithm { get; init; }

    public required string PublicKeyPem { get; init; }
}

/// <summary>
/// ERP servis kimliklerinin yönetimi.
///
/// <para>
/// Burada üretilen tek şey <b>istemci kimliğidir</b> ve o gizli değildir. Hiçbir sır
/// üretilmez, saklanmaz ve kullanıcıya gösterilmez — gösterilecek bir sır yoktur.
/// Bu, düz makine anahtarından ayrıldığımız noktadır: orada "anahtarı bir kez
/// gösteriyoruz, kaydedin" adımı olurdu ve o an ekran görüntüsüne, sohbete, bilet
/// sistemine düşerdi.
/// </para>
/// </summary>
public sealed class ErpServiceIdentityService(
    IErpServiceIdentityRepository identities,
    IUnitOfWork unitOfWork,
    CompanyAccessGuard access,
    IDateTimeProvider clock)
{
    /// <summary>Bir firmada tanımlı olabilecek azami kimlik.</summary>
    public const int MaximumPerCompany = 5;

    public async Task<IReadOnlyList<ErpServiceIdentityDto>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        await access.LoadAccessibleAsync(companyId, CompanyPermission.ManageProfile, cancellationToken);

        return (await identities.ListForCompanyAsync(companyId, cancellationToken))
            .Select(ToDto)
            .ToList();
    }

    public async Task<ErpServiceIdentityDto> CreateAsync(
        Guid companyId,
        CreateErpIdentityRequest request,
        CancellationToken cancellationToken = default)
    {
        var company = await access.LoadAccessibleAsync(
            companyId, CompanyPermission.ManageProfile, cancellationToken);

        var mevcut = await identities.ListForCompanyAsync(companyId, cancellationToken);

        DomainException.ThrowIf(
            mevcut.Count >= MaximumPerCompany,
            $"Bir firmada en çok {MaximumPerCompany} ERP kimliği tanımlanabilir.");

        var kimlik = new ErpServiceIdentity(
            company.TenantId, companyId, IstemciKimligiUret(), request.DisplayName, clock.UtcNow);

        kimlik.AddKey(new ErpSigningKey(
            company.TenantId, kimlik.Id, request.KeyId, request.Algorithm,
            request.PublicKeyPem, clock.UtcNow));

        await identities.AddAsync(kimlik, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(kimlik);
    }

    /// <summary>Anahtar ekler. Değişim sırasında iki anahtar birlikte etkin olabilir.</summary>
    public async Task<ErpServiceIdentityDto> AddKeyAsync(
        Guid identityId,
        AddErpKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        var kimlik = await YukleAsync(identityId, cancellationToken);

        kimlik.AddKey(new ErpSigningKey(
            kimlik.TenantId, kimlik.Id, request.KeyId, request.Algorithm,
            request.PublicKeyPem, clock.UtcNow));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(kimlik);
    }

    public async Task<ErpServiceIdentityDto> RevokeKeyAsync(
        Guid identityId,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        var kimlik = await YukleAsync(identityId, cancellationToken);

        kimlik.RevokeKey(keyId, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(kimlik);
    }

    /// <summary>
    /// Kimliği açar ya da kapatır.
    ///
    /// <para>
    /// Kapatmak, jetonların kısa ömrü sayesinde dakikalar içinde etkili olur. Uzun
    /// ömürlü jetonlarda bu düğme işe yaramazdı.
    /// </para>
    /// </summary>
    public async Task<ErpServiceIdentityDto> SetEnabledAsync(
        Guid identityId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var kimlik = await YukleAsync(identityId, cancellationToken);

        if (enabled)
        {
            kimlik.Enable();
        }
        else
        {
            kimlik.Disable();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(kimlik);
    }

    private async Task<ErpServiceIdentity> YukleAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var kimlik = await identities.GetAsync(identityId, cancellationToken)
            ?? throw new NotFoundException("ERP kimliği", identityId);

        await access.LoadAccessibleAsync(
            kimlik.CompanyId, CompanyPermission.ManageProfile, cancellationToken);

        return kimlik;
    }

    /// <summary>
    /// İstemci kimliği üretir.
    ///
    /// <para>
    /// Tahmin edilemez olması <b>gizlilik için değil</b>, çakışmayı ve kimlik taramayı
    /// zorlaştırmak içindir: değer tek başına hiçbir şeye yetmez, imza olmadan işe
    /// yaramaz.
    /// </para>
    /// </summary>
    private static string IstemciKimligiUret() =>
        "erp_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    private static ErpServiceIdentityDto ToDto(ErpServiceIdentity i) =>
        new(
            i.Id,
            i.CompanyId,
            i.ClientId,
            i.DisplayName,
            i.IsEnabled,
            i.CreatedAt,
            i.LastTokenIssuedAt,
            i.Keys
                .OrderByDescending(k => k.IsActive)
                .ThenByDescending(k => k.AddedAt)
                .Select(k => new ErpSigningKeyDto(k.KeyId, k.Algorithm, k.AddedAt, k.RevokedAt, k.IsActive))
                .ToList());
}
