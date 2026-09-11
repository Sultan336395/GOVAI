using GovAI.Domain.Common;

namespace GovAI.Application.Dashboard;

/// <summary>Aksiyonun aciliyeti. Sıralama bu alandan çıkar.</summary>
public enum DashboardActionPriority
{
    /// <summary>Süre kısıtı var; ertelenirse fırsat kapanır.</summary>
    Urgent = 1,

    /// <summary>Başvuruyu engelleyen bir eksik.</summary>
    High = 2,

    /// <summary>Bilgi eksikliği; kapatılmazsa karar belirsiz kalır.</summary>
    Normal = 3
}

/// <summary>
/// Ekranın en üstündeki tek iş satırı.
///
/// <para>
/// Her satır bir <b>yere</b> götürür (<see cref="Target"/>). Götürmeyen bir uyarı
/// kullanıcıyı "bir şey yapmalıyım ama nereden" durumunda bırakırdı.
/// </para>
/// </summary>
public sealed record DashboardActionDto(
    DashboardActionPriority Priority,
    string PriorityLabel,
    string Title,
    string Reason,
    string Target,
    DateTimeOffset? DueAt,
    int? DaysRemaining);

/// <summary>Profil doluluğu ve hangi alanın eksik olduğu.</summary>
public sealed record ProfileCompletenessDto(
    int Percentage,
    int KnownCount,
    int TotalCount,
    /// <summary>Eksik alanların kullanıcıya görünen adları.</summary>
    IReadOnlyList<string> MissingLabels,
    /// <summary>Eksik veri yüzünden kararı belirsiz kalan koşul sayısı.</summary>
    int DataGapCount);

/// <summary>
/// Fırsat hunisi.
///
/// <para>
/// Aşamalar daralan bir dizi değildir: "değerlendirilen" sistemin, "takibe alınan"
/// firmanın sayısıdır. Firma sistemin uygun görmediği bir ihaleyi de takibe alabilir,
/// bu yüzden alt basamak üst basamaktan büyük çıkabilir. Ekran bunu bir hata gibi
/// göstermemelidir.
/// </para>
/// </summary>
public sealed record OpportunityFunnelDto(
    int Evaluated,
    int Eligible,
    int ConditionallyEligible,
    int Tracked,
    int Submitted,
    int Won);

/// <summary>Bir haftanın özeti. Eğilim çizgisi bu noktalardan kurulur.</summary>
public sealed record TrendPointDto(
    DateOnly PeriodStart,
    int OpportunityCount,
    int TenderCount,
    int RiskCount,
    int UrgentDeadlineCount);

/// <summary>Son hareket akışındaki tek satır.</summary>
public sealed record ActivityItemDto(
    DateTimeOffset At,
    string Kind,
    string Title,
    string? Detail,
    string? Target,
    string? Actor);

/// <summary>Dashboard'un dört ek bölümü. Tek istekte gelir; ekran parça parça beklemez.</summary>
public sealed record DashboardInsightsDto(
    Guid CompanyId,
    IReadOnlyList<DashboardActionDto> Actions,
    ProfileCompletenessDto Profile,
    OpportunityFunnelDto Funnel,
    /// <summary>Eskiden yeniye haftalık noktalar. Rapor yoksa boştur.</summary>
    IReadOnlyList<TrendPointDto> Trend,
    IReadOnlyList<ActivityItemDto> Activity,
    /// <summary>Boş bölümlerin sebebi. Boşluk sessizce geçilmez.</summary>
    IReadOnlyList<string> Notes);

/// <summary>Aksiyon önceliğinin Türkçe karşılığı.</summary>
public static class DashboardActionLabels
{
    public static string Of(DashboardActionPriority priority) => priority switch
    {
        DashboardActionPriority.Urgent => "Acil",
        DashboardActionPriority.High => "Yüksek",
        _ => "Normal"
    };

    public static string Of(SupportCategory category) => category switch
    {
        SupportCategory.Tender => "İhale",
        _ => "Destek"
    };
}
