using GovAI.Domain.Common;
using GovAI.Domain.Sources;

namespace GovAI.Domain.Tests;

/// <summary>
/// Kaynağın işletim durumu (Faz 2).
///
/// <para>
/// Panelde operatörün gördüğü iki alan birlikte anlam taşır: <b>sağlık</b> ve
/// <b>son çalışma mesajı</b>. Bunlar ayrışırsa ekran yalan söyler — kaynak
/// "Healthy" görünürken yanında haftalar önce çözülmüş bir hata metni durur.
/// </para>
/// </summary>
public sealed class SourceTests
{
    private static SourceCrawlPlan GecerliPlan => new(
        StartUrl: "https://www.resmigazete.gov.tr/",
        ListSelector: "a.fihrist-link",
        ContentSelector: "#icerik",
        UrlPattern: @"/eskiler/\d{4}/\d{2}/\d{8}\.htm$",
        MaxPages: 3,
        AllowedDomains: "resmigazete.gov.tr",
        DocumentTypes: "text/html");

    private static Source Kaynak()
    {
        var kaynak = new Source(
            "Resmî Gazete", SourceType.OfficialGazette,
            "https://www.resmigazete.gov.tr", "0 6 * * *");

        kaynak.PlanCrawl(GecerliPlan);
        return kaynak;
    }

    // ═══════════════ Bayat hata mesajı ═══════════════

    [Fact(DisplayName = "S1. Doğrulama başarılı olunca eski hata mesajı silinir")]
    public void Dogrulama_eski_hata_mesajini_siler()
    {
        var kaynak = Kaynak();
        kaynak.FailVerification("Liste seçicisi hiçbir bağlantı bulamadı.");

        Assert.Equal(SourceHealth.Failing, kaynak.Health);
        Assert.NotNull(kaynak.LastRunMessage);

        kaynak.MarkVerified(DateTimeOffset.UtcNow);

        Assert.Equal(SourceHealth.Healthy, kaynak.Health);
        Assert.Null(kaynak.LastRunMessage);
    }

    [Fact(DisplayName = "S2. Başarısız taramanın mesajı doğrulamadan sonra kalmaz")]
    public void Basarisiz_taramanin_mesaji_dogrulamadan_sonra_kalmaz()
    {
        var kaynak = Kaynak();
        kaynak.MarkVerified(DateTimeOffset.UtcNow);
        kaynak.RecordRun(DateTimeOffset.UtcNow, CrawlStatus.Failed, "Sunucuya ulaşılamadı.");

        Assert.Equal("Sunucuya ulaşılamadı.", kaynak.LastRunMessage);

        // Operatör seçiciyi düzeltip yeniden doğruladı.
        kaynak.MarkVerified(DateTimeOffset.UtcNow);

        Assert.Null(kaynak.LastRunMessage);
    }

    [Fact(DisplayName = "S3. Doğrulama başarısızsa gerekçe korunur")]
    public void Basarisiz_dogrulamada_gerekce_korunur()
    {
        var kaynak = Kaynak();
        kaynak.FailVerification("İçerik seçicisi boş döndü.");

        // Bu mesaj operatörün tek ipucudur; silinmemeli.
        Assert.Equal("İçerik seçicisi boş döndü.", kaynak.LastRunMessage);
        Assert.False(kaynak.ConfigurationVerified);
    }

    [Fact(DisplayName = "S4. Doğrulanmış kaynağın sonraki başarılı taraması mesajsızdır")]
    public void Basarili_tarama_mesajsiz_kalir()
    {
        var kaynak = Kaynak();
        kaynak.MarkVerified(DateTimeOffset.UtcNow);
        kaynak.RecordRun(DateTimeOffset.UtcNow, CrawlStatus.Succeeded, null);

        Assert.Null(kaynak.LastRunMessage);
        Assert.Equal(SourceHealth.Healthy, kaynak.Health);
        Assert.Equal(0, kaynak.ConsecutiveFailureCount);
    }

    // ═══════════════ Doğrulama ön koşulu ═══════════════

    [Fact(DisplayName = "S5. Planı taranamaz kaynak doğrulanmış sayılamaz")]
    public void Plansiz_kaynak_dogrulanamaz()
    {
        var kaynak = new Source(
            "Elle açılmış kaynak", SourceType.TenderPortal,
            "https://ornek.gov.tr", "0 6 * * *");

        Assert.Throws<DomainException>(() => kaynak.MarkVerified(DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "S6. Plan değişince doğrulama düşer")]
    public void Plan_degisince_dogrulama_duser()
    {
        var kaynak = Kaynak();
        kaynak.MarkVerified(DateTimeOffset.UtcNow);

        kaynak.PlanCrawl(GecerliPlan with { ListSelector = "a.yeni-secici" });

        Assert.False(kaynak.ConfigurationVerified);
        Assert.Null(kaynak.ConfigurationVerifiedAt);
        Assert.Equal(SourceHealth.Unverified, kaynak.Health);
    }
}
