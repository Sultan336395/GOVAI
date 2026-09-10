using System.Globalization;
using System.Net;
using System.Text;

namespace GovAI.Application.Reporting;

/// <summary>
/// Raporun PDF gövdesi.
///
/// <para>
/// PDF üretici (QuestPDF) blok bazlı çalışır: başlık, paragraf ve tablo tanır. Buradaki
/// HTML bir web sayfası değil, o bloklara çevrilecek ara biçimdir — süslemesi yoktur,
/// çünkü PDF tarafında karşılığı da yoktur.
/// </para>
///
/// <para>
/// Boş bölüm <b>atlanmaz</b>: notu yazılır. Raporda görünmeyen bir bölüm, kullanıcıya
/// o konunun hiç incelenmediğini düşündürür.
/// </para>
/// </summary>
public static class WeeklyReportHtml
{
    public static string Build(WeeklyReportContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var html = new StringBuilder();
        var h = content.Header;

        html.Append($"<h1>{Kod(h.CompanyName)} — Haftalık Rapor</h1>")
            .Append($"<p>Dönem: {h.PeriodStart:dd.MM.yyyy} – {h.PeriodEnd:dd.MM.yyyy}</p>")
            .Append($"<p>Değerlendirilen çağrı: {h.EvaluatedOpportunityCount} · ")
            .Append($"Uygun: {h.EligibleCount} · ")
            .Append($"Kararı belirsiz: {h.UndeterminedCount}</p>");

        Bolum(html, "En Uygun Fon, Hibe ve Teşvikler",
            content.Supports,
            ["Çağrı", "Kurum", "Tür", "Skor", "Karar", "Son Başvuru"],
            i =>
            [
                i.Title, i.Publisher, i.CategoryLabel,
                Sayi(i.Score), i.VerdictLabel, Tarih(i.Deadline),
            ]);

        Bolum(html, "Teknoloji ve Yazılım İhaleleri",
            content.TechnologyTenders,
            ["İhale", "İdare", "Skor", "Karar", "Son Başvuru"],
            i => [i.Title, i.Publisher, Sayi(i.Score), i.VerdictLabel, Tarih(i.Deadline)]);

        Bolum(html, "Mevzuat Değişiklikleri",
            content.RegulatoryChanges,
            ["Düzenleme", "Kurum", "Yayım Tarihi"],
            i => [i.Title, i.Authority, Tarih(i.PublishedAt)]);

        Bolum(html, "Riskler ve Zorunlu Aksiyonlar",
            content.Risks,
            ["Cinsi", "Konu", "Durum", "Yapılacak", "Etkilenen Çağrı"],
            i =>
            [
                i.KindLabel, i.Subject, i.Description,
                i.Action ?? "—", i.AffectedOpportunityCount.ToString(CultureInfo.InvariantCulture),
            ]);

        Bolum(html, "Son Başvuru Takvimi",
            content.Deadlines,
            ["Çağrı", "Son Başvuru", "Kalan Gün", "Skor", "Karar"],
            i =>
            [
                i.Title, Tarih(i.Deadline),
                i.DaysRemaining.ToString(CultureInfo.InvariantCulture),
                Sayi(i.Score), i.VerdictLabel,
            ]);

        Bolum(html, "Önceliklendirilmiş Yapılacaklar",
            content.Todos,
            ["Öncelik", "İş", "Gerekçe", "Tarih"],
            i => [i.PriorityLabel, i.Title, i.Reason, Tarih(i.DueAt)]);

        if (content.Notes.Count > 0)
        {
            html.Append("<h2>Notlar</h2>");

            foreach (var not in content.Notes)
            {
                html.Append($"<p>· {Kod(not)}</p>");
            }
        }

        return html.ToString();
    }

