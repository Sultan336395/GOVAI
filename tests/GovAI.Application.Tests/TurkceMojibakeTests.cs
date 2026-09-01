using System.Text;
using GovAI.Application.Common;

namespace GovAI.Application.Tests;

/// <summary>
/// Bozuk Türkçe başlığın görüntüleme onarımı (Faz 2).
///
/// <para>
/// Karantina ekranı inceleyiciye kaydın ne olduğunu göstermek zorundadır.
/// "ARTIRMA, EKSÝLTME VE ÝHALE ÝLÂNLARI" gösteren bir satır hiçbir karar verdirmez.
/// </para>
///
/// <para>
/// Onarım <b>yalnızca görüntülemedir</b>; ham belge, sürümleri ve özeti kanıt olarak
/// olduğu gibi kalır. Bu yüzden kural ihtiyatlıdır: kesin olarak daha iyi bir sonuç
/// üretemiyorsa metne dokunmaz.
/// </para>
/// </summary>
public sealed class TurkceMojibakeTests
{
    static TurkceMojibakeTests() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Doğru metni verilen kümede yazıp ISO-8859-1 sanan bir okuyucuyu taklit eder.</summary>
    private static string Boz(string dogru, string kume) =>
        Encoding.Latin1.GetString(Encoding.GetEncoding(kume).GetBytes(dogru));

    // ═══════════════ Gerçek bozulma örnekleri ═══════════════

    [Fact(DisplayName = "M1. Windows-1254 gövde ISO-8859-1 sanılmışsa onarılır")]
    public void Windows1254_bozulmasi_onarilir()
    {
        const string dogru = "ARTIRMA, EKSİLTME VE İHALE İLÂNLARI";
        var bozuk = Boz(dogru, "windows-1254");

        Assert.NotEqual(dogru, bozuk);
        Assert.Equal(dogru, TurkceMojibake.Onar(bozuk));
    }

    [Fact(DisplayName = "M2. UTF-8 gövde tek baytlı sanılmışsa onarılır")]
    public void Utf8_bozulmasi_onarilir()
    {
        const string dogru = "İş güvenliği ve şüpheli ödeme: ğüçöşı";
        var bozuk = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(dogru));

        Assert.NotEqual(dogru, bozuk);
        Assert.Equal(dogru, TurkceMojibake.Onar(bozuk));
    }

    [Theory(DisplayName = "M3. Resmî belge başlıkları onarılır")]
    [InlineData("CUMHURBAŞKANI KARARI")]
    [InlineData("YÖNETMELİK")]
    [InlineData("TEBLİĞ")]
    [InlineData("Karar Sayısı: 11646")]
    [InlineData("ANAYASA MAHKEMESİ KARARI")]
    [InlineData("Sanayi ve Teknoloji Bakanlığından")]
    public void Resmi_basliklar_onarilir(string dogru)
    {
        Assert.Equal(dogru, TurkceMojibake.Onar(Boz(dogru, "windows-1254")));
        Assert.Equal(
            dogru,
            TurkceMojibake.Onar(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(dogru))));
    }

    // ═══════════════ İhtiyat: sağlam metne dokunulmaz ═══════════════

    [Theory(DisplayName = "M4. Sağlam metin aynen döner")]
    [InlineData("ARTIRMA, EKSİLTME VE İHALE İLÂNLARI")]
    [InlineData("Küçük ve Orta Ölçekli İşletmeleri Geliştirme Başkanlığı")]
    [InlineData("2026/1 sayılı Tebliğ — ihracat desteği (%50)")]
    [InlineData("Resmî Gazete Sayı : 33353")]
    [InlineData("Regulation (EU) 2026/1965")]
    [InlineData("")]
    public void Saglam_metin_degismez(string metin)
    {
        Assert.Equal(metin, TurkceMojibake.Onar(metin));
    }

    [Fact(DisplayName = "M5. Onarım daha iyi değilse özgün metin korunur")]
    public void Daha_iyi_degilse_ozgun_korunur()
    {
        // Şüpheli karakter içeriyor ama hiçbir aday bunu iyileştiremez.
        const string metin = "Belge ▓▓▓ okunamadı";

        Assert.Equal(metin, TurkceMojibake.Onar(metin));
    }

    [Fact(DisplayName = "M6. null ve boşluk çökertmez")]
    public void Bos_deger_cokertmez()
    {
        Assert.Equal(string.Empty, TurkceMojibake.Onar(null));
        Assert.Equal("   ", TurkceMojibake.Onar("   "));
    }

    // ═══════════════ Kararlılık ═══════════════

    [Fact(DisplayName = "M7. Onarılmış metnin tekrar onarılması değiştirmez")]
    public void Idempotent()
    {
        const string dogru = "İHALE İLÂNLARI — şüpheli ödeme";
        var birinci = TurkceMojibake.Onar(Boz(dogru, "windows-1254"));

        Assert.Equal(birinci, TurkceMojibake.Onar(birinci));
        Assert.Equal(dogru, birinci);
    }

    // ═══════════════ Bozukluk tespiti ═══════════════

    [Fact(DisplayName = "M8. Bozuk metin işaretlenebilir")]
    public void Bozukluk_tespit_edilir()
    {
        Assert.True(TurkceMojibake.Bozuk(Boz("İHALE İLÂNLARI", "windows-1254")));
        Assert.False(TurkceMojibake.Bozuk("İHALE İLÂNLARI"));
        Assert.False(TurkceMojibake.Bozuk(null));
    }
}
