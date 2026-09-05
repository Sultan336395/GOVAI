namespace GovAI.Application.Opportunities;

/// <summary>
/// "Resmî kaynağa git" bağlantısının doğrulanması (Faz 2).
///
/// <para>
/// Bu düğme ürünün en yüksek güven vaadidir: kullanıcı ona bastığında <b>resmî
/// kurumun kendi sayfasına</b> gittiğini varsayar. Bu yüzden bağlantı doğrulanmadan
/// gösterilmez — doğrulanamayan bir adres, düğmenin hiç görünmemesi demektir.
/// </para>
///
/// <para>
/// Doğrulama üç koşulu birden arar:
/// </para>
/// <list type="number">
///   <item>Adres mutlak ve <c>http</c>/<c>https</c> olmalı.</item>
///   <item>Sunucu adı kaynağın resmî alan adı olmalı ya da onun alt alan adı olmalı.</item>
///   <item>
///     Eşleşme <b>nokta sınırında</b> yapılmalı: <c>resmigazete.gov.tr.kotu.com</c>
///     resmî alan adı sayılmaz. Sonek karşılaştırması bu kontrol olmadan taklit
///     alan adlarını kabul eder.
///   </item>
/// </list>
/// </summary>
public static class OfficialLink
{
    /// <summary>Doğrulama sonucu. Reddedildiyse sebebi taşır.</summary>
    public readonly record struct Result(string? Url, string? RejectionReason)
    {
        public bool IsVerified => Url is not null;
    }

    public static Result Verify(string? url, string? officialDomain)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new Result(null, "Kayıtta resmî adres yok.");
        }

        if (string.IsNullOrWhiteSpace(officialDomain))
        {
            // Kaynağın resmî alan adı tanımlı değilse doğrulama YAPILAMAZ.
            // Doğrulanamayan bağlantı resmî sayılmaz; tahmin edilmez.
            return new Result(
                null,
                "Kaynağın resmî alan adı tanımlı değil; bağlantı doğrulanamıyor.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new Result(null, "Adres geçerli bir bağlantı değil.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return new Result(null, $"Adres '{uri.Scheme}' şemasını kullanıyor; yalnızca http/https kabul edilir.");
        }

        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        var domain = officialDomain.Trim().TrimEnd('.').ToLowerInvariant();

        // Alan adı listesi virgülle ayrılmış olabilir (ör. EUR-Lex içeriği CELLAR'dan gelir).
        foreach (var aday in domain.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var temiz = aday.TrimStart('.');

            if (host == temiz || host.EndsWith("." + temiz, StringComparison.Ordinal))
            {
                return new Result(uri.ToString(), null);
            }
        }

        return new Result(
            null,
            $"Adresin sunucusu ('{host}') kaynağın resmî alan adında ({domain}) değil; "
            + "resmî kaynak olarak gösterilmez.");
    }
}
