using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GovAI.Application.Integrations;
using GovAI.Domain.Integrations;
using Microsoft.Extensions.Logging;

namespace GovAI.Infrastructure.Integrations;

/// <summary>
/// Firmanın ERP'sinden JSON okuyan uyarlayıcı.
///
/// <para>
/// Ürün ne olursa olsun bağlantı tek bir uca gider ve JSON bekler. Logo, Netsis, SAP gibi
/// ürünlerin kendi ikili protokollerini konuşmak yerine bu yol seçildi: her kurulumun
/// sürümü, modülleri ve özel alanları farklıdır; ürüne özel bir istemci sahada tutmaz ve
/// her müşteride yeniden yazılır. JSON uç, müşterinin kendi BT ekibinin bir kez açtığı ve
/// içeriğini kontrol ettiği tek noktadır.
/// </para>
/// </summary>
public sealed class HttpErpDataSource(
    IHttpClientFactory httpClientFactory,
    ISecretProtector protector,
    ILogger<HttpErpDataSource> logger) : IErpDataSource
{
    /// <summary>ERP yanıtı için azami boyut; yanlış yapılandırılmış bir uç tüm belleği yemesin.</summary>
    private const int MaximumResponseBytes = 4 * 1024 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    public async Task<ErpSnapshot> FetchAsync(
        ErpConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var adres = new Uri(connection.BaseUrl, UriKind.Absolute);

        await GuvenliMiAsync(adres, connection.IsOnPremise, cancellationToken);

        using var client = httpClientFactory.CreateClient("erp");
        client.Timeout = Timeout;

        using var istek = new HttpRequestMessage(HttpMethod.Get, adres);
        KimlikEkle(istek, connection);

        HttpResponseMessage yanit;

        try
        {
            yanit = await client.SendAsync(istek, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "ERP adresine ulaşılamadı. CompanyId={CompanyId}", connection.CompanyId);

            throw new ErpFetchException("ERP adresine ulaşılamadı. Adresi ve ağ erişimini kontrol edin.");
        }
        catch (TaskCanceledException)
        {
            throw new ErpFetchException($"ERP {Timeout.TotalSeconds:0} saniyede yanıt vermedi.");
        }

        using (yanit)
        {
            if (!yanit.IsSuccessStatusCode)
            {
                // Gövde YAZILMAZ: ERP hata gövdesinde kimlik bilgisi ya da personel verisi
                // döndürebilir ve bu mesaj panelde görünür.
                throw new ErpFetchException(yanit.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                        "ERP kimlik bilgisi kabul edilmedi. Anahtarı yenileyin.",
                    HttpStatusCode.NotFound =>
                        "ERP adresinde veri ucu bulunamadı. Adresi kontrol edin.",
                    _ => $"ERP {(int)yanit.StatusCode} hatası döndürdü.",
                });
            }

            var govde = await OkuAsync(yanit, cancellationToken);

            return Coz(govde, connection);
        }
    }

    private static async Task<string> OkuAsync(HttpResponseMessage yanit, CancellationToken cancellationToken)
    {
        await using var akis = await yanit.Content.ReadAsStreamAsync(cancellationToken);

        var tampon = new MemoryStream();
        var parca = new byte[81920];
        int okunan;

        while ((okunan = await akis.ReadAsync(parca, cancellationToken)) > 0)
        {
            if (tampon.Length + okunan > MaximumResponseBytes)
            {
                throw new ErpFetchException("ERP yanıtı beklenenden büyük; veri ucunu daraltın.");
            }

            tampon.Write(parca, 0, okunan);
        }

        return Encoding.UTF8.GetString(tampon.ToArray());
    }

    /// <summary>
    /// Kimlik bilgisini isteğe ekler.
    ///
    /// <para>
    /// Açık hâl yalnızca bu metodun içinde var olur ve <b>hiçbir yere loglanmaz</b>.
    /// </para>
    /// </summary>
    private void KimlikEkle(HttpRequestMessage istek, ErpConnection connection)
    {
        var gizli = protector.Unprotect(connection.ProtectedSecret);

        switch (connection.AuthMode)
        {
            case ErpAuthMode.BearerToken:
                istek.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gizli);
                break;

            case ErpAuthMode.BasicAuth:
                var kodlu = Convert.ToBase64String(Encoding.UTF8.GetBytes(gizli));
                istek.Headers.Authorization = new AuthenticationHeaderValue("Basic", kodlu);
                break;

            default:
                istek.Headers.TryAddWithoutValidation("X-API-Key", gizli);
                break;
        }
    }

    /// <summary>
    /// Adres güvenli mi?
    ///
    /// <para>
    /// Kurum içi ERP tam da özel IP'dedir; SSRF engelini topyekûn uygulamak entegrasyonu
    /// imkânsız kılardı. Bu yüzden özel adresler <b>yalnızca</b> firma yöneticisi
    /// bağlantıyı kurum içi diye beyan ettiyse açılır ve beyan kayıtlıdır.
    /// </para>
    ///
    /// <para>
    /// Bulut metadata adresi (169.254.169.254) bu beyanla dahi açılmaz: orası bir ERP
    /// değil, sunucunun kendi kimlik bilgilerinin durduğu yerdir ve oraya gitmek
    /// GOVAI'nin kendi bulut kimliğini sızdırmak olurdu.
    /// </para>
    /// </summary>
    private static async Task GuvenliMiAsync(Uri adres, bool kurumIci, CancellationToken cancellationToken)
    {
        IPAddress[] adresler;

        try
        {
            adresler = await Dns.GetHostAddressesAsync(adres.Host, cancellationToken);
        }
        catch (SocketException)
        {
            throw new ErpFetchException("ERP adresi çözümlenemedi. Alan adını kontrol edin.");
        }

        if (adresler.Length == 0)
        {
            throw new ErpFetchException("ERP adresi çözümlenemedi. Alan adını kontrol edin.");
        }

        foreach (var ip in adresler)
        {
            if (MetadataAdresi(ip))
            {
                throw new ErpFetchException("Bu adrese bağlanılamaz.");
            }

            if (!kurumIci && !Genel(ip))
            {
                throw new ErpFetchException(
                    "Adres kurum içi bir ağa işaret ediyor. Bağlantıyı kurum içi olarak "
                    + "işaretlerseniz bu adrese gidilebilir.");
            }
        }
    }

    /// <summary>Bulut metadata uçları. Hiçbir koşulda açılmaz.</summary>
    private static bool MetadataAdresi(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var o = ip.GetAddressBytes();

        return o[0] == 169 && o[1] == 254;
    }

    private static bool Genel(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var o = ip.GetAddressBytes();

            return o[0] switch
            {
                0 or 10 or 127 => false,
                169 when o[1] == 254 => false,
                172 when o[1] >= 16 && o[1] <= 31 => false,
                192 when o[1] == 168 => false,
                100 when o[1] >= 64 && o[1] <= 127 => false,
                >= 224 => false,
                _ => true,
            };
        }

        return !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal && !ip.IsIPv6UniqueLocal;
    }

    /// <summary>ERP yanıtını eşlemeye göre okur.</summary>
    private ErpSnapshot Coz(string govde, ErpConnection connection)
    {
        var esleme = EslemeOku(connection);

        if (esleme.IsEmpty)
        {
            throw new ErpFetchException(
                "Bu bağlantı için alan eşlemesi tanımlı değil. ERP alan adlarını eşleyin.");
        }

        JsonDocument belge;

        try
        {
            belge = JsonDocument.Parse(govde);
        }
        catch (JsonException)
        {
            throw new ErpFetchException("ERP yanıtı JSON değil. Veri ucunu kontrol edin.");
        }

        using (belge)
        {
            var kok = belge.RootElement;
            var bulunamayan = new List<string>();

            decimal? Para(string? yol, string ad) => Sayi(kok, yol, ad, bulunamayan);
            int? Adet(string? yol, string ad) => (int?)Sayi(kok, yol, ad, bulunamayan);

            return new ErpSnapshot
            {
                AnnualRevenue = Para(esleme.AnnualRevenue, "Yıllık ciro"),
                BalanceSize = Para(esleme.BalanceSize, "Bilanço büyüklüğü"),
                Equity = Para(esleme.Equity, "Özkaynak"),
                ExportRevenue = Para(esleme.ExportRevenue, "İhracat cirosu"),
                EmployeeCount = Adet(esleme.EmployeeCount, "Toplam çalışan"),
                WomenEmployeeCount = Adet(esleme.WomenEmployeeCount, "Kadın çalışan"),
                YoungEmployeeCount = Adet(esleme.YoungEmployeeCount, "Genç çalışan"),
                RAndDEmployeeCount = Adet(esleme.RAndDEmployeeCount, "Ar-Ge personeli"),
                DisabledEmployeeCount = Adet(esleme.DisabledEmployeeCount, "Engelli çalışan"),
                YoungEmployeeMaxAge = Adet(esleme.YoungEmployeeMaxAge, "Genç çalışan üst yaşı"),
                Certificates = Belgeler(kok, esleme, bulunamayan),
                NotificationRecipients = Aliciar(kok, esleme, bulunamayan),
                MissingFields = bulunamayan,
            };
        }
    }

    private static ErpFieldMap EslemeOku(ErpConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.FieldMapJson))
        {
            return ErpFieldMap.Default(connection.Vendor);
        }

        try
        {
            return JsonSerializer.Deserialize<ErpFieldMap>(
                connection.FieldMapJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? ErpFieldMap.Default(connection.Vendor);
        }
        catch (JsonException)
        {
            throw new ErpFetchException("Alan eşlemesi okunamadı. Eşlemeyi yeniden kaydedin.");
        }
    }

    /// <summary>
    /// Nokta yoluyla değer okur.
    ///
    /// <para>
    /// Yol bulunamazsa <c>null</c> döner ve alan "bulunamayan" listesine yazılır —
    /// <b>sıfır dönmez</b>. Sıfır dönmek, ERP'de olmayan bir alanı "hiç yok" diye
    /// kaydetmek olurdu (bkz. <c>docs/adr/0003</c>).
    /// </para>
    /// </summary>
    private static decimal? Sayi(JsonElement kok, string? yol, string ad, List<string> bulunamayan)
    {
        if (string.IsNullOrWhiteSpace(yol))
        {
            return null;
        }

        if (!Bul(kok, yol, out var deger))
        {
            bulunamayan.Add(ad);
            return null;
        }

        return deger.ValueKind switch
        {
            JsonValueKind.Number when deger.TryGetDecimal(out var sayi) => sayi,

            // ERP'ler sayıyı metin olarak da döndürebiliyor; ayrıştırma kültürden
            // bağımsız yapılır, yoksa "1.234,5" ile "1,234.5" karışır.
            JsonValueKind.String when decimal.TryParse(
                deger.GetString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var metinSayi) => metinSayi,

            JsonValueKind.Null => null,

            _ => null,
        };
    }

    /// <summary>
    /// ERP'de tanımlı bildirim sorumlularını çıkarır.
    ///
    /// <para>
    /// Bölüm eşlemede yoksa <c>null</c> döner — "sorumlu yok" değil "bu ERP'de bu
    /// bölüm tanımlı değil" demektir ve mevcut tanımlara dokunulmaz. Bölüm eşlemede
    /// var ama yanıtta yoksa eksik alan olarak bildirilir ve yine <c>null</c> döner:
    /// geçici bir ERP arızası yüzünden bütün sorumluları pasifleştirmek, firmayı
    /// sessizce bildirimsiz bırakırdı.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ErpRecipient>? Aliciar(
        JsonElement kok,
        ErpFieldMap esleme,
        List<string> bulunamayan)
    {
        if (string.IsNullOrWhiteSpace(esleme.NotificationRecipients))
        {
            return null;
        }

        if (!Bul(kok, esleme.NotificationRecipients, out var deger))
        {
            bulunamayan.Add("Bildirim sorumluları");
            return null;
        }

        // Virgülle ayrılmış adres listesi: "ayse@firma.com, mehmet@firma.com"
        if (deger.ValueKind == JsonValueKind.String)
        {
            return (deger.GetString() ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(a => new ErpRecipient(a, null, null, null))
                .ToList();
        }

        if (deger.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var liste = new List<ErpRecipient>();

        foreach (var oge in deger.EnumerateArray())
        {
            if (oge.ValueKind == JsonValueKind.String)
            {
                if (oge.GetString() is { Length: > 0 } adres)
                {
                    liste.Add(new ErpRecipient(adres.Trim(), null, null, null));
                }

                continue;
            }

            if (oge.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var epostaAlani = esleme.RecipientEmailField ?? "email";

            if (!oge.TryGetProperty(epostaAlani, out var epostaDeger)
                || epostaDeger.GetString() is not { Length: > 0 } eposta)
            {
                continue;
            }

            liste.Add(new ErpRecipient(
                eposta.Trim(),
                Metin(oge, esleme.RecipientNameField),
                Metin(oge, esleme.RecipientRoleField),
                Metin(oge, esleme.RecipientExternalIdField)));
        }

        return liste;
    }

    /// <summary>Nesne içindeki isteğe bağlı metin alanı; yoksa <c>null</c>.</summary>
    private static string? Metin(JsonElement oge, string? alan)
    {
        if (string.IsNullOrWhiteSpace(alan)
            || !oge.TryGetProperty(alan, out var deger)
            || deger.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var metin = deger.GetString();

        return string.IsNullOrWhiteSpace(metin) ? null : metin.Trim();
    }

    private static IReadOnlyList<ErpCertificate> Belgeler(
        JsonElement kok,
        ErpFieldMap esleme,
        List<string> bulunamayan)
    {
        if (string.IsNullOrWhiteSpace(esleme.Certificates))
        {
            return [];
        }

        if (!Bul(kok, esleme.Certificates, out var deger))
        {
            bulunamayan.Add("Belgeler");
            return [];
        }

        // Virgülle ayrılmış metin: "ISO9001, ISO14001"
        if (deger.ValueKind == JsonValueKind.String)
        {
            return (deger.GetString() ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k => new ErpCertificate(k, null))
                .ToList();
        }

        if (deger.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var liste = new List<ErpCertificate>();

        foreach (var oge in deger.EnumerateArray())
        {
            if (oge.ValueKind == JsonValueKind.String)
            {
                var kod = oge.GetString();

                if (!string.IsNullOrWhiteSpace(kod))
                {
                    liste.Add(new ErpCertificate(kod.Trim(), null));
                }

                continue;
            }

            if (oge.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var kodAlani = esleme.CertificateCodeField ?? "code";

            if (!oge.TryGetProperty(kodAlani, out var kodDeger)
                || kodDeger.GetString() is not { Length: > 0 } belgeKodu)
            {
                continue;
            }

            DateOnly? gecerlilik = null;

            if (esleme.CertificateValidUntilField is { Length: > 0 } tarihAlani
                && oge.TryGetProperty(tarihAlani, out var tarihDeger)
                && tarihDeger.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(
                    tarihDeger.GetString(), CultureInfo.InvariantCulture, out var okunan))
            {
                gecerlilik = okunan;
            }

            liste.Add(new ErpCertificate(belgeKodu.Trim(), gecerlilik));
        }

        return liste;
    }

    /// <summary>Nokta yolunu izleyerek değeri bulur: <c>"personel.kadin"</c>.</summary>
    private static bool Bul(JsonElement kok, string yol, out JsonElement deger)
    {
        deger = kok;

        foreach (var parca in yol.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (deger.ValueKind != JsonValueKind.Object || !deger.TryGetProperty(parca, out deger))
            {
                return false;
            }
        }

        return true;
    }
}
