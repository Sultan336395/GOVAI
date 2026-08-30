using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using GovAI.Application.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace GovAI.Infrastructure.Sources;

/// <summary>
/// Kontrollü manuel içe aktarmanın indiricisi (Faz 2).
///
/// <para>
/// Adres kullanıcıdan gelir, bu yüzden <b>SSRF koruması burada zorunludur</b>.
/// Python toplayıcısındaki <c>collector/safety.py</c> ile aynı kurallar geçerlidir:
/// yalnızca http/https, iç ağ ve döngü adresleri yasak, bulut metadata uçları yasak,
/// yönlendirmeler <b>elle</b> izlenir ve her adım yeniden denetlenir.
/// </para>
///
/// <para>
/// Sertifika doğrulaması <b>kapatılmaz</b>. Resmî kurum sunucularının TLS
/// uyumsuzlukları toplayıcı tarafında, alan adı bazlı ve dar bir politikayla
/// çözülür (<c>collector/tls.py</c>); burada varsayılan katı davranış geçerlidir.
/// </para>
/// </summary>
public sealed partial class SafeDocumentDownloader(
    IHttpClientFactory httpClientFactory,
    ILogger<SafeDocumentDownloader> logger) : IDocumentDownloader
{
    /// <summary>Adı geçen istemci; zaman aşımı ve kullanıcı aracısı burada ayarlanır.</summary>
    public const string HttpClientName = "govai-manual-import";

    static SafeDocumentDownloader()
    {
        // windows-1254 ve iso-8859-9 .NET Core'da varsayılan olarak YOKTUR.
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    private const int MaxRedirects = 5;
    private const int MaxBytes = 15 * 1024 * 1024;

    private static readonly string[] AllowedMediaTypes =
    [
        "text/html", "application/xhtml+xml", "text/plain", "application/xml", "text/xml",
    ];

    public async Task<DownloadedDocument?> DownloadAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var current = url;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!await IsSafeAsync(current, cancellationToken))
            {
                logger.LogWarning("Güvenli olmayan adres reddedildi. Adres={Url}", Redact(current));
                return null;
            }

            using var response = await client.GetAsync(
                current, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // Yönlendirme: httpClient'ın kendi takibi KAPALI; her adım yeniden denetlenir.
            if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
            {
                current = location.IsAbsoluteUri
                    ? location.ToString()
                    : new Uri(new Uri(current), location).ToString();

                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Belge indirilemedi. Adres={Url} Durum={Status}",
                    Redact(current), (int)response.StatusCode);

                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "text/html";

            if (!AllowedMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Desteklenmeyen içerik türü. Adres={Url} Tür={MediaType}",
                    Redact(current), mediaType);

                return null;
            }

            if (response.Content.Headers.ContentLength is > MaxBytes)
            {
                logger.LogWarning("Belge çok büyük. Adres={Url}", Redact(current));
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (bytes.Length > MaxBytes)
            {
                logger.LogWarning("Belge çok büyük. Adres={Url}", Redact(current));
                return null;
            }

            var charset = response.Content.Headers.ContentType?.CharSet
                          ?? SniffMetaCharset(bytes);

            var content = Decode(bytes, charset);

            if (content is null)
            {
                // Metin güvenle çözülemedi. Bozuk metin RESMÎ KANIT OLARAK KULLANILMAZ:
                // "ARTIRMA, EKS?LTME VE ?HALE ?L?NLARI" gibi bir başlık kaydedilirse
                // belge sonradan doğru okunamaz ve kanıt zinciri bozulur.
                logger.LogWarning(
                    "Belge metni güvenle çözülemedi. Adres={Url} Charset={Charset}",
                    Redact(current), charset ?? "(bildirilmedi)");

                return null;
            }

            return new DownloadedDocument(
                content,
                mediaType,
                charset,
                current,
                (int)response.StatusCode,
                ExtractTitle(content) ?? current);
        }

        logger.LogWarning("Yönlendirme sınırı aşıldı. Adres={Url}", Redact(url));
        return null;
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Adres dışarıya mı gidiyor? İç ağ, döngü adresi ve metadata uçları reddedilir.
    /// Ad çözümlemesi burada yapılır: alan adı dışarıdaymış gibi görünüp özel bir IP'ye
    /// çözülebilir (DNS rebinding).
    /// </summary>
    private async Task<bool> IsSafeAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (uri.IsDefaultPort is false && uri.Port is not (80 or 443 or 8080 or 8443))
        {
            return false;
        }

        IPAddress[] adresler;

        try
        {
            adresler = uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
                ? [IPAddress.Parse(uri.Host)]
                : await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return false;
        }

        return adresler.Length > 0 && adresler.All(IsPublic);
    }

    private static bool IsPublic(IPAddress adres)
    {
        if (IPAddress.IsLoopback(adres))
        {
            return false;
        }

        if (adres.AddressFamily == AddressFamily.InterNetwork)
        {
            var o = adres.GetAddressBytes();

            return o[0] switch
            {
                0 or 10 or 127 => false,
                169 when o[1] == 254 => false,   // link-local; bulut metadata (169.254.169.254)
                172 when o[1] >= 16 && o[1] <= 31 => false,
                192 when o[1] == 168 => false,
                100 when o[1] >= 64 && o[1] <= 127 => false,  // CGNAT
                >= 224 => false,                 // çoklu yayın ve ayrılmış
                _ => true,
            };
        }

        if (adres.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !adres.IsIPv6LinkLocal
                && !adres.IsIPv6SiteLocal
                && !adres.IsIPv6UniqueLocal
                && !adres.IsIPv6Multicast;
        }

        return false;
    }

    /// <summary>
    /// Metni çözer. Güvenle çözülemiyorsa <c>null</c> döner.
    ///
    /// Sıra: bildirilen karakter kümesi → UTF-8 → Türkçe eski kümeler. Her adımda
    /// sonuçta <b>replacement character</b> (U+FFFD) kalıp kalmadığına bakılır;
    /// kalıyorsa o küme yanlıştır. Resmî Gazete sayfaları kümeyi HTTP başlığında
    /// değil <c>&lt;meta&gt;</c> etiketinde bildirir (Windows-1254); başlık boş diye
    /// UTF-8 varsayılınca Türkçe harfler bozuluyordu.
    /// </summary>
    /// <summary>Testten erişim; çözümleme kuralı ağ olmadan sınanabilmeli.</summary>
    internal static string? CozumleTest(byte[] bytes, string? charset) => Decode(bytes, charset);

    /// <summary>Testten erişim; meta tespiti ağ olmadan sınanabilmeli.</summary>
    internal static string? MetaCharsetTest(byte[] bytes) => SniffMetaCharset(bytes);

    private static string? Decode(byte[] bytes, string? charset)
    {
        foreach (var aday in Adaylar(charset))
        {
            Encoding encoding;

            try
            {
                encoding = Encoding.GetEncoding(aday);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var metin = encoding.GetString(bytes);

            if (MakulMetin(metin))
            {
                return metin;
            }
        }

        return null;
    }

    /// <summary>
    /// Çözülen metin gerçekten metin mi?
    ///
    /// Yalnızca <c>U+FFFD</c> aramak yetmez: <c>windows-1254</c> ve <c>iso-8859-9</c>
    /// tek baytlıdır ve neredeyse her baytı bir karaktere eşler, bu yüzden yanlış
    /// kümeyle çözülen ikili içerik hiç replacement character üretmez. UTF-16 bir
    /// gövde tek baytlı kümede okununca satır satır <c>NUL</c> çıkar — kontrol
    /// karakterleri bu yüzden ayrıca denetlenir.
    /// </summary>
    private static bool MakulMetin(string metin)
    {
        if (metin.Contains('\uFFFD'))
        {
            return false;
        }

        foreach (var ch in metin)
        {
            // Sekme, satır sonu ve satır başı dışında C0 kontrol karakteri beklenmez.
            if (char.IsControl(ch)
                && ch is not ('\t' or '\n' or '\r'))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> Adaylar(string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            yield return charset;
        }

        yield return "utf-8";

        // Türkçe kamu sitelerinde hâlâ yaygın olan eski kümeler.
        yield return "windows-1254";
        yield return "iso-8859-9";
    }

    /// <summary>
    /// Karakter kümesini <c>&lt;meta&gt;</c> etiketinden okur.
    ///
    /// Yalnızca ilk 2 KB taranır ve ASCII olarak yorumlanır: etiketin kendisi her
    /// zaman ASCII'dir, gövdenin kodlaması henüz bilinmiyor.
    /// </summary>
    private static string? SniffMetaCharset(byte[] bytes)
    {
        var bas = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        var eslesme = MetaCharsetRegex().Match(bas);

        return eslesme.Success ? eslesme.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractTitle(string content)
    {
        var eslesme = TitleRegex().Match(content);

        if (!eslesme.Success)
        {
            return null;
        }

        var baslik = WhitespaceRegex().Replace(eslesme.Groups[1].Value, " ").Trim();
        return string.IsNullOrWhiteSpace(baslik) ? null : baslik[..Math.Min(baslik.Length, 300)];
    }

    /// <summary>Logda tam adres yerine yalnızca sunucu ve yol gösterilir.</summary>
    private static string Redact(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}"
            : "(geçersiz adres)";

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"charset\s*=\s*[""']?([\w\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharsetRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
