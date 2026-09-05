using GovAI.Application.Opportunities;

namespace GovAI.Application.Tests;

/// <summary>
/// "Resmî kaynağa git" bağlantısının doğrulanması (Faz 2).
///
/// <para>
/// Bu düğme ürünün en yüksek güven vaadidir: kullanıcı ona bastığında resmî kurumun
/// kendi sayfasına gittiğini varsayar. Doğrulanamayan bir adres için düğme <b>hiç
/// gösterilmez</b>; "muhtemelen doğrudur" diye gösterilmez.
/// </para>
/// </summary>
public sealed class ResmiBaglantiTests
{
    private const string ResmiGazete = "resmigazete.gov.tr";

    // ═══════════════ Kabul edilen ═══════════════

    [Theory(DisplayName = "R1. Resmî alan adındaki adres doğrulanır")]
    [InlineData("https://www.resmigazete.gov.tr/eskiler/2026/08/20260827-1.htm")]
    [InlineData("https://resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-1.htm")]
    [InlineData("http://www.resmigazete.gov.tr/fihrist?tarih=2026-08-27")]
    public void Resmi_adres_dogrulanir(string adres)
    {
        var sonuc = OfficialLink.Verify(adres, ResmiGazete);

        Assert.True(sonuc.IsVerified);
        Assert.Equal(adres, sonuc.Url);
        Assert.Null(sonuc.RejectionReason);
    }

    [Fact(DisplayName = "R2. Virgülle ayrılmış alan adı listesindeki her biri kabul edilir")]
    public void Alan_adi_listesi_desteklenir()
    {
        // EUR-Lex içeriği AB Yayın Ofisi'nin CELLAR ucundan gelir.
        const string alanlar = "eur-lex.europa.eu, publications.europa.eu";

        Assert.True(OfficialLink.Verify("https://eur-lex.europa.eu/legal-content/TR/TXT/?uri=CELEX:32026R1965", alanlar).IsVerified);
        Assert.True(OfficialLink.Verify("https://publications.europa.eu/resource/celex/32026R1965", alanlar).IsVerified);
    }

    // ═══════════════ Reddedilen ═══════════════

    [Fact(DisplayName = "R3. Taklit alan adı reddedilir")]
    public void Taklit_alan_adi_reddedilir()
    {
        // Sonek karşılaştırması nokta sınırı denetlenmezse bunu KABUL ederdi.
        var sonuc = OfficialLink.Verify("https://resmigazete.gov.tr.kotu-site.com/ilan", ResmiGazete);

        Assert.False(sonuc.IsVerified);
        Assert.Null(sonuc.Url);
        Assert.Contains("resmî alan adında", sonuc.RejectionReason!, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "R4. Resmî olmayan alan adı kaynak sayılmaz")]
    [InlineData("https://ornek-blog.com/ihale-haberi")]
    [InlineData("https://www.google.com/search?q=ihale")]
    [InlineData("https://resmigazete.gov.tr.evil.io/x")]
    [InlineData("https://notresmigazete.gov.tr/x")]
    public void Resmi_olmayan_reddedilir(string adres)
    {
        Assert.False(OfficialLink.Verify(adres, ResmiGazete).IsVerified);
    }

    [Theory(DisplayName = "R5. http/https dışı şema reddedilir")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://resmigazete.gov.tr/dosya")]
    public void Guvensiz_sema_reddedilir(string adres)
    {
        Assert.False(OfficialLink.Verify(adres, ResmiGazete).IsVerified);
    }

    [Fact(DisplayName = "R6. Kaynağın resmî alan adı tanımlı değilse doğrulama yapılmaz")]
    public void Alan_adi_tanimli_degilse_dogrulanmaz()
    {
        var sonuc = OfficialLink.Verify("https://resmigazete.gov.tr/ilan", officialDomain: null);

        // Tahmin edilmez: doğrulanamayan bağlantı resmî sayılmaz.
        Assert.False(sonuc.IsVerified);
        Assert.Contains("doğrulanamıyor", sonuc.RejectionReason!, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "R7. Boş ve bozuk adres reddedilir")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bu bir adres değil")]
    [InlineData("/sadece/yol")]
    public void Bos_veya_bozuk_adres_reddedilir(string? adres)
    {
        var sonuc = OfficialLink.Verify(adres, ResmiGazete);

        Assert.False(sonuc.IsVerified);
        Assert.NotNull(sonuc.RejectionReason);
    }

    [Fact(DisplayName = "R8. Alt alan adı kabul edilir, üst alan adı edilmez")]
    public void Alt_alan_adi_kabul_ust_alan_reddedilir()
    {
        Assert.True(OfficialLink.Verify("https://arsiv.resmigazete.gov.tr/x", ResmiGazete).IsVerified);
        Assert.False(OfficialLink.Verify("https://gov.tr/x", ResmiGazete).IsVerified);
    }

    [Fact(DisplayName = "R9. Ret sebebi kullanıcıya söylenir, sessizce gizlenmez")]
    public void Ret_sebebi_bildirilir()
    {
        var sonuc = OfficialLink.Verify("https://baska-site.com/ilan", ResmiGazete);

        Assert.NotNull(sonuc.RejectionReason);
        Assert.Contains("baska-site.com", sonuc.RejectionReason, StringComparison.Ordinal);
    }
}
