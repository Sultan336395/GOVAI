namespace GovAI.Domain.Common;

/// <summary>
/// Türkçe metin karşılaştırma yardımcıları.
///
/// <para>
/// Faz 3'te <c>GovAI.Application.Common</c> altından buraya taşındı: mevzuat etki
/// motoru resmî belge metninde deterministik arama yapıyor ve <c>Domain</c> katmanı
/// <c>Application</c>'a bağımlı olamaz. Kopya bir katlama yazmak, iki tarafın zamanla
/// farklı eşleşmesi demekti — aynı belgede biri "İŞVEREN"i bulur, diğeri bulmazdı.
/// </para>
/// </summary>
public static class TurkceMetin
{
    /// <summary>
    /// Türkçe metni karşılaştırma için katlar.
    ///
    /// <para>
    /// Gerekli, çünkü ne <c>ToUpperInvariant</c> ne de <c>OrdinalIgnoreCase</c> noktasız
    /// <c>ı</c> ile noktalı <c>I</c>'yı eşleştirir: "Karar Sayısı" ile "KARAR SAYISI"
    /// tutmaz ve gerçek mevzuat kaçırılır. Kültüre bağlı <c>ToUpper("tr-TR")</c> ise
    /// sunucunun yereline göre değişir; deterministik olmaz.
    /// </para>
    /// </summary>
    public static string Katla(string value)
    {
        var buffer = new System.Text.StringBuilder(value.Length);

        foreach (var ch in value)
        {
            buffer.Append(ch switch
            {
                'ı' or 'İ' or 'i' or 'I' => 'i',
                'ş' or 'Ş' => 's',
                'ğ' or 'Ğ' => 'g',
                'ü' or 'Ü' => 'u',
                'ö' or 'Ö' => 'o',
                'ç' or 'Ç' => 'c',
                'â' or 'Â' => 'a',
                'î' or 'Î' => 'i',
                'û' or 'Û' => 'u',
                _ => char.ToLowerInvariant(ch),
            });
        }

        return buffer.ToString();
    }

    /// <summary>
    /// Başlık karşılaştırması için katlar ve gürültüyü atar: harf ve rakam dışındaki
    /// her şey tek boşluğa iner. Böylece "ARTIRMA, EKSİLTME VE İHALE İLÂNLARI" ile
    /// "ARTIRMA EKSILTME VE IHALE ILANLARI" aynı sayılır — resmî sayfalarda noktalama
    /// ve şapka kullanımı yıldan yıla değişiyor.
    /// </summary>
    public static string BasligiKatla(string value)
    {
        var katlanmis = Katla(value);
        var buffer = new System.Text.StringBuilder(katlanmis.Length);
        var oncekiBosluk = true;

        foreach (var ch in katlanmis)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer.Append(ch);
                oncekiBosluk = false;
            }
            else if (!oncekiBosluk)
            {
                buffer.Append(' ');
                oncekiBosluk = true;
            }
        }

        return buffer.ToString().Trim();
    }
}
