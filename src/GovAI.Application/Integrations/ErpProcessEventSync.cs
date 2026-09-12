using GovAI.Domain.Integrations;

namespace GovAI.Application.Integrations;

/// <summary>Bir çekme turunun sonucu.</summary>
public sealed record ProcessEventSyncResult
{
    /// <summary>Kaydedilecek yeni olaylar.</summary>
    public required IReadOnlyList<ErpProcessEvent> ToInsert { get; init; }

    /// <summary>Zaten kayıtlı olduğu için atlanan olay sayısı.</summary>
    public required int Duplicates { get; init; }

    /// <summary>Zorunlu alanı eksik olduğu için alınamayan olay sayısı.</summary>
    public required int Invalid { get; init; }

    /// <summary>Bölüm ERP yanıtında hiç yoktu; günlük çekilmedi.</summary>
    public required bool SectionAbsent { get; init; }
}

/// <summary>
/// ERP'den gelen süreç olaylarını mevcut günlükle uzlaştırır.
///
/// <para>
/// Saf ve test edilebilir: veritabanına, saate ve ağa dokunmaz. Girdi gelen olaylar ve
/// hâlihazırda kayıtlı tekilleştirme anahtarları, çıktı eklenecek olaylardır.
/// </para>
///
/// <para>
/// <b>Günlük yalnızca BÜYÜR.</b> Var olan olay güncellenmez ve silinmez: bir olay
/// gerçekleşmiştir, sonradan değişmez. ERP bir kaydı düzeltirse bu yeni bir olaydır,
/// eskisinin üzerine yazılması değil. Üzerine yazsaydık geçmiş ölçümler sessizce
/// değişir ve nedensel analiz zeminini kaybederdi.
/// </para>
///
/// <para>
/// <b>Bölüm yoksa hiçbir şey yapılmaz</b> (<see cref="ProcessEventSyncResult.SectionAbsent"/>).
/// Boş liste ile bölümün hiç olmaması aynı şey değildir; §2.2.8'deki alıcı kuralıyla
/// aynı gerekçe. Bölüm yokluğunu "olay olmadı" saymak, ERP arızasını gerçek veri gibi
/// göstermek olurdu.
/// </para>
/// </summary>
public static class ErpProcessEventSync
{
    /// <summary>
    /// Tek turda alınacak azami olay sayısı.
    ///
    /// <para>
    /// Sınır gerekli: olay günlükleri yüz binlerce satır olabilir ve sınırsız bir çekme
    /// gece turunu saatlerce sürdürür, belleği şişirir. Sınıra takılan tur eksik değil
    /// <b>kısmi</b>dir; kalanı bir sonraki tur alır.
    /// </para>
    /// </summary>
    public const int MaximumPerRun = 5_000;

    /// <summary>
    /// Gelen olayları uzlaştırır.
    /// </summary>
    /// <param name="incoming">
    /// ERP yanıtından çıkarılan olaylar. <c>null</c> ise bölüm yanıtta yoktu.
    /// </param>
    /// <param name="existingKeys">Firmada hâlihazırda kayıtlı tekilleştirme anahtarları.</param>
    public static ProcessEventSyncResult Reconcile(
        Guid tenantId,
        Guid companyId,
        IReadOnlyList<ProcessEventCandidate>? incoming,
        IReadOnlySet<string> existingKeys)
    {
        ArgumentNullException.ThrowIfNull(existingKeys);

        if (incoming is null)
        {
            return new ProcessEventSyncResult
            {
                ToInsert = [],
                Duplicates = 0,
                Invalid = 0,
                SectionAbsent = true,
            };
        }

        var eklenecekler = new List<ErpProcessEvent>();
        var gorulen = new HashSet<string>(StringComparer.Ordinal);
        var mukerrer = 0;
        var gecersiz = 0;

        foreach (var aday in incoming)
        {
            if (eklenecekler.Count >= MaximumPerRun)
            {
                break;
            }

            if (!aday.IsUsable)
            {
                gecersiz++;
                continue;
            }

            var olay = new ErpProcessEvent(
                tenantId,
                companyId,
                aday.CaseId!,
                aday.Activity!,
                aday.OccurredAt!.Value,
                aday.Resource,
                aday.Department,
                aday.ExternalId);

            // Hem veritabanındaki hem AYNI TURDAKİ mükerrerler elenir; ikincisi
            // atlanırsa aynı yanıtta iki kez geçen olay iki satır olurdu.
            if (existingKeys.Contains(olay.DeduplicationKey) || !gorulen.Add(olay.DeduplicationKey))
            {
                mukerrer++;
                continue;
            }

            eklenecekler.Add(olay);
        }

        return new ProcessEventSyncResult
        {
            ToInsert = eklenecekler,
            Duplicates = mukerrer,
            Invalid = gecersiz,
            SectionAbsent = false,
        };
    }
}

/// <summary>
/// ERP yanıtından çıkarılmış ham olay adayı.
///
/// <para>
/// Ayrı bir tip çünkü alan modeli geçersiz olay üretmeyi reddeder; ayrıştırma sırasında
/// eksik alanları taşıyabilen gevşek bir kaba ihtiyaç var. Zorunlu üçlüsü eksik olan
/// aday <b>sessizce atlanmaz</b>, sayılır ve raporlanır.
/// </para>
/// </summary>
public sealed record ProcessEventCandidate
{
    public string? CaseId { get; init; }

    public string? Activity { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }

    public string? Resource { get; init; }

    public string? Department { get; init; }

    public string? ExternalId { get; init; }

    /// <summary>Süreç madenciliğinin zorunlu üçlüsü tam mı?</summary>
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(CaseId)
        && !string.IsNullOrWhiteSpace(Activity)
        && OccurredAt is not null;
}
