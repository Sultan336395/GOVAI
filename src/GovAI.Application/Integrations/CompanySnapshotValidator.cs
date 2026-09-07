using GovAI.Application.Reference;

namespace GovAI.Application.Integrations;

/// <summary>
/// ERP'den gelen şirket anlık görüntüsünün doğrulanması ve <b>arındırılması</b> (Faz 3).
///
/// <para>
/// Doğrulama iki iş yapar. Birincisi bilinen kontroller: sektör ve NACE katalogda var mı,
/// birbirini tutuyor mu, personel kırılımı toplamı aşıyor mu. İkincisi ve daha önemlisi
/// <b>veri minimizasyonu</b>: sözleşmede tanımlı olmayan hiçbir alan sisteme giremez.
/// </para>
///
/// <para>
/// Bu sınıf ağ ve veritabanı bilmez; saf karardır ve testten doğrudan çağrılır. Kararın
/// dışarıya bağımlı olmaması, "hangi veri kabul edilir" sorusunun tek bir yerde ve
/// okunabilir biçimde durmasını sağlar.
/// </para>
/// </summary>
public static class CompanySnapshotValidator
{
    /// <summary>Sözleşmede yeri olmayan, gelirse REDDEDİLEN alan adları.</summary>
    public static readonly IReadOnlySet<string> ForbiddenFields = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "firstName", "lastName", "fullName", "employeeName", "adSoyad",
        "nationalId", "tcKimlikNo", "tckn", "identityNumber",
        "salary", "wage", "netSalary", "grossSalary", "maas", "ucret",
        "iban", "bankAccount", "bankaHesabi",
        "healthData", "unionMembership", "criminalRecord", "religion", "biometric",
    };

    public sealed record Result(bool IsValid, string? Reason)
    {
        public static readonly Result Ok = new(true, null);

        public static Result Fail(string reason) => new(false, reason);
    }

    /// <summary>
    /// Gövdede sözleşme dışı alan var mı? Denetim ham JSON anahtarları üzerinde yapılır:
    /// tipli sözleşmede zaten yer yoktur ama gövde tipe bağlanmadan önce görülmelidir,
    /// aksi hâlde "yok sayıldı" ile "hiç gelmedi" ayırt edilemez ve KVKK ihlali sessizce
    /// geçer.
    /// </summary>
    public static Result CheckForbiddenFields(IEnumerable<string> incomingFieldNames)
    {
        var ihlal = incomingFieldNames.FirstOrDefault(ForbiddenFields.Contains);

        return ihlal is null
            ? Result.Ok
            : Result.Fail(
                $"'{ihlal}' alanı sözleşmede yoktur. MVP kapsamında kişisel çalışan verisi "
                + "aktarılmaz; istek tamamen reddedildi.");
    }

    /// <summary>Anlık görüntünün kendi içinde tutarlılığı.</summary>
    public static Result Validate(CompanySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.IsNullOrWhiteSpace(snapshot.ExternalCompanyId))
        {
            return Result.Fail("externalCompanyId zorunludur; eşleme onsuz kurulamaz.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.TaxNumber))
        {
            return Result.Fail("taxNumber zorunludur; eşlemenin doğru şirkete işaret ettiği onsuz doğrulanamaz.");
        }

        var sektor = ActivityCatalog.NormalizeSector(snapshot.MainSector);

        if (snapshot.MainSector is not null && sektor is null)
        {
            return Result.Fail(
                $"'{snapshot.MainSector}' katalogda yok. Sektör serbest metin değildir; "
                + "ERP tarafı katalog adını göndermelidir.");
        }

        foreach (var kod in snapshot.NaceCodes)
        {
            if (ActivityCatalog.NormalizeNace(kod) is null)
            {
                return Result.Fail($"'{kod}' geçerli bir NACE kodu değil.");
            }

            // Sektör ile kodun tutması panelde de zorunludur; ERP yolu bu kuralı
            // atlayamaz, aksi hâlde entegrasyon veri kalitesini bozan bir arka kapı olur.
            if (sektor is not null && !ActivityCatalog.NaceBelongsToSectors(kod, [sektor]))
            {
                return Result.Fail(
                    $"'{ActivityCatalog.NormalizeNace(kod)}' kodu '{ActivityCatalog.SectorOfNace(kod)}' "
                    + $"sektörüne aittir, bildirilen '{sektor}' sektörüne değil.");
            }
        }

        if (snapshot.EmployeeCount is < 0)
        {
            return Result.Fail("employeeCount negatif olamaz.");
        }

        if (snapshot.Employment is { } istihdam && snapshot.EmployeeCount is { } toplam)
        {
            foreach (var (ad, deger) in new (string, int?)[]
                     {
                         ("womenEmployeeCount", istihdam.WomenEmployeeCount),
                         ("youngEmployeeCount", istihdam.YoungEmployeeCount),
                         ("disabledEmployeeCount", istihdam.DisabledEmployeeCount),
                         ("rAndDEmployeeCount", istihdam.RAndDEmployeeCount),
                     })
            {
                if (deger is < 0)
                {
                    return Result.Fail($"{ad} negatif olamaz.");
                }

                if (deger > toplam)
                {
                    return Result.Fail($"{ad} ({deger}) toplam çalışan sayısını ({toplam}) aşamaz.");
                }
            }
        }

        if (snapshot.Financials is { AnnualRevenue: < 0 } or { BalanceSize: < 0 })
        {
            return Result.Fail("Mali tutarlar negatif olamaz.");
        }

        return Result.Ok;
    }
}
