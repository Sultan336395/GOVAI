using GovAI.Persistence.Design;

namespace GovAI.Application.Tests;

/// <summary>
/// Migration hedef güvenlik kilidi.
///
/// Bu testler bir davranış sözleşmesidir: Faz 2'de iki migration istemeden 5180'in
/// veritabanına uygulandı, çünkü <c>dotnet ef</c> açık bir hedef verilmediğinde
/// <c>appsettings.Development.json</c>'daki <c>localhost:5432</c>'ye — yani 5180'e —
/// sessizce düşüyordu. Aşağıdaki testlerden biri gevşetilirse o tuzak geri gelir.
/// </summary>
public sealed class EfMigrationTargetTests
{
    /// <summary>Sahte ortam: yalnızca verilen değişkenleri bilir.</summary>
    private static Func<string, string?> Ortam(params (string Ad, string Deger)[] degiskenler) =>
        ad => degiskenler.FirstOrDefault(d => d.Ad == ad).Deger;

    private const string OnizlemeBaglantisi =
        "Host=localhost;Port=15437;Database=govai;Username=govai;Password=onizleme-parolasi";

    private const string UretimBaglantisi =
        "Host=localhost;Port=5432;Database=govai;Username=govai;Password=uretim-parolasi";

    [Fact(DisplayName = "K1. Bağlantı değişkeni yoksa komut durur; varsayılana düşmez")]
    public void Baglanti_verilmezse_durur()
    {
        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam()));

        Assert.Contains(EfMigrationTarget.ConnectionVariable, hata.Message, StringComparison.Ordinal);

        // Hata açıklayıcı olmalı: nereye düşeceğini ve ne yapılacağını söylemeli.
        Assert.Contains("localhost:5432", hata.Message, StringComparison.Ordinal);
        Assert.Contains("5180", hata.Message, StringComparison.Ordinal);
        Assert.Contains("15437", hata.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "K2. Boş veya yalnızca boşluk olan değişken de kabul edilmez")]
    public void Bos_baglanti_kabul_edilmez()
    {
        Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam((EfMigrationTarget.ConnectionVariable, "   "))));
    }

    [Fact(DisplayName = "K3. 5180 veritabanına onaysız gidilemez")]
    public void Korunan_hedefe_onaysiz_gidilemez()
    {
        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam(
                (EfMigrationTarget.ConnectionVariable, UretimBaglantisi))));

        Assert.Contains(EfMigrationTarget.ProductionConsentVariable, hata.Message, StringComparison.Ordinal);
        Assert.Contains("localhost:5432/govai", hata.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "K4. Yanlış veya yaklaşık onay değeri yeterli değildir")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("evet")]
    [InlineData("EVET")]
    [InlineData("EVET-5180-VERITABANINI-DEGISTIRME")]
    public void Yaklasik_onay_yetmez(string onay)
    {
        Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam(
                (EfMigrationTarget.ConnectionVariable, UretimBaglantisi),
                (EfMigrationTarget.ProductionConsentVariable, onay))));
    }

    [Fact(DisplayName = "K5. Açık onayla 5180 hedefi kabul edilir")]
    public void Acik_onayla_korunan_hedef_kabul_edilir()
    {
        var hedef = EfMigrationTarget.Resolve(Ortam(
            (EfMigrationTarget.ConnectionVariable, UretimBaglantisi),
            (EfMigrationTarget.ProductionConsentVariable, EfMigrationTarget.ProductionConsentValue)));

        Assert.Equal("localhost:5432/govai", hedef.Description);
        Assert.False(hedef.IsModelOnly);
    }

    [Theory(DisplayName = "K6. Korunan adres 127.0.0.1 ve IPv6 yazımlarıyla da atlatılamaz")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("LOCALHOST")]
    public void Korunan_adres_yazim_degistirerek_atlatilamaz(string sunucu)
    {
        var baglanti = $"Host={sunucu};Port=5432;Database=govai;Username=govai;Password=x";

        Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam((EfMigrationTarget.ConnectionVariable, baglanti))));
    }

    [Fact(DisplayName = "K7. Önizleme veritabanı onay istemez")]
    public void Onizleme_hedefi_serbesttir()
    {
        var hedef = EfMigrationTarget.Resolve(Ortam(
            (EfMigrationTarget.ConnectionVariable, OnizlemeBaglantisi)));

        Assert.Equal("localhost:15437/govai", hedef.Description);
        Assert.False(hedef.IsModelOnly);
        Assert.Equal(OnizlemeBaglantisi, hedef.ConnectionString);
    }

    [Fact(DisplayName = "K8. Veritabanı adı verilmemişse komut durur")]
    public void Veritabani_adi_zorunludur()
    {
        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam(
                (EfMigrationTarget.ConnectionVariable,
                 "Host=localhost;Port=15437;Username=govai;Password=x"))));

        Assert.Contains("veritabanı adı", hata.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "K9. model-only kipi veritabanına erişemeyen bir hedefe bağlanır")]
    public void Model_only_kipi_veritabanina_erisemez()
    {
        var hedef = EfMigrationTarget.Resolve(Ortam(
            (EfMigrationTarget.ConnectionVariable, EfMigrationTarget.ModelOnlyValue)));

        Assert.True(hedef.IsModelOnly);

        // ".invalid" hiçbir zaman çözümlenmez; bu dizeyle yanlışlıkla yazmak mümkün değil.
        Assert.Contains(".invalid", hedef.ConnectionString, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", hedef.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("5432", hedef.ConnectionString, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "K10. Parola ve kullanıcı adı hata metinlerine sızmaz")]
    public void Gizli_alanlar_hata_metnine_sizmaz()
    {
        const string parola = "cok-gizli-parola-42";
        const string kullanici = "gizli-kullanici";

        var baglanti = $"Host=localhost;Port=5432;Database=govai;Username={kullanici};Password={parola}";

        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam((EfMigrationTarget.ConnectionVariable, baglanti))));

        Assert.DoesNotContain(parola, hata.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(kullanici, hata.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "K11. Bozuk bağlantı dizesi ham hâliyle yansıtılmaz")]
    public void Bozuk_baglanti_yansitilmaz()
    {
        const string bozuk = "Host=localhost;Port=abc;Password=sizmamali";

        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam((EfMigrationTarget.ConnectionVariable, bozuk))));

        Assert.DoesNotContain("sizmamali", hata.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "K12. Tanım yalnızca sunucu, port ve veritabanını açar")]
    public void Tanim_gizli_alan_icermez()
    {
        var basarili = EfMigrationTarget.TryDescribe(
            OnizlemeBaglantisi, out var sunucu, out var port, out var veritabani, out var hata);

        Assert.True(basarili);
        Assert.Null(hata);
        Assert.Equal("localhost", sunucu);
        Assert.Equal(15437, port);
        Assert.Equal("govai", veritabani);
    }

    [Fact(DisplayName = "K13. Varsayılan port 5432 açıkça yazılmasa da korunur")]
    public void Varsayilan_port_da_korunur()
    {
        // Port yazılmazsa Npgsql 5432 varsayar; kilit bu durumu da yakalamalı.
        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam(
                (EfMigrationTarget.ConnectionVariable,
                 "Host=localhost;Database=govai;Username=govai;Password=x"))));

        Assert.Contains(EfMigrationTarget.ProductionConsentVariable, hata.Message, StringComparison.Ordinal);
    }
}
