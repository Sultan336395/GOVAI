using GovAI.Domain.Common;

namespace GovAI.Domain.Calibration;

/// <summary>
/// Bir danışmanın, sistemin ürettiği değerlendirmeye karşı verdiği <b>kendi kararı</b>.
///
/// <para>
/// Projenin Ar-Ge iddiası, skorun uzman görüşüyle tutarlı olduğunun <b>ölçülebilmesine</b>
/// dayanır. Ölçüm yapılabilmesi için iki şeyin aynı anda saklanması gerekir: uzmanın
/// kararı ve sistemin o an ne dediği. Sistem kararı sonradan yeniden hesaplanırsa
/// karşılaştırma anlamını yitirir — bugünün skoruyla dünün uzman görüşü kıyaslanmış olur.
/// Bu yüzden sistem kararı ve skoru <b>kopyalanarak</b> tutulur; değerlendirme kaydı
/// değişse bile bu satır o anın fotoğrafıdır.
/// </para>
///
/// <para>
/// Uzman kararı sistemin skorunu <b>değiştirmez</b>. Kalibrasyon verisi karar
/// mekanizmasının girdisi değil, denetçisidir: motor deterministik kalır ve uzman
/// görüşü ağırlıkların doğruluğunu sınamak için kullanılır.
/// </para>
/// </summary>
public class ExpertVerdict : AggregateRoot, IAuditable, ITenantScoped
{
    private ExpertVerdict()
    {
    }

    public ExpertVerdict(
        Guid tenantId,
        Guid companyId,
        Guid opportunityId,
        Guid assessmentId,
        EligibilityVerdict systemVerdict,
        decimal systemScore,
        bool systemHadDataGap,
        EligibilityVerdict expertOpinion,
        VerdictDisagreementReason disagreementReason,
        string? note,
        DateTimeOffset recordedAt,
        string recordedBy)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(recordedBy), "Değerlendirmeyi veren kişi zorunludur.");

        TenantId = tenantId;
        CompanyId = companyId;
        OpportunityId = opportunityId;
        AssessmentId = assessmentId;
        SystemVerdict = systemVerdict;
        SystemScore = systemScore;
        SystemHadDataGap = systemHadDataGap;
        ExpertOpinion = expertOpinion;
        RecordedAt = recordedAt;
        RecordedBy = recordedBy;

        Update(expertOpinion, disagreementReason, note, recordedAt, recordedBy);
    }

    public Guid TenantId { get; set; }

    public Guid CompanyId { get; private set; }

    public Guid OpportunityId { get; private set; }

    /// <summary>Karşılaştırmanın yapıldığı değerlendirme kaydı.</summary>
    public Guid AssessmentId { get; private set; }

    /// <summary>Sistemin O AN verdiği karar. Sonradan yeniden hesaplanmaz.</summary>
    public EligibilityVerdict SystemVerdict { get; private set; }

    /// <summary>Sistemin o anki skoru.</summary>
    public decimal SystemScore { get; private set; }

    /// <summary>
    /// Sistem kararını verirken firma profilinde eksik veri var mıydı?
    ///
    /// <para>
    /// Ayrışmanın sebebi çoğu zaman modelin yanlışlığı değil, verinin eksikliğidir.
    /// İkisini ayırmadan yapılan bir kalibrasyon, veri toplama sorununu ağırlık sorunu
    /// sanarak ağırlıkları bozar.
    /// </para>
    /// </summary>
    public bool SystemHadDataGap { get; private set; }

    /// <summary>Uzmanın kararı.</summary>
    public EligibilityVerdict ExpertOpinion { get; private set; }

    /// <summary>Uzman ile sistem ayrıştıysa uzmanın belirttiği sebep.</summary>
    public VerdictDisagreementReason DisagreementReason { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public string RecordedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Sistem ile uzman aynı kararda mı?</summary>
    public bool Agrees => SystemVerdict == ExpertOpinion;

    /// <summary>
    /// Sistem uygun dedi, uzman uygun değil dedi: <b>yanlış pozitif</b>.
    ///
    /// <para>
    /// Bu hata türü daha pahalıdır: firma uygun olmadığı bir programa zaman ayırır.
    /// "Şartlı uygun" pozitif sayılır, çünkü firmayı başvuru hazırlığına yönlendirir.
    /// </para>
    /// </summary>
    public bool IsFalsePositive =>
        Pozitif(SystemVerdict) && ExpertOpinion == EligibilityVerdict.NotEligible;

    /// <summary>
    /// Sistem uygun değil dedi, uzman uygun dedi: <b>yanlış negatif</b>.
    ///
    /// <para>
    /// Bu hata sessizdir: firma fırsatı hiç görmez, kaçırdığını da bilmez.
    /// </para>
    /// </summary>
    public bool IsFalseNegative =>
        SystemVerdict == EligibilityVerdict.NotEligible && Pozitif(ExpertOpinion);

    private static bool Pozitif(EligibilityVerdict verdict) =>
        verdict is EligibilityVerdict.Eligible or EligibilityVerdict.ConditionallyEligible;

    /// <summary>
    /// Uzman kararını değiştirir. Kayıt <b>silinmez</b>: aynı çift için ikinci bir satır
    /// açmak, kalibrasyon sayımını aynı vakayı iki kez sayarak bozardı.
    /// </summary>
    public void Update(
        EligibilityVerdict expertOpinion,
        VerdictDisagreementReason disagreementReason,
        string? note,
        DateTimeOffset recordedAt,
        string recordedBy)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(recordedBy), "Değerlendirmeyi veren kişi zorunludur.");

        // Sebep yalnızca ayrışma varken anlamlıdır; uyum hâlinde saklamak, raporda
        // olmayan bir hatanın gerekçesini gösterirdi.
        var ayrisma = SystemVerdict != expertOpinion;

        DomainException.ThrowIf(
            ayrisma && disagreementReason == VerdictDisagreementReason.None,
            "Sistemden farklı karar verildiğinde sebep belirtilmelidir.");

        ExpertOpinion = expertOpinion;
        DisagreementReason = ayrisma ? disagreementReason : VerdictDisagreementReason.None;
        Note = note?.Trim() is { Length: > 0 } temiz ? temiz[..Math.Min(temiz.Length, 2000)] : null;
        RecordedAt = recordedAt;
        RecordedBy = recordedBy;
    }
}

/// <summary>
/// Uzmanın sistemden neden ayrıldığı.
///
/// <para>
/// Serbest metin yerine sabit bir liste kullanılır: hata türlerinin sayılabilmesi
/// kalibrasyonun ön koşuludur. "Kural yanlış çıkarılmış" ile "ağırlık yanlış" ayrı
/// düzeltmeler gerektirir ve ikisini tek başlık altında toplamak, hangisinin
/// düzeltileceğini belirsizleştirir.
/// </para>
/// </summary>
public enum VerdictDisagreementReason
{
    /// <summary>Ayrışma yok.</summary>
    None = 0,

    /// <summary>Çağrı metninden çıkarılan kural hatalı ya da eksik.</summary>
    RuleExtraction = 1,

    /// <summary>Firma profilindeki veri yanlış veya güncel değil.</summary>
    CompanyData = 2,

    /// <summary>Kurallar doğru ama boyut ağırlıkları sonucu yanlış tarafa çekiyor.</summary>
    ScoreWeighting = 3,

    /// <summary>Metinde yazmayan, kurumun uygulamasından bilinen bir koşul.</summary>
    UnwrittenPractice = 4,

    /// <summary>Yukarıdakilerin dışında; not alanında açıklanır.</summary>
    Other = 99,
}
