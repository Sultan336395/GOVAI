using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Integrations;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Integrations;

/// <summary>ERP'nin sunduğu beyan. Tek alanı vardır; başka bir şey kabul edilmez.</summary>
public sealed record ErpTokenRequest
{
    /// <summary>ERP'nin kendi özel anahtarıyla imzaladığı kısa ömürlü JWT.</summary>
    public required string Assertion { get; init; }
}

public sealed record ErpTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresInSeconds,
    /// <summary>Jetonun konuşabileceği tek firma. ERP'nin doğru bağlandığını teyit eder.</summary>
    Guid CompanyId,
    string Scope);

/// <summary>
/// Beyanın imzasını doğrulayıp kısa ömürlü GOVAI jetonu üreten liman.
///
/// <para>
/// Kriptografi altyapıdadır; uygulama katmanı imza bilmez.
/// </para>
/// </summary>
public interface IErpAssertionReader
{
    /// <summary>Beyanı ayrıştırıp imzasını doğrular. İçerik kuralları burada uygulanmaz.</summary>
    (bool Ok, ErpAssertionRejection Rejection, string? Detail, ErpAssertionClaims? Claims) Read(
        string assertion,
        IReadOnlyList<ErpSigningKey> keys);

    /// <summary>Beyanın <c>iss</c> alanını imzayı doğrulamadan okur; kimliği bulmak için.</summary>
    string? PeekIssuer(string assertion);
}

/// <summary>ERP modülü jetonu üretir. Kullanıcı jetonundan ayrı tutulur.</summary>
public interface IErpTokenIssuer
{
    (string Token, DateTimeOffset ExpiresAt) Issue(
        Guid tenantId,
        Guid companyId,
        string clientId,
        string subject,
        string? subjectName,
        ErpPrincipalKind kind,
        DateTimeOffset now);
}

/// <summary>
/// ERP'nin imzalı beyanını kısa ömürlü GOVAI jetonuna çevirir.
///
/// <para>
/// Model bilinçli olarak <b>düz makine anahtarı değildir</b>. Paylaşılan bir sır
/// olsaydı: GOVAI onu saklamak zorunda kalır (yedek, log ve yapılandırmada sızabilir),
/// sızdığında süresiz kullanılabilir ve kimin kullandığı ayırt edilemezdi. Burada GOVAI
/// yalnızca <b>açık</b> anahtarı tutar; imzalayan taraf ERP'dir ve her istek en çok
/// beş dakikalık, tek kullanımlık bir beyanla yapılır.
/// </para>
///
/// <para>
/// Kiracı ve şirket <b>kimlik kaydından</b> okunur, beyandan değil. Beyandan okunsaydı
/// ERP kendi beyanına başka bir şirketin kimliğini yazıp o şirketin verisini alabilirdi.
/// </para>
///
/// <para>
/// Hata mesajları <b>ayrım yapmaz</b>: bilinmeyen istemci, devre dışı kimlik, bozuk imza
/// ve süresi dolmuş beyan dışarıya aynı cevabı verir. Ayırt edilseydi saldırgan hangi
/// istemci kimliklerinin var olduğunu ve nerede takıldığını öğrenirdi.
/// </para>
/// </summary>
public sealed class ErpTokenService(
    IErpServiceIdentityRepository identities,
    IErpAssertionReplayGuard replayGuard,
    IErpAssertionReader reader,
    IErpTokenIssuer issuer,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<ErpTokenService> logger)
{
    /// <summary>Dışarıya dönen tek hata metni. Ayrıntı yalnızca kayda yazılır.</summary>
    public const string GenericFailure = "Beyan kabul edilmedi.";

    /// <summary>Jetonun kapsamı. ERP modülü yalnızca okuma yapar.</summary>
    public const string Scope = "erp.module.read";

    public async Task<ErpTokenResponse> IssueAsync(
        ErpTokenRequest request,
        string expectedAudience,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var clientId = reader.PeekIssuer(request.Assertion);

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw Reddet(ErpAssertionRejection.MissingClaim, "Beyanda iss yok.", null);
        }

        var identity = await identities.GetByClientIdAsync(clientId, cancellationToken);

        if (identity is null)
        {
            // Bilinmeyen istemci ile devre dışı kimlik AYNI cevabı alır; aksi hâlde
            // hangi istemci kimliklerinin var olduğu sızardı.
            throw Reddet(ErpAssertionRejection.IssuerMismatch, "Bilinmeyen istemci.", clientId);
        }

        var anahtarlar = identity.ActiveKeys.ToList();

        var (imzaOk, imzaSebep, imzaAyrinti, claims) = reader.Read(request.Assertion, anahtarlar);

        if (!imzaOk || claims is null)
        {
            throw Reddet(imzaSebep, imzaAyrinti ?? "İmza doğrulanamadı.", clientId);
        }

        // Tekrar kontrolü, içerik kurallarından SONRA ve jeton üretiminden ÖNCE yapılır:
        // önce yapılsaydı geçersiz bir beyan da jti'yi harcar ve saldırgan meşru bir
        // beyanın jti'sini önceden "kullanılmış" yapabilirdi.
        var icerik = ErpAssertionPolicy.Evaluate(
            claims, identity.ClientId, expectedAudience, identity.IsEnabled, alreadySeen: false, now);

        if (!icerik.IsValid)
        {
            throw Reddet(icerik.Rejection, icerik.Detail ?? "Beyan içeriği kabul edilmedi.", clientId);
        }

        var ilkKullanim = await replayGuard.TryMarkUsedAsync(
            identity.ClientId, claims.TokenId, ErpAssertionPolicy.ReplayWindow, cancellationToken);

        if (!ilkKullanim)
        {
            throw Reddet(ErpAssertionRejection.Replayed, "Beyan tekrar kullanıldı.", clientId);
        }

        var (token, expiresAt) = issuer.Issue(
            identity.TenantId,
            identity.CompanyId,
            identity.ClientId,
            claims.Subject,
            claims.SubjectName,
            claims.Kind,
            now);

        identity.RecordTokenIssued(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "ERP jetonu verildi. Istemci={ClientId} Sirket={CompanyId} Tur={Kind} Ozne={Subject}",
            identity.ClientId, identity.CompanyId, claims.Kind, claims.Subject);

        return new ErpTokenResponse(
            token,
            "Bearer",
            (int)(expiresAt - now).TotalSeconds,
            identity.CompanyId,
            Scope);
    }

    /// <summary>
    /// Reddi kaydeder ve <b>ayrımsız</b> bir hata fırlatır.
    ///
    /// <para>
    /// Sebep koda yazılır çünkü operatörün "neden bağlanamıyor" sorusuna cevap vermesi
    /// gerekir; ama dışarıya sızmaz.
    /// </para>
    /// </summary>
    private ErpTokenRejectedException Reddet(
        ErpAssertionRejection sebep,
        string ayrinti,
        string? clientId)
    {
        logger.LogWarning(
            "ERP jetonu reddedildi. Sebep={Sebep} Istemci={ClientId} Ayrinti={Ayrinti}",
            sebep, clientId ?? "(bilinmiyor)", ayrinti);

        return new ErpTokenRejectedException(sebep);
    }
}

/// <summary>
/// Jeton isteği reddedildi.
///
/// <para>
/// <see cref="Reason"/> yalnızca kayıt ve test içindir; dışarıya
/// <see cref="ErpTokenService.GenericFailure"/> dönülür.
/// </para>
/// </summary>
public sealed class ErpTokenRejectedException(ErpAssertionRejection reason)
    : Exception(ErpTokenService.GenericFailure)
{
    public ErpAssertionRejection Reason { get; } = reason;
}
