using GovAI.Application.Common;

namespace GovAI.Application.Opportunities;

/// <summary>
/// Toplu bölüm başlığı tespiti (Faz 2).
///
/// <para>
/// Resmî Gazete'nin ilan bölümü tek bir sayfada <b>onlarca ayrı ilan</b> yayımlar ve
/// sayfanın başlığı bunların ortak başlığıdır: "ARTIRMA, EKSİLTME VE İHALE İLÂNLARI".
/// Bu bir ihale kaydı değildir — ne açan kurumu, ne konusu, ne tarihi, ne bedeli
/// vardır. Fırsat kataloğuna böyle bir kayıt açılırsa şirketlere "size uygun bir ihale
/// var" denir ve bağlantı bir liste sayfasına götürür; danışman hangi ihaleden
/// bahsedildiğini bilemez.
/// </para>
///
/// <para>
/// Kural <b>tam eşleşmedir</b>, "içeriyor" değil. Gerçek bir ilanın başlığında bu
/// ifadenin geçmesi mümkündür (ör. "... ihale ilânları kapsamında düzeltme"); yalnızca
/// başlığın <b>kendisi</b> toplu başlıksa kayıt açılmaz. Böylece yanlışlıkla gerçek
/// bir ilan elenmez.
/// </para>
/// </summary>
public static class SectionHeading
{
    /// <summary>
    /// Resmî yayınların toplu ilan bölümü başlıkları.
    ///
    /// Noktalama ve şapka yazımı yıllara göre değiştiği için karşılaştırma
    /// <see cref="TurkceMetin.BasligiKatla"/> ile yapılır; listede tek yazım yeterlidir.
    /// </summary>
    private static readonly string[] Collective =
    [
        "ARTIRMA, EKSİLTME VE İHALE İLÂNLARI",
        "ARTIRMA EKSİLTME VE İHALE İLANLARI",
        "ÇEŞİTLİ İLÂNLAR",
        "İLÂN BÖLÜMÜ",
        "İLAN BÖLÜMÜ",
        "İHALE İLÂNLARI",
        "İHALE İLANLARI",
        "İLÂNLAR",
        "İLANLAR",
        "DUYURULAR",
        "RESMÎ İLÂNLAR",
    ];

    private static readonly HashSet<string> Folded =
        Collective.Select(TurkceMetin.BasligiKatla).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Başlık, tek bir ilanın değil bir <b>bölümün</b> başlığı mı?
    /// </summary>
    public static bool IsCollective(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return Folded.Contains(TurkceMetin.BasligiKatla(title));
    }

    /// <summary>Reddedilen kayıt için operatöre gösterilecek gerekçe.</summary>
    public static string Explanation(string title) =>
        $"'{title.Trim()}' bir ilanın başlığı değil, birden çok ilanı barındıran bölümün "
        + "başlığıdır. Fırsat kaydı tekil bir ilandan açılır; bu sayfadaki ilanlar ayrı "
        + "ayrı ayrıştırılmalıdır.";
}
