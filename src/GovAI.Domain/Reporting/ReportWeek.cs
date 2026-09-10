namespace GovAI.Domain.Reporting;

/// <summary>
/// Raporun kapsadığı hafta: Pazartesi–Pazar, <b>Türkiye saatiyle</b>.
///
/// <para>
/// Hafta sınırı raporun tamamını belirler; hangi mevzuat değişikliğinin "bu hafta"
/// sayıldığı buradan çıkar. Sunucu UTC ile çalıştığı için sınırı UTC'ye göre çizmek,
/// Pazartesi 02:00'de yayımlanan bir Resmî Gazete kararını bir önceki haftaya
/// yazardı — kullanıcı onu geçen haftanın raporunda arar, bulamazdı.
/// </para>
///
/// <para>
/// Saat farkı sabit +03:00 yazılır. Türkiye 2016'dan beri yaz saati uygulamıyor ve
/// sabit değer, işletim sistemi saat dilimi veritabanına bağımlılığı ortadan kaldırır:
/// aynı girdi her makinede aynı haftayı verir (determinizm gereği).
/// </para>
/// </summary>
public readonly record struct ReportWeek
{
    /// <summary>Türkiye saat farkı. Yaz saati uygulanmadığı için sabittir.</summary>
    public static readonly TimeSpan TurkiyeFarki = TimeSpan.FromHours(3);

    private ReportWeek(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    /// <summary>Verilen anın içinde bulunduğu hafta.</summary>
    public static ReportWeek Containing(DateTimeOffset asOf)
    {
        var yerel = DateOnly.FromDateTime(asOf.ToOffset(TurkiyeFarki).DateTime);

        // Pazar günü DayOfWeek 0'dır; Pazartesi başlangıçlı haftada 6 gün geriye gider.
        var geriye = yerel.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)yerel.DayOfWeek - 1;
        var start = yerel.AddDays(-geriye);

        return new ReportWeek(start, start.AddDays(6));
    }

    /// <summary>
    /// Verilen andan önceki <b>tamamlanmış</b> hafta.
    ///
    /// <para>
    /// Haftalık rapor Pazartesi sabahı üretilir ve geçen haftayı anlatır. İçinde
    /// bulunulan haftayı raporlamak yarım veri sunmak olurdu.
    /// </para>
    /// </summary>
    public static ReportWeek CompletedBefore(DateTimeOffset asOf) =>
        Containing(asOf.AddDays(-7));

    /// <summary>Dönemin UTC karşılığı: veri sorguları bu aralıkla çalışır.</summary>
    public DateTimeOffset StartUtc =>
        new DateTimeOffset(Start.ToDateTime(TimeOnly.MinValue), TurkiyeFarki).ToUniversalTime();

    /// <summary>Dönem sonu <b>dışlayıcıdır</b>: Pazar 23:59:59.999 dahil, Pazartesi 00:00 hariç.</summary>
    public DateTimeOffset EndUtcExclusive =>
        new DateTimeOffset(End.AddDays(1).ToDateTime(TimeOnly.MinValue), TurkiyeFarki).ToUniversalTime();

    public bool Contains(DateTimeOffset moment) =>
        moment >= StartUtc && moment < EndUtcExclusive;

    public override string ToString() => $"{Start:dd.MM.yyyy} – {End:dd.MM.yyyy}";
}
