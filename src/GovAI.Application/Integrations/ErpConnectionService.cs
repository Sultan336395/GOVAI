using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using GovAI.Domain.Integrations;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Integrations;

public sealed record UpsertErpConnectionRequest
{
    public required ErpVendor Vendor { get; init; }

    public required string BaseUrl { get; init; }

    public required ErpAuthMode AuthMode { get; init; }

    /// <summary>
    /// Kimlik bilgisi. Güncellemede boş bırakılırsa <b>mevcut kimlik korunur</b>.
    ///
    /// <para>
    /// Ekranın kayıtlı kimliği geri göstermesi gerekmesin diye böyle: gösterilen bir sır,
    /// ekran görüntüsüne ve tarayıcı geçmişine düşer.
    /// </para>
    /// </summary>
    public string? Secret { get; init; }

    /// <summary>ERP kurum ağının içinde mi? Özel IP'lere ancak bu beyanla gidilir.</summary>
    public bool IsOnPremise { get; init; }

    /// <summary>Alan eşlemesi. Boşsa üreticinin varsayılanı kullanılır.</summary>
    public ErpFieldMap? FieldMap { get; init; }
}

/// <summary>
/// Bağlantının panelde gösterilen hâli.
///
/// <para>
/// Kimlik bilgisi <b>hiçbir koşulda</b> dönmez; yalnızca tanımlı olup olmadığı bilinir.
/// </para>
/// </summary>
public sealed record ErpConnectionDto(
    Guid Id,
    Guid CompanyId,
    ErpVendor Vendor,
    string BaseUrl,
    ErpAuthMode AuthMode,
    bool HasSecret,
    bool IsOnPremise,
    bool IsEnabled,
    ErpFieldMap FieldMap,
    DateTimeOffset? LastRunAt,
    ErpSyncStatus LastRunStatus,
    string? LastRunMessage,
    int ConsecutiveFailureCount);

/// <summary>
/// ERP bağlantısının kurulması ve yönetimi.
///
/// <para>
/// Bağlantı kurmak profil verisini değiştirebilecek bir yetki devridir; bu yüzden
/// <see cref="CompanyPermission.ManageProfile"/> aranır. Analiz yapabilen bir kullanıcı
/// (uzman) ERP bağlantısı kuramaz.
/// </para>
/// </summary>
public sealed class ErpConnectionService(
    IErpConnectionRepository connections,
    IUnitOfWork unitOfWork,
    ISecretProtector protector,
    CompanyAccessGuard access,
    ILogger<ErpConnectionService> logger)
{
    public async Task<ErpConnectionDto?> GetAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        await access.LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);

        var connection = await connections.GetByCompanyAsync(companyId, cancellationToken);

        return connection is null ? null : ToDto(connection);
    }

    public async Task<ErpConnectionDto> UpsertAsync(
        Guid companyId,
        UpsertErpConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        await access.LoadAccessibleAsync(companyId, CompanyPermission.ManageProfile, cancellationToken);

        var esleme = request.FieldMap is null || request.FieldMap.IsEmpty
            ? null
            : System.Text.Json.JsonSerializer.Serialize(request.FieldMap);

        var mevcut = await connections.GetByCompanyAsync(companyId, cancellationToken);

        if (mevcut is not null)
        {
            mevcut.UpdateEndpoint(
                request.Vendor, request.BaseUrl, request.AuthMode, request.IsOnPremise, esleme);

            // Boş gönderim eskisini SİLMEZ: ekran kayıtlı kimliği geri göstermek zorunda
            // kalmasın diye. Gösterilen bir sır, ekran görüntüsüne ve geçmişe düşer.
            if (!string.IsNullOrWhiteSpace(request.Secret))
            {
                mevcut.ReplaceSecret(protector.Protect(request.Secret));
            }

            // Yeniden yapılandırma, durmuş bağlantıyı yeniden açar: kullanıcı sorunu
            // düzeltip kaydettiğinde ayrıca "etkinleştir" demek zorunda kalmamalı.
            mevcut.Enable();

            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "ERP bağlantısı güncellendi. CompanyId={CompanyId} Ürün={Vendor}",
                companyId, request.Vendor);

            return ToDto(mevcut);
        }

        if (string.IsNullOrWhiteSpace(request.Secret))
        {
            throw new DomainException("Yeni bağlantı için kimlik bilgisi zorunludur.");
        }

        var yeni = new ErpConnection(
            access.RequireTenant(),
            companyId,
            request.Vendor,
            request.BaseUrl,
            request.AuthMode,
            protector.Protect(request.Secret),
            request.IsOnPremise,
            esleme);

        await connections.AddAsync(yeni, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "ERP bağlantısı kuruldu. CompanyId={CompanyId} Ürün={Vendor}", companyId, request.Vendor);

        return ToDto(yeni);
    }

    public async Task SetEnabledAsync(
        Guid companyId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await access.LoadAccessibleAsync(companyId, CompanyPermission.ManageProfile, cancellationToken);

        var connection = await connections.GetByCompanyAsync(companyId, cancellationToken)
            ?? throw new NotFoundException("ERP bağlantısı", companyId);

        if (enabled)
        {
            connection.Enable();
        }
        else
        {
            connection.Disable();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Bağlantıyı siler.
    ///
    /// <para>
    /// Silme burada <b>vardır</b> — kalibrasyon kayıtlarının aksine. Sebebi: bu bir ölçüm
    /// kaydı değil, bir kimlik bilgisidir. Müşteri entegrasyonu sonlandırdığında
    /// kimliğinin sistemde kalmaması gerekir.
    /// </para>
    /// </summary>
    public async Task DeleteAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        await access.LoadAccessibleAsync(companyId, CompanyPermission.ManageProfile, cancellationToken);

        var connection = await connections.GetByCompanyAsync(companyId, cancellationToken)
            ?? throw new NotFoundException("ERP bağlantısı", companyId);

        connections.Remove(connection);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("ERP bağlantısı silindi. CompanyId={CompanyId}", companyId);
    }

    private static ErpConnectionDto ToDto(ErpConnection connection) => new(
        connection.Id,
        connection.CompanyId,
        connection.Vendor,
        connection.BaseUrl,
        connection.AuthMode,
        // Kimliğin kendisi değil, VARLIĞI bildirilir.
        HasSecret: !string.IsNullOrWhiteSpace(connection.ProtectedSecret),
        connection.IsOnPremise,
        connection.IsEnabled,
        Esleme(connection),
        connection.LastRunAt,
        connection.LastRunStatus,
        connection.LastRunMessage,
        connection.ConsecutiveFailureCount);

    /// <summary>Kayıtlı eşleme yoksa üreticinin varsayılanı gösterilir; ekran boş kalmaz.</summary>
    private static ErpFieldMap Esleme(ErpConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.FieldMapJson))
        {
            return ErpFieldMap.Default(connection.Vendor);
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ErpFieldMap>(
                connection.FieldMapJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? ErpFieldMap.Default(connection.Vendor);
        }
        catch (System.Text.Json.JsonException)
        {
            return ErpFieldMap.Default(connection.Vendor);
        }
    }
}
