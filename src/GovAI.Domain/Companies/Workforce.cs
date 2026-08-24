using GovAI.Domain.Common;

namespace GovAI.Domain.Companies;

/// <summary>
/// Firmanın personel yapısı. İstihdam teşviklerinin büyük bölümü bu alanlar üzerinden filtrelenir.
/// Değişmez (immutable) bir değer nesnesidir; İK/ERP eşitlemesi her seferinde yenisini üretir.
/// </summary>
public sealed record Workforce
{
    /// <summary>
    /// Boş değer. Her çağrıda <b>yeni bir örnek</b> döner.
    ///
    /// Bilinçli olarak <c>static readonly</c> alan DEĞİLDİR: EF Core sahipli (owned)
    /// tipleri örnek kimliğine göre izler. Tek bir paylaşılan örnek iki farklı şirkete
    /// atandığında EF sahipliği karıştırır ve satırlardan birinin sütunlarını NULL yazar.
    /// Bu, finansal bilgisi girilmemiş ikinci şirket eklenirken
    /// "null value in column annual_revenue violates not-null constraint" hatası olarak
    /// ortaya çıkmıştı. Bellek içi sağlayıcı bu hatayı gizler; gerçek PostgreSQL yakalar.
    /// </summary>
    public static Workforce Empty => new(0, 0, 0, 0, 0);

    public Workforce(
        int employeeCount,
        int womenEmployeeCount,
        int youngEmployeeCount,
        int rAndDEmployeeCount,
        int disabledEmployeeCount)
    {
        DomainException.ThrowIf(employeeCount < 0, "Çalışan sayısı negatif olamaz.");
        DomainException.ThrowIf(
            womenEmployeeCount < 0 || youngEmployeeCount < 0 || rAndDEmployeeCount < 0 || disabledEmployeeCount < 0,
            "Personel kırılım sayıları negatif olamaz.");
        DomainException.ThrowIf(womenEmployeeCount > employeeCount, "Kadın çalışan sayısı toplam çalışan sayısını aşamaz.");
        DomainException.ThrowIf(youngEmployeeCount > employeeCount, "Genç çalışan sayısı toplam çalışan sayısını aşamaz.");
        DomainException.ThrowIf(rAndDEmployeeCount > employeeCount, "Ar-Ge personeli sayısı toplam çalışan sayısını aşamaz.");
        DomainException.ThrowIf(disabledEmployeeCount > employeeCount, "Engelli çalışan sayısı toplam çalışan sayısını aşamaz.");

        EmployeeCount = employeeCount;
        WomenEmployeeCount = womenEmployeeCount;
        YoungEmployeeCount = youngEmployeeCount;
        RAndDEmployeeCount = rAndDEmployeeCount;
        DisabledEmployeeCount = disabledEmployeeCount;
    }

    public int EmployeeCount { get; init; }

    public int WomenEmployeeCount { get; init; }

    /// <summary>29 yaş altı çalışan sayısı; genç istihdam teşviklerinde kullanılır.</summary>
    public int YoungEmployeeCount { get; init; }

    public int RAndDEmployeeCount { get; init; }

    public int DisabledEmployeeCount { get; init; }

    /// <summary><c>womenEmployeeRate</c> — 0..1 aralığında oran.</summary>
    public decimal WomenEmployeeRate => Ratio(WomenEmployeeCount);

    /// <summary><c>youngEmployeeRate</c> — 0..1 aralığında oran.</summary>
    public decimal YoungEmployeeRate => Ratio(YoungEmployeeCount);

    public decimal RAndDEmployeeRate => Ratio(RAndDEmployeeCount);

    private decimal Ratio(int part) => EmployeeCount == 0 ? 0m : Math.Round((decimal)part / EmployeeCount, 4);
}
