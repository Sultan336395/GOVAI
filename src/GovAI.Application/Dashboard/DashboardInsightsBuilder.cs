using GovAI.Domain.Assessments;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Integrations;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Reporting;
using GovAI.Domain.Tenders;

namespace GovAI.Application.Dashboard;

/// <summary>
/// Dashboard'un dört ek bölümünü kayıtlı veriden kurar.
///
/// <para>
/// Servisten ayrı ve <b>saf</b>tır: girdisi yüklenmiş kayıtlar ile <c>asOf</c>'tur,
/// içeride ne veri okunur ne tarih alınır. Böylece aynı girdi her zaman aynı tabloyu
/// verir ve davranış yedi sahte depo kurmadan sınanabilir.
/// </para>
///
/// <para>
/// Burada <b>hiçbir şey yeniden skorlanmaz</b>; karar ve puan her zaman
/// <c>EligibilityEngine</c>'den gelir (CLAUDE.md §2.1).
/// </para>
/// </summary>
public static class DashboardInsightsBuilder
{
    /// <summary>Aksiyon listesinin üst sınırı. Uzun liste önceliklendirmeyi yok eder.</summary>
    public const int MaximumActions = 8;

    /// <summary>Eğilim çizgisindeki hafta sayısı.</summary>
    public const int TrendWeeks = 8;

    public const int ActivityLimit = 12;

    /// <summary>Bu gün sayısının altındaki çağrı "acil" sayılır.</summary>
    public const int UrgentDayThreshold = 15;

    public static DashboardInsightsDto Build(
        Company company,
        IReadOnlyList<EligibilityAssessment> assessments,
        IReadOnlyList<TenderPursuit> pursuits,
        IReadOnlyList<WeeklyReport> reports,
        IReadOnlyList<ReportInquiry> inquiries,
        ErpConnection? erp,
        IReadOnlyDictionary<Guid, Opportunity> opportunities,
        DateTimeOffset asOf)
    {
        var bugun = DateOnly.FromDateTime(asOf.UtcDateTime);

        var profil = Profil(company, assessments, bugun);
        var aksiyonlar = Aksiyonlar(assessments, pursuits, opportunities, profil, asOf);
        var huni = Huni(assessments, pursuits);

        var egilim = reports
            .OrderBy(r => r.PeriodStart)
            .Select(r => new TrendPointDto(
                r.PeriodStart, r.OpportunityCount, r.TenderCount, r.RiskCount, r.UrgentDeadlineCount))
            .ToList();

        var hareketler = Hareketler(company, pursuits, reports, inquiries, erp, assessments, opportunities);

        return new DashboardInsightsDto(
            company.Id,
            aksiyonlar,
            profil,
            huni,
            egilim,
            hareketler,
            Notlar(aksiyonlar, egilim, hareketler, assessments));
    }

    private static ProfileCompletenessDto Profil(
        Company company,
        IReadOnlyList<EligibilityAssessment> degerlendirmeler,
        DateOnly bugun)
    {
        var alanlar = ProfileCompleteness.Evaluate(company, bugun);

        return new ProfileCompletenessDto(
            ProfileCompleteness.Percentage(alanlar),
            alanlar.Count(a => a.IsKnown),
            alanlar.Count,
            alanlar.Where(a => !a.IsKnown).Select(a => a.Label).ToList(),
            degerlendirmeler.Sum(a => a.DataGapCount));
    }

