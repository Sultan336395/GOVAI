using GovAI.Application.Abstractions.Persistence;
using GovAI.Domain.Integrations;
using Microsoft.EntityFrameworkCore;

namespace GovAI.Persistence.Repositories;

/// <summary>
/// ERP servis kimliklerinin veri erişimi.
///
/// <para>
/// <see cref="GetByClientIdAsync"/> kiracı süzgecini <b>aşar</b>: jeton isteği oturumsuz
/// gelir ve kiracı tam da bu sorgunun sonucundan öğrenilir. Sınır gevşemez — bulunan
/// kaydın kiracısı jetona yazılır ve sonraki her adım onunla sınırlanır.
/// </para>
/// </summary>
public sealed class ErpServiceIdentityRepository(GovAiDbContext context)
    : IErpServiceIdentityRepository
{
    public async Task<ErpServiceIdentity?> GetByClientIdAsync(
        string clientId,
        CancellationToken cancellationToken = default) =>
        await context.ErpServiceIdentities.IgnoreQueryFilters()
            .Include(i => i.Keys)
            .FirstOrDefaultAsync(i => i.ClientId == clientId, cancellationToken);

    public async Task<ErpServiceIdentity?> GetAsync(
        Guid identityId,
        CancellationToken cancellationToken = default) =>
        await context.ErpServiceIdentities
            .Include(i => i.Keys)
            .FirstOrDefaultAsync(i => i.Id == identityId, cancellationToken);

    public async Task<IReadOnlyList<ErpServiceIdentity>> ListForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        await context.ErpServiceIdentities
            .Include(i => i.Keys)
            .Where(i => i.CompanyId == companyId)
            .OrderByDescending(i => i.IsEnabled)
            .ThenBy(i => i.DisplayName)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(
        ErpServiceIdentity identity,
        CancellationToken cancellationToken = default) =>
        await context.ErpServiceIdentities.AddAsync(identity, cancellationToken);
}

/// <summary>
/// Tekrar önlemenin veritabanı karşılığı.
///
/// <para>
/// İki kat koruma vardır ve ikisi de gereklidir:
/// </para>
///
/// <list type="number">
///   <item>
///     <b>Benzersiz indeks</b> — asıl güvence. İki eşzamanlı istek aynı <c>jti</c> ile
///     gelirse biri kısıt ihlali alır. Yalnızca sorguya güvenilseydi, "önce bak sonra
///     yaz" arasındaki boşlukta ikisi de geçerdi.
///   </item>
///   <item>
///     <b>Ön sorgu</b> — sıradan tekrarı kısıt ihlali üretmeden yakalar ve testlerin
///     kullandığı bellek içi sağlayıcıda (indeks zorlanmaz) korumanın davranışını
///     sağlar. Bu olmasaydı koruma testlerde hiç sınanmamış olurdu.
///   </item>
/// </list>
/// </summary>
public sealed class ErpAssertionReplayGuard(GovAiDbContext context) : IErpAssertionReplayGuard
{
    /// <summary>Bir turda temizlenecek azami süresi dolmuş kayıt.</summary>
    private const int CleanupBatch = 200;

    public async Task<bool> TryMarkUsedAsync(
        string clientId,
        string tokenId,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var gorulmus = await context.ErpAssertionUses
            .AsNoTracking()
            .AnyAsync(u => u.ClientId == clientId && u.TokenId == tokenId, cancellationToken);

        if (gorulmus)
        {
            return false;
        }

        await TemizleAsync(now, cancellationToken);

        context.ErpAssertionUses.Add(
            new ErpAssertionUse(clientId, tokenId, now, now.Add(window)));

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException)
        {
            // Benzersiz indeks ihlali: ön sorgudan sonra başka bir istek öne geçti.
            context.ChangeTracker.Clear();

            return false;
        }
    }

    /// <summary>
    /// Süresi dolmuş kayıtları siler.
    ///
    /// <para>
    /// Burada yapılır çünkü ayrı bir bakım işi kurmak, bu tablonun unutulup sınırsız
    /// büyümesi demek olurdu. Toplu silme yerine sıradan silme kullanılır: toplu silme
    /// sağlayıcıya özgüdür ve testlerin bellek içi sağlayıcısında çalışmaz.
    /// </para>
    /// </summary>
    private async Task TemizleAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var eskiler = await context.ErpAssertionUses
            .Where(u => u.ExpiresAt < now)
            .Take(CleanupBatch)
            .ToListAsync(cancellationToken);

        if (eskiler.Count > 0)
        {
            context.ErpAssertionUses.RemoveRange(eskiler);
        }
    }
}
