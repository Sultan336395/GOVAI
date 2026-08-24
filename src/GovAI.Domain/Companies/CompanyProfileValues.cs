using GovAI.Domain.Common;

namespace GovAI.Domain.Companies;

/// <summary>
/// Şirketin resmî sicil bilgileri. Kural motoruna girmez; başvuru dosyasında ve
/// mükerrer kayıt kontrolünde kullanılır.
///
/// Sahipli (owned) tip olarak <c>companies</c> tablosunda kolonlara açılır —
/// <see cref="Workforce"/> ve <see cref="Financials"/> ile aynı desen.
/// </summary>
public sealed record CompanyRegistry
{
    public static readonly CompanyRegistry Empty = new();

    public CompanyRegistry()
    {
    }

    public CompanyRegistry(
        string? shortName,
        string? taxOffice,
        string? mersisNumber,
        string? tradeRegistryNumber)
    {
        ShortName = Normalize(shortName);
        TaxOffice = Normalize(taxOffice);
        MersisNumber = Normalize(mersisNumber);
        TradeRegistryNumber = Normalize(tradeRegistryNumber);
    }

    /// <summary>Günlük kullanımdaki kısa ad; ekranlarda unvan yerine gösterilebilir.</summary>
    public string? ShortName { get; private set; }

    public string? TaxOffice { get; private set; }

    /// <summary>MERSİS numarası (16 hane). Doğrulaması servis katmanındadır.</summary>
    public string? MersisNumber { get; private set; }

    public string? TradeRegistryNumber { get; private set; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Şirketin iletişim ve merkez adres bilgileri.
///
/// Not: burada tutulan adres <b>merkez</b> adresidir. Operasyonel şubeler ve tesisler
/// ayrı tüzel kişilik olmadıkları için <see cref="CompanyLocation"/> ile tutulur;
/// böylece tüzel şirket ile fiziksel lokasyon kavramları karışmaz.
/// </summary>
public sealed record CompanyContact
{
    public static readonly CompanyContact Empty = new();

    public CompanyContact()
    {
    }

    public CompanyContact(
        string? website,
        string? phone,
        string? corporateEmail,
        string? country,
        string? city,
        string? address)
    {
        Website = Normalize(website);
        Phone = Normalize(phone);
        CorporateEmail = Normalize(corporateEmail)?.ToLowerInvariant();
        Country = Normalize(country);
        City = Normalize(city);
        Address = Normalize(address);
    }

    public string? Website { get; private set; }

    public string? Phone { get; private set; }

    public string? CorporateEmail { get; private set; }

    /// <summary>ISO ülke adı veya kodu. Şirket eklemede zorunlu alandır.</summary>
    public string? Country { get; private set; }

    public string? City { get; private set; }

    public string? Address { get; private set; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
