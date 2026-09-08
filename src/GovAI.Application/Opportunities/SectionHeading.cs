using GovAI.Application.Common;
using GovAI.Domain.Common;

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
        "DESTEKLER",
        "DESTEKLER LİSTESİ",
        "DESTEK PROGRAMLARI",
        "YÜRÜRLÜKTEN KALDIRILAN DESTEKLER",
        "MEVZUAT",
        "GENELGELER",
        "TEBLİĞLER",
    ];

    /// <summary>
    /// Kurum siteleri başlığa kurum adını ekler: "Destekler Listesi - KOSGEB T.C. Küçük
    /// ve Orta Ölçekli İşletmeleri Geliştirme ve Destekleme İdaresi Başkanlığı". Ayraçtan
    /// önceki ilk parça sayfanın kendi başlığıdır; kurum adı her sayfada aynıdır ve
    /// ayırt edici değildir.
    /// </summary>
    private static readonly char[] TitleSeparators = ['-', '|', '–', '—', '·'];

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

        if (Folded.Contains(TurkceMetin.BasligiKatla(title)))
        {
            return true;
        }

        // Kurum adı eklenmiş başlık: ilk parçaya bakılır. KOSGEB'in liste sayfası
        // "Destekler Listesi - KOSGEB …" başlığıyla geliyordu ve tam eşleşmeye
        // takılmadan fırsat kaydına dönüşüyordu.
        var first = title.Split(TitleSeparators, 2)[0];

        return !string.IsNullOrWhiteSpace(first)
               && Folded.Contains(TurkceMetin.BasligiKatla(first));
    }

    /// <summary>Reddedilen kayıt için operatöre gösterilecek gerekçe.</summary>
    public static string Explanation(string title) =>
        $"'{title.Trim()}' bir ilanın başlığı değil, birden çok ilanı barındıran bölümün "
        + "başlığıdır. Fırsat kaydı tekil bir ilandan açılır; bu sayfadaki ilanlar ayrı "
        + "ayrı ayrıştırılmalıdır.";
}
