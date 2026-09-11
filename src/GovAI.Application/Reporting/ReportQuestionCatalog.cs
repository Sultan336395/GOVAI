using GovAI.Domain.Common;
using GovAI.Domain.Reporting;

namespace GovAI.Application.Reporting;

/// <summary>Kullanıcıya gösterilen tek bir soru seçeneği.</summary>
public sealed record ReportQuestion(
    ReportQuestionKind Kind,
    /// <summary>Aynı türden birden çok soru olabilir (her çağrı için ayrı); bu onları ayırır.</summary>
    string Key,
    string Text,
    /// <summary>Soru bir çağrıya bağlıysa onun kimliği; cevap o kayıttan üretilir.</summary>
    Guid? OpportunityId);

/// <summary>
/// Rapora özel soru seçeneklerini üretir.
///
/// <para>
/// Sorular <b>rapordan</b> çıkar: "«X» çağrısına neden tam uygun değilim?" sorusu ancak
/// raporda X varsa ve şartlı uygunsa sorulabilir. Sabit bir soru listesi göstermek,
/// kullanıcıyı raporunda karşılığı olmayan bir soruya tıklatıp "bu konuda veri yok"
/// cevabı almasına yol açardı — hak harcanır, bilgi gelmez.
/// </para>
///
/// <para>
/// Üretim <b>deterministiktir</b>: aynı rapor her zaman aynı soruları verir. Model çağrısı
/// yoktur. Soruyu modele ürettirmek iki riski birden açardı: cevabı raporda olmayan bir
/// soru sorulması ve aynı raporun her açılışında farklı sorular gelmesi.
/// </para>
/// </summary>
public static class ReportQuestionCatalog
{
    /// <summary>Aynı türden en çok kaç çağrıya özel soru üretilir.</summary>
    private const int MaximumPerOpportunity = 3;

    public static IReadOnlyList<ReportQuestion> Build(WeeklyReportContent report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sorular = new List<ReportQuestion>();

        void Ekle(ReportQuestionKind tur, string metin, Guid? cagri = null)
        {
            var anahtar = cagri is { } id ? $"{tur}:{id}" : tur.ToString();

            sorular.Add(new ReportQuestion(tur, anahtar, metin, cagri));
        }

        var listelenen = Listelenen(report);

        if (report.Todos.Count > 0)
        {
            Ekle(ReportQuestionKind.EnAcilIs, "Bu hafta en acil ne yapmalıyım?");
        }

        if (report.Risks.Count > 0)
        {
            Ekle(
                ReportQuestionKind.EnEtkiliEksik,
                "Hangi eksiğimi kapatırsam en çok çağrı bana açılır?");
        }

        if (report.Deadlines.Count > 1)
        {
            Ekle(ReportQuestionKind.BasvuruOnceligi, "Hangi çağrıya önce başvurmalıyım?");
        }

        // Çağrıya özel sorular yalnızca ŞARTLI uygun olanlar için. Tam uygun bir çağrıya
        // "neden tam uygun değilim" diye sormak anlamsızdır ve hak harcatır.
        var sartlilar = listelenen
            .Where(o => o.Verdict == EligibilityVerdict.ConditionallyEligible)
            .OrderByDescending(o => o.Score)
            .ThenBy(o => o.Title, StringComparer.Ordinal)
            .Take(MaximumPerOpportunity);

        foreach (var cagri in sartlilar)
        {
            Ekle(
                ReportQuestionKind.CagriNedenSartli,
                $"«{Kisalt(cagri.Title)}» çağrısına neden tam uygun değilim?",
                cagri.OpportunityId);
        }

        if (listelenen.Any(o => o.SectorFit == SectorFit.Unverified))
        {
            Ekle(
                ReportQuestionKind.SektorUyumu,
                "Sektör uyumum neden bazı çağrılarda doğrulanamadı?");
        }

        if (report.Risks.Any(r => r.Kind == ReportRiskKind.MissingDocument))
        {
            Ekle(ReportQuestionKind.EksikBelgeler, "Hangi belgeleri temin etmem gerekiyor?");
        }

        if (report.RegulatoryChanges.Count > 0)
        {
            Ekle(
                ReportQuestionKind.MevzuatEtkisi,
                "Bu haftaki mevzuat değişiklikleri firmamı nasıl etkiliyor?");
        }

        if (report.PastPeriodGaps.Count > 0)
        {
            Ekle(
                ReportQuestionKind.GecmisDonemEksikleri,
                "Geçmiş dönem eksiklerim neden hâlâ listede duruyor?");
        }

        if (report.Risks.Any(r => r.Kind == ReportRiskKind.DataGap))
        {
            Ekle(
                ReportQuestionKind.EksikProfilEtkisi,
                "Profilimdeki eksik bilgiler kararlarımı nasıl etkiliyor?");
        }

        if (report.TechnologyTenders.Count == 0 && listelenen.Count > 0)
        {
            Ekle(
                ReportQuestionKind.TeknolojiIhaleleri,
                "Bu dönemde neden teknoloji veya yazılım ihalesi çıkmadı?");
        }

        return sorular;
    }

