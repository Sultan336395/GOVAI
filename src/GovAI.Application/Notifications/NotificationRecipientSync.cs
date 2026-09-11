using GovAI.Domain.Notifications;

namespace GovAI.Application.Notifications;

/// <summary>Bir uzlaştırma turunun sonucu.</summary>
/// <param name="Added">Yeni açılan alıcı sayısı.</param>
/// <param name="Reactivated">Pasifken yeniden etkinleşen alıcı sayısı.</param>
/// <param name="Deactivated">ERP'den düştüğü için pasifleşen alıcı sayısı.</param>
/// <param name="Invalid">Adresi geçersiz olduğu için atlananlar; kullanıcıya bildirilir.</param>
public sealed record RecipientSyncResult(
    /// <summary>Depoya eklenmesi gereken YENİ kayıtlar. Uzlaştırıcı kendisi eklemez.</summary>
    IReadOnlyList<NotificationRecipient> Added,
    int Reactivated,
    int Deactivated,
    IReadOnlyList<string> Invalid)
{
    public bool Changed => Added.Count > 0 || Reactivated > 0 || Deactivated > 0;

    public static RecipientSyncResult None { get; } = new([], 0, 0, []);
}

/// <summary>ERP'den gelen bir alıcı satırı.</summary>
public sealed record IncomingRecipient(string Email, string? FullName, string? Role, string? ExternalId);

/// <summary>
/// ERP'den gelen bildirim sorumlularını mevcut kayıtlarla uzlaştırır.
///
/// <para>
/// Saftır: girdisi mevcut kayıtlar ile gelen listedir, içeride ne veri okunur ne tarih
/// alınır. Uzlaştırma kuralları bir ERP olmadan sınanabilir olmalıdır — sahada yanlış
/// uzlaştırmanın bedeli, bir firmanın sessizce bildirimsiz kalmasıdır.
/// </para>
/// </summary>
public static class NotificationRecipientSync
{
    /// <summary>
    /// Mevcut kayıtları gelen listeye göre günceller.
    ///
    /// <para>
    /// <paramref name="incoming"/> <c>null</c> ise <b>hiçbir şey yapılmaz</c>: bölüm
    /// eşlemede tanımlı değil ya da ERP yanıtında hiç yok demektir. Boş liste ise
    /// "artık sorumlu yok" demektir ve ERP kaynaklı kayıtlar pasifleşir. İkisini
    /// karıştırmak, geçici bir ERP arızasında bütün sorumluları silmek olurdu.
    /// </para>
    ///
    /// <para>
    /// <b>Elle tanımlanmış alıcılar ERP listesinde yoksa pasifleştirilmez.</b> Onlar
    /// ERP'sinde bu modül olmayan firmalar için bilinçli olarak girilmiştir; ERP'nin
    /// sessizliği onları silmek için gerekçe değildir. Ama aynı adres ERP'den de
    /// gelirse kayıt ERP'ye devredilir ve tek doğruluk kaynağı kalır.
    /// </para>
    /// </summary>
    public static RecipientSyncResult Reconcile(
        Guid tenantId,
        Guid companyId,
        IReadOnlyList<NotificationRecipient> existing,
        IReadOnlyList<IncomingRecipient>? incoming)
    {
        if (incoming is null)
        {
            return RecipientSyncResult.None;
        }

        var gecersiz = new List<string>();
        var gelenAdresler = new HashSet<string>(StringComparer.Ordinal);
        var eklenecekler = new List<NotificationRecipient>();
        var yenidenEtkin = 0;

        var mevcutlar = existing
            .GroupBy(r => r.Email, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var satir in incoming)
        {
            if (!NotificationRecipient.IsValidEmail(satir.Email))
            {
                // Tek bir bozuk satır turu düşürmez; atlanır ve kullanıcıya bildirilir.
                gecersiz.Add(satir.Email);
                continue;
            }

            var adres = NotificationRecipient.Normalize(satir.Email);

            if (!gelenAdresler.Add(adres))
            {
                // ERP aynı kişiyi iki kez listeleyebilir; ikinci satır yok sayılır.
                continue;
            }

            if (mevcutlar.TryGetValue(adres, out var kayit))
            {
                kayit.Update(satir.FullName, satir.Role, satir.ExternalId);
                kayit.ChangeSource(RecipientSource.ErpPull);

                if (!kayit.IsActive)
                {
                    kayit.Activate();
                    yenidenEtkin++;
                }

                continue;
            }

            eklenecekler.Add(new NotificationRecipient(
                tenantId, companyId, adres, satir.FullName, satir.Role,
                RecipientSource.ErpPull, satir.ExternalId));
        }

        var pasiflesen = 0;

        foreach (var kayit in existing.Where(r =>
                     r.IsActive
                     && r.Source == RecipientSource.ErpPull
                     && !gelenAdresler.Contains(r.Email)))
        {
            kayit.Deactivate();
            pasiflesen++;
        }

        return new RecipientSyncResult(eklenecekler, yenidenEtkin, pasiflesen, gecersiz);
    }
}
