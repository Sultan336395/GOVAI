using GovAI.Domain.Common;
using GovAI.Domain.Tenders;

namespace GovAI.Application.Tenders;

/// <summary>Takip kartının tek satırı: ihale künyesi, firmanın kendi durumu ve sistemin kararı.</summary>
public sealed record TenderPursuitDto(
    Guid Id,
    Guid OpportunityId,
    string Title,
    string Publisher,
    SupportCategory Category,
    string CategoryLabel,
    string? SourceUrl,
    DateTimeOffset? Deadline,
    /// <summary>Son başvuruya kalan gün. Tarih yoksa <c>null</c>; sıfır yazılmaz.</summary>
    int? DaysToDeadline,
    TenderPursuitStatus Status,
    string StatusLabel,
    TenderOutcome? Outcome,
    string? OutcomeLabel,
    string? Note,
    string? Owner,
    DateTimeOffset StartedAt,
    DateTimeOffset StatusChangedAt,
    bool IsClosed,
    /// <summary>Süre doldu ama takip hâlâ açık. Ekranda uyarı olarak görünür.</summary>
    bool IsOverdue,
    /// <summary>Sistemin kendi değerlendirmesi; hiç değerlendirilmemişse <c>null</c>.</summary>
    TenderAssessmentDto? Assessment,
    IReadOnlyList<TenderPursuitEventDto> History);

/// <summary>
/// Takip edilen ihalenin sistem değerlendirmesi.
///
/// <para>
/// Takip ekranında gösterilir ama takipten <b>etkilenmez</b>: firma bir ihaleyi
/// işaretlediği için skoru yükselmez. Buradaki amaç tersidir — firmanın hazırlığa
/// aldığı bir ihalede "sektör uyumu doğrulanamadı" uyarısını zamanında görmesi.
/// </para>
/// </summary>
public sealed record TenderAssessmentDto(
    decimal Score,
    EligibilityVerdict Verdict,
    string VerdictLabel,
    SectorFit SectorFit,
    string SectorFitLabel,
    int MissingConditionCount,
    int MissingMandatoryDocumentCount,
    DateTimeOffset EvaluatedAt);

public sealed record TenderPursuitEventDto(
    TenderPursuitStatus? FromStatus,
    string? FromStatusLabel,
    TenderPursuitStatus ToStatus,
    string ToStatusLabel,
    TenderOutcome? Outcome,
    string? OutcomeLabel,
    string? Note,
    DateTimeOffset At,
    string By);

/// <summary>Takip listesi ve üstündeki sayaçlar.</summary>
public sealed record TenderBoardDto(
    Guid CompanyId,
    IReadOnlyList<TenderPursuitDto> Pursuits,
    TenderBoardCountsDto Counts);

/// <summary>
/// Aşama sayaçları.
///
/// <para>
/// Kazanılan ve kaybedilen ayrı sayılır; "sonuçlandı" tek sayı olarak verilseydi ekran
/// başarıyı da başarısızlığı da aynı rakamla gösterirdi.
/// </para>
/// </summary>
public sealed record TenderBoardCountsDto(
    int Inceleniyor,
    int Hazirlaniyor,
    int TeklifVerildi,
    int Kazanildi,
    int Kaybedildi,
    int Iptal,
    int Vazgecildi,
    /// <summary>Süresi dolduğu hâlde kapatılmamış takipler.</summary>
    int SuresiGecen);

public sealed record StartTenderPursuitRequest
{
    public required Guid OpportunityId { get; init; }

    public string? Note { get; init; }

    public string? Owner { get; init; }
}

public sealed record ChangeTenderPursuitRequest
{
    public required TenderPursuitStatus Status { get; init; }

    /// <summary><see cref="TenderPursuitStatus.Sonuclandi"/> için zorunludur.</summary>
    public TenderOutcome? Outcome { get; init; }

    public string? Note { get; init; }

    public string? Owner { get; init; }
}

/// <summary>Aşama ve sonuç etiketleri. Ham enum adı kullanıcıya gösterilmez.</summary>
public static class TenderLabels
{
    public static string Of(TenderPursuitStatus status) => status switch
    {
        TenderPursuitStatus.Inceleniyor => "İnceleniyor",
        TenderPursuitStatus.Hazirlaniyor => "Hazırlanıyor",
        TenderPursuitStatus.TeklifVerildi => "Teklif verildi",
        TenderPursuitStatus.Sonuclandi => "Sonuçlandı",
        TenderPursuitStatus.Vazgecildi => "Vazgeçildi",
        _ => status.ToString()
    };

    public static string Of(TenderOutcome outcome) => outcome switch
    {
        TenderOutcome.Kazanildi => "Kazanıldı",
        TenderOutcome.Kaybedildi => "Kaybedildi",
        TenderOutcome.Iptal => "İptal edildi",
        _ => outcome.ToString()
    };
}
