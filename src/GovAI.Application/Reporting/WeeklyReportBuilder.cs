using GovAI.Application.Eligibility;
using GovAI.Domain.Assessments;
using GovAI.Domain.Companies;
using GovAI.Domain.Common;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Regulatory;
using GovAI.Domain.Reporting;

namespace GovAI.Application.Reporting;

/// <summary>Raporu kurmak için gereken her şey. Kurucu veritabanını tanımaz.</summary>
public sealed record WeeklyReportInput
{
    public required Guid CompanyId { get; init; }

    public required string CompanyName { get; init; }

    public required ReportWeek Week { get; init; }

    /// <summary>Raporun üretildiği an; "kaç gün kaldı" hesapları buna göredir.</summary>
    public required DateTimeOffset AsOf { get; init; }

    /// <summary>Firmanın her çağrı için en güncel değerlendirmesi ve çağrının kendisi.</summary>
    public required IReadOnlyList<AssessedOpportunity> Assessments { get; init; }

    /// <summary>Rapor haftasında yayımlanan mevzuat değişiklikleri.</summary>
    public required IReadOnlyList<RegulatoryChange> RegulatoryChanges { get; init; }

    /// <summary>Firmanın hiç değerlendirmesi yoksa sebebini açıklayan not.</summary>
    public string? EmptyReason { get; init; }

    /// <summary>
    /// Ayrıntı gövdesi okunamayan değerlendirme sayısı. Sıfırdan büyükse rapor
    /// "eksik görünmüyor" demez; okuyamadığını söyler.
    /// </summary>
    public int UnreadableDetailCount { get; init; }
}

/// <summary>Bir değerlendirme ve dayandığı çağrı.</summary>
public sealed record AssessedOpportunity(
    EligibilityAssessment Assessment,
    Opportunity Opportunity,
    AssessmentDetailSnapshot? Detail);

/// <summary>
/// Haftalık raporu kuran <b>deterministik</b> çekirdek.
///
/// <para>
/// Burada model çağrısı, rastgelelik ve <c>DateTime.Now</c> yoktur: aynı girdi her
/// zaman aynı raporu üretir. Rapor firmanın karar aldığı belgedir; iki kez üretildiğinde
/// farklı çıkması, ürünün en temel iddiasını (determinizm) bozardı.
/// </para>
///
/// <para>
/// Hiçbir bölüm veri uydurmaz. Bir bölüm boşsa <see cref="WeeklyReportContent.Notes"/>
/// içine sebebi yazılır — boşluk sessizce geçilmez.
/// </para>
/// </summary>
public static class WeeklyReportBuilder
{
    /// <summary>Rapora giren çağrı sayısı. Rapor okunacak bir belgedir, veri dökümü değildir.</summary>
    private const int MaximumSupports = 15;

    private const int MaximumTenders = 15;

    /// <summary>Takvime giren pencere: bu süreden uzaktaki son tarih bu haftanın işi değildir.</summary>
    private const int DeadlineHorizonDays = 60;

    /// <summary>Bu süre içinde kapanan çağrı "acil" sayılır.</summary>
    private const int UrgentDeadlineDays = 14;

    /// <summary>
    /// Teknoloji ihalesini belirleyen NACE kökleri.
    ///
    /// <para>
    /// Anahtar kelime aramak yerine kaynağın kendi sınıflandırması kullanılır:
    /// ayrıştırıcı ilanın konusundan bir sektör kuralı üretir
    /// (<c>parser/sektor.py</c> → "Bilişim, yazılım ve donanım"), rapor da o kuralı
    /// okur. Başlıkta "yazılım" geçen bir temizlik ihalesi böylece listeye girmez.
    /// </para>
    /// </summary>
    private static readonly string[] TechnologyNaceRoots = ["62", "63", "2620", "4651", "2611", "2612"];

    /// <summary>Fon, hibe ve teşvik sayılan türler. İhale ayrı bölümdedir.</summary>
    private static readonly SupportCategory[] SupportCategories =
    [
        SupportCategory.Grant,
        SupportCategory.EmploymentIncentive,
        SupportCategory.InvestmentIncentive,
        SupportCategory.RndSupport,
        SupportCategory.DigitalTransformation,
        SupportCategory.ExportSupport,
        SupportCategory.GreenTransformation,
        SupportCategory.Loan,
    ];