    /// <summary>
    /// "Bugün ne yapmalıyım" listesi.
    ///
    /// <para>
    /// Kaynak dört yerdir: kapanmak üzere olan uygun çağrılar, eksik zorunlu belgeler,
    /// süresi geçtiği hâlde kapatılmamış ihale takipleri ve profil eksikleri. Hepsi
    /// kayıtlı veriden çıkar; hiçbiri burada hesaplanmaz.
    /// </para>
    /// </summary>
    private static IReadOnlyList<DashboardActionDto> Aksiyonlar(
        IReadOnlyList<EligibilityAssessment> degerlendirmeler,
        IReadOnlyList<TenderPursuit> takipler,
        IReadOnlyDictionary<Guid, Opportunity> cagrilar,
        ProfileCompletenessDto profil,
        DateTimeOffset simdi)
    {
        var liste = new List<DashboardActionDto>();

        // 1) Süresi yaklaşan ve gerçekten başvurulabilir çağrılar.
        var yaklasan = degerlendirmeler
            .Where(a => a.Verdict is EligibilityVerdict.Eligible or EligibilityVerdict.ConditionallyEligible)
            .Select(a => (Degerlendirme: a, Cagri: cagrilar.GetValueOrDefault(a.OpportunityId)))
            .Where(x => x.Cagri?.Deadline is { } son && son > simdi
                        && (son - simdi).TotalDays <= UrgentDayThreshold)
            .OrderBy(x => x.Cagri!.Deadline)
            .Take(3);

        foreach (var (degerlendirme, cagri) in yaklasan)
        {
            var kalan = (int)Math.Floor((cagri!.Deadline!.Value - simdi).TotalDays);

            liste.Add(new DashboardActionDto(
                DashboardActionPriority.Urgent,
                DashboardActionLabels.Of(DashboardActionPriority.Urgent),
                $"Başvuruyu hazırla: {cagri.Title}",
                $"Son başvuruya {kalan} gün kaldı; karar: {GovAI.Application.Reporting.VerdictLabels.Of(degerlendirme.Verdict)}.",
                $"/matches/{degerlendirme.Id}",
                cagri.Deadline,
                kalan));
        }

        // 2) Zorunlu belgesi eksik olan çağrılar. Belge temini zaman alır; tarih
        // beklenmeden görünmelidir.
        var belgesiz = degerlendirmeler
            .Where(a => a.MissingMandatoryDocumentCount > 0)
            .OrderByDescending(a => a.FinalScore)
            .Take(2);

        foreach (var degerlendirme in belgesiz)
        {
            var cagri = cagrilar.GetValueOrDefault(degerlendirme.OpportunityId);

            liste.Add(new DashboardActionDto(
                DashboardActionPriority.High,
                DashboardActionLabels.Of(DashboardActionPriority.High),
                $"Eksik belgeleri tamamla: {cagri?.Title ?? "çağrı"}",
                $"{degerlendirme.MissingMandatoryDocumentCount} zorunlu belge eksik.",
                $"/matches/{degerlendirme.Id}",
                cagri?.Deadline,
                null));
        }

        // 3) Süresi geçtiği hâlde kapatılmamış takipler.
        var acikKalan = takipler
            .Where(p => !p.IsClosed
                        && cagrilar.GetValueOrDefault(p.OpportunityId)?.Deadline is { } son
                        && son < simdi)
            .Take(2);

        foreach (var takip in acikKalan)
        {
            liste.Add(new DashboardActionDto(
                DashboardActionPriority.High,
                DashboardActionLabels.Of(DashboardActionPriority.High),
                $"Takibi sonuçlandır: {cagrilar.GetValueOrDefault(takip.OpportunityId)?.Title ?? "ihale"}",
                "Son başvuru tarihi geçti ama süreç kapatılmadı.",
                "/tenders",
                null,
                null));
        }

        // 4) Profil eksikleri. Eksik alan kararı "belirsiz" yapar, firmayı elemez —
        // bu yüzden acil değil, ama kapatılmazsa hiçbir çağrıda net cevap alınmaz.
        if (profil.MissingLabels.Count > 0)
        {
            liste.Add(new DashboardActionDto(
                DashboardActionPriority.Normal,
                DashboardActionLabels.Of(DashboardActionPriority.Normal),
                "Firma profilindeki eksikleri tamamla",
                profil.DataGapCount > 0
                    ? $"{profil.MissingLabels.Count} alan girilmemiş; {profil.DataGapCount} koşulda karar verilemedi."
                    : $"{profil.MissingLabels.Count} alan girilmemiş.",
                "/company",
                null,
                null));
        }

        return liste
            .OrderBy(a => a.Priority)
            .ThenBy(a => a.DaysRemaining ?? int.MaxValue)
            .Take(MaximumActions)
            .ToList();
    }

    private static OpportunityFunnelDto Huni(
        IReadOnlyList<EligibilityAssessment> degerlendirmeler,
        IReadOnlyList<TenderPursuit> takipler) =>
        new(
            degerlendirmeler.Count,
            degerlendirmeler.Count(a => a.Verdict == EligibilityVerdict.Eligible),
            degerlendirmeler.Count(a => a.Verdict == EligibilityVerdict.ConditionallyEligible),
            takipler.Count,
            takipler.Count(p => p.Status is TenderPursuitStatus.TeklifVerildi or TenderPursuitStatus.Sonuclandi),
            takipler.Count(p => p.Outcome == TenderOutcome.Kazanildi));

