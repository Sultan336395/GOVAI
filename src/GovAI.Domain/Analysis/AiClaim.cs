using GovAI.Domain.Common;

namespace GovAI.Domain.Analysis;

/// <summary>Yapay zekâ iddiasının türü. Serbest metin değil; sayılı bir küme.</summary>
public enum AiClaimType
{
    /// <summary>Kriterin sağlandığını destekleyen kanıt gösteriyor.</summary>
    SupportsCriterion = 1,

    /// <summary>Kriterin sağlanmadığını gösteren kanıt gösteriyor.</summary>
    ContradictsCriterion = 2,

    /// <summary>Eksik bilgiyi belgedeki bir ifadeyle kapatmayı öneriyor.</summary>
    ResolvesUnknown = 3,

    /// <summary>Karara etki etmeyen, yalnızca açıklayıcı not.</summary>
    Clarification = 4
}

/// <summary>Modelin çalışma durumu. Ekranda bu durum gizlenmez.</summary>
public enum AiAnalysisStatus
{
    /// <summary>
    /// Model yapılandırılmamış veya erişilemedi. Kural motoru çalışmaya devam eder;
    /// sistem bu durumda <b>"hibrit çalışıyor" demez</b>.
    /// </summary>
    AIUnavailable = 0,

    /// <summary>Model yanıt verdi ve iddialar doğrulandı.</summary>
    Succeeded = 1,

    /// <summary>Model yanıt verdi ancak çıktı şemaya uymadı; sonuç kullanılmadı.</summary>
    InvalidOutput = 2,

    /// <summary>Model hata döndürdü. Kural sonuçları KORUNUR.</summary>
    Error = 3
}

/// <summary>
/// Yapay zekânın tek bir iddiası.
///
/// <para>
/// Beş alan da <b>zorunludur</b>. Kanıt kimliği olmayan bir iddia doğrulanamaz;
/// doğrulanamayan iddia kullanıcıya gösterilmez. Modelden serbest paragraf değil
/// bu yapı istenir — paragrafın hangi cümlesinin hangi kanıta dayandığı sonradan
/// çıkarılamaz.
/// </para>
/// </summary>
public sealed record AiClaim
{
    public required AiClaimType ClaimType { get; init; }

    /// <summary>Kısa Türkçe açıklama; kullanıcıya bu metin gösterilir.</summary>
    public required string Explanation { get; init; }

    /// <summary>Dayanılan kanıt parçası. Analizin kanıt kümesinde bulunmak zorundadır.</summary>
    public required Guid EvidenceChunkId { get; init; }

    /// <summary>İlgili kriter kodu; katalogda tanımlı olmak zorundadır.</summary>
    public required string CriterionCode { get; init; }

    /// <summary>Modelin kendi güven değeri (0..1). Kural sonucunu değiştirmeye yetmez.</summary>
    public required decimal Confidence { get; init; }

    /// <summary>
    /// Modelin kanıttan aldığını iddia ettiği alıntı. Kanıt metninde <b>gerçekten</b>
    /// bulunmazsa iddia reddedilir; uydurma alıntı en sık görülen halüsinasyon biçimidir.
    /// </summary>
    public string? Quote { get; init; }
}

/// <summary>Bir iddianın neden reddedildiği. Sessiz eleme yapılmaz; sebep kaydedilir.</summary>
public enum ClaimRejectionReason
{
    None = 0,

    /// <summary>Kanıt kimliği bu analizin kanıt kümesinde yok.</summary>
    UnknownEvidence = 1,

    /// <summary>Alıntı, gösterilen kanıt parçasında bulunamadı.</summary>
    QuoteNotFound = 2,

    /// <summary>Kriter kodu katalogda tanımlı değil.</summary>
    UnknownCriterion = 3,

    /// <summary>Güven değeri 0..1 aralığında değil.</summary>
    InvalidConfidence = 4,

    /// <summary>Açıklama boş.</summary>
    EmptyExplanation = 5,

    /// <summary>Zorunlu bir kriterin <c>NotMet</c> sonucunu değiştirmeye çalışıyor.</summary>
    OverridesMandatoryFailure = 6,

    /// <summary>Kanıtsız şekilde <c>Unknown</c> kriteri <c>Met</c> yapmaya çalışıyor.</summary>
    UnsupportedResolution = 7,

    /// <summary>Açıklamada belgede doğrulanamayan tarih, tutar, oran veya mevzuat maddesi var.</summary>
    UnverifiableFact = 8,

    /// <summary>Açıklamada geçersiz veya resmî olmayan bir bağlantı var.</summary>
    InvalidOfficialUrl = 9
}

/// <summary>Doğrulamadan geçmiş ya da reddedilmiş tek bir iddia.</summary>
public sealed record ValidatedClaim
{
    public required AiClaim Claim { get; init; }

    public required bool Accepted { get; init; }

    public ClaimRejectionReason RejectionReason { get; init; } = ClaimRejectionReason.None;

    /// <summary>Reddin Türkçe gerekçesi; denetim kaydına ve geliştirici loguna girer.</summary>
    public string? RejectionNote { get; init; }
}

/// <summary>
/// Modelin bir analiz için ürettiği tüm çıktı.
///
/// <para>
/// <see cref="Status"/> <see cref="AiAnalysisStatus.Succeeded"/> değilse
/// <see cref="Claims"/> boştur ve kural sonuçları olduğu gibi kalır. Model hatası
/// kesin kural sonuçlarını <b>silmez</b>.
/// </para>
/// </summary>
public sealed record AiAnalysisOutput
{
    public static AiAnalysisOutput Unavailable(string reason) => new()
    {
        Status = AiAnalysisStatus.AIUnavailable,
        Claims = [],
        StatusNote = reason
    };

    public static AiAnalysisOutput Failed(AiAnalysisStatus status, string reason)
    {
        DomainException.ThrowIf(
            status == AiAnalysisStatus.Succeeded,
            "Başarılı sonuç hata olarak kaydedilemez.");

        return new AiAnalysisOutput { Status = status, Claims = [], StatusNote = reason };
    }

    public required AiAnalysisStatus Status { get; init; }

    public required IReadOnlyList<AiClaim> Claims { get; init; }

    /// <summary>Modelin kısa özeti. Kanıtlı iddia yoksa kullanıcıya gösterilmez.</summary>
    public string? Summary { get; init; }

    public string? ModelProvider { get; init; }

    public string? ModelName { get; init; }

    /// <summary>Model parametreleri (sıcaklık vb.); tekrarlanabilirlik için kaydedilir.</summary>
    public string? ModelParameters { get; init; }

    public string? PromptVersion { get; init; }

    /// <summary>Prompt şablonunun SHA-256 özeti; şablon değişince analiz farklılaşır.</summary>
    public string? PromptTemplateHash { get; init; }

    public string? OutputSchemaVersion { get; init; }

    /// <summary>Durumun Türkçe açıklaması; hata metni ham hâliyle loglanmaz.</summary>
    public string? StatusNote { get; init; }

    public bool IsUsable => Status == AiAnalysisStatus.Succeeded;
}
