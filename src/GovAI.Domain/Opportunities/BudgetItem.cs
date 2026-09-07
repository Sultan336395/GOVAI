using GovAI.Domain.Common;

namespace GovAI.Domain.Opportunities;

/// <summary>
/// Bir çağrı belgesinde geçen tutarın <b>türü</b> (Faz 3).
///
/// <para>
/// Tür bilinmeden tutar kullanıcıya sunulamaz. Gerçek KOSGEB belgesinde aynı sayfada
/// "geri ödemesiz destek üst limiti 1.500.000 TL" ve "İşletme Başına Kredi Üst Limiti:
/// 20.000.000 TL" yazıyor. Eski sürüm en büyüğünü alıp 20 milyonu <b>hibe</b> gibi
/// gösteriyordu. Kredi bir borçtur; hibe değildir ve karışması başvuru kararını
/// doğrudan yanlış yönlendirir.
/// </para>
/// </summary>
public enum BudgetItemType
{
    /// <summary>Türü belgeden kesin olarak çıkarılamadı; <b>hibe gibi gösterilemez</b>.</summary>
    Undetermined = 0,

    /// <summary>Programın tamamına ayrılan bütçe.</summary>
    TotalProgrammeBudget = 1,

    /// <summary>Geri ödemesiz destek üst limiti.</summary>
    GrantCeiling = 2,

    /// <summary>Kredi veya finansman üst limiti — borçtur, destek değildir.</summary>
    CreditCeiling = 3,

    /// <summary>Geri ödemeli destek tutarı.</summary>
    RepayableSupport = 4,

    /// <summary>Uygun harcama / uygun maliyet tutarı.</summary>
    EligibleExpenditure = 5
}

/// <summary>Bir yüzdenin türü. Metindeki her yüzde destek oranı değildir.</summary>
public enum BudgetRateType
{
    /// <summary>Bağlam kesin değil; destek oranı olarak gösterilemez.</summary>
    Undetermined = 0,

    /// <summary>Destek/hibe oranı.</summary>
    SupportRate = 1,

    /// <summary>
    /// Öz kaynak, ortaklık veya eş finansman payı. Bir <b>başvuru koşuludur</b>;
    /// destek oranı sanılırsa danışman hibe oranını yanlış hesaplar. Gerçek KOSGEB
    /// Girişimci Destek Programı metninde "ortaklık payı en az %50" cümlesi destek
    /// oranı diye okunuyordu.
    /// </summary>
    OwnContributionRate = 2
}

/// <summary>
/// Çağrı belgesinden çıkarılmış, türü belirlenmiş tek bir tutar.
///
/// <para>
/// Her kalem kendi <b>kanıt alıntısına ve karakter aralığına</b> bağlıdır: kullanıcı
/// tutarın belgede nerede yazdığını görebilir. Kanıtsız tutar gösterilmez.
/// </para>
/// </summary>
public class BudgetItem : Entity
{
    private BudgetItem()
    {
    }

    public BudgetItem(
        BudgetItemType type,
        decimal amount,
        string currency,
        string? excerpt,
        int startOffset,
        int endOffset)
    {
        DomainException.ThrowIf(amount < 0, "Bütçe tutarı negatif olamaz.");
        DomainException.ThrowIf(startOffset < 0 || endOffset < startOffset, "Kanıt aralığı geçersiz.");

        Type = type;
        Amount = amount;
        Currency = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.Trim().ToUpperInvariant();
        Excerpt = excerpt?[..Math.Min(excerpt.Length, 600)];
        StartOffset = startOffset;
        EndOffset = endOffset;
    }

    public Guid OpportunityId { get; private set; }

    public BudgetItemType Type { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "TRY";

    /// <summary>Tutarın belgede geçtiği cümle; gerekçe ekranında gösterilir.</summary>
    public string? Excerpt { get; private set; }

    public int StartOffset { get; private set; }

    public int EndOffset { get; private set; }

    /// <summary>Türü belirlenememiş kalem insan incelemesi ister.</summary>
    public bool NeedsReview => Type == BudgetItemType.Undetermined;
}

/// <summary>Çağrı belgesinden çıkarılmış, türü belirlenmiş tek bir yüzde.</summary>
public class BudgetRate : Entity
{
    private BudgetRate()
    {
    }

    public BudgetRate(BudgetRateType type, decimal rate, string? excerpt, int startOffset, int endOffset)
    {
        DomainException.ThrowIf(rate is < 0m or > 1m, "Oran 0 ile 1 arasında olmalıdır.");
        DomainException.ThrowIf(startOffset < 0 || endOffset < startOffset, "Kanıt aralığı geçersiz.");

        Type = type;
        Rate = rate;
        Excerpt = excerpt?[..Math.Min(excerpt.Length, 600)];
        StartOffset = startOffset;
        EndOffset = endOffset;
    }

    public Guid OpportunityId { get; private set; }

    public BudgetRateType Type { get; private set; }

    /// <summary>0..1 aralığında oran.</summary>
    public decimal Rate { get; private set; }

    public string? Excerpt { get; private set; }

    public int StartOffset { get; private set; }

    public int EndOffset { get; private set; }

    public bool NeedsReview => Type == BudgetRateType.Undetermined;
}