    public static WeeklyReportContent Build(WeeklyReportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var notes = new List<string>();

        // Karantinadaki çağrı rapora girmez: katalogda gösterilmeyen bir kayıt için
        // firmaya "başvurun" demek olurdu.
        var canli = input.Assessments
            .Where(a => a.Opportunity.IsPublishable)
            .ToList();

        var supports = BuildSupports(canli, input.AsOf);
        var tenders = BuildTenders(canli, input.AsOf);
        var others = BuildOthers(canli, input.AsOf, supports, tenders);
        var regulatory = BuildRegulatory(input.RegulatoryChanges);
        // Riskler raporun LİSTELEDİĞİ çağrılardan çıkarılır.
        //
        // Eskiden bütün değerlendirmelerden çıkarılıyordu: son başvurusu dün kapanmış
        // bir çağrı yüzünden "SGK borcu yoktur yazısını temin edin" satırı üretiliyor
        // ve "1 çağrıyı etkiliyor" deniyordu — oysa o çağrıya artık başvurulamaz.
        // Rapor, göstermediği bir kaydın üzerinden iş veremez.
        var listelenen = supports.Concat(tenders).Concat(others)
            .Select(i => i.OpportunityId)
            .ToHashSet();

        var risks = BuildRisks(canli.Where(a => listelenen.Contains(a.Opportunity.Id)).ToList());
        var deadlines = BuildDeadlines(canli, input.AsOf);
        var todos = BuildTodos(risks, deadlines, input.AsOf);

        AddNotes(notes, input, canli, supports, tenders, others, regulatory, risks, deadlines);

        return new WeeklyReportContent
        {
            Header = new WeeklyReportHeader(
                input.CompanyId,
                input.CompanyName,
                input.Week.Start,
                input.Week.End,
                input.AsOf,
                canli.Count,
                canli.Count(a => a.Assessment.Verdict == EligibilityVerdict.Eligible),
                canli.Count(a => a.Assessment.Verdict == EligibilityVerdict.Indeterminate)),
            Supports = supports,
            TechnologyTenders = tenders,
            OtherOpportunities = others,
            RegulatoryChanges = regulatory,
            Risks = risks,
            Deadlines = deadlines,
            Todos = todos,
            Notes = notes,
        };
    }

    /// <summary>
    /// Liste ekranının okuduğu sayılar.
    ///
    /// <para>
    /// <see cref="WeeklyReportCounters.OpportunityCount"/> rapordaki <b>bütün</b>
    /// çağrıları sayar. Eskiden yalnızca destekleri sayıyordu: destek bölümü boş olan
    /// bir rapor listede baştan sona sıfır görünüyor, oysa içinde üç acil iş
    /// bulunuyordu. Liste satırının işi raporu doğru temsil etmektir.
    /// </para>
    /// </summary>
    public static WeeklyReportCounters Counters(WeeklyReportContent content) => new(
        content.Supports.Count + content.TechnologyTenders.Count + content.OtherOpportunities.Count,
        content.TechnologyTenders.Count,
        content.RegulatoryChanges.Count,
        content.Risks.Count,
        content.Todos.Count,
        content.Deadlines.Count(d => d.DaysRemaining <= UrgentDeadlineDays),
        content.Deadlines.Count);

    // ── Bölüm 1: fon, hibe, teşvik ──────────────────────────────────────────

    private static List<ReportOpportunityItem> BuildSupports(
        IReadOnlyList<AssessedOpportunity> assessments,
        DateTimeOffset asOf) =>
        assessments
            .Where(a => SupportCategories.Contains(a.Opportunity.SupportCategory))
            .Where(a => !Kapandi(a.Opportunity, asOf))
            .Where(a => a.Assessment.Verdict != EligibilityVerdict.NotEligible)
            .OrderByDescending(a => a.Assessment.SectorFit == SectorFit.Matched)
            .ThenByDescending(a => a.Assessment.FinalScore)
            .ThenBy(a => a.Opportunity.Title, StringComparer.Ordinal)
            .Take(MaximumSupports)
            .Select(a => ToItem(a, asOf))
            .ToList();

