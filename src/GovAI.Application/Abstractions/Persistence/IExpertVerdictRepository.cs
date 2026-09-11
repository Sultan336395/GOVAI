using GovAI.Application.Calibration;
using GovAI.Domain.Calibration;

namespace GovAI.Application.Abstractions.Persistence;

/// <summary>
/// Uzman değerlendirmelerinin veri erişimi.
///
/// <para>
/// Silme yöntemi <b>bilerek yoktur</b>. Kalibrasyon, ölçümün kendisi kadar ölçümün
/// geçmişine de dayanır: uyumsuz çıkan kayıtların silinebilmesi, raporu istenen sonuca
/// göre şekillendirmeyi mümkün kılardı.
/// </para>
/// </summary>
public interface IExpertVerdictRepository
{
    Task<ExpertVerdict?> GetByAssessmentAsync(Guid assessmentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpertVerdictDto>> ListForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    /// <summary>Rapor için ham kayıtlar. Firma verilmezse kiracının tamamı.</summary>
    Task<IReadOnlyList<ExpertVerdict>> ListForCalibrationAsync(
        Guid? companyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kural çıkarımının dolaylı başarı ölçüsü: danışmanın elle düzelttiği kural oranı.
    ///
    /// <para>
    /// Ayrı bir veri toplama ekranı gerektirmez; düzeltme zaten ürünün akışında yapılıyor
    /// ve <c>OpportunityRule.IsManuallyOverridden</c> alanında duruyor. Var olan veriden
    /// ölçmek, ölçüm için yeni iş yaratmaktan iyidir.
    /// </para>
    /// </summary>
    Task<RuleExtractionQualityDto> GetRuleExtractionQualityAsync(CancellationToken cancellationToken = default);

    Task AddAsync(ExpertVerdict verdict, CancellationToken cancellationToken = default);
}
