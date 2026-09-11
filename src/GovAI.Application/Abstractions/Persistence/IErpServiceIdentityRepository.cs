using GovAI.Domain.Integrations;

namespace GovAI.Application.Abstractions.Persistence;

/// <summary>
/// ERP servis kimliklerinin veri erişimi.
///
/// <para>
/// <see cref="GetByClientIdAsync"/> kiracı süzgecini <b>aşar</b> ve aşmak zorundadır:
/// jeton isteği kimliksiz gelir, oturum yoktur, dolayısıyla kiracı bilinmez. Kiracı
/// tam da bu sorgunun <b>sonucundan</b> öğrenilir. Sınır gevşemez — bulunan kaydın
/// kiracısı ve şirketi jetona yazılır ve sonraki her adım onunla sınırlanır.
/// </para>
/// </summary>
public interface IErpServiceIdentityRepository
{
    /// <summary>Beyanın <c>iss</c> alanındaki istemci kimliğiyle arar; anahtarlarıyla döner.</summary>
    Task<ErpServiceIdentity?> GetByClientIdAsync(
        string clientId,
        CancellationToken cancellationToken = default);

    Task<ErpServiceIdentity?> GetAsync(Guid identityId, CancellationToken cancellationToken = default);

    /// <summary>Bir firmanın tanımlı kimlikleri. Yönetim ekranı için.</summary>
    Task<IReadOnlyList<ErpServiceIdentity>> ListForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task AddAsync(ErpServiceIdentity identity, CancellationToken cancellationToken = default);
}

/// <summary>
/// Kullanılmış beyan kimliklerinin (<c>jti</c>) kısa ömürlü kaydı.
///
/// <para>
/// Tekrar saldırısını bu engeller: ağdan yakalanan bir beyan, ömrü dolana kadar
/// sınırsız kez kullanılabilirdi. Kayıtlar beyan ömründen biraz uzun tutulur ve
/// kendiliğinden düşer; kalıcı saklamanın bir faydası yoktur.
/// </para>
/// </summary>
public interface IErpAssertionReplayGuard
{
    /// <summary>
    /// Beyanı kullanılmış olarak işaretler.
    /// </summary>
    /// <returns>
    /// Daha önce kullanılmamışsa <c>true</c>. <c>false</c> dönerse beyan tekrardır.
    /// İşaretleme ve kontrol <b>tek adımdır</b>: ayrı olsaydı iki eşzamanlı istek
    /// aradaki boşlukta ikisi de "görülmemiş" cevabını alırdı.
    /// </returns>
    Task<bool> TryMarkUsedAsync(
        string clientId,
        string tokenId,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}
