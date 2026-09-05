using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Tests;

/// <summary>
/// Fırsat kaydının yeniden yakalanışta tazelenmesi (Faz 2).
///
/// <para>
/// Aynı kaynak belge yeniden ayrıştırıldığında düzelen alanların katalogda da
/// düzelmesi gerekir. Başlık ve kurum yalnızca kayıt AÇILIRKEN yazılsaydı, bir
/// ayrıştırıcı düzeltmesi kullanıcıya hiç ulaşmazdı.
/// </para>
///
/// <para>
/// Ters yön de korunur: boş bir değer bilinen doğru değeri <b>silmez</b>. Bir
/// sonraki yakalanışta alan çıkarılamadıysa eldeki bilgi kaybedilmemelidir.
/// </para>
/// </summary>
public sealed class OpportunityTests
{
    private static Opportunity Firsat() => new(
        Guid.CreateVersion7(),
        SourceType.OfficialGazette,
        SupportCategory.Tender,
        "TAŞINMAZ SATILACAKTIR",
        "Resmî Gazete İhale İlanları",
        DateTimeOffset.UtcNow.AddDays(-1));

    [Fact(DisplayName = "O1. Kurum yeni yakalanıştan tazelenir")]
    public void Kurum_tazelenir()
    {
        var firsat = Firsat();

        firsat.RefreshPublisher("Çay İşletmeleri Genel Müdürlüğünden");

        Assert.Equal("Çay İşletmeleri Genel Müdürlüğünden", firsat.Publisher);
    }

    [Theory(DisplayName = "O2. Boş kurum mevcut değeri silmez")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Bos_kurum_silmez(string? bos)
    {
        var firsat = Firsat();

        firsat.RefreshPublisher(bos);

        Assert.Equal("Resmî Gazete İhale İlanları", firsat.Publisher);
    }

    [Fact(DisplayName = "O3. Kurum adının baştaki ve sondaki boşlukları kırpılır")]
    public void Kurum_kirpilir()
    {
        var firsat = Firsat();

        firsat.RefreshPublisher("  TCDD 3. Bölge Müdürlüğünden  ");

        Assert.Equal("TCDD 3. Bölge Müdürlüğünden", firsat.Publisher);
    }

    [Theory(DisplayName = "O4. Boş başlık mevcut başlığı silmez")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Bos_baslik_silmez(string? bos)
    {
        var firsat = Firsat();

        firsat.RefreshTitle(bos);

        Assert.Equal("TAŞINMAZ SATILACAKTIR", firsat.Title);
    }

    [Fact(DisplayName = "O5. Süresi geçmiş çağrı açık sayılmaz")]
    public void Suresi_gecmis_acik_sayilmaz()
    {
        var firsat = Firsat();
        var simdi = DateTimeOffset.UtcNow;

        firsat.SetSchedule(simdi.AddDays(-10), simdi.AddDays(-3));

        Assert.False(firsat.IsOpenOn(simdi));
        Assert.True(firsat.DaysUntilDeadline(simdi) < 0);
    }

    [Fact(DisplayName = "O6. Son başvurusu olmayan çağrı sürekli açıktır")]
    public void Son_basvurusu_olmayan_acik()
    {
        var firsat = Firsat();

        firsat.SetSchedule(DateTimeOffset.UtcNow.AddDays(-1), deadline: null);

        Assert.True(firsat.IsOpenOn(DateTimeOffset.UtcNow));
        Assert.Null(firsat.DaysUntilDeadline(DateTimeOffset.UtcNow));
    }
}
