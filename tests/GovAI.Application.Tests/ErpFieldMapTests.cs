using System.Text.Json;
using GovAI.Domain.Integrations;

namespace GovAI.Application.Tests;

/// <summary>
/// ERP alan eşlemesi.
///
/// <para>
/// Aynı bilgi her üründe başka adla durur. Eşlemenin işi bu farkı tek modele indirmek;
/// ama <b>bulunamayan alanı sıfır saymamak</b> da onun sorumluluğudur. ERP'de olmayan bir
/// alanı 0 yazmak, firmayı "hiç kadın çalışanı yok" diye kaydetmek olurdu ve ürünün
/// üçüncü iddiasını ERP yolunda çiğnerdi (bkz. docs/adr/0003).
/// </para>
/// </summary>
public class ErpFieldMapTests
{
    [Theory(DisplayName = "EE1. Türkçe alan adlı ürünlerin varsayılan eşlemesi vardır")]
    [InlineData(ErpVendor.Logo)]
    [InlineData(ErpVendor.Netsis)]
    [InlineData(ErpVendor.Mikro)]
    public void Turkce_urunlerin_varsayilani_vardir(ErpVendor vendor)
    {
        var esleme = ErpFieldMap.Default(vendor);

        Assert.False(esleme.IsEmpty);
        Assert.Equal("personel.kadin", esleme.WomenEmployeeCount);
        Assert.Equal("mali.yillikCiro", esleme.AnnualRevenue);
    }

    [Theory(DisplayName = "EE2. İngilizce alan adlı ürünlerin varsayılan eşlemesi vardır")]
    [InlineData(ErpVendor.Sap)]
    [InlineData(ErpVendor.Nebim)]
    public void Ingilizce_urunlerin_varsayilani_vardir(ErpVendor vendor)
    {
        var esleme = ErpFieldMap.Default(vendor);

        Assert.False(esleme.IsEmpty);
        Assert.Equal("workforce.femaleHeadcount", esleme.WomenEmployeeCount);
        Assert.Equal("financials.annualRevenue", esleme.AnnualRevenue);
    }

    [Fact(DisplayName = "EE3. Ürün bilinmiyorsa alan adı UYDURULMAZ")]
    public void Bilinmeyen_urunde_ad_uydurulmaz()
    {
        // Yanlış bir varsayılan, ERP'de olmayan alanı arar ve kullanıcıya "veri yok"
        // dedirtir; oysa sorun eşlemededir. Boş eşleme kullanıcıyı eşlemeyi tanımlamaya
        // zorlar ve bu dürüst olandır.
        var esleme = ErpFieldMap.Default(ErpVendor.GenericRest);

        Assert.True(esleme.IsEmpty);
    }

    [Fact(DisplayName = "EE4. Eşleme JSON olarak kaydedilip geri okunabilir")]
    public void Esleme_kaydedilip_okunabilir()
    {
        // Eşleme bağlantı bazında saklanır: aynı ürünün sürümleri ve müşteriye özel
        // alanlar farklılık gösterir.
        var ozgun = ErpFieldMap.Default(ErpVendor.Logo) with { WomenEmployeeCount = "ik.kadinSayisi" };

        var json = JsonSerializer.Serialize(ozgun);

        var okunan = JsonSerializer.Deserialize<ErpFieldMap>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(okunan);
        Assert.Equal("ik.kadinSayisi", okunan.WomenEmployeeCount);
        Assert.Equal(ozgun.AnnualRevenue, okunan.AnnualRevenue);
    }
}

/// <summary>
/// ERP bağlantı kaydının kuralları.
/// </summary>
public class ErpConnectionTests
{
    private static readonly DateTimeOffset An = new(2026, 9, 11, 3, 0, 0, TimeSpan.Zero);

    private static ErpConnection Baglanti(string adres = "https://erp.ornek.com/api/govai") =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            ErpVendor.Logo,
            adres,
            ErpAuthMode.ApiKeyHeader,
            "sifrelenmis-kimlik",
            isOnPremise: false);

    [Fact(DisplayName = "EB1. Geçersiz adres kabul edilmez")]
    public void Gecersiz_adres_reddedilir()
    {
        Assert.Throws<Domain.Common.DomainException>(() => Baglanti("bu bir adres degil"));
        Assert.Throws<Domain.Common.DomainException>(() => Baglanti("ftp://erp.ornek.com"));
    }

    [Fact(DisplayName = "EB2. Üst üste başarısızlıkta bağlantı KENDİLİĞİNDEN durur")]
    public void Surekli_hatada_baglanti_durur()
    {
        // Yanlış kimlikle her gece denemeye devam etmek, müşterinin ERP'sinde hesabı
        // kilitletir.
        var baglanti = Baglanti();

        for (var i = 0; i < ErpConnection.FailureLimit; i++)
        {
            Assert.True(baglanti.IsEnabled || i == ErpConnection.FailureLimit - 1);
            baglanti.RecordFailure(An, "Kimlik kabul edilmedi.");
        }

        Assert.False(baglanti.IsEnabled);
        Assert.Equal(ErpSyncStatus.Failed, baglanti.LastRunStatus);
    }

    [Fact(DisplayName = "EB3. Başarılı çalıştırma hata sayacını SIFIRLAR")]
    public void Basari_sayaci_sifirlar()
    {
        var baglanti = Baglanti();

        baglanti.RecordFailure(An, "Geçici hata.");
        baglanti.RecordFailure(An, "Geçici hata.");
        baglanti.RecordSuccess(An, "Güncellendi.");

        Assert.Equal(0, baglanti.ConsecutiveFailureCount);
        Assert.True(baglanti.IsEnabled);
    }

    [Fact(DisplayName = "EB4. Veri değişmediyse bu HATA sayılmaz")]
    public void Degisiklik_yoksa_hata_degildir()
    {
        // ERP'ye ulaşıldı ve veri okundu; yalnızca profil zaten güncel. Bunu hata saymak,
        // sağlıklı bir bağlantıyı beş gecede kapatırdı.
        var baglanti = Baglanti();

        baglanti.RecordNoChange(An);

        Assert.Equal(ErpSyncStatus.NoChange, baglanti.LastRunStatus);
        Assert.Equal(0, baglanti.ConsecutiveFailureCount);
        Assert.True(baglanti.IsEnabled);
    }

    [Fact(DisplayName = "EB5. Kimlik bilgisi boş bırakılamaz")]
    public void Kimlik_bos_birakilamaz()
    {
        var baglanti = Baglanti();

        Assert.Throws<Domain.Common.DomainException>(() => baglanti.ReplaceSecret("  "));
    }
}