    // ── Bölüm 2: teknoloji ve yazılım ihaleleri ─────────────────────────────

    private static List<ReportOpportunityItem> BuildTenders(
        IReadOnlyList<AssessedOpportunity> assessments,
        DateTimeOffset asOf) =>
        assessments
            .Where(a => a.Opportunity.SupportCategory == SupportCategory.Tender)
            .Where(a => TeknolojiIhalesiMi(a.Opportunity))
            .Where(a => !Kapandi(a.Opportunity, asOf))
            .OrderByDescending(a => a.Assessment.SectorFit == SectorFit.Matched)
            .ThenByDescending(a => a.Assessment.FinalScore)
            .ThenBy(a => a.Opportunity.Title, StringComparer.Ordinal)
            .Take(MaximumTenders)
            .Select(a => ToItem(a, asOf))
            .ToList();

    // ── Bölüm 3: diğer açık çağrı ve ihaleler ───────────────────────────────

    /// <summary>
    /// Destek ve teknoloji bölümlerine girmeyen, ama firmaya hâlâ açık olan çağrılar.
    ///
    /// <para>
    /// Rapor üzerine iş verdiği hiçbir çağrıyı gizlemez. Taşınmaz satışı, dikili ağaç
    /// satışı, sigorta aracılığı gibi ihaleler destek türü değildir ve konusu teknoloji
    /// de değildir; iki bölümün arasından düşüp yalnızca takvimde ve yapılacaklarda
    /// görünüyorlardı. Kullanıcı "başvuruyu hazırla" satırını okuyup o çağrıyı raporun
    /// hiçbir yerinde bulamıyordu.
    /// </para>
    /// </summary>
    private static List<ReportOpportunityItem> BuildOthers(
        IReadOnlyList<AssessedOpportunity> assessments,
        DateTimeOffset asOf,
        IReadOnlyList<ReportOpportunityItem> supports,
        IReadOnlyList<ReportOpportunityItem> tenders)
    {
        var gosterilen = supports.Select(s => s.OpportunityId)
            .Concat(tenders.Select(t => t.OpportunityId))
            .ToHashSet();

        return assessments
            .Where(a => !gosterilen.Contains(a.Opportunity.Id))
            .Where(a => !Kapandi(a.Opportunity, asOf))
            .Where(a => a.Assessment.Verdict != EligibilityVerdict.NotEligible)
            .OrderByDescending(a => a.Assessment.SectorFit == SectorFit.Matched)
            .ThenByDescending(a => a.Assessment.FinalScore)
            .ThenBy(a => a.Opportunity.Title, StringComparer.Ordinal)
            .Select(a => ToItem(a, asOf))
            .ToList();
    }

