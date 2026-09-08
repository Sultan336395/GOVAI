using System.Security.Cryptography;
using System.Text;

namespace GovAI.Domain.Maintenance;

/// <summary>
/// Bir bakım planının parmak izi.
///
/// <para>
/// <b>Neden gerekli:</b> "önce planı göster, onaylanmadan uygulama" kuralı yalnızca
/// arayüzde uygulanırsa gerçek bir güvence değildir — uç doğrudan çağrılabilir. Parmak
/// izi bu kuralı <b>sunucuda</b> zorunlu kılar: uygulama isteği, kullanıcıya gösterilen
/// planın özetini taşımak zorundadır.
/// </para>
///
/// <para>
/// İkinci işlevi tazelik: plan gösterildikten sonra veri değişirse (başka bir kayıt
/// karantinaya girdi, yeni bir belge tarandı) özet tutmaz ve uygulama reddedilir.
/// Kullanıcı planı yeniden görüp yeniden onaylar. Böylece "gördüğüm liste ile uygulanan
/// liste aynı" güvencesi verilir.
/// </para>
///
/// <para>
/// Satırlar <b>sıralanır</b>: aynı plan, kayıtların veritabanından hangi sırayla geldiğine
/// bakılmaksızın aynı özeti üretmelidir.
/// </para>
/// </summary>
public static class PlanFingerprint
{
    /// <summary>Boş planın özeti; uygulanacak bir şey yokken de karşılaştırma yapılabilsin.</summary>
    public const string Empty = "bos-plan";

    /// <summary>
    /// Satırlardan özet üretir. Her satır, o kayıt için değişecek olan <b>her şeyi</b>
    /// içermelidir; eksik bir alan, değişen bir planın aynı özeti üretmesine yol açar.
    /// </summary>
    public static string Compute(IEnumerable<string> satirlar)
    {
        ArgumentNullException.ThrowIfNull(satirlar);

        var sirali = satirlar.Where(s => !string.IsNullOrWhiteSpace(s)).OrderBy(s => s, StringComparer.Ordinal).ToList();

        if (sirali.Count == 0)
        {
            return Empty;
        }

        var metin = string.Join('\n', sirali);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(metin)));
    }
}
