using System.Globalization;
using System.Text.RegularExpressions;
using GovAI.Domain.Common;

namespace GovAI.Domain.Analysis;

/// <summary>
/// Model çıktısındaki olgusal ifadeleri belgeye karşı doğrulayan deterministik
/// denetleyiciler (Faz 3 — Aşama 2).
///
/// <para>
/// Dil modelleri sayı ve tarih uydurmakta çok iyidir; cümle akıcı, rakam yanlış olur.
/// "Son başvuru 15 Ekim 2026" cümlesi belgede 15 Kasım yazıyorken de aynı doğallıkta
/// çıkar. Bu yüzden modelin ürettiği <b>her</b> tarih, tutar, oran ve mevzuat maddesi
/// kanıt metninde aranır; bulunamayan iddia reddedilir.
/// </para>
///
/// <para>
/// Doğrulama tek yönlüdür: metinde geçmeyen bir olgu reddedilir, geçen bir olgu
/// "doğru" ilan edilmez — yalnızca belgeye dayandığı gösterilmiş olur.
/// </para>
/// </summary>
public static partial class DeterministicValidators
{
    /// <summary>Türkçe ay adları; belgelerde hem "15.10.2026" hem "15 Ekim 2026" geçer.</summary>
    private static readonly string[] Months =
    [
        "ocak", "subat", "mart", "nisan", "mayis", "haziran",
        "temmuz", "agustos", "eylul", "ekim", "kasim", "aralik"
    ];