    private static void Bolum<T>(
        StringBuilder html,
        string baslik,
        IReadOnlyList<T> satirlar,
        string[] basliklar,
        Func<T, string[]> hucreler)
    {
        html.Append($"<h2>{Kod(baslik)}</h2>");

        if (satirlar.Count == 0)
        {
            html.Append("<p>Bu dönemde kayıt bulunmadı.</p>");
            return;
        }

        html.Append("<table><tr>");

        foreach (var b in basliklar)
        {
            html.Append($"<th>{Kod(b)}</th>");
        }

        html.Append("</tr>");

        foreach (var satir in satirlar)
        {
            html.Append("<tr>");

            foreach (var hucre in hucreler(satir))
            {
                html.Append($"<td>{Kod(hucre)}</td>");
            }

            html.Append("</tr>");
        }

        html.Append("</table>");
    }

    private static string Tarih(DateTimeOffset? deger) =>
        deger?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "—";

    private static string Sayi(decimal deger) =>
        deger.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Kod(string? deger) => WebUtility.HtmlEncode(deger ?? string.Empty);
}

/// <summary>
/// Raporun Excel gövdesi.
///
/// <para>
/// Excel tek sayfadır ve her satır hangi bölümden geldiğini <b>ilk sütunda</b> söyler.
/// Altı ayrı sayfa yerine tek sayfa seçilmesinin sebebi kullanımdır: danışman dosyayı
/// süzüp sıralıyor, sayfalar arasında gezinmiyor.
/// </para>
/// </summary>
public static class WeeklyReportSheet
{
    public static IReadOnlyList<string> Headers =>
    [
        "Bölüm", "Başlık", "Kurum", "Tür / Cinsi", "Skor",
        "Karar", "Tarih", "Kalan Gün", "Açıklama",
    ];

    public static IReadOnlyList<IReadOnlyList<object?>> Rows(WeeklyReportContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rows = new List<IReadOnlyList<object?>>();

        foreach (var i in content.Supports)
        {
            rows.Add(["Fon / Hibe / Teşvik", i.Title, i.Publisher, i.CategoryLabel, i.Score,
                i.VerdictLabel, i.Deadline?.DateTime, i.DaysToDeadline,
                i.MissingConditions.Count == 0 ? "Eksik koşul yok" : string.Join("; ", i.MissingConditions)]);
        }

        foreach (var i in content.TechnologyTenders)
        {
            rows.Add(["Teknoloji İhalesi", i.Title, i.Publisher, i.CategoryLabel, i.Score,
                i.VerdictLabel, i.Deadline?.DateTime, i.DaysToDeadline,
                i.MissingConditions.Count == 0 ? "Eksik koşul yok" : string.Join("; ", i.MissingConditions)]);
        }

        foreach (var i in content.RegulatoryChanges)
        {
            rows.Add(["Mevzuat Değişikliği", i.Title, i.Authority, null, null,
                null, i.PublishedAt.DateTime, null, i.Summary]);
        }

        foreach (var i in content.Risks)
        {
            rows.Add(["Risk", i.Subject, null, i.KindLabel, null,
                null, null, null, i.Action ?? i.Description]);
        }

        foreach (var i in content.Deadlines)
        {
            rows.Add(["Son Başvuru", i.Title, null, null, i.Score,
                i.VerdictLabel, i.Deadline.DateTime, i.DaysRemaining, null]);
        }

        foreach (var i in content.Todos)
        {
            rows.Add(["Yapılacak", i.Title, null, i.PriorityLabel, null,
                null, i.DueAt?.DateTime, null, i.Reason]);
        }

        // Boş rapor da bir cevaptır: kullanıcı indirdiği dosyada hiçbir satır
        // görmezse dosyanın bozuk olduğunu düşünür.
        if (rows.Count == 0)
        {
            rows.Add(["Bilgi", "Bu dönemde raporlanacak kayıt bulunmadı.", null, null, null,
                null, null, null, string.Join(" ", content.Notes)]);
        }

        return rows;
    }
}
