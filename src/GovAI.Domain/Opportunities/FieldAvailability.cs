using GovAI.Domain.Common;

namespace GovAI.Domain.Opportunities;

/// <summary>
/// Bir alanın neden boş olduğu (Faz 2).
///
/// <c>null</c> tek başına iki farklı şeyi anlatamaz: "resmî kaynakta yazmıyor" ile
/// "bu çağrı için zaten geçerli değil" aynı değildir. Birincisi eksik veridir ve
/// tamamlanması gerekir; ikincisi doğru ve nihai bir cevaptır.
///
/// Eksik değer <b>tahmin edilmez</b>. Arayüz boş alan yerine
/// "Resmî kaynakta belirtilmemiş" gösterir.
/// </summary>
public enum FieldAvailability
{
    /// <summary>Değer resmî kaynaktan çıkarıldı.</summary>
    Provided = 0,

    /// <summary>Resmî kaynakta yazmıyor. Uydurulmaz.</summary>
    NotProvided = 1,

    /// <summary>Bu çağrı türü için anlamlı değil (ör. sürekli açık çağrıda son başvuru).</summary>
    NotApplicable = 2
}

/// <summary>
/// Fırsatın çıkarılamayan alanlarının kaydı (Faz 2).
///
/// Sahipli (owned) tip olarak <c>opportunities</c> tablosunda kolonlara açılır.
/// Yalnızca listelenen sekiz alan izlenir; hepsi fon/hibe/teşvik/ihale kayıtlarında
/// kullanıcı kararını doğrudan etkileyen alanlardır.
/// </summary>
public sealed record OpportunityFieldAvailability(
    FieldAvailability Deadline,
    FieldAvailability Budget,
    FieldAvailability Currency,
    FieldAvailability EligibleApplicant,
    FieldAvailability Geography,
    FieldAvailability Sector,
    FieldAvailability ProgrammeType,
    FieldAvailability OfficialDocumentUrl)
{
    /// <summary>Hiçbir alanın durumu bilinmiyorken kullanılan başlangıç değeri.</summary>
    public static OpportunityFieldAvailability Unknown => new(
        FieldAvailability.NotProvided,
        FieldAvailability.NotProvided,
        FieldAvailability.NotProvided,
        FieldAvailability.NotProvided,
        FieldAvailability.NotProvided,
        FieldAvailability.NotProvided,
        FieldAvailability.NotProvided,
        FieldAvailability.NotProvided);

    /// <summary>Resmî kaynakta bulunamayan alan sayısı.</summary>
    public int MissingCount =>
        new[]
        {
            Deadline, Budget, Currency, EligibleApplicant,
            Geography, Sector, ProgrammeType, OfficialDocumentUrl
        }.Count(f => f == FieldAvailability.NotProvided);

    /// <summary>Katalog için asgari veri var mı? Sekiz alanın tamamı eksikse yoktur.</summary>
    public bool HasAnyProvidedField => MissingCount < 8;
}
