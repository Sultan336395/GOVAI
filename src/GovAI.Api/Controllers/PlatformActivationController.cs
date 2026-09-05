using GovAI.Api.Infrastructure;
using GovAI.Application.Identity;
using GovAI.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>Aktivasyon bağlantısı üretme isteği.</summary>
public sealed record CreatePlatformActivationBody(string Email, string FullName);

/// <summary>
/// Platform hesabının güvenli aktivasyonu (Faz 2).
///
/// <para>
/// <b>Yetki neden bir ortam sırrına dayanıyor:</b> platform rollerini kiracı
/// yöneticisi atayamaz (<c>AuthenticationService.EnsureAssignableByTenantAdmin</c>) ve
/// temiz bir kurulumda henüz hiçbir platform hesabı yoktur. Yani bu ucu koruyabilecek
/// bir oturum mevcut değildir — yumurta-tavuk. Çözüm, projenin başka yerinde de
/// kullanılan açık onay değişkeni desenidir (bkz. <c>GOVAI_EF_ALLOW_PRODUCTION</c>):
/// isteği yapan kişi sunucunun ortam değişkenini bilmek zorundadır.
/// </para>
///
/// <para>
/// Sır tanımlı değilse uç <b>tamamen kapalıdır</b>. Böylece varsayılan kurulumda
/// açıkta bir hesap açma yolu bulunmaz.
/// </para>
/// </summary>
[ApiController]
[Route("api/platform/activations")]
[Produces("application/json")]
public sealed class PlatformActivationController(
    PlatformActivationService service,
    IConfiguration configuration) : ControllerBase
{
    /// <summary>Bootstrap sırrının okunduğu ortam değişkeni.</summary>
    public const string BootstrapSecretKey = "GOVAI_PLATFORM_BOOTSTRAP_SECRET";

    /// <summary>İsteğin sırrı taşıdığı başlık.</summary>
    public const string BootstrapHeader = "X-GovAI-Platform-Bootstrap";

    /// <summary>
    /// <see cref="UserRole.PlatformReviewer"/> hesabı açar ve tek kullanımlık bir
    /// aktivasyon bağlantısı üretir.
    ///
    /// <para>
    /// Açık jeton <b>yalnızca bu yanıtta, bir kez</b> döner. Loglara, audit kaydına
    /// ve veritabanına yazılmaz; veritabanında yalnızca SHA-256 özeti durur.
    /// </para>
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [Audited("Platform.ActivationCreated", "AppUser")]
    public async Task<ActionResult<PlatformActivationResult>> Create(
        [FromBody] CreatePlatformActivationBody body,
        CancellationToken cancellationToken)
    {
        var beklenen = configuration[BootstrapSecretKey];

        if (string.IsNullOrWhiteSpace(beklenen))
        {
            // Sır tanımlı değilse uç yok sayılır: varsayılan kurulumda hesap açma
            // yolu açıkta bırakılmaz.
            return NotFound();
        }

        var gelen = Request.Headers[BootstrapHeader].ToString();

        if (!FixedTimeEquals(gelen, beklenen))
        {
            return Forbid();
        }

        var tenantId = await service.ResolveDefaultTenantAsync(cancellationToken);

        var sonuc = await service.CreateAsync(
            body.Email,
            UserRole.PlatformReviewer,
            tenantId,
            string.IsNullOrWhiteSpace(body.FullName) ? "Platform İnceleyicisi" : body.FullName,
            cancellationToken);

        return Ok(sonuc);
    }

    /// <summary>
    /// Bağlantı hâlâ kullanılabilir mi? Parola ekranı formu göstermeden önce sorar.
    /// Geçersiz jeton için hesap bilgisi sızdırılmaz.
    /// </summary>
    [HttpGet("{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<PlatformActivationStatus>> Status(
        string token,
        CancellationToken cancellationToken) =>
        Ok(await service.GetStatusAsync(token, cancellationToken));

    /// <summary>
    /// Aktivasyonu tamamlar: kullanıcı kendi parolasını belirler, hesap etkinleşir ve
    /// bağlantı bir daha kullanılamaz.
    /// </summary>
    [HttpPost("complete")]
    [AllowAnonymous]
    [Audited("Platform.ActivationCompleted", "AppUser")]
    public async Task<ActionResult<PlatformActivationStatus>> Complete(
        [FromBody] CompleteActivationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteAsync(request, cancellationToken));

    /// <summary>
    /// Sabit süreli karşılaştırma: sırrın uzunluğu ya da ilk karakterleri yanıt
    /// süresinden çıkarılamamalı.
    /// </summary>
    private static bool FixedTimeEquals(string? gelen, string beklenen)
    {
        if (string.IsNullOrEmpty(gelen))
        {
            return false;
        }

        var a = System.Text.Encoding.UTF8.GetBytes(gelen);
        var b = System.Text.Encoding.UTF8.GetBytes(beklenen);

        return a.Length == b.Length
               && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}
