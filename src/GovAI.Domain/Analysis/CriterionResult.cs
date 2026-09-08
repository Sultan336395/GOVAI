using GovAI.Domain.Common;

namespace GovAI.Domain.Analysis;

/// <summary>
/// Tek bir kriterin sonucu (Faz 3 — DeepTech analiz motoru).
///
/// <para>
/// Mevcut <see cref="Eligibility.RuleOutcome"/> dört değerlidir ve <b>çelişkiyi</b>
/// anlatamaz. Gerçek belgelerde çelişki oluyor: aynı KOSGEB sayfasında bir yerde
/// "tüm sektörler", başka yerde "yalnızca C-İmalat" yazabiliyor. Bu durumda kural
/// motorunun ikisinden birini seçmesi, seçmediğini sessizce yok saymasıdır.
/// Kullanıcıya "belgede çelişki var, doğrulayın" demek doğru cevaptır.
/// </para>
/// </summary>
public enum CriterionOutcome
{
    /// <summary>Kriter sağlanıyor.</summary>
    Met = 1,

    /// <summary>Kriter sağlanmıyor. <b>Yalnızca</b> firma verisi bilinip koşulu karşılamadığında.</summary>
    NotMet = 2,

    /// <summary>
    /// Karar verilemedi. Eksik veri <b>asla</b> <see cref="NotMet"/> sayılmaz: profili
    /// eksik olduğu için firma elenirse sistem kendi eksiğini firmaya fatura etmiş olur.
    /// </summary>
    Unknown = 3,

    /// <summary>Bu çağrı/mevzuat bu kriteri hiç kullanmıyor.</summary>
    NotApplicable = 4,

    /// <summary>Resmî belge aynı kriter için birbiriyle çelişen iki koşul içeriyor.</summary>
    ConflictingEvidence = 5
}

/// <summary>
/// Bir kriterin dayandığı kanıt. Kanıtsız iddia kullanıcıya gösterilmez; bu kayıt
/// "bu sonuç neden çıktı?" sorusunun cevabıdır.
/// </summary>
public sealed record CriterionEvidence
{
    /// <summary>Kanıt parçasının kimliği. Kural metninden gelen kanıtlarda <c>null</c> olabilir.</summary>
    public Guid? EvidenceChunkId { get; init; }

    /// <summary>Kanıtın alındığı belge sürümü.</summary>
    public Guid? DocumentVersionId { get; init; }

    /// <summary>Belgedeki cümle. Uydurulmaz; yoksa alan boş kalır.</summary>
    public required string Excerpt { get; init; }

    /// <summary>Kanıtın belgedeki yeri (bölüm başlığı, sayfa vb.).</summary>
    public string? Locator { get; init; }
}

/// <summary>
/// Tek bir kriterin değerlendirme kaydı — açıklanabilirliğin atom birimi.
///
/// <para>
/// <see cref="Eligibility.RuleEvaluation"/> belgeden çıkarılmış <b>tek bir kuralın</b>
/// sonucudur; bir çağrıda aynı konuda birden çok kural olabilir. Kriter ise konunun
/// kendisidir ("çalışan sayısı", "coğrafi kapsam"). Kullanıcı ekranında ve altın veri
/// setinde konuşulan birim budur: kriter kodu sabittir, kural kimlikleri her yeniden
/// ayrıştırmada değişir.
/// </para>
/// </summary>
public sealed record CriterionResult
{
    private readonly string _code = string.Empty;
    private readonly string _rationale = string.Empty;

    /// <summary>Sabit kriter kodu (ör. <c>SECTOR_NACE</c>). Sürümler arasında değişmez.</summary>
    public required string Code
    {
        get => _code;
        init
        {
            DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), "Kriter kodu zorunludur.");
            _code = value;
        }
    }

    /// <summary>Kullanıcıya gösterilen Türkçe ad.</summary>
    public required string Name { get; init; }

    /// <summary>Sağlanmaması başvuruyu doğrudan engelliyor mu?</summary>
    public required bool IsMandatory { get; init; }

    public required CriterionOutcome Outcome { get; init; }

    /// <summary>
    /// Sonucun kural gerekçesi — hangi koşul, hangi değerle karşılaştırıldı.
    /// Boş bırakılamaz: gerekçesiz bir kriter sonucu kullanıcıya gösterilemez.
    /// </summary>
    public required string Rationale
    {
        get => _rationale;
        init
        {
            DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), "Kriter gerekçesi zorunludur.");
            _rationale = value;
        }
    }

    /// <summary>Kullanılan firma profili alanları (ör. <c>Workforce.EmployeeCount</c>).</summary>
    public IReadOnlyList<string> CompanyFields { get; init; } = [];

    /// <summary>Kullanılan kanıtlar.</summary>
    public IReadOnlyList<CriterionEvidence> Evidence { get; init; } = [];

    /// <summary>Kriterin puana katkısı (0..1 aralığında boyut değeri).</summary>
    public required decimal ScoreImpact { get; init; }

    /// <summary>
    /// Eksik veya çelişkili verinin açıklaması. <see cref="CriterionOutcome.Unknown"/> ve
    /// <see cref="CriterionOutcome.ConflictingEvidence"/> sonuçlarında <b>zorunludur</b>:
    /// kullanıcı neyi tamamlaması gerektiğini bilmeden eksiği kapatamaz.
    /// </summary>
    public string? MissingOrConflictExplanation { get; init; }

    /// <summary>Sonucu üreten kural setinin sürümü.</summary>
    public required string RuleSetVersion { get; init; }

    /// <summary>Puan kırılımında hangi başlık altında toplandığı.</summary>
    public required ScoreGroup Group { get; init; }

    /// <summary>Kriteri besleyen kuralların kimlikleri; kural silinse de kriter kodu kalır.</summary>
    public IReadOnlyList<Guid> RuleIds { get; init; } = [];

    /// <summary>Zorunlu kriterde başarısızlık — analiz bunu "uygun" gösteremez.</summary>
    public bool IsMandatoryFailure => IsMandatory && Outcome == CriterionOutcome.NotMet;

    /// <summary>Kullanıcıdan veri beklenen kriter.</summary>
    public bool NeedsData => Outcome == CriterionOutcome.Unknown;
}

/// <summary>
/// Puan kırılımı başlıkları. Ağırlıklar <see cref="AnalysisRuleSet"/> içinde sürümlenir;
/// koda dağılmaz.
/// </summary>
public enum ScoreGroup
{
    /// <summary>Zorunlu kriterler — sağlanmazsa puan sıfırlanır.</summary>
    Mandatory = 1,

    SectorNace = 2,

    /// <summary>Ölçek ve mali yapı.</summary>
    ScaleAndFinancials = 3,

    Geography = 4,

    /// <summary>İş gücü: çalışan sayısı ve personel yapısı.</summary>
    Workforce = 5,

    /// <summary>Tarih: başvuru dönemi ve süre.</summary>
    Timing = 6,

    /// <summary>Belge, sertifika ve özel başvuru koşulları.</summary>
    DocumentsAndConditions = 7
}
