using GovAI.Application.Opportunities;
using GovAI.Persistence.Seed;

namespace GovAI.Application.Tests;

/// <summary>
/// Faz 3 kaynak düzeltmeleri: liste sayfalarının elenmesi ve GİB'in resmî alan adı.
///
/// Testlerin tamamı sahada görülmüş somut hatalara dayanır; hiçbiri varsayımsal değildir.
/// </summary>
public sealed class Faz3KaynakTests
{
    // ═══════════ KOSGEB liste sayfaları ═══════════

    [Theory(DisplayName = "F1. KOSGEB liste sayfası fırsat oluşturmaz")]
    [InlineData("Destekler Listesi - KOSGEB T.C. Küçük ve Orta Ölçekli İşletmeleri Geliştirme ve Destekleme İdaresi Başkanlığı")]
    [InlineData("Destekler Listesi")]
    [InlineData("Destekler")]
    [InlineData("Destek Programları")]
    public void Kosgeb_liste_sayfasi_firsat_olusturmaz(string baslik)
    {
        // İlk taramada tam olarak bu başlıkla iki çöp fırsat kaydı açılmıştı: sayfa
        // birden çok destek programını listeler, tek bir çağrı değildir.
        Assert.True(SectionHeading.IsCollective(baslik), baslik);
    }

    [Fact(DisplayName = "F2. Yürürlükten kaldırılmış destek aktif fırsat gibi görünmez")]
    public void Yururlukten_kaldirilan_destek_aktif_gorunmez()
    {
        Assert.True(SectionHeading.IsCollective("Yürürlükten Kaldırılan Destekler"));
        Assert.True(SectionHeading.IsCollective("Yürürlükten Kaldırılan Destekler - KOSGEB"));
    }

    [Theory(DisplayName = "F3. Gerçek destek programı başlığı ELENMEZ")]
    [InlineData("KOBİ Dijital Dönüşüm Destek Programı")]
    [InlineData("Girişimci Destek Programı")]
    [InlineData("2026 Yılı Ar-Ge ve Dijitalleşme Mali Destek Programı")]
    [InlineData("Stratejik Ürün Destek Programı")]
    public void Gercek_destek_programi_elenmez(string baslik)
    {
        // Fazla eleme, çağrının hiç görünmemesi demektir; bu, çöp kayıttan pahalıdır.
        Assert.False(SectionHeading.IsCollective(baslik), baslik);
    }

    [Fact(DisplayName = "F4. Kurum adı eki gerçek başlığı elemez")]
    public void Kurum_adi_eki_gercek_basligi_elemez()
    {
        Assert.False(SectionHeading.IsCollective("KOBİ Dijital Dönüşüm Destek Programı - KOSGEB"));
    }

    // ═══════════ GİB ═══════════

    [Fact(DisplayName = "F5. GİB için yalnızca resmî alan adı kabul edilir")]
    public void Gib_icin_yalnizca_resmi_alan_adi_kabul_edilir()
    {
        var gib = OfficialSourceCatalog.All.Single(d => d.Name == "Gelir İdaresi Başkanlığı");

        Assert.Equal("gib.gov.tr", gib.OfficialDomain);

        // Resmî alan adında olan adres kabul edilir.
        Assert.Null(OfficialLink.Verify("https://www.gib.gov.tr/duyuru-arsivi/guncel", gib.OfficialDomain).RejectionReason);

        // Benzeyen ama farklı alan adları REDDEDİLİR: nokta sınırına bakılır, düz
        // "içeriyor" araması "gib.gov.tr.saldirgan.com" adresini kabul ederdi.
        foreach (var sahte in new[]
                 {
                     "https://gib.gov.tr.example.com/duyuru",
                     "https://notgib.gov.tr/duyuru",
                     "https://gib-gov-tr.example.net/duyuru",
                 })
        {
            Assert.NotNull(OfficialLink.Verify(sahte, gib.OfficialDomain).RejectionReason);
        }
    }

    [Fact(DisplayName = "F6. GİB kaynağı doğrulanmamış olduğu için kapalı gelir")]
    public void Gib_kaynagi_kapali_gelir()
    {
        // Sitesi tarayıcıda üretiliyor ve statik HTML'de hiç bağlantı yok. Seed,
        // doğrulanmamış kaynağı devre dışı bırakır; bu kayıt da o yoldan kapalı kalır.
        var gib = OfficialSourceCatalog.ToSource(
            OfficialSourceCatalog.All.Single(d => d.Name == "Gelir İdaresi Başkanlığı"));

        Assert.False(gib.ConfigurationVerified);
    }

    [Fact(DisplayName = "F7. Resmî Gazete üzerinden gelen GİB kaydında kurum ve yayın kaynağı AYRIDIR")]
    public void Kurum_ve_yayin_kaynagi_ayridir()
    {
        // GİB tebliğleri Resmî Gazete'de yayımlanır. Kayıt "GİB'in sitesi tarandı"
        // demez: yayın kaynağı Resmî Gazete, düzenlemeyi yapan kurum GİB'dir ve ikisi
        // ayrı alanlarda durur.
        var resmiGazete = OfficialSourceCatalog.All.Single(d => d.Name == "Resmî Gazete");
        var gib = OfficialSourceCatalog.All.Single(d => d.Name == "Gelir İdaresi Başkanlığı");

        Assert.NotEqual(resmiGazete.Authority, gib.Authority);
        Assert.NotEqual(resmiGazete.OfficialDomain, gib.OfficialDomain);

        var degisiklik = new GovAI.Domain.Regulatory.RegulatoryChange(
            sourceId: Guid.CreateVersion7(),
            sourceDocumentId: Guid.CreateVersion7(),
            documentVersionId: Guid.CreateVersion7(),
            jurisdiction: "TR",
            authority: gib.Authority,
            domain: GovAI.Domain.Common.RegulationDomain.Tax,
            changeType: GovAI.Domain.Common.RegulatoryChangeType.Communique,
            title: "Vergi Usul Kanunu Genel Tebliği",
            officialUrl: "https://www.resmigazete.gov.tr/eskiler/2026/09/20260907-5.htm",
            contentHash: new string('a', 64),
            detectedAt: DateTimeOffset.UtcNow);

        // Kurum GİB, adres Resmî Gazete: ikisi karışmaz.
        Assert.Equal("Gelir İdaresi Başkanlığı", degisiklik.Authority);
        Assert.Contains("resmigazete.gov.tr", degisiklik.OfficialUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("gib.gov.tr", degisiklik.OfficialUrl, StringComparison.Ordinal);
    }
}
