using GovAI.Domain.Common;

namespace GovAI.Domain.Maintenance;

/// <summary>Hangi bakım işlemi çalıştırıldı?</summary>
public enum MaintenanceOperation
{
    /// <summary>KOSGEB/SGK katalog onarımı: karantina ve başlık düzeltme.</summary>
    CatalogRepair = 1,

    /// <summary>Mevcut fırsat kurallarına geriye dönük kanıt bağlama.</summary>
    RuleEvidenceBackfill = 2
}

/// <summary>
/// Uygulanmış bir bakım işleminin kaydı (Faz 3).
///
/// <para>
/// <b>Neden ayrı bir tablo:</b> denetim kaydı (<c>audit_log</c>) "kim ne zaman ne yaptı"
/// sorusunu cevaplar ama <b>geri almaya</b> yetmez. Geri almak için değişikliğin
/// öncesindeki değer gerekir — karantinaya alınan kaydın kimliği, düzeltilen başlığın
/// eski hâli, kurulan kanıt bağlantısının tam üçlüsü. Bunlar burada durur.
/// </para>
///
/// <para>
/// <see cref="PlanHash"/> onaylanan planın özetidir. Uygulama isteği bu özeti taşımak
/// zorundadır: kullanıcının <b>görmediği</b> bir plan uygulanamaz ve plan gösterildikten
/// sonra veri değişmişse özet tutmaz, işlem reddedilir.
/// </para>
///
/// <para>
/// Kayıt <b>silinmez</b>. Geri alınan çalıştırma da durur, yalnızca damgalanır: "üç ay
/// önce bu onarım neden yapıldı ve neden geri alındı" sorusu cevaplanabilir kalır.
/// </para>
/// </summary>
public class MaintenanceRun : AggregateRoot, IAuditable
{
    private MaintenanceRun()
    {
    }

    public MaintenanceRun(
        MaintenanceOperation operation,
        string planHash,
        string detailJson,
        int changedCount,
        string? performedBy,
        DateTimeOffset startedAt)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(planHash), "Plan özeti zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(detailJson), "Çalıştırma ayrıntısı zorunludur.");
        DomainException.ThrowIf(changedCount < 0, "Değişen kayıt sayısı negatif olamaz.");

        Operation = operation;
        PlanHash = planHash;
        DetailJson = detailJson;
        ChangedCount = changedCount;
        PerformedBy = performedBy;
        StartedAt = startedAt;
    }

    public MaintenanceOperation Operation { get; private set; }

    /// <summary>Uygulanan planın özeti; onay bu değere karşı doğrulanır.</summary>
    public string PlanHash { get; private set; } = string.Empty;

    /// <summary>
    /// Geri almak için gereken ayrıntı: hangi kayda ne yapıldı, önceki değer neydi.
    /// <b>Kişisel veri içermez</b> — yalnızca katalog kayıtlarının kimlikleri ve başlıkları.
    /// </summary>
    public string DetailJson { get; private set; } = string.Empty;

    public int ChangedCount { get; private set; }

    /// <summary>İşlemi yapan platform hesabının e-postası.</summary>
    public string? PerformedBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? UndoneAt { get; private set; }

    public string? UndoneBy { get; private set; }

    /// <summary>Geri alınabilir mi: henüz geri alınmamış ve gerçekten bir şey değiştirmiş.</summary>
    public bool CanUndo => UndoneAt is null && ChangedCount > 0;

    public void MarkUndone(string? undoneBy, DateTimeOffset at)
    {
        DomainException.ThrowIf(UndoneAt is not null, "Bu çalıştırma zaten geri alınmış.");

        UndoneAt = at;
        UndoneBy = undoneBy;
    }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
