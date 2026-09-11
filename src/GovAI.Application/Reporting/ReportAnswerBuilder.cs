using System.Globalization;
using System.Text;
using GovAI.Domain.Common;
using GovAI.Domain.Reporting;

namespace GovAI.Application.Reporting;

/// <summary>
/// Soruların cevabını <b>rapordan</b> üretir.
///
/// <para>
/// Cevaplar deterministiktir ve tamamı raporun kendi verisine dayanır. Model çağrısı
/// yoktur; olsaydı üç şey birden riske girerdi: cevap raporla çelişebilir, aynı soru her
/// seferinde farklı cevaplanabilir ve anahtar tanımlı değilken özellik hiç çalışmazdı.
/// Rapor zaten bütün veriyi taşıdığı için modele ihtiyaç da yoktur.
/// </para>
///
/// <para>
/// Cevap <b>uydurmaz</b>. Raporda karşılığı olmayan bir soru sorulursa cevap bunu açıkça
/// söyler; boşluğu tahminle doldurmaz.
/// </para>
/// </summary>
public static class ReportAnswerBuilder
{
    public static string Build(
        ReportQuestion question,
        WeeklyReportContent report)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(report);

        return question.Kind switch
        {
            ReportQuestionKind.EnAcilIs => EnAcilIs(report),
            ReportQuestionKind.EnEtkiliEksik => EnEtkiliEksik(report),
            ReportQuestionKind.BasvuruOnceligi => BasvuruOnceligi(report),
            ReportQuestionKind.CagriNedenSartli => CagriNedenSartli(report, question.OpportunityId),
            ReportQuestionKind.SektorUyumu => SektorUyumu(report),
            ReportQuestionKind.EksikBelgeler => EksikBelgeler(report),
            ReportQuestionKind.MevzuatEtkisi => MevzuatEtkisi(report),
            ReportQuestionKind.GecmisDonemEksikleri => GecmisDonemEksikleri(report),
            ReportQuestionKind.EksikProfilEtkisi => EksikProfilEtkisi(report),
            ReportQuestionKind.TeknolojiIhaleleri => TeknolojiIhaleleri(report),
            _ => "Bu soru için raporda karşılık bulunamadı.",
        };
    }

    // ── Bu hafta en acil ne yapmalıyım? ─────────────────────────────────────

    private static string EnAcilIs(WeeklyReportContent report)
    {
        var acil = report.Todos.Where(t => t.Priority == ReportTodoPriority.Urgent).ToList();
        var isler = acil.Count > 0 ? acil : report.Todos.Take(3).ToList();

        if (isler.Count == 0)
        {
            return "Bu dönem için açık bir iş çıkmadı.";
        }

        var y = new StringBuilder();

        y.Append(acil.Count > 0
            ? $"Acil olarak işaretlenen {Adet(acil.Count, "iş")} var. "
            : "Acil işaretli iş yok; en yakın tarihli işler şunlar. ");

        y.AppendLine("Sıra raporun kendi önceliklendirmesinden geliyor:");
        y.AppendLine();

        var sira = 1;

        foreach (var t in isler)
        {
            y.AppendLine($"{sira++}. {t.Title}");
            y.AppendLine($"   Gerekçe: {t.Reason}");

            if (t.DueAt is { } tarih)
            {
                y.AppendLine($"   Son tarih: {tarih:dd.MM.yyyy}");
            }
        }

        // Aciliyetin eşiği açıkça yazılır: kullanıcı "neden bu acil" diye sormak zorunda
        // kalmasın.
        y.AppendLine();
        y.Append("Bir iş, ilgili çağrının son başvurusuna iki haftadan az kaldıysa acil sayılır.");

        return y.ToString().TrimEnd();
    }

    // ── Hangi eksiğimi kapatırsam en çok çağrı açılır? ──────────────────────

    private static string EnEtkiliEksik(WeeklyReportContent report)
    {
        if (report.Risks.Count == 0)
        {
            return "Raporda başvuruyu engelleyen bir eksik görünmüyor.";
        }

        // Etkilenen çağrı sayısı en yüksek olan eksik, kapatıldığında en çok kapıyı açar.
        var sirali = report.Risks
            .OrderByDescending(r => r.AffectedOpportunityCount)
            .ThenBy(r => r.Subject, StringComparer.Ordinal)
            .Take(3)
            .ToList();

        var y = new StringBuilder();
        var ilk = sirali[0];

        y.AppendLine(
            $"En çok kapı açan eksik: **{ilk.Subject}** — {Adet(ilk.AffectedOpportunityCount, "çağrıyı")} etkiliyor.");

        if (ilk.Action is { Length: > 0 } aksiyon)
        {
            y.AppendLine($"Yapılması gereken: {aksiyon}");
        }
        else
        {
            // Sistem öneri üretemediyse uydurulmaz.
            y.AppendLine("Bu eksik için sistem bir aksiyon önerisi üretemedi; danışman değerlendirmesi gerekiyor.");
        }

        if (sirali.Count > 1)
        {
            y.AppendLine();
            y.AppendLine("Sıradaki eksikler:");

            foreach (var r in sirali.Skip(1))
            {
                y.AppendLine($"- {r.Subject} ({Adet(r.AffectedOpportunityCount, "çağrı")}) — {r.KindLabel}");
            }
        }

        y.AppendLine();
        y.Append(
            "Sıralama, eksiğin kaç çağrıyı etkilediğine göre yapıldı; tek bir çağrıyı "
            + "etkileyen bir eksik daha kolay kapatılabilir olsa da toplam etkisi düşüktür.");

        return y.ToString().TrimEnd();
    }

    // ── Hangi çağrıya önce başvurmalıyım? ──────────────────────────────────

    private static string BasvuruOnceligi(WeeklyReportContent report)
    {
        if (report.Deadlines.Count == 0)
        {
            return "Yakın dönemde kapanan bir çağrı yok; başvuru sırası için acele gerekmiyor.";
        }

        // Sıralama iki ölçüte bakar: önce kapanma yakınlığı, sonra uygunluk.
        var sirali = report.Deadlines
            .OrderBy(d => d.DaysRemaining)
            .ThenByDescending(d => d.Score)
            .Take(3)
            .ToList();

        var y = new StringBuilder();
        var ilk = sirali[0];

        y.AppendLine($"Önce **{ilk.Title}** çağrısına bakın.");
        y.AppendLine(
            $"Son başvuruya {ilk.DaysRemaining} gün kaldı; uygunluk skoru {Sayi(ilk.Score)}, "
            + $"karar: {ilk.VerdictLabel}, {ilk.SectorFitLabel}.");

        if (sirali.Count > 1)
        {
            y.AppendLine();
            y.AppendLine("Sonraki sıra:");

            foreach (var d in sirali.Skip(1))
            {
                y.AppendLine(
                    $"- {d.Title} — {d.DaysRemaining} gün, skor {Sayi(d.Score)}, {d.VerdictLabel}");
            }
        }

        y.AppendLine();
        y.Append(
            "Sıra önce kapanma tarihine, eşitlikte skora göre kuruldu: yüksek skorlu ama "
            + "iki ay sonra kapanan bir çağrı, düşük skorlu ama bu hafta kapanandan sonra gelir.");

        return y.ToString().TrimEnd();
    }

    // ── Belirli bir çağrıya neden tam uygun değilim? ────────────────────────

    private static string CagriNedenSartli(WeeklyReportContent report, Guid? opportunityId)
    {
        var cagri = ReportQuestionCatalog.Listelenen(report)
            .FirstOrDefault(o => o.OpportunityId == opportunityId);

        if (cagri is null)
        {
            return "Bu çağrı raporda bulunamadı. Rapor yeniden üretilmiş olabilir.";
        }

        var y = new StringBuilder();

        y.AppendLine($"**{cagri.Title}** ({cagri.CategoryLabel})");
        y.AppendLine($"Skor: {Sayi(cagri.Score)} · Karar: {cagri.VerdictLabel} · {cagri.SectorFitLabel}");
        y.AppendLine();

        if (cagri.MissingConditions.Count > 0)
        {
            y.AppendLine("Kapatılabilir eksikler:");

            foreach (var eksik in cagri.MissingConditions)
            {
                y.AppendLine($"- {eksik}");
            }

            y.AppendLine();
            y.AppendLine("Bu koşullar sağlandığında karar \"uygun\"a döner.");
        }
        else if (cagri.Verdict == EligibilityVerdict.ConditionallyEligible)
        {
            // "Şartlı uygun" ama listelenmiş bir eksik yoksa sebep veri eksikliğidir.
            // Bunu söylememek, kullanıcıyı olmayan bir koşulu aramaya iter.
            y.AppendLine(
                "Bu çağrıda karşılanmayan bir koşul listelenmedi. Karar \"şartlı uygun\" "
                + "çünkü bazı koşullar firma profilindeki eksik bilgi yüzünden "
                + "değerlendirilemedi — sistem eksik veriyi \"hayır\" saymaz, kararı "
                + "belirsiz bırakır.");

            var veriEksikleri = report.Risks
                .Where(r => r.Kind == ReportRiskKind.DataGap)
                .Take(3)
                .ToList();

            if (veriEksikleri.Count > 0)
            {
                y.AppendLine();
                y.AppendLine("Profilde eksik görünen bilgiler:");

                foreach (var r in veriEksikleri)
                {
                    y.AppendLine($"- {r.Subject}");
                }
            }
        }
        else
        {
            y.AppendLine("Bu çağrıda kapatılması gereken bir eksik bulunmuyor.");
        }

        if (cagri.DaysToDeadline is { } gun)
        {
            y.AppendLine();
            y.Append(gun >= 0
                ? $"Son başvuruya {gun} gün kaldı."
                : "Son başvuru tarihi geçti.");
        }

        return y.ToString().TrimEnd();
    }

    // ── Sektör uyumu ────────────────────────────────────────────────────────

    private static string SektorUyumu(WeeklyReportContent report)
    {
        var dogrulanamayan = ReportQuestionCatalog.Listelenen(report)
            .Where(o => o.SectorFit == SectorFit.Unverified)
            .ToList();

        if (dogrulanamayan.Count == 0)
        {
            return "Raporda sektör uyumu doğrulanamayan çağrı yok.";
        }

        var y = new StringBuilder();

        y.AppendLine(
            $"{Adet(dogrulanamayan.Count, "çağrıda")} sektör uyumu doğrulanamadı. "
            + "Bu \"sektörünüz uymuyor\" demek DEĞİLDİR.");
        y.AppendLine();
        y.AppendLine(
            "Sebep şu: çağrı metninden sektöre dair bir koşul çıkarılamadı. Sistem böyle "
            + "durumda tam puan vermez — kuralsız her ilan listenin başına çıkardı — ama "
            + "firmayı da elemez; kayıt listede kalır ve \"doğrulanamadı\" etiketiyle görünür.");
        y.AppendLine();
        y.AppendLine("Etkilenen çağrılar:");

        foreach (var o in dogrulanamayan.Take(5))
        {
            y.AppendLine($"- {o.Title} (skor {Sayi(o.Score)})");
        }

        y.AppendLine();
        y.Append(
            "Firma kartındaki ana sektör ve NACE kodlarının dolu ve birbiriyle tutarlı "
            + "olması, doğrulanabilen çağrılarda skoru yükseltir.");

        return y.ToString().TrimEnd();
    }

    // ── Eksik belgeler ──────────────────────────────────────────────────────

    private static string EksikBelgeler(WeeklyReportContent report)
    {
        var belgeler = report.Risks
            .Where(r => r.Kind == ReportRiskKind.MissingDocument)
            .OrderByDescending(r => r.AffectedOpportunityCount)
            .ToList();

        if (belgeler.Count == 0)
        {
            return "Raporda eksik zorunlu belge görünmüyor.";
        }

        var y = new StringBuilder();

        y.AppendLine($"{Adet(belgeler.Count, "zorunlu belge")} eksik görünüyor:");
        y.AppendLine();

        foreach (var b in belgeler)
        {
            y.AppendLine($"**{b.Subject}** — {Adet(b.AffectedOpportunityCount, "çağrıyı")} etkiliyor");
            y.AppendLine(b.Action is { Length: > 0 } aksiyon
                ? $"   {aksiyon}"
                : "   Temin yolu için danışman değerlendirmesi gerekiyor.");
        }

        y.AppendLine();
        y.Append(
            "Belge hazırlık düzeyi skorun yedi boyutundan biridir: belgeler tamamlandıkça "
            + "aynı çağrılardaki skorunuz yükselir.");

        return y.ToString().TrimEnd();
    }

    // ── Mevzuat etkisi ──────────────────────────────────────────────────────

    private static string MevzuatEtkisi(WeeklyReportContent report)
    {
        if (report.RegulatoryChanges.Count == 0)
        {
            return "Bu hafta firmayı ilgilendiren mevzuat değişikliği yayımlanmadı.";
        }

        var y = new StringBuilder();

        y.AppendLine($"Bu hafta {Adet(report.RegulatoryChanges.Count, "mevzuat kaydı")} yayımlandı:");
        y.AppendLine();

        foreach (var m in report.RegulatoryChanges.Take(5))
        {
            y.AppendLine($"- **{m.Title}** — {m.Authority}, {m.PublishedAt:dd.MM.yyyy}");

            if (m.Summary is { Length: > 0 } ozet)
            {
                y.AppendLine($"  {ozet}");
            }
        }

        y.AppendLine();

        // Ürünün sınırı açıkça söylenir: mevzuatın firmaya etkisi HESAPLANMIYOR.
        // Bunu söylememek, kullanıcının "sistem baktı, sorun yok" sanmasına yol açar.
        y.Append(
            "Önemli sınır: sistem bu kayıtları resmî kaynaktan toplar ve doğrular, ama "
            + "firmanıza özel etkisini **hesaplamaz**. Mevzuat uyulacak bir kuraldır, "
            + "başvurulacak bir çağrı değildir; etki değerlendirmesi danışman işidir.");

        return y.ToString().TrimEnd();
    }

    // ── Geçmiş dönem eksikleri ──────────────────────────────────────────────

    private static string GecmisDonemEksikleri(WeeklyReportContent report)
    {
        if (report.PastPeriodGaps.Count == 0)
        {
            return "Başvuru süresi geçmiş çağrılardan kalan bir eksik yok.";
        }

        var y = new StringBuilder();

        y.AppendLine(
            $"{Adet(report.PastPeriodGaps.Count, "eksik")} başvuru süresi geçmiş çağrılardan kalıyor.");
        y.AppendLine();
        y.AppendLine(
            "Bu satırlar için **şimdi yapılacak bir iş yoktur**: ilgili çağrıların başvuru "
            + "süresi doldu. Ama eksiğin kendisi kaybolmadı — aynı koşulu isteyen yeni bir "
            + "çağrı açıldığında güncel risk listesine geçerler.");
        y.AppendLine();

        foreach (var g in report.PastPeriodGaps.Take(5))
        {
            y.AppendLine($"- {g.Subject} ({g.KindLabel})");
        }

        return y.ToString().TrimEnd();
    }

    // ── Eksik profil bilgisinin etkisi ──────────────────────────────────────

    private static string EksikProfilEtkisi(WeeklyReportContent report)
    {
        var bosluklar = report.Risks
            .Where(r => r.Kind == ReportRiskKind.DataGap)
            .OrderByDescending(r => r.AffectedOpportunityCount)
            .ToList();

        if (bosluklar.Count == 0)
        {
            return "Firma profilinde değerlendirmeyi engelleyen eksik bilgi görünmüyor.";
        }

        var y = new StringBuilder();

        y.AppendLine(
            $"{Adet(bosluklar.Count, "profil alanı")} eksik ve bu alanlara bağlı koşullar "
            + "değerlendirilemiyor.");
        y.AppendLine();
        y.AppendLine(
            "Eksik bilgi sizi **elemez**: sistem \"bilmiyorum\" ile \"hayır\"ı ayrı tutar. "
            + "Ama kararı belirsiz bırakır ve skorun güvenilirliğini düşürür — yani "
            + "gerçekte uygun olduğunuz bir çağrı listenin alt sıralarında kalabilir.");
        y.AppendLine();

        foreach (var b in bosluklar)
        {
            y.AppendLine($"**{b.Subject}** — {Adet(b.AffectedOpportunityCount, "çağrıyı")} etkiliyor");

            if (b.Action is { Length: > 0 } aksiyon)
            {
                y.AppendLine($"   {aksiyon}");
            }
        }

        y.AppendLine();
        y.Append(
            "Bu alanlar firma kartından doldurulabilir; ERP bağlantısı kurulduysa çoğu "
            + "kendiliğinden gelir.");

        return y.ToString().TrimEnd();
    }

    // ── Teknoloji ihaleleri ─────────────────────────────────────────────────

    private static string TeknolojiIhaleleri(WeeklyReportContent report)
    {
        var y = new StringBuilder();

        y.AppendLine("Bu dönemde teknoloji veya yazılım konulu açık ihale bulunamadı.");
        y.AppendLine();
        y.AppendLine(
            "Bu bölüm başlıkta kelime aramaz. Bir ihalenin teknoloji sayılması için "
            + "ayrıştırıcının ilanın **konusundan** ürettiği sektör kuralının bilişim "
            + "alanına denk gelmesi gerekir. Böylece başlığında \"yazılım\" geçen bir "
            + "temizlik ihalesi listeye girmez.");

        var digerleri = report.OtherOpportunities.Count;

        if (digerleri > 0)
        {
            y.AppendLine();
            y.Append(
                $"Bu dönemde {Adet(digerleri, "başka açık çağrı")} var ve \"Diğer Açık Çağrı "
                + "ve İhaleler\" bölümünde listeleniyor.");
        }

        return y.ToString().TrimEnd();
    }

    // ── Ortak ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Türkçe sayı biçimi.
    ///
    /// <para>
    /// Kültür <b>adla aranmaz</b>, elle kurulur: proje <c>InvariantGlobalization</c> ile
    /// derleniyor (bkz. <c>Directory.Build.props</c>) ve <c>GetCultureInfo("tr-TR")</c>
    /// çalışma anında istisna atar. Ad arayan bir satır burada derlenir, testte geçer
    /// ama üretimde cevabı hiç üretemez.
    /// </para>
    /// </summary>
    private static readonly NumberFormatInfo TurkceSayi = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
    };

    /// <summary>Türkçede sayıdan sonra çoğul eki kullanılmaz: "3 çağrılar" yanlıştır.</summary>
    private static string Adet(int sayi, string ad) =>
        $"{sayi.ToString("N0", TurkceSayi)} {ad}";

    private static string Sayi(decimal deger) =>
        deger.ToString("0.0", CultureInfo.InvariantCulture);
}