    /// <summary>
    /// Bir soruya cevap verildikten sonra açılan sorular.
    ///
    /// <para>
    /// Devam soruları da <b>rapordan</b> gelir: cevabın konusuyla bağlantılı olanlar öne
    /// çıkar. Böylece kullanıcı beş hakkını rastgele değil, bir düşünce zincirini takip
    /// ederek harcar.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ReportQuestion> FollowUps(
        ReportQuestionKind answered,
        WeeklyReportContent report,
        IReadOnlyCollection<string> alreadyAskedKeys)
    {
        var hepsi = Build(report);

        ReportQuestionKind[] sonraki = answered switch
        {
            // "En acil iş" cevabı bir çağrıya işaret eder; doğal devamı o çağrının
            // koşulları ve başvuru sırasıdır.
            ReportQuestionKind.EnAcilIs =>
                [ReportQuestionKind.BasvuruOnceligi, ReportQuestionKind.CagriNedenSartli],

            ReportQuestionKind.EnEtkiliEksik =>
                [ReportQuestionKind.EksikBelgeler, ReportQuestionKind.EksikProfilEtkisi],

            ReportQuestionKind.BasvuruOnceligi =>
                [ReportQuestionKind.CagriNedenSartli, ReportQuestionKind.EnEtkiliEksik],

            ReportQuestionKind.CagriNedenSartli =>
                [ReportQuestionKind.EksikBelgeler, ReportQuestionKind.SektorUyumu],

            ReportQuestionKind.SektorUyumu =>
                [ReportQuestionKind.EksikProfilEtkisi, ReportQuestionKind.TeknolojiIhaleleri],

            ReportQuestionKind.EksikBelgeler =>
                [ReportQuestionKind.GecmisDonemEksikleri, ReportQuestionKind.EnAcilIs],

            ReportQuestionKind.MevzuatEtkisi =>
                [ReportQuestionKind.EnAcilIs, ReportQuestionKind.EksikProfilEtkisi],

            ReportQuestionKind.GecmisDonemEksikleri =>
                [ReportQuestionKind.EksikBelgeler, ReportQuestionKind.EnEtkiliEksik],

            ReportQuestionKind.EksikProfilEtkisi =>
                [ReportQuestionKind.SektorUyumu, ReportQuestionKind.EnEtkiliEksik],

            _ => [ReportQuestionKind.EnAcilIs, ReportQuestionKind.EnEtkiliEksik],
        };

        // Sorulmuş soru tekrar önerilmez: kullanıcı aynı cevabı ikinci kez okumak için
        // hak harcamamalı.
        return hepsi
            .Where(s => sonraki.Contains(s.Kind))
            .Where(s => !alreadyAskedKeys.Contains(s.Key))
            .ToList();
    }

    /// <summary>Raporun listelediği bütün çağrılar; sorular yalnızca bunlardan çıkar.</summary>
    internal static IReadOnlyList<ReportOpportunityItem> Listelenen(WeeklyReportContent report) =>
        report.Supports
            .Concat(report.TechnologyTenders)
            .Concat(report.OtherOpportunities)
            .ToList();

    internal static string Kisalt(string baslik) =>
        baslik.Length <= 60 ? baslik : baslik[..57] + "…";
}
