using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using GovAI.Domain.Notifications;

namespace GovAI.Application.Notifications;

public sealed record NotificationRecipientDto(
    Guid Id,
    string Email,
    string? FullName,
    string? Role,
    RecipientSource Source,
    string SourceLabel,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record ReplaceRecipientsRequest
{
    /// <summary>Şirketin elle tanımlanan bildirim sorumluları.</summary>
    public required IReadOnlyList<RecipientInput> Recipients { get; init; }
}

public sealed record RecipientInput
{
    public required string Email { get; init; }

    public string? FullName { get; init; }

    public string? Role { get; init; }
}

/// <summary>
/// Bildirim sorumlularının panelden yönetimi.
///
/// <para>
/// Asıl yol ERP'dir: sorumlular şirketin kendi sisteminde tanımlanır ve gece turunda
/// çekilir. Bu servis <b>ERP'sinde bu modül olmayan</b> ya da henüz bağlantı kurmamış
/// firmalar içindir; olmasaydı o firmalara hiç e-posta gidemezdi.
/// </para>
///
/// <para>
/// Elle tanımlanan kayıtlar ERP turunda <b>silinmez</b>; ama aynı adres ERP'den de
/// gelirse kayıt ERP'ye devredilir ve tek doğruluk kaynağı kalır
/// (<see cref="NotificationRecipientSync"/>).
/// </para>
/// </summary>
public sealed class NotificationRecipientService(
    INotificationRecipientRepository recipients,
    IUnitOfWork unitOfWork,
    CompanyAccessGuard access)
{
    /// <summary>Bir şirkette tanımlanabilecek azami alıcı sayısı.</summary>
    public const int MaximumPerCompany = 25;

    public async Task<IReadOnlyList<NotificationRecipientDto>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        await access.LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);

        return (await recipients.ListForCompanyAsync(companyId, cancellationToken))
            .Select(ToDto)
            .ToList();
    }

    /// <summary>
    /// Elle tanımlı listeyi verilen listeyle değiştirir.
    ///
    /// <para>
    /// ERP kaynaklı kayıtlara <b>dokunulmaz</b>: onların doğruluk kaynağı şirketin
    /// kendi sistemidir ve panelden silinebilmeleri, gece turunda geri gelip
    /// kullanıcıyı "sildim ama duruyor" durumunda bırakırdı.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<NotificationRecipientDto>> ReplaceManualAsync(
        Guid companyId,
        ReplaceRecipientsRequest request,
        CancellationToken cancellationToken = default)
    {
        var company = await access.LoadAccessibleAsync(
            companyId, CompanyPermission.ManageProfile, cancellationToken);

        var gelen = request.Recipients ?? [];

        DomainException.ThrowIf(
            gelen.Count > MaximumPerCompany,
            $"En çok {MaximumPerCompany} bildirim sorumlusu tanımlanabilir.");

        foreach (var satir in gelen)
        {
            DomainException.ThrowIf(
                !NotificationRecipient.IsValidEmail(satir.Email),
                $"Geçersiz e-posta adresi: {satir.Email}");
        }

        // Gelen liste ÖNCE tekilleştirilir: aynı adres iki kez yazılırsa ikinci satır
        // ilkini "mevcut" sanıp henüz kaydedilmemiş bir kaydı arardı. İlk satır kazanır.
        var benzersiz = gelen
            .GroupBy(s => NotificationRecipient.Normalize(s.Email), StringComparer.Ordinal)
            .Select(g => (Adres: g.Key, Satir: g.First()))
            .ToList();

        var mevcut = await recipients.ListForCompanyAsync(companyId, cancellationToken);

        var istenen = benzersiz.Select(b => b.Adres).ToHashSet(StringComparer.Ordinal);

        // Yalnızca ELLE tanımlı kayıtlar bu listeye göre yönetilir.
        foreach (var kayit in mevcut.Where(r => r.Source == RecipientSource.Manual))
        {
            if (istenen.Contains(kayit.Email))
            {
                kayit.Activate();
            }
            else if (kayit.IsActive)
            {
                kayit.Deactivate();
            }
        }

        var mevcutlar = mevcut.ToDictionary(r => r.Email, StringComparer.Ordinal);

        foreach (var (adres, satir) in benzersiz)
        {
            if (mevcutlar.TryGetValue(adres, out var kayit))
            {
                kayit.Update(satir.FullName, satir.Role, null);
                kayit.Activate();

                continue;
            }

            await recipients.AddAsync(
                new NotificationRecipient(
                    company.TenantId, companyId, adres, satir.FullName, satir.Role,
                    RecipientSource.Manual),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ListAsync(companyId, cancellationToken);
    }

    private static NotificationRecipientDto ToDto(NotificationRecipient r) =>
        new(
            r.Id,
            r.Email,
            r.FullName,
            r.Role,
            r.Source,
            r.Source == RecipientSource.ErpPull ? "ERP'den geldi" : "Elle tanımlandı",
            r.IsActive,
            r.CreatedAt);
}