    /// <summary>Çağrının sektör kuralı teknoloji NACE köklerinden birine dokunuyor mu?</summary>
    private static bool TeknolojiIhalesiMi(Opportunity opportunity) =>
        opportunity.Rules
            .Where(r => r.Dimension == RuleDimension.Sector && r.Operator == RuleOperator.NaceMatch)
            .SelectMany(r => r.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(NaceCode.Normalize)
            .Any(kod => TechnologyNaceRoots.Any(kok => kod.StartsWith(kok, StringComparison.Ordinal)));

    // ── Bölüm 3: mevzuat değişiklikleri ─────────────────────────────────────

    private static List<ReportRegulatoryItem> BuildRegulatory(
        IReadOnlyList<RegulatoryChange> changes) =>
        changes
            .Where(c => c.Status != RegulatoryChangeStatus.Quarantined)
            // Kurum yayım tarihini her zaman vermez; o hâlde belgenin tespit edildiği an
            // kullanılır. Uydurulmuş bir tarih yazmaktansa gerçekten bilinen an yazılır.
            .OrderByDescending(c => c.PublicationDate ?? c.DetectedAt)
            .ThenBy(c => c.Title, StringComparer.Ordinal)
            .Select(c => new ReportRegulatoryItem(
                c.Id,
                c.Title,
                c.Authority,
                c.PublicationDate ?? c.DetectedAt,
                c.Summary,
                c.OfficialUrl))
            .ToList();

    // ── Bölüm 4: riskler ve zorunlu aksiyonlar ──────────────────────────────

    /// <summary>
    /// Riskler <b>tekilleştirilir</b>. Aynı eksik belge on çağrıyı birden bloke
    /// ediyorsa bu on ayrı risk değil, on çağrıyı etkileyen tek bir risktir; listeyi
    /// aynı satırın kopyalarıyla doldurmak asıl işi görünmez yapar.
    /// </summary>
    private static List<ReportRiskItem> BuildRisks(IReadOnlyList<AssessedOpportunity> assessments)
    {
        var toplanan = new Dictionary<(ReportRiskKind, string),
            (string Subject, string Description, string? Action, HashSet<Guid> Opportunities)>();

        void Ekle(
            ReportRiskKind kind,
            string subject,
            string description,
            string? action,
            Guid opportunityId,
            string? gosterilecekAd = null)
        {
            // Tekilleştirme Türkçe katlamayla yapılır: "İŞ GÜVENLİĞİ" ile "İş Güvenliği"
            // aynı eksiktir. ToUpperInvariant bunu yapamaz (noktasız ı / noktalı İ).
            var anahtar = (kind, TurkceMetin.Katla(subject));

            if (toplanan.TryGetValue(anahtar, out var mevcut))
            {
                mevcut.Opportunities.Add(opportunityId);

                // Gerekçesi olan kayıt gerekçesizi ezer; tersi olmaz.
                toplanan[anahtar] = mevcut with { Action = mevcut.Action ?? action };
                return;
            }

            toplanan[anahtar] = (gosterilecekAd ?? subject, description, action, [opportunityId]);
        }

        foreach (var a in assessments)
        {
            if (a.Detail is not { } detail)
            {
                continue;
            }

            foreach (var kural in detail.BlockingFailures)
            {
                Ekle(
                    ReportRiskKind.BlockingCondition,
                    kural.Requirement,
                    $"{kural.Requirement} — firmanın değeri: {kural.ActualValue}",
                    kural.SuggestedAction,
                    a.Opportunity.Id);
            }

            foreach (var belge in detail.MissingMandatoryDocuments)
            {
                Ekle(
                    ReportRiskKind.MissingDocument,
                    belge.Name,
                    belge.Status == DocumentStatus.Expired
                        ? $"{belge.Name} belgesinin geçerliliği dolmuş."
                        : $"{belge.Name} belgesi firma kaydında yok.",
                    belge.Action,
                    a.Opportunity.Id);
            }

            foreach (var kural in detail.DataGaps)
            {
                // Tekilleştirme ALAN ADINA göre yapılır (aynı eksik bilgi, tek risk),
                // ama kullanıcıya iç tanımlayıcı değil okunabilir ad gösterilir:
                // raporda "Company.Nuts2Codes" yazıyordu.
                Ekle(
                    ReportRiskKind.DataGap,
                    kural.Field,
                    $"{kural.Requirement} — firma profilinde bu bilgi girilmemiş.",
                    kural.SuggestedAction,
                    a.Opportunity.Id,
                    CompanyFieldResolver.Label(kural.Field));
            }
        }

        return toplanan
            .Select(kv => new ReportRiskItem(
                kv.Key.Item1,
                RiskLabel(kv.Key.Item1),
                kv.Value.Subject,
                kv.Value.Description,
                kv.Value.Action,
                kv.Value.Opportunities.Count))
            .OrderBy(r => r.Kind)
            .ThenByDescending(r => r.AffectedOpportunityCount)
            .ThenBy(r => r.Subject, StringComparer.Ordinal)
            .ToList();
    }

    private static string RiskLabel(ReportRiskKind kind) => kind switch
    {
        ReportRiskKind.BlockingCondition => "Engelleyici koşul",
        ReportRiskKind.MissingDocument => "Eksik zorunlu belge",
        _ => "Eksik profil bilgisi",
    };

    // ── Bölüm 5: son başvuru takvimi ────────────────────────────────────────

    private static List<ReportDeadlineItem> BuildDeadlines(
        IReadOnlyList<AssessedOpportunity> assessments,
        DateTimeOffset asOf) =>
        assessments
            .Where(a => a.Opportunity.Deadline is not null)
            .Where(a => !Kapandi(a.Opportunity, asOf))
            .Where(a => a.Assessment.Verdict != EligibilityVerdict.NotEligible)
            .Where(a => GunFarki(a.Opportunity.Deadline!.Value, asOf) <= DeadlineHorizonDays)
            .OrderBy(a => a.Opportunity.Deadline)
            .ThenBy(a => a.Opportunity.Title, StringComparer.Ordinal)
            .Select(a => new ReportDeadlineItem(
                a.Opportunity.Id,
                a.Opportunity.Title,
                a.Opportunity.Deadline!.Value,
                GunFarki(a.Opportunity.Deadline!.Value, asOf),
                a.Assessment.FinalScore,
                a.Assessment.Verdict,
                VerdictLabels.Of(a.Assessment.Verdict),
                a.Assessment.SectorFit,
                SectorFitLabels.Of(a.Assessment.SectorFit)))
            .ToList();

    // ── Bölüm 6: önceliklendirilmiş yapılacaklar ────────────────────────────

    /// <summary>
    /// Yapılacaklar türetilir, ayrıca beslenmez: her satır ya bir son tarihten ya da
    /// bir riskten gelir ve <c>Reason</c> alanında hangisinden geldiğini söyler.
    /// Sıralaması açıklanamayan bir öncelik listesi, kullanıcıya güven vermez.
    /// </summary>
    private static List<ReportTodoItem> BuildTodos(
        IReadOnlyList<ReportRiskItem> risks,
        IReadOnlyList<ReportDeadlineItem> deadlines,
        DateTimeOffset asOf)
    {
        var todos = new List<ReportTodoItem>();

        foreach (var deadline in deadlines.Where(d => d.Verdict != EligibilityVerdict.NotEligible))
        {
            var oncelik = deadline.DaysRemaining <= UrgentDeadlineDays
                ? ReportTodoPriority.Urgent
                : deadline.DaysRemaining <= 30
                    ? ReportTodoPriority.High
                    : ReportTodoPriority.Normal;

            todos.Add(new ReportTodoItem(
                oncelik,
                PriorityLabel(oncelik),
                $"Başvuruyu hazırla: {deadline.Title}",
                deadline.DaysRemaining <= 0
                    ? "Son başvuru bugün."
                    : $"Son başvuruya {deadline.DaysRemaining} gün kaldı.",
                deadline.Deadline,
                deadline.OpportunityId));
        }

        foreach (var risk in risks.Where(r => r.Action is not null))
        {
            // Engelleyici koşul ile eksik belge, kapatılmadıkça başvuru yapılamaz
            // demektir; ikisi de acildir. Eksik profil bilgisi başvuruyu engellemez,
            // yalnızca kararı belirsiz bırakır.
            var oncelik = risk.Kind == ReportRiskKind.DataGap
                ? ReportTodoPriority.Normal
                : ReportTodoPriority.High;

            todos.Add(new ReportTodoItem(
                oncelik,
                PriorityLabel(oncelik),
                risk.Action!,
                risk.AffectedOpportunityCount == 1
                    ? "1 çağrıyı etkiliyor."
                    : $"{risk.AffectedOpportunityCount} çağrıyı etkiliyor.",
                null,
                null));
        }

        return todos
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.DueAt ?? DateTimeOffset.MaxValue)
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ToList();
    }