    /// <summary>
    /// Son hareketler.
    ///
    /// <para>
    /// Akış denetim kaydından değil, <b>kayıtların kendisinden</b> kurulur: denetim
    /// kaydı eylem adlarına göre filtrelenir ve bir eylemin adı değiştiğinde akış
    /// sessizce boşalırdı. Buradaki her satırın arkasında duran bir kayıt vardır.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ActivityItemDto> Hareketler(
        Company company,
        IReadOnlyList<TenderPursuit> takipler,
        IReadOnlyList<WeeklyReport> raporlar,
        IReadOnlyList<ReportInquiry> sorular,
        ErpConnection? baglanti,
        IReadOnlyList<EligibilityAssessment> degerlendirmeler,
        IReadOnlyDictionary<Guid, Opportunity> cagrilar)
    {
        var liste = new List<ActivityItemDto>();

        foreach (var olay in takipler.SelectMany(p => p.Events.Select(e => (Takip: p, Olay: e))))
        {
            liste.Add(new ActivityItemDto(
                olay.Olay.At,
                "TenderPursuit",
                $"İhale aşaması: {GovAI.Application.Tenders.TenderLabels.Of(olay.Olay.ToStatus)}",
                cagrilar.GetValueOrDefault(olay.Takip.OpportunityId)?.Title,
                "/tenders",
                olay.Olay.By));
        }

        foreach (var rapor in raporlar)
        {
            liste.Add(new ActivityItemDto(
                rapor.GeneratedAt,
                "WeeklyReport",
                rapor.Trigger == ReportTrigger.Scheduled
                    ? "Haftalık rapor otomatik üretildi"
                    : "Haftalık rapor elle üretildi",
                $"{rapor.PeriodStart:dd.MM.yyyy} – {rapor.PeriodEnd:dd.MM.yyyy}",
                $"/weekly-reports/{rapor.Id}",
                null));
        }

        foreach (var soru in sorular)
        {
            liste.Add(new ActivityItemDto(
                soru.AskedAt,
                "ReportInquiry",
                "Rapora soru soruldu",
                soru.QuestionText,
                $"/weekly-reports/{soru.WeeklyReportId}",
                soru.AskedBy));
        }

        if (company.UpdatedAt is { } guncelleme)
        {
            liste.Add(new ActivityItemDto(
                guncelleme,
                "CompanyProfile",
                "Firma profili güncellendi",
                null,
                "/company",
                company.UpdatedBy));
        }

        // ERP çekmesi yalnızca BAŞARILI olduğunda hareket sayılır; başarısız deneme
        // bağlantı ekranının konusudur ve akışta tekrar tekrar görünmesi asıl
        // hareketleri bastırırdı. "Değişiklik yok" da başarılı bir çekimdir; sistem
        // ERP'ye ulaşmış ve veriyi okumuştur.
        if (baglanti is { LastRunAt: { } cekim }
            && baglanti.LastRunStatus is ErpSyncStatus.Succeeded
                or ErpSyncStatus.NoChange)
        {
            liste.Add(new ActivityItemDto(
                cekim,
                "ErpPull",
                "ERP verisi çekildi",
                baglanti.LastRunStatus == ErpSyncStatus.NoChange
                    ? $"{baglanti.Vendor} · değişiklik yok"
                    : baglanti.Vendor.ToString(),
                "/company/erp",
                null));
        }

        // Skorlama toplu çalışır; her değerlendirme için ayrı satır akışı doldururdu.
        if (degerlendirmeler.Count > 0)
        {
            var son = degerlendirmeler.Max(a => a.EvaluatedAt);

            liste.Add(new ActivityItemDto(
                son,
                "Scoring",
                "Çağrılar yeniden değerlendirildi",
                $"{degerlendirmeler.Count} çağrı",
                "/matches",
                null));
        }

        return liste.OrderByDescending(h => h.At).Take(ActivityLimit).ToList();
    }

    private static IReadOnlyList<string> Notlar(
        IReadOnlyList<DashboardActionDto> aksiyonlar,
        IReadOnlyList<TrendPointDto> egilim,
        IReadOnlyList<ActivityItemDto> hareketler,
        IReadOnlyList<EligibilityAssessment> degerlendirmeler)
    {
        var notlar = new List<string>();

        if (degerlendirmeler.Count == 0)
        {
            notlar.Add(
                "Henüz değerlendirme yok; aksiyonlar ve huni hesaplanamadı. "
                + "\"Yeniden skorla\" ile hesaplama başlatılabilir.");
        }
        else if (aksiyonlar.Count == 0)
        {
            notlar.Add("Süre kısıtı olan veya engelleyici eksik taşıyan bir çağrı görünmüyor.");
        }

        if (egilim.Count == 0)
        {
            notlar.Add("Eğilim için haftalık rapor geçmişi gerekir; ilk rapor Pazartesi sabahı üretilir.");
        }

        if (hareketler.Count == 0)
        {
            notlar.Add("Bu firmada kayıtlı bir hareket yok.");
        }

        return notlar;
    }
}
