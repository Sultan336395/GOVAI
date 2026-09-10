using GovAI.Domain.Reporting;

namespace GovAI.Domain.Tests;

/// <summary>
/// Rapor haftasının sınırı.
///
/// <para>
/// Hafta sınırı raporun tamamını belirler: hangi mevzuat değişikliğinin "bu hafta"
/// sayıldığı buradan çıkar. Sınırı UTC'ye göre çizmek, Pazartesi 02:00'de yayımlanan
/// bir Resmî Gazete kararını bir önceki haftaya yazardı — kullanıcı onu geçen haftanın
/// raporunda arar, bulamazdı.
/// </para>
/// </summary>
public class ReportWeekTests
{
    private static DateTimeOffset Turkiye(int yil, int ay, int gun, int saat = 12, int dakika = 0) =>
        new(yil, ay, gun, saat, dakika, 0, TimeSpan.FromHours(3));

    [Theory(DisplayName = "RW1. Haftanın her günü aynı Pazartesi–Pazar aralığını verir")]
    // 07.09.2026 Pazartesi … 13.09.2026 Pazar
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void Haftanin_her_gunu_ayni_araligi_verir(int gun)
    {
        var hafta = ReportWeek.Containing(Turkiye(2026, 9, gun));

        Assert.Equal(new DateOnly(2026, 9, 7), hafta.Start);
        Assert.Equal(new DateOnly(2026, 9, 13), hafta.End);
    }

    [Fact(DisplayName = "RW2. Pazar günü bir sonraki haftaya KAYMAZ")]
    public void Pazar_sonraki_haftaya_kaymaz()
    {
        // DayOfWeek Pazar için 0'dır; naif bir çıkarma Pazar'ı ertesi haftanın başı yapar.
        var hafta = ReportWeek.Containing(Turkiye(2026, 9, 13, 23, 30));

        Assert.Equal(new DateOnly(2026, 9, 7), hafta.Start);
    }

    [Fact(DisplayName = "RW3. Pazartesi 02:00 Türkiye saati YENİ haftaya girer")]
    public void Pazartesi_gece_yeni_haftaya_girer()
    {
        // Aynı an UTC'de Pazar 23:00'tır. UTC'ye göre çizilen bir sınır bunu geçen
        // haftaya yazar ve Resmî Gazete kararı yanlış raporda görünür.
        var an = Turkiye(2026, 9, 14, 2, 0);

        Assert.Equal(new DateOnly(2026, 9, 14), ReportWeek.Containing(an).Start);
        Assert.Equal(23, an.UtcDateTime.Hour);
        Assert.Equal(13, an.UtcDateTime.Day);
    }

    [Fact(DisplayName = "RW4. Tamamlanmış hafta, içinde bulunulan hafta DEĞİLDİR")]
    public void Tamamlanmis_hafta_gecen_haftadir()
    {
        var hafta = ReportWeek.CompletedBefore(Turkiye(2026, 9, 14, 7, 30));

        Assert.Equal(new DateOnly(2026, 9, 7), hafta.Start);
        Assert.Equal(new DateOnly(2026, 9, 13), hafta.End);
    }

    [Fact(DisplayName = "RW5. Dönem sonu DIŞLAYICIDIR: Pazar gecesi dahil, Pazartesi 00:00 hariç")]
    public void Donem_sonu_dislayicidir()
    {
        var hafta = ReportWeek.Containing(Turkiye(2026, 9, 9));

        Assert.True(hafta.Contains(Turkiye(2026, 9, 7, 0, 0)));
        Assert.True(hafta.Contains(Turkiye(2026, 9, 13, 23, 59)));
        Assert.False(hafta.Contains(Turkiye(2026, 9, 14, 0, 0)));
        Assert.False(hafta.Contains(Turkiye(2026, 9, 6, 23, 59)));
    }

    [Fact(DisplayName = "RW6. UTC aralığı Türkiye saatinden 3 saat geri kaydırılır")]
    public void Utc_araligi_dogru_kaydirilir()
    {
        var hafta = ReportWeek.Containing(Turkiye(2026, 9, 9));

        // Pazartesi 00:00 Türkiye = Pazar 21:00 UTC.
        Assert.Equal(new DateTime(2026, 9, 6, 21, 0, 0, DateTimeKind.Utc), hafta.StartUtc.UtcDateTime);
        Assert.Equal(new DateTime(2026, 9, 13, 21, 0, 0, DateTimeKind.Utc), hafta.EndUtcExclusive.UtcDateTime);
    }

    [Fact(DisplayName = "RW7. Girdi hangi saat diliminde verilirse verilsin aynı hafta çıkar")]
    public void Saat_dilimi_sonucu_degistirmez()
    {
        // Aynı an, üç ayrı gösterim. Determinizm gereği üçü de aynı haftayı vermeli.
        var an = Turkiye(2026, 9, 9, 12, 0);

        var a = ReportWeek.Containing(an);
        var b = ReportWeek.Containing(an.ToUniversalTime());
        var c = ReportWeek.Containing(an.ToOffset(TimeSpan.FromHours(-5)));

        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }
}
