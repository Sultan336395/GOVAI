using System.Text;
using GovAI.Infrastructure.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovAI.Application.Tests;

/// <summary>
/// Türkçe karakterlerin bozulmaması.
///
/// <para>
/// Resmî Gazete sayfaları karakter kümesini HTTP başlığında DEĞİL
/// <c>&lt;meta&gt;</c> etiketinde bildirir (Windows-1254). İndirici yalnızca başlığa
/// baktığı için UTF-8 varsayıyor ve başlık
/// "ARTIRMA, EKS?LTME VE ?HALE ?L?NLARI" olarak kaydediliyordu.
/// </para>
///
/// <para>
/// Bozuk metin <b>resmî kanıt olarak kullanılamaz</b>: belge sonradan doğru
/// okunamaz ve kanıt zinciri kırılır. Bu yüzden güvenle çözülemeyen içerik
/// kaydedilmez.
/// </para>
/// </summary>
public sealed class TurkceKarakterTests
{
    private const string TurkceBaslik = "ARTIRMA, EKSİLTME VE İHALE İLÂNLARI";

    private static SafeDocumentDownloader Indirici() =>
        new(new SahteHttpClientFactory(), NullLogger<SafeDocumentDownloader>.Instance);

    private static byte[] Sayfa(string charsetMeta, Encoding encoding, string baslik)
    {
        var html =
            "<html><head>" + charsetMeta +
            $"<title>{baslik}</title></head><body>" +
            "Çağrı metni: iş güvenliği, şüpheli ödeme, ğüçöşı." +
            "</body></html>";

        return encoding.GetBytes(html);
    }

    static TurkceKarakterTests() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // ═══════════════ Çözümleme ═══════════════

    [Fact(DisplayName = "TR1. Windows-1254 meta etiketiyle bildirilirse doğru çözülür")]
    public void Windows1254_meta_ile_cozulur()
    {
        var encoding = Encoding.GetEncoding("windows-1254");
        var bytes = Sayfa("<meta charset=\"Windows-1254\">", encoding, TurkceBaslik);

        var metin = SafeDocumentDownloader.CozumleTest(bytes, charset: null);

        Assert.NotNull(metin);
        Assert.Contains(TurkceBaslik, metin, StringComparison.Ordinal);
        Assert.DoesNotContain('�', metin);
    }

    [Fact(DisplayName = "TR2. ISO-8859-9 de doğru çözülür")]
    public void Iso88599_cozulur()
    {
        var encoding = Encoding.GetEncoding("iso-8859-9");
        var bytes = Sayfa("<meta charset=\"ISO-8859-9\">", encoding, TurkceBaslik);

        var metin = SafeDocumentDownloader.CozumleTest(bytes, charset: null);

        Assert.NotNull(metin);
        Assert.Contains(TurkceBaslik, metin, StringComparison.Ordinal);
        Assert.DoesNotContain('�', metin);
    }

    [Fact(DisplayName = "TR3. UTF-8 sayfa aynen çözülür")]
    public void Utf8_cozulur()
    {
        var bytes = Sayfa("<meta charset=\"utf-8\">", Encoding.UTF8, TurkceBaslik);

        var metin = SafeDocumentDownloader.CozumleTest(bytes, charset: "utf-8");

        Assert.NotNull(metin);
        Assert.Contains(TurkceBaslik, metin, StringComparison.Ordinal);
        Assert.DoesNotContain('�', metin);
    }

    [Fact(DisplayName = "TR4. Hiç bildirim yoksa Türkçe kümeler denenir")]
    public void Bildirim_yoksa_turkce_kumeler_denenir()
    {
        // Faz 2'deki gerçek durum: ne HTTP başlığı ne meta etiketi var.
        var encoding = Encoding.GetEncoding("windows-1254");
        var bytes = Sayfa(string.Empty, encoding, TurkceBaslik);

        var metin = SafeDocumentDownloader.CozumleTest(bytes, charset: null);

        Assert.NotNull(metin);
        Assert.DoesNotContain('�', metin);
    }

    [Fact(DisplayName = "TR5. HTTP başlığı yanlışsa doğru küme bulunur")]
    public void Yanlis_baslik_duzeltilir()
    {
        // Sunucu utf-8 diyor ama gövde windows-1254.
        var encoding = Encoding.GetEncoding("windows-1254");
        var bytes = Sayfa(string.Empty, encoding, TurkceBaslik);

        var metin = SafeDocumentDownloader.CozumleTest(bytes, charset: "utf-8");

        Assert.NotNull(metin);
        Assert.DoesNotContain('�', metin);
        Assert.Contains(TurkceBaslik, metin, StringComparison.Ordinal);
    }

    // ═══════════════ Çözülemeyen içerik ═══════════════

    [Fact(DisplayName = "TR6. Hiçbir kümeyle çözülemeyen içerik kaydedilmez")]
    public void Cozulemeyen_icerik_kaydedilmez()
    {
        // UTF-16 gövde: denenen kümelerin hiçbiri bunu temiz çözemez.
        var bytes = Encoding.Unicode.GetBytes(
            "<html><head><title>Bozuk</title></head><body>" +
            new string('中', 400) + "</body></html>");

        var metin = SafeDocumentDownloader.CozumleTest(bytes, charset: null);

        Assert.Null(metin);
    }

    // ═══════════════ Meta tespiti ═══════════════

    [Theory(DisplayName = "TR7. Meta etiketi farklı yazımlarla okunur")]
    [InlineData("<meta charset=\"Windows-1254\">", "Windows-1254")]
    [InlineData("<meta charset='windows-1254'>", "windows-1254")]
    [InlineData("<meta charset=windows-1254>", "windows-1254")]
    [InlineData("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=ISO-8859-9\">",
        "ISO-8859-9")]
    public void Meta_charset_okunur(string meta, string beklenen)
    {
        var bytes = Encoding.ASCII.GetBytes($"<html><head>{meta}</head><body>x</body></html>");

        Assert.Equal(beklenen, SafeDocumentDownloader.MetaCharsetTest(bytes));
    }

    [Fact(DisplayName = "TR8. Meta etiketi yoksa null döner")]
    public void Meta_yoksa_null()
    {
        var bytes = Encoding.ASCII.GetBytes("<html><head><title>x</title></head></html>");

        Assert.Null(SafeDocumentDownloader.MetaCharsetTest(bytes));
    }

    private sealed class SahteHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
