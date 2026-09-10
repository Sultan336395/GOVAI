using GovAI.Domain.Common;

namespace GovAI.Domain.Reporting;

/// <summary>
/// Bir firmanın belirli bir haftaya ait raporunun <b>kalıcı kaydı</b>.
///
/// <para>
/// Rapor bir sorgu değil, <b>anlık görüntüdür</b>. Geçmiş rapor açıldığında o hafta
/// ne görüldüyse onu gösterir; bugünün verisiyle yeniden hesaplanmaz. Aksi hâlde
/// "geçen hafta bu fırsatı neden görmedim" sorusu cevaplanamaz ve firmanın o hafta
/// aldığı karar, bugünkü veriyle yargılanmış olurdu.
/// </para>
///
/// <para>
/// Bölümlerin tamamı <see cref="ContentJson"/> içinde durur. Sayaçlar ayrıca kolon
/// olarak tutulur; liste ekranı yüzlerce raporun gövdesini açmak zorunda kalmasın.
/// </para>
/// </summary>
public class WeeklyReport : AggregateRoot, IAuditable, ITenantScoped
{
    private WeeklyReport()
    {
    }

    public WeeklyReport(
        Guid tenantId,
        Guid companyId,
        string companyName,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateTimeOffset generatedAt,
        ReportTrigger trigger,
        string contentJson,
        WeeklyReportCounters counters)
    {
        DomainException.ThrowIf(periodEnd < periodStart, "Rapor dönemi ters olamaz.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(contentJson), "Rapor gövdesi boş olamaz.");

        TenantId = tenantId;
        CompanyId = companyId;
        CompanyName = companyName;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        GeneratedAt = generatedAt;
        Trigger = trigger;
        ContentJson = contentJson;

        Apply(counters);
    }

    public Guid TenantId { get; set; }

    public Guid CompanyId { get; private set; }

    /// <summary>
    /// Firmanın rapor anındaki adı. Firma sonradan yeniden adlandırılsa bile geçmiş
    /// rapor, o hafta hangi ad altında üretildiğini söylemeye devam eder.
    /// </summary>
    public string CompanyName { get; private set; } = string.Empty;

    /// <summary>Raporun kapsadığı haftanın ilk günü (Pazartesi, Türkiye saatiyle).</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>Raporun kapsadığı haftanın son günü (Pazar).</summary>
    public DateOnly PeriodEnd { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    public ReportTrigger Trigger { get; private set; }

    /// <summary>Bütün bölümlerin anlık görüntüsü.</summary>
    public string ContentJson { get; private set; } = "{}";

    /// <summary>Listede gösterilen sayılar; gövdeyi açmadan okunabilsin diye kolon.</summary>
    public int OpportunityCount { get; private set; }

    public int TenderCount { get; private set; }

    public int RegulatoryChangeCount { get; private set; }

    public int RiskCount { get; private set; }

    public int ActionCount { get; private set; }

    /// <summary>Rapor haftasından sonraki 14 gün içinde kapanan çağrı sayısı.</summary>
    public int UrgentDeadlineCount { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Aynı haftanın raporu yeniden üretilirse kayıt <b>değiştirilir</b>, ikincisi açılmaz.
    ///
    /// <para>
    /// Otomatik üretim ile elle üretim aynı haftada çakışabilir; her çakışmada yeni
    /// satır açmak, geçmiş listesini aynı haftanın kopyalarıyla doldururdu. Hangi
    /// tetikleyicinin son yazdığı da kaydedilir, çünkü "bu raporu kim istedi" sorusu
    /// denetim açısından anlamlıdır.
    /// </para>
    /// </summary>
    public void Replace(
        DateTimeOffset generatedAt,
        ReportTrigger trigger,
        string contentJson,
        WeeklyReportCounters counters,
        string companyName)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(contentJson), "Rapor gövdesi boş olamaz.");

        GeneratedAt = generatedAt;
        Trigger = trigger;
        ContentJson = contentJson;
        CompanyName = companyName;

        Apply(counters);
    }

    private void Apply(WeeklyReportCounters counters)
    {
        OpportunityCount = counters.OpportunityCount;
        TenderCount = counters.TenderCount;
        RegulatoryChangeCount = counters.RegulatoryChangeCount;
        RiskCount = counters.RiskCount;
        ActionCount = counters.ActionCount;
        UrgentDeadlineCount = counters.UrgentDeadlineCount;
    }
}

/// <summary>Liste ekranının gövdeyi açmadan okuduğu sayılar.</summary>
public sealed record WeeklyReportCounters(
    int OpportunityCount,
    int TenderCount,
    int RegulatoryChangeCount,
    int RiskCount,
    int ActionCount,
    int UrgentDeadlineCount);

/// <summary>Raporu kimin istediği.</summary>
public enum ReportTrigger
{
    /// <summary>Haftalık takvim tetikledi.</summary>
    Scheduled = 1,

    /// <summary>Kullanıcı panelden istedi.</summary>
    Manual = 2,
}