    private static string PriorityLabel(ReportTodoPriority priority) => priority switch
    {
        ReportTodoPriority.Urgent => "Acil",
        ReportTodoPriority.High => "Yüksek",
        _ => "Normal",
    };

    // ── Boş bölümlerin gerekçesi ────────────────────────────────────────────

    private static void AddNotes(
        List<string> notes,
        WeeklyReportInput input,
        IReadOnlyList<AssessedOpportunity> canli,
        IReadOnlyList<ReportOpportunityItem> supports,
        IReadOnlyList<ReportOpportunityItem> tenders,
        IReadOnlyList<ReportOpportunityItem> others,
        IReadOnlyList<ReportRegulatoryItem> regulatory,
        IReadOnlyList<ReportRiskItem> risks,
        IReadOnlyList<ReportDeadlineItem> deadlines)
    {
        if (input.EmptyReason is { } sebep)
        {
            notes.Add(sebep);
            return;
        }

        if (canli.Count == 0)
        {
            notes.Add("Firma için henüz değerlendirme yapılmamış. Profil tamamlandığında " +
                      "çağrılar otomatik olarak puanlanır.");
            return;
        }

        // Okunamayan değerlendirme varsa rapor ÖNCE bunu söyler.
        //
        // Sahada en tehlikeli kusur buydu: ayrıntı gövdesi hiç okunamıyordu, risk listesi
        // boş kalıyordu ve rapor "başvuruyu engelleyen bir eksik görünmüyor" diye
        // yazıyordu — oysa her değerlendirmede dört eksik zorunlu belge vardı. Eksik
        // bilgiyi "sorun yok" diye sunmak, hiç bilgi vermemekten kötüdür.
        if (input.UnreadableDetailCount > 0)
        {
            notes.Add(
                $"{input.UnreadableDetailCount} değerlendirmenin ayrıntısı okunamadı; " +
                "bu kayıtların riskleri ve eksik koşulları raporda YER ALMIYOR. " +
                "Skorlar ve son başvuru tarihleri etkilenmedi.");
        }

        if (supports.Count == 0)
        {
            notes.Add("Bu dönemde firmaya uygun açık fon, hibe veya teşvik bulunamadı.");
        }

        if (tenders.Count == 0)
        {
            notes.Add("Bu dönemde teknoloji veya yazılım konulu açık ihale bulunamadı.");
        }

        if (others.Count == 0)
        {
            notes.Add("Destek ve teknoloji dışında açık başka bir çağrı bulunamadı.");
        }

        if (regulatory.Count == 0)
        {
            notes.Add("Bu hafta firmayı ilgilendiren mevzuat değişikliği yayımlanmadı.");
        }

        // "Eksik yok" demek bir GÜVENCEDİR; yalnızca bütün ayrıntılar okunabildiyse verilir.
        if (risks.Count == 0 && input.UnreadableDetailCount == 0)
        {
            notes.Add("Firma profilinde başvuruyu engelleyen bir eksik görünmüyor.");
        }

        if (deadlines.Count == 0)
        {
            notes.Add($"Önümüzdeki {DeadlineHorizonDays} gün içinde kapanan bir çağrı yok.");
        }
    }

