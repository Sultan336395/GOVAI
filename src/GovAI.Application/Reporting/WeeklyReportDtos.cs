using GovAI.Domain.Common;
using GovAI.Domain.Reporting;

namespace GovAI.Application.Reporting;

/// <summary>
/// Haftalık raporun gövdesi: yedi bölümün tamamı.
///
/// <para>
/// Bu nesne olduğu gibi saklanır. Alan eklemek serbesttir, <b>alan çıkarmak veya
/// yeniden adlandırmak değildir</b>: geçmiş raporlar eski gövdeyle yazıldı ve
/// okunmaya devam etmeleri gerekir.
/// </para>
/// </summary>
public sealed record WeeklyReportContent
{
    public required WeeklyReportHeader Header { get; init; }

    /// <summary>Bölüm 1 — en uygun fon, hibe ve teşvikler.</summary>
    public required IReadOnlyList<ReportOpportunityItem> Supports { get; init; }

    /// <summary>Bölüm 2 — teknoloji ve yazılım ihaleleri.</summary>
    public required IReadOnlyList<ReportOpportunityItem> TechnologyTenders { get; init; }

    /// <summary>
    /// Bölüm 3 — diğer açık çağrı ve ihaleler.
    ///
    /// <para>
    /// Bu bölüm bir artık değil, <b>boşluk kapatıcıdır</b>. Sahada rapor "uygun destek
    /// bulunamadı" yazarken aynı anda "Acil: başvuruyu hazırla — DİKİLİ AĞAÇ
    /// SATILACAKTIR" diyordu: taşınmaz, ağaç ve sigorta ihaleleri ne destek
    /// bölümüne (destek türü değil) ne teknoloji bölümüne (konusu teknoloji değil)
    /// giriyor, ama takvime ve yapılacaklara giriyordu. Rapor kendisiyle çelişiyor ve
    /// üzerine iş verdiği çağrıyı hiç göstermiyordu.
    /// </para>
    /// </summary>
    public required IReadOnlyList<ReportOpportunityItem> OtherOpportunities { get; init; }

    /// <summary>Bölüm 4 — bu hafta yayımlanan mevzuat değişiklikleri.</summary>
    public required IReadOnlyList<ReportRegulatoryItem> RegulatoryChanges { get; init; }

    /// <summary>Bölüm 5 — riskler ve zorunlu aksiyonlar.</summary>
    public required IReadOnlyList<ReportRiskItem> Risks { get; init; }

    /// <summary>
    /// Bölüm 6 — geçmiş dönem eksikleri.
    ///
    /// <para>
    /// Başvuru süresi geçmiş çağrılardan kalan eksikler. <b>Bilgilendiricidir, iş
    /// listesi değildir</b>: kapanmış bir çağrı için iş vermek, yapılamayacak bir iş
    /// vermektir. Ama eksiğin kendisi kaybolmamıştır — aynı koşulu isteyen yeni bir
    /// çağrı açıldığında güncel listeye geçer.
    /// </para>
    /// </summary>
    public required IReadOnlyList<ReportRiskItem> PastPeriodGaps { get; init; }

    /// <summary>Bölüm 7 — son başvuru takvimi.</summary>
    public required IReadOnlyList<ReportDeadlineItem> Deadlines { get; init; }

    /// <summary>Bölüm 8 — önceliklendirilmiş yapılacaklar.</summary>
    public required IReadOnlyList<ReportTodoItem> Todos { get; init; }

    /// <summary>
    /// Bölümlerin neden boş olduğunu açıklayan notlar.
    ///
    /// <para>
    /// Boş bölüm sessizce geçilmez. "Bu hafta mevzuat değişikliği yayımlanmadı" ile
    /// "mevzuat kaynağı çalışmıyor" farklı şeylerdir ve kullanıcı ikisini ayırt
    /// edemezse boş listeyi arıza sanır (ya da arızayı boşluk sanar).
    /// </para>
    /// </summary>
    public required IReadOnlyList<string> Notes { get; init; }
}

public sealed record WeeklyReportHeader(
    Guid CompanyId,
    string CompanyName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset GeneratedAt,
    /// <summary>Değerlendirmeye giren toplam çağrı sayısı.</summary>
    int EvaluatedOpportunityCount,
    /// <summary>Firmanın uygun bulunduğu çağrı sayısı.</summary>
    int EligibleCount,
    /// <summary>Profil eksikliği yüzünden karar verilemeyen çağrı sayısı.</summary>
    int UndeterminedCount);

