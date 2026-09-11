using GovAI.Domain.Common;

namespace GovAI.Domain.Tenders;

/// <summary>
/// Bir firmanın takibe aldığı ihale ve başvuru sürecinin durumu.
///
/// <para>
/// Kayıt firmanın <b>kendi beyanıdır</b>; skoru, kararı ve sıralamayı etkilemez.
/// Etkileseydi ürünün ilk iki iddiası çökerdi: aynı firma–çağrı çifti kimin neyi
/// işaretlediğine göre farklı puan alır (determinizm gider) ve kayan puanın dayanağı
/// çağrı metninde bulunmazdı (açıklanabilirlik gider).
/// </para>
///
/// <para>
/// Aşamalar arasında <b>serbest geçiş</b> vardır. Katı bir boru hattı zorlamak sahayla
/// çatışırdı: firma bir ihaleyi teklifi verdikten sonra sisteme girebilir, hazırlık
/// aşamasından incelemeye geri dönebilir ya da iptal edilip yenilenen bir ihaleyi
/// yeniden açabilir. Doğruluk, geçişi kısıtlamakla değil her değişikliği
/// <see cref="TenderPursuitEvent"/> ile kaydetmekle korunur.
/// </para>
/// </summary>
public class TenderPursuit : AggregateRoot, IAuditable, ITenantScoped
{
    private readonly List<TenderPursuitEvent> _events = [];

    private TenderPursuit()
    {
    }

    public TenderPursuit(
        Guid tenantId,
        Guid companyId,
        Guid opportunityId,
        DateTimeOffset at,
        string by,
        string? note = null)
    {
        DomainException.ThrowIf(companyId == Guid.Empty, "Firma zorunludur.");
        DomainException.ThrowIf(opportunityId == Guid.Empty, "İhale zorunludur.");

        TenantId = tenantId;
        CompanyId = companyId;
        OpportunityId = opportunityId;
        Status = TenderPursuitStatus.Inceleniyor;
        Note = Kirp(note);
        StartedAt = at;
        StatusChangedAt = at;

        _events.Add(new TenderPursuitEvent(
            tenantId, Id, null, TenderPursuitStatus.Inceleniyor, null, Note, at, by));
    }

    public Guid TenantId { get; set; }

    public Guid CompanyId { get; private set; }

    public Guid OpportunityId { get; private set; }

    public TenderPursuitStatus Status { get; private set; }

    /// <summary>Yalnızca <see cref="TenderPursuitStatus.Sonuclandi"/> durumunda doludur.</summary>
    public TenderOutcome? Outcome { get; private set; }

    /// <summary>Firmanın kendi notu. Serbest metindir ve hiçbir hesaplamaya girmez.</summary>
    public string? Note { get; private set; }

    /// <summary>Takibi yürüten kişi. Boş bırakılabilir; zorunlu kılmak takibi geciktirirdi.</summary>
    public string? Owner { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset StatusChangedAt { get; private set; }

    /// <summary>Durum geçmişi. Silinmez; "kim ne zaman ne dedi" sorusunun tek cevabıdır.</summary>
    public IReadOnlyCollection<TenderPursuitEvent> Events => _events.AsReadOnly();

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Süreç kapandı mı? Kapalı takipler varsayılan listede öne çıkmaz.</summary>
    public bool IsClosed =>
        Status is TenderPursuitStatus.Sonuclandi or TenderPursuitStatus.Vazgecildi;

    /// <summary>
    /// Aşamayı değiştirir.
    ///
    /// <para>
    /// Aynı aşamaya yeniden geçmek geçmişe satır <b>eklemez</b>: arayüzde iki kez
    /// tıklamak kaydı aynı durumun tekrarlarıyla doldururdu. Ama not değiştiyse bu bir
    /// bilgi taşır ve kaydedilir.
    /// </para>
    /// </summary>
    public void ChangeStatus(
        TenderPursuitStatus status,
        TenderOutcome? outcome,
        string? note,
        DateTimeOffset at,
        string by)
    {
        DomainException.ThrowIf(
            status == TenderPursuitStatus.Sonuclandi && outcome is null,
            "Sonuçlanan ihalede sonucun (kazanıldı / kaybedildi / iptal) yazılması zorunludur.");

        DomainException.ThrowIf(
            status != TenderPursuitStatus.Sonuclandi && outcome is not null,
            "Sonuç yalnızca sonuçlanmış ihaleye yazılabilir.");

        var yeniNot = Kirp(note);
        var degisti = status != Status || outcome != Outcome || yeniNot != Note;

        if (!degisti)
        {
            return;
        }

        var oncekiDurum = Status;

        Status = status;
        Outcome = outcome;
        Note = yeniNot;
        StatusChangedAt = at;

        _events.Add(new TenderPursuitEvent(
            TenantId, Id, oncekiDurum, status, outcome, yeniNot, at, by));
    }

    /// <summary>Takibi yürüten kişiyi yazar. Geçmişe satır açmaz; aşama değişmemiştir.</summary>
    public void AssignOwner(string? owner) => Owner = Kirp(owner);

    /// <summary>Boş ve yalnızca boşluktan oluşan not <c>null</c> sayılır.</summary>
    private static string? Kirp(string? metin) =>
        string.IsNullOrWhiteSpace(metin) ? null : metin.Trim();
}

/// <summary>
/// Takipteki bir aşama değişikliği.
///
/// <para>
/// Geçmiş ayrı bir tabloda tutulur çünkü takip kaydının kendisi "şu an neredeyiz"
/// sorusuna cevap verir; "ne zaman teklif verdik, kim hazırlığa aldı" sorusunun cevabı
/// üzerine yazılarak kaybolmamalıdır.
/// </para>
/// </summary>
public class TenderPursuitEvent : Entity, ITenantScoped
{
    private TenderPursuitEvent()
    {
    }

    public TenderPursuitEvent(
        Guid tenantId,
        Guid pursuitId,
        TenderPursuitStatus? fromStatus,
        TenderPursuitStatus toStatus,
        TenderOutcome? outcome,
        string? note,
        DateTimeOffset at,
        string by)
    {
        TenantId = tenantId;
        TenderPursuitId = pursuitId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Outcome = outcome;
        Note = note;
        At = at;
        By = by;
    }

    public Guid TenantId { get; set; }

    public Guid TenderPursuitId { get; private set; }

    /// <summary>İlk kayıtta boştur; takip o anda açılmıştır.</summary>
    public TenderPursuitStatus? FromStatus { get; private set; }

    public TenderPursuitStatus ToStatus { get; private set; }

    public TenderOutcome? Outcome { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string By { get; private set; } = string.Empty;
}