    // ── Ortak ───────────────────────────────────────────────────────────────

    private static ReportOpportunityItem ToItem(AssessedOpportunity a, DateTimeOffset asOf) =>
        new(
            a.Opportunity.Id,
            a.Opportunity.Title,
            a.Opportunity.Publisher,
            a.Opportunity.SupportCategory,
            CategoryLabels.Of(a.Opportunity.SupportCategory),
            a.Assessment.FinalScore,
            a.Assessment.Verdict,
            VerdictLabels.Of(a.Assessment.Verdict),
            a.Assessment.SectorFit,
            SectorFitLabels.Of(a.Assessment.SectorFit),
            a.Opportunity.Deadline,
            a.Opportunity.Deadline is { } son ? GunFarki(son, asOf) : null,
            a.Opportunity.SourceUrl,
            a.Detail?.MissingConditions.Select(m => m.Requirement).Distinct(StringComparer.Ordinal).ToList()
                ?? []);

    /// <summary>Son başvurusu geçmiş çağrı rapora girmez.</summary>
    private static bool Kapandi(Opportunity opportunity, DateTimeOffset asOf) =>
        opportunity.Deadline is { } son && son < asOf;

    /// <summary>Takvim günü farkı. Saat farkı yüzünden "0,7 gün" gibi bir sonuç çıkmaz.</summary>
    private static int GunFarki(DateTimeOffset deadline, DateTimeOffset asOf)
    {
        var bitis = DateOnly.FromDateTime(deadline.ToOffset(ReportWeek.TurkiyeFarki).DateTime);
        var bugun = DateOnly.FromDateTime(asOf.ToOffset(ReportWeek.TurkiyeFarki).DateTime);

        return bitis.DayNumber - bugun.DayNumber;
    }
}
