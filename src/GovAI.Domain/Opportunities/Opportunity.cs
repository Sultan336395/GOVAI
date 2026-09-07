using GovAI.Domain.Common;

namespace GovAI.Domain.Opportunities;

/// <summary>
/// Resmî bir teşvik / hibe / ihale çağrısı. Teknik dokümandaki <c>OpportunityRule</c> yapısının
/// kalıcı karşılığıdır: temel künye alanları burada, makine tarafından değerlendirilen koşullar
/// <see cref="Rules"/> altında tutulur.
/// </summary>
public class Opportunity : AggregateRoot, IAuditable, ISoftDeletable
{
    private readonly List<OpportunityRule> _rules = [];
    private readonly List<BudgetItem> _budgetItems = [];
    private readonly List<BudgetRate> _budgetRates = [];
    private readonly List<DocumentRequirement> _documentChecklist = [];

    private Opportunity()
    {
    }

    public Opportunity(
        Guid sourceId,
        SourceType sourceType,
        SupportCategory supportCategory,
        string title,
        string publisher,
        DateTimeOffset publishedAt)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), "Çağrı başlığı zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(publisher), "Yayınlayan kurum zorunludur.");

        SourceId = sourceId;
        SourceType = sourceType;
        SupportCategory = supportCategory;
        Title = title.Trim();
        Publisher = publisher.Trim();
        PublishedAt = publishedAt;
    }

    public Guid SourceId { get; private set; }

    /// <summary>Ham dokümana geri izlenebilirlik; açıklanabilirlik için raporda kaynak gösterilir.</summary>
    public Guid? SourceDocumentId { get; private set; }

    public SourceType SourceType { get; private set; }

    public SupportCategory SupportCategory { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Publisher { get; private set; } = string.Empty;

    public string? Summary { get; private set; }

    /// <summary>Çağrı metninin kalıcı adresi.</summary>
    public string? SourceUrl { get; private set; }

    public DateTimeOffset PublishedAt { get; private set; }

    /// <summary>Son başvuru tarihi. <c>timingScore</c> ve son tarih bildirimlerinin girdisidir.</summary>
    public DateTimeOffset? Deadline { get; private set; }

    public BudgetRange? Budget { get; private set; }

    /// <summary>
    /// Çağrının mevzuat dayanağı — metinde geçtiği biçimiyle (Faz 3).
    /// Örnek: "5746 sayılı Kanun md. 3; Ar-Ge Merkezleri Yönetmeliği".
    ///
    /// <para>
    /// Bir destek çağrısı boşlukta durmaz; bir kanuna, yönetmeliğe ya da karara
    /// dayanır. Danışman başvurunun hukuki zeminini bu alan olmadan kaynağa kadar
    /// takip edemez.
    /// </para>
    ///
    /// <para>
    /// Metindeki yazım KORUNUR, resmî tam ada genişletilmez: genişletme bir eşleme
    /// tablosu gerektirir ve tablo eskidiğinde alan sessizce yanlış adı gösterir.
    /// Bulunamazsa <c>null</c> kalır; dayanak asla uydurulmaz.
    /// </para>
    /// </summary>
    public string? LegalBasis { get; private set; }

    /// <summary>Çağrı metninden çıkarılan koşulların ne kadarının otomatik doğrulanabildiği (0..1).</summary>
    public decimal RuleExtractionConfidence { get; private set; }

    /// <summary>Danışman, otomatik çıkarılan kuralları gözden geçirip onayladı mı?</summary>
    public bool IsReviewedByConsultant { get; private set; }

    public IReadOnlyCollection<OpportunityRule> Rules => _rules.AsReadOnly();

    /// <summary>
    /// Belgeden çıkarılmış, <b>türü belirlenmiş</b> tutarlar (Faz 3). Eski tek alanlı
    /// <see cref="Budget"/> geriye dönük uyum için durur; yeni analizler bu listeyi
    /// kullanır çünkü hibe ile krediyi ayırt eder.
    /// </summary>
    public IReadOnlyCollection<BudgetItem> BudgetItems => _budgetItems.AsReadOnly();

    public IReadOnlyCollection<BudgetRate> BudgetRates => _budgetRates.AsReadOnly();

    /// <summary>Hibe üst limiti; belgede yoksa <c>null</c> (NotProvided).</summary>
    public BudgetItem? GrantCeiling =>
        _budgetItems.FirstOrDefault(b => b.Type == BudgetItemType.GrantCeiling);

    /// <summary>Kredi üst limiti; hibe ile <b>karıştırılmaz</b>.</summary>
    public BudgetItem? CreditCeiling =>
        _budgetItems.FirstOrDefault(b => b.Type == BudgetItemType.CreditCeiling);

    /// <summary>Destek oranı; öz kaynak payı bu alana YAZILMAZ.</summary>
    public BudgetRate? SupportRate =>
        _budgetRates.FirstOrDefault(r => r.Type == BudgetRateType.SupportRate);

    /// <summary>Türü belirlenememiş kalem var mı? Varsa ekran "incelenmeli" der.</summary>
    public bool HasUndeterminedBudget =>
        _budgetItems.Any(b => b.NeedsReview) || _budgetRates.Any(r => r.NeedsReview);

    public IReadOnlyCollection<DocumentRequirement> DocumentChecklist => _documentChecklist.AsReadOnly();

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public bool IsOpenOn(DateTimeOffset asOf) => Deadline is null || Deadline >= asOf;

    public int? DaysUntilDeadline(DateTimeOffset asOf) =>
        Deadline is null ? null : (int)Math.Ceiling((Deadline.Value - asOf).TotalDays);

    /// <summary>
    /// Başlığı yeni yakalanıştan tazeler.
    ///
    /// Aynı kaynak dokümandan gelen kayıt güncellendiğinde başlık ilk hâlinde
    /// donuyordu; ilk yakalanışta karakter kümesi yanlış çözülmüşse bozuk başlık
    /// ("ARTIRMA, EKS?LTME…") katalogda kalıyordu. Boş başlık mevcut başlığı SİLMEZ.
    /// </summary>
    public void RefreshTitle(string? title)
    {
        var aday = title?.Trim();

        if (!string.IsNullOrWhiteSpace(aday))
        {
            Title = aday;
        }
    }

    /// <summary>
    /// Yayımlayan kurumu yeni yakalanıştan tazeler.
    ///
    /// Kurum yalnızca kayıt AÇILIRKEN yazılıyordu. İhale ilanlarında ihaleyi açan
    /// idare belgenin içinde yazılıdır; ayrıştırıcı onu sonradan çıkarabilir hâle
    /// geldiğinde katalogdaki eski değer ("Resmî Gazete İhale İlanları" gibi kaynak
    /// adı) yerinde kalıyor ve düzelme kullanıcıya hiç ulaşmıyordu.
    ///
    /// Boş değer mevcut kurumu SİLMEZ: bir sonraki yakalanışta alan çıkarılamadıysa
    /// bilinen doğru değer kaybedilmemelidir.
    /// </summary>
    /// <summary>
    /// Mevzuat dayanağını günceller. Boş değer mevcut dayanağı SİLMEZ: yeniden
    /// ayrıştırmada kalıp tutmazsa daha önce bulunmuş doğru bilgi kaybolmamalıdır.
    /// </summary>
    public void RefreshLegalBasis(string? legalBasis)
    {
        var aday = legalBasis?.Trim();

        if (!string.IsNullOrWhiteSpace(aday))
        {
            LegalBasis = aday;
        }
    }

    public void RefreshPublisher(string? publisher)
    {
        var aday = publisher?.Trim();

        if (!string.IsNullOrWhiteSpace(aday))
        {
            Publisher = aday;
        }
    }

    public void Describe(string? summary, string? sourceUrl, Guid? sourceDocumentId)
    {
        Summary = summary?.Trim();
        SourceUrl = sourceUrl?.Trim();
        SourceDocumentId = sourceDocumentId;
    }

    public void SetSchedule(DateTimeOffset publishedAt, DateTimeOffset? deadline)
    {
        DomainException.ThrowIf(deadline is not null && deadline < publishedAt, "Son başvuru tarihi yayın tarihinden önce olamaz.");
        PublishedAt = publishedAt;
        Deadline = deadline;
    }

    public void SetBudget(BudgetRange? budget) => Budget = budget;

    /// <summary>
    /// Türlü bütçe kalemlerini değiştirir. Boş liste gelirse mevcut kalemler
    /// SİLİNMEZ: yeniden ayrıştırmada kalıp tutmazsa daha önce çıkarılmış doğru
    /// bilgi kaybolmamalıdır.
    /// </summary>
    public void ReplaceBudgetItems(IEnumerable<BudgetItem> items, IEnumerable<BudgetRate> rates)
    {
        var yeniKalemler = items.ToList();
        var yeniOranlar = rates.ToList();

        if (yeniKalemler.Count > 0)
        {
            _budgetItems.Clear();
            _budgetItems.AddRange(yeniKalemler);
        }

        if (yeniOranlar.Count > 0)
        {
            _budgetRates.Clear();
            _budgetRates.AddRange(yeniOranlar);
        }
    }

    // ── Faz 2: veri kalitesi ──────────────────────────────────────────────

    /// <summary>
    /// Çıkarılamayan alanların kaydı. Eksik değer <b>tahmin edilmez</b>; arayüz boş
    /// alan yerine "Resmî kaynakta belirtilmemiş" gösterir.
    /// </summary>
    public OpportunityFieldAvailability FieldAvailability { get; private set; } =
        OpportunityFieldAvailability.Unknown;

    /// <summary>Karantinadaysa nedeni. Karantinadaki fırsat skorlanmaz ve gösterilmez.</summary>
    public QuarantineReason QuarantineReason { get; private set; } = QuarantineReason.None;

    public string? QuarantineNote { get; private set; }

    /// <summary>
    /// Katalogda gösterilebilir mi? Karantinadaki kayıt eşleşmeye girmez, skorlanmaz,
    /// bildirim üretmez ve haftalık rapora yazılmaz.
    /// </summary>
    public bool IsPublishable => QuarantineReason == QuarantineReason.None;

    public void SetFieldAvailability(OpportunityFieldAvailability availability) =>
        FieldAvailability = availability;

    /// <summary>Karantinaya alır. Kayıt <b>silinmez</b>; PlatformReviewer inceleyebilir.</summary>
    public void Quarantine(QuarantineReason reason, string? note = null)
    {
        DomainException.ThrowIf(reason == QuarantineReason.None, "Karantina nedeni belirtilmelidir.");

        QuarantineReason = reason;
        QuarantineNote = note?[..Math.Min(note.Length, 1000)];
    }

    /// <summary>Karantinadan çıkarır.</summary>
    public void ReleaseFromQuarantine()
    {
        QuarantineReason = QuarantineReason.None;
        QuarantineNote = null;
    }

    /// <summary>Parser/AI tarafından çıkarılan kural setini değiştirir. Onay bayrağı sıfırlanır.</summary>
    public void ReplaceRules(IEnumerable<OpportunityRule> rules, decimal extractionConfidence)
    {
        DomainException.ThrowIf(
            extractionConfidence is < 0m or > 1m,
            "Kural çıkarım güveni 0 ile 1 arasında olmalıdır.");

        _rules.Clear();
        _rules.AddRange(rules);
        RuleExtractionConfidence = extractionConfidence;
        IsReviewedByConsultant = false;
    }

    public void ReplaceDocumentChecklist(IEnumerable<DocumentRequirement> requirements)
    {
        _documentChecklist.Clear();
        _documentChecklist.AddRange(requirements);
    }

    /// <summary>Danışman onayı; istisna yönetimi ve sektörel yorum riskine karşı zorunlu adım.</summary>
    public void MarkReviewed() => IsReviewedByConsultant = true;
}

/// <summary>Destek tutarı aralığı ve destek oranı.</summary>
public sealed record BudgetRange
{
    public BudgetRange(decimal? minAmount, decimal? maxAmount, string currency, decimal? supportRate)
    {
        DomainException.ThrowIf(minAmount is < 0 || maxAmount is < 0, "Bütçe tutarı negatif olamaz.");
        DomainException.ThrowIf(
            minAmount is not null && maxAmount is not null && maxAmount < minAmount,
            "Üst bütçe sınırı alt sınırdan küçük olamaz.");
        DomainException.ThrowIf(supportRate is < 0m or > 1m, "Destek oranı 0 ile 1 arasında olmalıdır.");

        MinAmount = minAmount;
        MaxAmount = maxAmount;
        Currency = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.ToUpperInvariant();
        SupportRate = supportRate;
    }

    public decimal? MinAmount { get; init; }

    public decimal? MaxAmount { get; init; }

    public string Currency { get; init; } = "TRY";

    /// <summary>Hibe/destek oranı (ör. 0.75 = %75 hibe).</summary>
    public decimal? SupportRate { get; init; }
}
