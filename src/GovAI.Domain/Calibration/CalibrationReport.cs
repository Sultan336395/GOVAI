using GovAI.Domain.Common;

namespace GovAI.Domain.Calibration;

/// <summary>
/// Sistem kararlarının uzman görüşüyle tutarlılığının ölçümü.
///
/// <para>
/// Rapor tamamen <b>deterministiktir</b>: aynı kayıt kümesi her zaman aynı sonucu verir.
/// Model çağrısı, rastgelelik ve <c>DateTime.Now</c> yoktur. Kalibrasyon, karar
/// mekanizmasının denetçisidir; denetçinin kendisi yorum yapamaz.
/// </para>
///
/// <para>
/// Rapor <b>öneri üretmez</b>. Hangi ağırlığın nasıl değiştirileceği insan kararıdır;
/// buradaki iş, hatayı türüne göre sayıp nerede durduğunu göstermektir. Sayılara bakıp
/// otomatik ağırlık değiştiren bir mekanizma, motorun deterministikliğini ve
/// açıklanabilirliğini bozardı.
/// </para>
/// </summary>
public static class CalibrationReport
{
    /// <summary>
    /// Anlamlı yorum için gereken asgari kayıt sayısı.
    ///
    /// <para>
    /// Üç kayıtla hesaplanan bir "%33 yanlış pozitif oranı" istatistik değil gürültüdür;
    /// ağırlıkları böyle bir sayıya bakarak değiştirmek modeli bozar. Rapor bu eşiğin
    /// altında da üretilir, ama yetersiz olduğunu <b>söyler</b>.
    /// </para>
    /// </summary>
    public const int MinimumSampleSize = 20;

    public static CalibrationSummary Build(IReadOnlyList<ExpertVerdict> verdicts)
    {
        ArgumentNullException.ThrowIfNull(verdicts);

        var toplam = verdicts.Count;

        if (toplam == 0)
        {
            return CalibrationSummary.Empty;
        }

        var uyumlu = verdicts.Count(v => v.Agrees);
        var yanlisPozitif = verdicts.Count(v => v.IsFalsePositive);
        var yanlisNegatif = verdicts.Count(v => v.IsFalseNegative);

        // Eksik veriyle verilen karar ayrı sayılır: ayrışmanın sebebi modelin yanlışlığı
        // değil verinin yokluğu olabilir ve ikisi ayrı düzeltme gerektirir.
        var eksikVeriliAyrisma = verdicts.Count(v => !v.Agrees && v.SystemHadDataGap);

        var matris = Matris(verdicts);
        var sebepler = Sebepler(verdicts);
        var skorAyrimi = SkorAyrimi(verdicts);

        return new CalibrationSummary(
            toplam,
            uyumlu,
            Oran(uyumlu, toplam),
            yanlisPozitif,
            Oran(yanlisPozitif, toplam),
            yanlisNegatif,
            Oran(yanlisNegatif, toplam),
            eksikVeriliAyrisma,
            toplam >= MinimumSampleSize,
            matris,
            sebepler,
            skorAyrimi);
    }

    /// <summary>
    /// Karışıklık matrisi: sistem kararı × uzman kararı.
    ///
    /// <para>
    /// Tek bir "uyum oranı" hatanın <b>yönünü</b> göstermez. Sistemin sürekli fazla
    /// iyimser mi yoksa fazla temkinli mi olduğu ancak matriste görülür ve iki durum
    /// ters yönde düzeltme gerektirir.
    /// </para>
    /// </summary>
    private static IReadOnlyList<CalibrationMatrixCell> Matris(IReadOnlyList<ExpertVerdict> verdicts) =>
        verdicts
            .GroupBy(v => (v.SystemVerdict, v.ExpertOpinion))
            .Select(g => new CalibrationMatrixCell(g.Key.SystemVerdict, g.Key.ExpertOpinion, g.Count()))
            .OrderBy(c => c.SystemVerdict)
            .ThenBy(c => c.ExpertOpinion)
            .ToList();

    /// <summary>Ayrışmaların sebebe göre dağılımı: hangi düzeltme en çok işe yarar.</summary>
    private static IReadOnlyList<CalibrationReasonCount> Sebepler(IReadOnlyList<ExpertVerdict> verdicts) =>
        verdicts
            .Where(v => !v.Agrees)
            .GroupBy(v => v.DisagreementReason)
            .Select(g => new CalibrationReasonCount(g.Key, g.Count()))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Reason)
            .ToList();

    /// <summary>
    /// Uzmanın her kararı için sistemin ortalama skoru.
    ///
    /// <para>
    /// Skorun işe yarayıp yaramadığı buradan okunur: uzmanın "uygun" dediği vakaların
    /// ortalaması, "uygun değil" dediklerinden belirgin biçimde yüksek olmalıdır. İki
    /// ortalama birbirine yakınsa skor ayrım üretmiyor demektir ve sorun eşikte değil,
    /// modelin kendisindedir.
    /// </para>
    /// </summary>
    private static IReadOnlyList<CalibrationScoreBand> SkorAyrimi(IReadOnlyList<ExpertVerdict> verdicts) =>
        verdicts
            .GroupBy(v => v.ExpertOpinion)
            .Select(g => new CalibrationScoreBand(
                g.Key,
                g.Count(),
                Math.Round(g.Average(v => v.SystemScore), 1),
                Math.Round(g.Min(v => v.SystemScore), 1),
                Math.Round(g.Max(v => v.SystemScore), 1)))
            .OrderBy(b => b.ExpertOpinion)
            .ToList();

    private static decimal Oran(int pay, int payda) =>
        payda == 0 ? 0m : Math.Round((decimal)pay / payda, 4);
}

/// <summary>Kalibrasyon ölçümünün sonucu.</summary>
public sealed record CalibrationSummary(
    int TotalVerdicts,
    int AgreementCount,
    decimal AgreementRate,
    int FalsePositiveCount,
    decimal FalsePositiveRate,
    int FalseNegativeCount,
    decimal FalseNegativeRate,
    /// <summary>Eksik veriyle verilmiş karardan doğan ayrışma sayısı.</summary>
    int DataGapDisagreementCount,
    /// <summary>Örneklem yorum yapmaya yetiyor mu?</summary>
    bool IsSampleSufficient,
    IReadOnlyList<CalibrationMatrixCell> Matrix,
    IReadOnlyList<CalibrationReasonCount> Reasons,
    IReadOnlyList<CalibrationScoreBand> ScoreBands)
{
    public static CalibrationSummary Empty { get; } =
        new(0, 0, 0m, 0, 0m, 0, 0m, 0, false, [], [], []);
}

public sealed record CalibrationMatrixCell(
    EligibilityVerdict SystemVerdict,
    EligibilityVerdict ExpertOpinion,
    int Count);

public sealed record CalibrationReasonCount(VerdictDisagreementReason Reason, int Count);

public sealed record CalibrationScoreBand(
    EligibilityVerdict ExpertOpinion,
    int Count,
    decimal AverageSystemScore,
    decimal MinSystemScore,
    decimal MaxSystemScore);
