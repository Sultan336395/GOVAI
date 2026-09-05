using GovAI.Application.Opportunities;

namespace GovAI.Application.Tests;

/// <summary>
/// Toplu bölüm başlığının fırsat sayılmaması (Faz 2).
///
/// <para>
/// Resmî Gazete'nin ilan bölümü tek sayfada onlarca ayrı ilan yayımlar ve sayfanın
/// başlığı bunların ortak başlığıdır: "ARTIRMA, EKSİLTME VE İHALE İLÂNLARI". Böyle bir
/// kayıt fırsat kataloğuna girerse şirkete "size uygun bir ihale var" denir, bağlantı
/// bir liste sayfasına götürür ve danışman hangi ihaleden bahsedildiğini bilemez.
/// </para>
///
/// <para>
/// Kural <b>tam eşleşmedir</b>: gerçek bir ilanın başlığında bu ifade geçebilir,
/// yalnızca başlığın kendisi toplu başlıksa kayıt açılmaz.
/// </para>
/// </summary>
public sealed class BolumBasligiTests
{
    [Theory(DisplayName = "B1. Toplu ilan başlıkları fırsat sayılmaz")]
    [InlineData("ARTIRMA, EKSİLTME VE İHALE İLÂNLARI")]
    [InlineData("ARTIRMA, EKSILTME VE IHALE ILANLARI")]
    [InlineData("artırma, eksiltme ve ihale ilânları")]
    [InlineData("ÇEŞİTLİ İLÂNLAR")]
    [InlineData("ÇEŞİTLİ İLANLAR")]
    [InlineData("İLÂN BÖLÜMÜ")]
    [InlineData("İHALE İLANLARI")]
    [InlineData("İLANLAR")]
    [InlineData("DUYURULAR")]
    public void Toplu_basliklar_reddedilir(string baslik)
    {
        Assert.True(SectionHeading.IsCollective(baslik));
    }

    [Theory(DisplayName = "B2. Noktalama ve şapka farkı sonucu değiştirmez")]
    [InlineData("ARTIRMA  EKSİLTME  VE  İHALE  İLANLARI")]
    [InlineData("ARTIRMA, EKSİLTME VE İHALE İLÂNLARI ")]
    [InlineData("  ARTIRMA, EKSİLTME VE İHALE İLÂNLARI\n")]
    [InlineData("ARTIRMA - EKSİLTME - VE - İHALE - İLANLARI")]
    public void Yazim_farklari_etkilemez(string baslik)
    {
        Assert.True(SectionHeading.IsCollective(baslik));
    }

    [Theory(DisplayName = "B3. Gerçek tekil ihale ilanları elenmez")]
    [InlineData("Mersin Büyükşehir Belediyesi Su ve Kanalizasyon İdaresi Genel Müdürlüğünden: TAŞINMAZ SATILACAKTIR")]
    [InlineData("TCDD 3. Bölge Müdürlüğünden: HİZMET ALINACAKTIR")]
    [InlineData("Sağlık Bakanlığından: TIBBİ CİHAZ SATIN ALINACAKTIR")]
    [InlineData("Türkiye Elektrik İletim A.Ş. Genel Müdürlüğünden: TAŞINMAZ KİRAYA VERİLECEKTİR")]
    [InlineData("2026/1 sayılı ihale ilânları kapsamında düzeltme")]
    [InlineData("KOSGEB İşletme Geliştirme Destek Programı Çağrısı")]
    public void Gercek_ilanlar_elenmez(string baslik)
    {
        Assert.False(SectionHeading.IsCollective(baslik));
    }

    [Theory(DisplayName = "B4. Boş başlık toplu başlık sayılmaz")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Bos_baslik_reddedilmez(string? baslik)
    {
        // Boş başlık ayrı bir sorundur ve başka kural tarafından ele alınır.
        Assert.False(SectionHeading.IsCollective(baslik));
    }

    [Fact(DisplayName = "B5. Ret gerekçesi ne yapılması gerektiğini söyler")]
    public void Gerekce_yol_gosterir()
    {
        var gerekce = SectionHeading.Explanation("ARTIRMA, EKSİLTME VE İHALE İLÂNLARI");

        Assert.Contains("bölümün", gerekce, StringComparison.Ordinal);
        Assert.Contains("ayrı ayrı ayrıştırılmalıdır", gerekce, StringComparison.Ordinal);
    }
}
