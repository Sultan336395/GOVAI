using GovAI.Domain.Eligibility;

namespace GovAI.Domain.Tests;

/// <summary>
/// Alan adlarının kullanıcıya gösterilen hâli.
///
/// <para>
/// <see cref="CompanyFieldResolver.SupportedFields"/> açıklamaları geliştiriciye
/// yazılmıştır ve teknik ipuçları taşır. Haftalık raporda bunlar doğrudan risk satırının
/// konusu olarak göründü; eşleşme bulunamayan alanlarda ise iç tanımlayıcı
/// (<c>Company.Nuts2Codes</c>) göründü.
/// </para>
/// </summary>
public class AlanAdiTests
{
    [Theory(DisplayName = "AA1. Teknik ipucu kullanıcıya gösterilmez")]
    [InlineData("Company.Nuts2Codes", "İstatistiki bölge kodları")]
    [InlineData("Company.NaceCodes", "Firmanın NACE kodları")]
    [InlineData("Workforce.WomenEmployeeRate", "Kadın çalışan oranı")]
    [InlineData("Company.ExportFlag", "İhracat yapıyor mu")]
    [InlineData("Financials.AnnualRevenue", "Yıllık ciro")]
    public void Teknik_ipucu_gosterilmez(string alan, string beklenen)
    {
        Assert.Equal(beklenen, CompanyFieldResolver.Label(alan));
    }

    [Fact(DisplayName = "AA2. Hiçbir alan adında parantez veya 'ör.' kalmaz")]
    public void Hicbir_adda_teknik_iz_kalmaz()
    {
        foreach (var alan in CompanyFieldResolver.SupportedFields.Keys)
        {
            var ad = CompanyFieldResolver.Label(alan);

            Assert.DoesNotContain("(", ad, StringComparison.Ordinal);
            Assert.DoesNotContain("ör.", ad, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(ad));
        }
    }

    [Fact(DisplayName = "AA3. Tanınmayan alan için ad UYDURULMAZ")]
    public void Taninmayan_alan_icin_ad_uydurulmaz()
    {
        // Yanlış bir ad, kullanıcıyı profilinde olmayan bir alana gönderir.
        Assert.Equal("Boyle.BirAlanYok", CompanyFieldResolver.Label("Boyle.BirAlanYok"));
    }
}