    [GeneratedRegex(@"\b(\d{1,2})[./](\d{1,2})[./](\d{4})\b")]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"\b(\d{1,2})\s+([A-Za-zÇĞİÖŞÜçğıöşü]+)\s+(\d{4})\b")]
    private static partial Regex WrittenDate();

    /// <summary>Para tutarı: 1.500.000 TL, 1.500.000,50 TL, 20.000.000 ₺.</summary>
    [GeneratedRegex(@"\b\d{1,3}(?:\.\d{3})+(?:,\d+)?\b|\b\d{4,}(?:,\d+)?\b")]
    private static partial Regex Amount();

    [GeneratedRegex(@"%\s?\d{1,3}(?:[.,]\d+)?|\b\d{1,3}(?:[.,]\d+)?\s?%")]
    private static partial Regex Rate();

    /// <summary>Mevzuat maddesi: "5746 sayılı Kanun", "madde 12", "geçici madde 3".</summary>
    [GeneratedRegex(@"\b\d{3,5}\s+sayılı\b|\b(?:geçici\s+)?madde\s+\d{1,3}\b", RegexOptions.IgnoreCase)]
    private static partial Regex LegalArticle();

    /// <summary>
    /// Açıklamada geçen her olgunun kanıt metninde bulunduğunu doğrular.
    ///
    /// <para>
    /// Kanıt metni <b>bütün olarak</b> verilir: model bir cümleyi bir parçadan, tarihi
    /// başka bir parçadan alabilir ve bu meşrudur. Yasak olan, hiçbir parçada
    /// bulunmayan bir olgu üretmektir.
    /// </para>
    /// </summary>
    public static bool FactsAreGrounded(string explanation, string evidenceText, out string? unverified)
    {
        unverified = null;

        if (string.IsNullOrWhiteSpace(explanation))
        {
            return true;
        }

        var evidence = TurkceMetin.Katla(evidenceText);

        foreach (var tarih in ExtractDates(explanation))
        {
            if (!DateAppears(tarih, evidence))
            {
                unverified = $"Tarih belgede bulunamadı: {tarih:dd.MM.yyyy}";
                return false;
            }
        }

        foreach (var tutar in Amount().Matches(explanation).Select(m => m.Value))
        {
            if (!NumberAppears(tutar, evidence))
            {
                unverified = $"Tutar belgede bulunamadı: {tutar}";
                return false;
            }
        }

        foreach (var oran in Rate().Matches(explanation).Select(m => m.Value))
        {
            if (!RateAppears(oran, evidence))
            {
                unverified = $"Oran belgede bulunamadı: {oran}";
                return false;
            }
        }

        foreach (var madde in LegalArticle().Matches(explanation).Select(m => m.Value))
        {
            if (!evidence.Contains(TurkceMetin.Katla(madde), StringComparison.Ordinal))
            {
                unverified = $"Mevzuat maddesi belgede bulunamadı: {madde}";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Alıntının kanıt metninde gerçekten geçtiğini doğrular.
    ///
    /// <para>
    /// Karşılaştırma Türkçe katlama ve boşluk normalizasyonuyla yapılır: model
    /// büyük/küçük harfi ve satır sonlarını değiştirebilir, bu halüsinasyon değildir.
    /// Kelimelerin kendisi değişmişse alıntı uydurmadır.
    /// </para>
    /// </summary>
    public static bool QuoteAppears(string quote, string evidenceText)
    {
        if (string.IsNullOrWhiteSpace(quote))
        {
            return true;
        }

        return Normalize(evidenceText).Contains(Normalize(quote), StringComparison.Ordinal);
    }

    /// <summary>
    /// Bağlantının kabul edilebilir bir resmî adres olup olmadığı.
    ///
    /// <para>
    /// Model kaynak göstermek için adres uydurabiliyor. Yalnızca HTTPS ve resmî alan
    /// adı kabul edilir; kısaltma servisleri ve resmî olmayan alan adları reddedilir.
    /// </para>
    /// </summary>
    public static bool IsOfficialUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();

        return host.EndsWith(".gov.tr", StringComparison.Ordinal)
               || host is "gov.tr"
               || host.EndsWith(".europa.eu", StringComparison.Ordinal)
               || host is "europa.eu";
    }

    /// <summary>Metindeki bağlantıları çıkarır.</summary>
    public static IReadOnlyList<string> ExtractUrls(string text) =>
        text.Split([' ', '\n', '\r', '\t', '"', '\'', '<', '>', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.TrimEnd('.', ',', ';', ':'))
            .ToList();

    private static IEnumerable<DateOnly> ExtractDates(string text)
    {
        foreach (Match m in NumericDate().Matches(text))
        {
            if (TryDate(m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value, out var d))
            {
                yield return d;
            }
        }

        foreach (Match m in WrittenDate().Matches(text))
        {
            var ay = Array.IndexOf(Months, TurkceMetin.Katla(m.Groups[2].Value)) + 1;
            if (ay > 0 && TryDate(m.Groups[3].Value, ay.ToString(CultureInfo.InvariantCulture), m.Groups[1].Value, out var d))
            {
                yield return d;
            }
        }
    }

    private static bool TryDate(string yil, string ay, string gun, out DateOnly date)
    {
        date = default;

        return int.TryParse(yil, out var y)
               && int.TryParse(ay, out var a)
               && int.TryParse(gun, out var g)
               && a is >= 1 and <= 12
               && g >= 1 && g <= DateTime.DaysInMonth(y, a)
               && (date = new DateOnly(y, a, g)) != default;
    }

    /// <summary>Aynı tarih belgede sayı ya da yazıyla geçebilir; ikisi de kabul edilir.</summary>
    private static bool DateAppears(DateOnly date, string foldedEvidence)
    {
        var adaylar = new[]
        {
            $"{date.Day:00}.{date.Month:00}.{date.Year}",
            $"{date.Day}.{date.Month}.{date.Year}",
            $"{date.Day:00}/{date.Month:00}/{date.Year}",
            $"{date.Day} {Months[date.Month - 1]} {date.Year}",
            $"{date.Day:00} {Months[date.Month - 1]} {date.Year}"
        };

        return adaylar.Any(a => foldedEvidence.Contains(TurkceMetin.Katla(a), StringComparison.Ordinal));
    }

    /// <summary>Tutar karşılaştırması ayırıcılardan bağımsızdır: 1.500.000 = 1500000.</summary>
    private static bool NumberAppears(string value, string foldedEvidence)
    {
        var sade = Digits(value);
        if (sade.Length == 0)
        {
            return true;
        }

        foreach (Match m in Amount().Matches(foldedEvidence))
        {
            if (Digits(m.Value) == sade)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RateAppears(string value, string foldedEvidence)
    {
        var sade = Digits(value);

        foreach (Match m in Rate().Matches(foldedEvidence))
        {
            if (Digits(m.Value) == sade)
            {
                return true;
            }
        }

        return false;
    }

    private static string Digits(string value) =>
        new([.. value.Where(char.IsDigit)]);

    /// <summary>Katlar ve fazla boşlukları teke indirir.</summary>
    private static string Normalize(string value)
    {
        var katlanmis = TurkceMetin.Katla(value);
        var buffer = new System.Text.StringBuilder(katlanmis.Length);
        var oncekiBosluk = false;

        foreach (var ch in katlanmis)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!oncekiBosluk && buffer.Length > 0)
                {
                    buffer.Append(' ');
                }

                oncekiBosluk = true;
                continue;
            }

            buffer.Append(ch);
            oncekiBosluk = false;
        }

        return buffer.ToString().TrimEnd();
    }
}
