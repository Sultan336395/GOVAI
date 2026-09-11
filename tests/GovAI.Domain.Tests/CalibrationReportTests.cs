using GovAI.Domain.Calibration;
using GovAI.Domain.Common;

namespace GovAI.Domain.Tests;

/// <summary>
/// Kalibrasyon ölçümü.
///
/// <para>
/// Projenin Ar-Ge iddiası, skorun uzman görüşüyle tutarlılığının <b>ölçülebilmesine</b>
/// dayanır. Bu testler ölçümün kendisini sabitler: hata türleri doğru sayılmalı,
/// yönü karışmamalı ve yetersiz örneklem yeterliymiş gibi sunulmamalıdır.
/// </para>
/// </summary>
public class CalibrationReportTests
{
    private static readonly DateTimeOffset An = new(2026, 9, 11, 9, 0, 0, TimeSpan.FromHours(3));

    private static ExpertVerdict Kayit(
        EligibilityVerdict sistem,
        EligibilityVerdict uzman,
        decimal skor = 70m,
        bool eksikVeri = false,
        VerdictDisagreementReason sebep = VerdictDisagreementReason.RuleExtraction) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sistem,
            skor,
            eksikVeri,
            uzman,
            sistem == uzman ? VerdictDisagreementReason.None : sebep,
            note: null,
            An,
            "danisman@govai.local");

    // ── Hata türleri ────────────────────────────────────────────────────────

    [Fact(DisplayName = "KL1. Sistem uygun, uzman uygun değil: YANLIŞ POZİTİF")]
    public void Yanlis_pozitif_dogru_sayilir()
    {
        // Bu hata pahalıdır: firma uygun olmadığı bir programa zaman ayırır.
        var kayit = Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.NotEligible);

        Assert.True(kayit.IsFalsePositive);
        Assert.False(kayit.IsFalseNegative);
        Assert.False(kayit.Agrees);
    }

    [Fact(DisplayName = "KL2. Sistem uygun değil, uzman uygun: YANLIŞ NEGATİF")]
    public void Yanlis_negatif_dogru_sayilir()
    {
        // Bu hata sessizdir: firma fırsatı hiç görmez, kaçırdığını da bilmez.
        var kayit = Kayit(EligibilityVerdict.NotEligible, EligibilityVerdict.Eligible);

        Assert.True(kayit.IsFalseNegative);
        Assert.False(kayit.IsFalsePositive);
    }

    [Fact(DisplayName = "KL3. Şartlı uygun POZİTİF sayılır")]
    public void Sartli_uygun_pozitiftir()
    {
        // "Şartlı uygun" firmayı başvuru hazırlığına yönlendirir; uzman "uygun değil"
        // diyorsa bu da boşa harcanmış zamandır.
        Assert.True(Kayit(EligibilityVerdict.ConditionallyEligible, EligibilityVerdict.NotEligible)
            .IsFalsePositive);

        Assert.True(Kayit(EligibilityVerdict.NotEligible, EligibilityVerdict.ConditionallyEligible)
            .IsFalseNegative);
    }

    [Fact(DisplayName = "KL4. Uygun ile şartlı uygun arasındaki fark hata SAYILMAZ")]
    public void Pozitifler_arasi_fark_hata_degildir()
    {
        // İkisi de firmayı aynı yöne sevk eder; bunu hata saymak, gerçek hataların
        // oranını gürültü içinde kaybederdi.
        var kayit = Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.ConditionallyEligible);

        Assert.False(kayit.Agrees);
        Assert.False(kayit.IsFalsePositive);
        Assert.False(kayit.IsFalseNegative);
    }

    // ── Rapor ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "KL5. Uyum oranı ve hata oranları doğru hesaplanır")]
    public void Oranlar_dogru_hesaplanir()
    {
        var rapor = CalibrationReport.Build(
        [
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.Eligible),
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.Eligible),
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.NotEligible),
            Kayit(EligibilityVerdict.NotEligible, EligibilityVerdict.Eligible),
        ]);

        Assert.Equal(4, rapor.TotalVerdicts);
        Assert.Equal(2, rapor.AgreementCount);
        Assert.Equal(0.5m, rapor.AgreementRate);
        Assert.Equal(1, rapor.FalsePositiveCount);
        Assert.Equal(0.25m, rapor.FalsePositiveRate);
        Assert.Equal(1, rapor.FalseNegativeCount);
        Assert.Equal(0.25m, rapor.FalseNegativeRate);
    }

    [Fact(DisplayName = "KL6. Yetersiz örneklem yeterliymiş gibi SUNULMAZ")]
    public void Yetersiz_orneklem_isaretlenir()
    {
        // Üç kayıtla hesaplanan "%33 hata oranı" istatistik değil gürültüdür; bu sayıya
        // bakarak ağırlık değiştirmek modeli bozar.
        var az = CalibrationReport.Build([
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.NotEligible),
        ]);

        Assert.False(az.IsSampleSufficient);

        var yeterli = CalibrationReport.Build(
            Enumerable.Range(0, CalibrationReport.MinimumSampleSize)
                .Select(_ => Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.Eligible))
                .ToList());

        Assert.True(yeterli.IsSampleSufficient);
    }

    [Fact(DisplayName = "KL7. Eksik veriden doğan ayrışma AYRI sayılır")]
    public void Eksik_veri_ayrismasi_ayri_sayilir()
    {
        // Ayrışmanın sebebi modelin yanlışlığı değil verinin yokluğu olabilir. İkisini
        // ayırmadan kalibrasyon yapmak, veri sorununu ağırlık sorunu sanıp ağırlıkları
        // bozar.
        var rapor = CalibrationReport.Build(
        [
            Kayit(EligibilityVerdict.Indeterminate, EligibilityVerdict.Eligible, eksikVeri: true),
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.NotEligible, eksikVeri: false),
        ]);

        Assert.Equal(2, rapor.TotalVerdicts);
        Assert.Equal(1, rapor.DataGapDisagreementCount);
    }

    [Fact(DisplayName = "KL8. Karışıklık matrisi hatanın YÖNÜNÜ gösterir")]
    public void Matris_hatanin_yonunu_gosterir()
    {
        // Tek bir uyum oranı, sistemin fazla iyimser mi fazla temkinli mi olduğunu
        // söylemez; iki durum ters yönde düzeltme gerektirir.
        var rapor = CalibrationReport.Build(
        [
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.NotEligible),
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.NotEligible),
            Kayit(EligibilityVerdict.NotEligible, EligibilityVerdict.Eligible),
        ]);

        var iyimser = rapor.Matrix.Single(c =>
            c.SystemVerdict == EligibilityVerdict.Eligible
            && c.ExpertOpinion == EligibilityVerdict.NotEligible);

        Assert.Equal(2, iyimser.Count);
        Assert.Equal(2, rapor.Matrix.Count);
    }

    [Fact(DisplayName = "KL9. Ayrışma sebepleri sayılır, en sık olan başta")]
    public void Sebepler_siklikla_siralanir()
    {
        var rapor = CalibrationReport.Build(
        [
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.NotEligible,
                sebep: VerdictDisagreementReason.CompanyData),
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.NotEligible,
                sebep: VerdictDisagreementReason.CompanyData),
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.NotEligible,
                sebep: VerdictDisagreementReason.ScoreWeighting),
        ]);

        Assert.Equal(VerdictDisagreementReason.CompanyData, rapor.Reasons[0].Reason);
        Assert.Equal(2, rapor.Reasons[0].Count);
    }

    [Fact(DisplayName = "KL10. Skor ayrımı: uzmanın kararına göre ortalama skor")]
    public void Skor_ayrimi_hesaplanir()
    {
        // İki ortalama birbirine yakınsa skor ayrım üretmiyor demektir; sorun eşikte
        // değil modelin kendisindedir.
        var rapor = CalibrationReport.Build(
        [
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.Eligible, skor: 80m),
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.Eligible, skor: 90m),
            Kayit(EligibilityVerdict.NotEligible, EligibilityVerdict.NotEligible, skor: 20m),
        ]);

        var uygun = rapor.ScoreBands.Single(b => b.ExpertOpinion == EligibilityVerdict.Eligible);

        Assert.Equal(85m, uygun.AverageSystemScore);
        Assert.Equal(80m, uygun.MinSystemScore);
        Assert.Equal(90m, uygun.MaxSystemScore);
    }

    [Fact(DisplayName = "KL11. Kayıt yoksa rapor BOŞ döner, sıfıra bölme olmaz")]
    public void Kayitsiz_rapor_cokmez()
    {
        var rapor = CalibrationReport.Build([]);

        Assert.Equal(0, rapor.TotalVerdicts);
        Assert.Equal(0m, rapor.AgreementRate);
        Assert.False(rapor.IsSampleSufficient);
        Assert.Empty(rapor.Matrix);
    }

    [Fact(DisplayName = "KL12. Aynı girdi aynı raporu üretir")]
    public void Rapor_deterministiktir()
    {
        var kayitlar = new[]
        {
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.NotEligible),
            Kayit(EligibilityVerdict.NotEligible, EligibilityVerdict.Eligible),
            Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.Eligible),
        };

        var bir = CalibrationReport.Build(kayitlar);
        var iki = CalibrationReport.Build(kayitlar);

        Assert.Equal(bir.AgreementRate, iki.AgreementRate);
        Assert.Equal(
            bir.Matrix.Select(c => (c.SystemVerdict, c.ExpertOpinion, c.Count)),
            iki.Matrix.Select(c => (c.SystemVerdict, c.ExpertOpinion, c.Count)));
    }

    // ── Kayıt kuralları ─────────────────────────────────────────────────────

    [Fact(DisplayName = "KL13. Sistemden farklı karar verilirken SEBEP zorunludur")]
    public void Ayrismada_sebep_zorunludur()
    {
        // Sebepsiz ayrışma sayılabilir ama düzeltilemez: hangi hatanın giderileceği
        // bilinmeden kalibrasyon yapılamaz.
        var hata = Assert.Throws<DomainException>(() => new ExpertVerdict(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            EligibilityVerdict.Eligible, 80m, false,
            EligibilityVerdict.NotEligible, VerdictDisagreementReason.None,
            note: null, An, "danisman@govai.local"));

        Assert.Contains("sebep", hata.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "KL14. Uyum hâlinde sebep SAKLANMAZ")]
    public void Uyumda_sebep_saklanmaz()
    {
        // Raporda olmayan bir hatanın gerekçesini göstermek okuyucuyu yanıltır.
        var kayit = Kayit(EligibilityVerdict.Eligible, EligibilityVerdict.Eligible);

        Assert.Equal(VerdictDisagreementReason.None, kayit.DisagreementReason);
    }
}