/// <summary>Rapora giren tek bir çağrı. Skor ve gerekçe birlikte taşınır.</summary>
public sealed record ReportOpportunityItem(
    Guid OpportunityId,
    string Title,
    string Publisher,
    SupportCategory Category,
    string CategoryLabel,
    decimal Score,
    EligibilityVerdict Verdict,
    string VerdictLabel,
    SectorFit SectorFit,
    string SectorFitLabel,
    DateTimeOffset? Deadline,
    int? DaysToDeadline,
    string? SourceUrl,
    /// <summary>Kapatılabilir eksikler; boşsa firma bu çağrıya bugünkü hâliyle uygundur.</summary>
    IReadOnlyList<string> MissingConditions);

public sealed record ReportRegulatoryItem(
    Guid RegulatoryChangeId,
    string Title,
    string Authority,
    DateTimeOffset PublishedAt,
    string? Summary,
    string? SourceUrl);

/// <summary>Riskin cinsi. Aciliyeti bu belirler, metin değil.</summary>
public enum ReportRiskKind
{
    /// <summary>Firmayı çağrıdan doğrudan eleyen koşul.</summary>
    BlockingCondition = 1,

    /// <summary>Zorunlu belge eksik ya da süresi dolmuş.</summary>
    MissingDocument = 2,

    /// <summary>Profil verisi eksik olduğu için karar verilemiyor.</summary>
    DataGap = 3,
}

public sealed record ReportRiskItem(
    ReportRiskKind Kind,
    string KindLabel,
    string Subject,
    /// <summary>Neyin eksik olduğu.</summary>
    string Description,
    /// <summary>Ne yapılması gerektiği. Sistem öneri üretemiyorsa <c>null</c> kalır.</summary>
    string? Action,
    /// <summary>Kaç çağrıyı etkilediği — aynı eksik birden çok çağrıyı bloke edebilir.</summary>
    int AffectedOpportunityCount);

public sealed record ReportDeadlineItem(
    Guid OpportunityId,
    string Title,
    DateTimeOffset Deadline,
    int DaysRemaining,
    decimal Score,
    EligibilityVerdict Verdict,
    string VerdictLabel,
    /// <summary>
    /// Sektör uyumu takvimde de yazılır. Sıralamanın birincil ölçütü budur
    /// (CLAUDE.md §2.2.1); yalnızca skor gösteren bir takvim, sektörü doğrulanamamış
    /// yüksek puanlı bir çağrıyı güvenli gibi gösterirdi.
    /// </summary>
    SectorFit SectorFit,
    string SectorFitLabel);

/// <summary>Yapılacak işin aciliyeti.</summary>
public enum ReportTodoPriority
{
    /// <summary>Bu hafta yapılmazsa fırsat kaçar.</summary>
    Urgent = 1,

    /// <summary>Yakın vadeli; planlanmalı.</summary>
    High = 2,

    /// <summary>Sırası geldiğinde.</summary>
    Normal = 3,
}

public sealed record ReportTodoItem(
    ReportTodoPriority Priority,
    string PriorityLabel,
    string Title,
    /// <summary>Bu işin neden bu sırada olduğu. Sıralama açıklanabilir olmalıdır.</summary>
    string Reason,
    DateTimeOffset? DueAt,
    Guid? OpportunityId);

/// <summary>Liste ekranı için özet satır; rapor gövdesi açılmaz.</summary>
public sealed record WeeklyReportSummaryDto(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset GeneratedAt,
    ReportTrigger Trigger,
    int OpportunityCount,
    int TenderCount,
    int RegulatoryChangeCount,
    int RiskCount,
    int ActionCount,
    int UrgentDeadlineCount,
    int DeadlineCount,
    int PastGapCount);

public sealed record WeeklyReportDetailDto(
    Guid Id,
    ReportTrigger Trigger,
    WeeklyReportContent Content);

/// <summary>Elle rapor isteği. Dönem verilmezse tamamlanmış son hafta alınır.</summary>
public sealed record GenerateWeeklyReportRequest
{
    /// <summary>Raporlanacak haftanın içinde kalan herhangi bir gün.</summary>
    public DateOnly? WeekOf { get; init; }
}

public sealed record WeeklyReportBatchResult(
    int CompanyCount,
    int GeneratedCount,
    int FailedCount,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);
