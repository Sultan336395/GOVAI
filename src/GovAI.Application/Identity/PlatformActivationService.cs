using System.Security.Cryptography;
using System.Text;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using GovAI.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Identity;

/// <summary>
/// Aktivasyon üretiminin sonucu. Açık jeton <b>yalnızca burada, bir kez</b> döner.
/// </summary>
public sealed record PlatformActivationResult(
    Guid ActivationId,
    Guid UserId,
    string Email,
    UserRole Role,
    DateTimeOffset ExpiresAt,

    /// <summary>
    /// Açık jeton. Veritabanında yalnızca özeti saklanır ve bu değer hiçbir yere
    /// yazılmaz — loglara, audit kaydına ve hata mesajlarına dahil edilmez.
    /// </summary>
    string Token,

    /// <summary>Hesap yeni mi açıldı, yoksa var olan hesaba yeni bağlantı mı üretildi?</summary>
    bool UserCreated);

/// <summary>Bağlantının kullanılabilir olup olmadığı; parola ekranı bunu sorar.</summary>
public sealed record PlatformActivationStatus(bool IsRedeemable, string? Email, string? Reason);

public sealed record CompleteActivationRequest(
    string Token,
    string Password,
    string PasswordConfirmation);

/// <summary>
/// Platform hesabının güvenli aktivasyonu (Faz 2).
///
/// <para>
/// Platform hesabına <b>parola atanmaz</b>: hesap parolasız ve pasif açılır, parolayı
/// yalnızca aktivasyon bağlantısını açan kişi belirler. Böylece kodda, seed verisinde,
/// veritabanında ya da logda hiçbir noktada bir parola bulunmaz.
/// </para>
///
/// <para>
/// Jetonun açık metni yalnızca üretim anında, bir kez döner; veritabanında SHA-256
/// özeti saklanır. Bağlantı 24 saat geçerlidir ve tek kullanımlıktır.
/// </para>
/// </summary>
public sealed class PlatformActivationService(
    IUserRepository users,
    IPlatformActivationRepository activations,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<PlatformActivationService> logger)
{
    /// <summary>
    /// Hesabı (yoksa) açar ve yeni bir aktivasyon bağlantısı üretir.
    ///
    /// <para>
    /// Aynı e-posta için <b>mükerrer hesap açılmaz</b>: var olan hesap yeniden
    /// kullanılır. Var olan geçerli bağlantılar iptal edilir; aynı anda birden çok
    /// bağlantı dolaşımda kalmamalıdır.
    /// </para>
    /// </summary>
    public async Task<PlatformActivationResult> CreateAsync(
        string email,
        UserRole role,
        Guid tenantId,
        string fullName,
        CancellationToken cancellationToken = default)
    {
        if ((int)role < 10)
        {
            throw new ValidationException(
                nameof(role),
                "Bu mekanizma yalnızca platform rolleri içindir; kiracı rolleri normal "
                + "kullanıcı yönetiminden verilir.");
        }

        var normalized = email.Trim().ToLowerInvariant();
        var now = clock.UtcNow;

        var user = await users.GetByEmailAsync(normalized, cancellationToken);
        var created = false;

        if (user is null)
        {
            // Parolasız ve PASİF açılır: aktivasyon tamamlanana kadar giriş yapılamaz.
            user = new AppUser(tenantId, normalized, fullName, role);
            user.Deactivate();

            await users.AddAsync(user, cancellationToken);
            created = true;
        }
        else if (user.Role != role)
        {
            throw new ValidationException(
                nameof(email),
                $"'{normalized}' zaten farklı bir rolle kayıtlı ({user.Role}). "
                + "Rol değişikliği aktivasyon bağlantısıyla yapılmaz.");
        }

        // Dolaşımdaki eski bağlantılar geçersizleşir.
        foreach (var eski in await activations.ListActiveForUserAsync(user.Id, cancellationToken))
        {
            eski.Revoke(now);
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var activation = new PlatformActivation(user.Id, normalized, role, HashToken(token), now);
        await activations.AddAsync(activation, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Jeton LOGLANMAZ. Yalnızca hangi hesap için üretildiği kayda geçer.
        logger.LogInformation(
            "Platform aktivasyon bağlantısı üretildi. Email={Email} Rol={Rol} Geçerlilik={Gecerlilik}",
            normalized, role, activation.ExpiresAt);

        return new PlatformActivationResult(
            activation.Id, user.Id, normalized, role, activation.ExpiresAt, token, created);
    }

    /// <summary>
    /// Bağlantı hâlâ kullanılabilir mi? Parola ekranı formu göstermeden önce sorar.
    ///
    /// Geçersiz jeton için hesap bilgisi <b>sızdırılmaz</b>: yalnızca kullanılamaz
    /// olduğu söylenir.
    /// </summary>
    public async Task<PlatformActivationStatus> GetStatusAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var activation = await activations.GetByTokenHashAsync(HashToken(token), cancellationToken);

        if (activation is null)
        {
            return new PlatformActivationStatus(false, null, "Bağlantı geçersiz.");
        }

        if (!activation.IsRedeemable(clock.UtcNow))
        {
            return new PlatformActivationStatus(
                false, null, "Bağlantının süresi dolmuş ya da daha önce kullanılmış.");
        }

        return new PlatformActivationStatus(true, activation.Email, null);
    }

    /// <summary>
    /// Aktivasyonu tamamlar: parolayı belirler, hesabı etkinleştirir ve bağlantıyı
    /// tek kullanımlık olarak damgalar.
    /// </summary>
    public async Task<PlatformActivationStatus> CompleteAsync(
        CompleteActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Password, request.PasswordConfirmation, StringComparison.Ordinal))
        {
            throw new ValidationException(
                nameof(request.PasswordConfirmation), "Parolalar eşleşmiyor.");
        }

        if (!PasswordPolicy.IsStrong(request.Password, out var gerekce))
        {
            throw new ValidationException(nameof(request.Password), gerekce);
        }

        var activation = await activations.GetByTokenHashAsync(HashToken(request.Token), cancellationToken)
                         ?? throw new ValidationException(nameof(request.Token), "Bağlantı geçersiz.");

        var now = clock.UtcNow;

        if (!activation.IsRedeemable(now))
        {
            throw new ValidationException(
                nameof(request.Token),
                "Bağlantının süresi dolmuş ya da daha önce kullanılmış.");
        }

        var user = await users.GetForActivationAsync(activation.UserId, cancellationToken)
                   ?? throw new ValidationException(nameof(request.Token), "Bağlantı geçersiz.");

        user.SetPasswordHash(passwordHasher.Hash(request.Password));

        // Rol burada TEYİT edilir: kayıt açıldıktan sonra rolü değiştirilmiş olsa bile
        // aktivasyonun vaat ettiği rol geçerlidir ve yalnızca odur.
        user.ChangeRole(activation.Role);
        user.Activate();

        activation.Activate(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Platform hesabı etkinleştirildi. Email={Email} Rol={Rol}", activation.Email, activation.Role);

        return new PlatformActivationStatus(true, activation.Email, null);
    }

    /// <summary>
    /// Platform hesabının bağlanacağı kiracı.
    ///
    /// Platform kullanıcıları bir kiracıya ait değildir ama <c>AppUser</c> şemada
    /// kiracı kimliği taşır (worker hesabı da aynı yolu izler). Tek kiracılı
    /// kurulumda o kiracı, çoklu kurulumda ilk oluşturulan kiracı kullanılır.
    /// </summary>
    public async Task<Guid> ResolveDefaultTenantAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = await users.GetAnyTenantIdAsync(cancellationToken);

        return tenantId
               ?? throw new ValidationException(
                   "tenant",
                   "Sistemde kiracı bulunmuyor; platform hesabı açılamaz. "
                   + "Önce başlangıç verisi yüklenmelidir.");
    }

    /// <summary>Jeton özeti; açık metin hiçbir zaman saklanmaz.</summary>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));
}
