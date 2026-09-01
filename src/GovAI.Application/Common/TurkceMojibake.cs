using System.Text;

namespace GovAI.Application.Common;

/// <summary>
/// Yanlış karakter kümesiyle okunmuş Türkçe metni <b>görüntülemek için</b> onarır (Faz 2).
///
/// <para>
/// İndirici artık hiçbir kümeyle temiz çözülemeyen içeriği kaydetmiyor
/// (<c>SafeDocumentDownloader</c>), ama o düzeltmeden <b>önce</b> toplanmış kayıtlar
/// veritabanında bozuk başlıklarla duruyor: "ARTIRMA, EKSÝLTME VE ÝHALE ÝLÂNLARI".
/// Karantina ekranında inceleyicinin gördüğü metin budur ve bu hâliyle kaydın ne
/// olduğu anlaşılamaz.
/// </para>
///
/// <para>
/// <b>Ham belge değiştirilmez.</b> Onarım yalnızca görüntülenen başlığa uygulanır;
/// belgenin gövdesi, sürümleri ve özeti kanıt olarak olduğu gibi kalır. Kanıt
/// zincirinin bozulmaması için ham veriye dokunulmaz.
/// </para>
///
/// <para>
/// İki bozulma ailesi ele alınır:
/// </para>
/// <list type="number">
///   <item>UTF-8 gövdenin tek baytlı bir kümeymiş gibi okunması — <c>ÅŸ</c>, <c>Ä°</c>, <c>Ã¼</c>.</item>
///   <item>Windows-1254 gövdenin ISO-8859-1 sanılması — <c>Ý</c>, <c>Þ</c>, <c>ð</c>.</item>
/// </list>
///
/// <para>
/// Kural <b>ihtiyatlıdır</b>: onarılmış aday özgün metinden kesin olarak daha az şüpheli
/// karakter içermiyorsa özgün metin korunur. Şüphede kalınca dokunmamak, doğru bir
/// başlığı bozmaktan iyidir.
/// </para>
/// </summary>
public static class TurkceMojibake
{
    static TurkceMojibake() =>
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    /// <summary>Resmî Türkçe metinde beklenen karakterler. Dışındakiler şüpheli sayılır.</summary>
    private const string Beklenen =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
        + "abcdefghijklmnopqrstuvwxyz"
        + "ÇĞİÖŞÜçğıöşüÂÎÛâîû"
        + "0123456789"
        + " \t\r\n"
        + ".,;:!?'\"()[]{}<>«»/\\|-–—_+*=%&@#§°²³·…’‘“”"
        + "₺€$";

    private static readonly System.Collections.Frozen.FrozenSet<char> BeklenenKume =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(Beklenen);

    /// <summary>
    /// Metni onarır. Onarılamıyorsa ya da onarım daha iyi değilse metin <b>aynen</b> döner.
    /// </summary>
    public static string Onar(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin))
        {
            return metin ?? string.Empty;
        }

        var ozgunSkor = SupheliSayisi(metin);

        // Hiç şüpheli karakter yoksa metin zaten sağlam; dokunma.
        if (ozgunSkor == 0)
        {
            return metin;
        }

        var enIyi = metin;
        var enIyiSkor = ozgunSkor;

        foreach (var aday in Adaylar(metin))
        {
            var skor = SupheliSayisi(aday);

            // Kesin olarak daha iyi olmalı; eşitlik özgün metni korur.
            if (skor < enIyiSkor)
            {
                enIyi = aday;
                enIyiSkor = skor;
            }
        }

        return enIyi;
    }

    /// <summary>
    /// Metin görüntülemeye uygun mu? Karantina ekranı bozuk başlığı ayrıca işaretleyebilsin
    /// diye dışarı açılır.
    /// </summary>
    public static bool Bozuk(string? metin) =>
        !string.IsNullOrWhiteSpace(metin) && SupheliSayisi(metin) > 0;

    private static IEnumerable<string> Adaylar(string metin)
    {
        // Tek baytlı kümeye geri yazılamayan metin bu ailelerden gelmiyor demektir.
        if (metin.Any(c => c > 0xFF))
        {
            yield break;
        }

        var baytlar = Encoding.Latin1.GetBytes(metin);

        // Aile 1: UTF-8 gövde tek baytlı sanılmış.
        yield return Encoding.UTF8.GetString(baytlar);

        // Aile 2: Windows-1254 gövde ISO-8859-1 sanılmış.
        yield return Encoding.GetEncoding(1254).GetString(baytlar);
    }

    private static int SupheliSayisi(string metin)
    {
        var sayi = 0;

        foreach (var ch in metin)
        {
            if (!BeklenenKume.Contains(ch))
            {
                sayi++;
            }
        }

        return sayi;
    }
}
