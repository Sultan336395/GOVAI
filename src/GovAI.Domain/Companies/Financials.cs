using GovAI.Domain.Common;

namespace GovAI.Domain.Companies;

/// <summary>
/// Mali göstergeler. Muhasebe/ERP entegrasyonundan gelir ve mali yeterlilik skorunu besler.
/// </summary>
public sealed record Financials
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
    public static Financials Empty => new(0, 0, 0, 0, "TRY", null);

    public Financials(
        decimal annualRevenue,
        decimal balanceSize,
        decimal equity,
        decimal exportRevenue,
        string currency,
        int? fiscalYear)
    {
        DomainException.ThrowIf(annualRevenue < 0, "Yıllık ciro negatif olamaz.");
        DomainException.ThrowIf(balanceSize < 0, "Bilanço büyüklüğü negatif olamaz.");
        DomainException.ThrowIf(exportRevenue < 0, "İhracat cirosu negatif olamaz.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(currency), "Para birimi zorunludur.");

        AnnualRevenue = annualRevenue;
        BalanceSize = balanceSize;
        Equity = equity;
        ExportRevenue = exportRevenue;
        Currency = currency.ToUpperInvariant();
        FiscalYear = fiscalYear;
    }

    public decimal AnnualRevenue { get; init; }

    /// <summary><c>balanceSize</c> — aktif toplamı.</summary>
    public decimal BalanceSize { get; init; }

    /// <summary>Özkaynak; negatif özkaynak birçok destekte doğrudan eleme sebebidir.</summary>
    public decimal Equity { get; init; }

    public decimal ExportRevenue { get; init; }

    public string Currency { get; init; } = "TRY";

    /// <summary>Verinin ait olduğu mali yıl; eski veri skoru düşürür.</summary>
    public int? FiscalYear { get; init; }

    public decimal ExportRatio => AnnualRevenue == 0 ? 0m : Math.Round(ExportRevenue / AnnualRevenue, 4);

    public bool HasNegativeEquity => Equity < 0;
}
