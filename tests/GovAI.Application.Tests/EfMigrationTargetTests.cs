using System.Net;
using GovAI.Persistence.Design;

namespace GovAI.Application.Tests;

/// <summary>
/// Migration hedef güvenlik kilidi.
///
/// Bu testler bir davranış sözleşmesidir: Faz 2'de iki migration istemeden 5180'in
/// veritabanına uygulandı, çünkü <c>dotnet ef</c> açık bir hedef verilmediğinde
/// <c>appsettings.Development.json</c>'daki <c>localhost:5432</c>'ye — yani 5180'e —
/// sessizce düşüyordu. Aşağıdaki testlerden biri gevşetilirse o tuzak geri gelir.
///
/// Koruma üç katmanlıdır ve her katmanın kendi testleri vardır:
/// hedef açıkça verilmeli, beyan edilen ortam gerçek hedefle tutmalı, üretim ayrıca
/// onay istemeli.
/// </summary>
public sealed class EfMigrationTargetTests
{
    /// <summary>Sahte ortam: yalnızca verilen değişkenleri bilir.</summary>
    private static Func<string, string?> Ortam(params (string Ad, string Deger)[] degiskenler) =>
        ad => degiskenler.FirstOrDefault(d => d.Ad == ad).Deger;

    /// <summary>Hiçbir adı çözemeyen çözümleyici — liste tabanlı denetimi yalın sınar.</summary>
    private static readonly Func<string, IPAddress[]> CozumlemeYok = _ => [];

    /// <summary>Her adı döngü adresine çözen çözümleyici — takma ad senaryosu.</summary>
    private static readonly Func<string, IPAddress[]> HerSeyYerel = _ => [IPAddress.Loopback];

    private const string OnizlemeBaglantisi =
        "Host=localhost;Port=15437;Database=govai;Username=govai;Password=onizleme-parolasi";

    private const string UretimBaglantisi =
        "Host=localhost;Port=5432;Database=govai;Username=govai;Password=uretim-parolasi";

    private static (string, string) Beyan(string ortam) =>
        (EfMigrationTarget.EnvironmentVariable, ortam);

    private static (string, string) Onay() =>
        (EfMigrationTarget.ProductionConsentVariable, EfMigrationTarget.ProductionConsentValue);

    private static (string, string) Baglanti(string deger) =>
        (EfMigrationTarget.ConnectionVariable, deger);

    // ═══════════════ Katman 1: hedef açıkça verilmeli ═══════════════

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
            EfMigrationTarget.Resolve(Ortam(Baglanti("   "))));
    }

    [Fact(DisplayName = "K3. Veritabanı adı verilmemişse komut durur")]
    public void Veritabani_adi_zorunludur()
    {
        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam(
                Baglanti("Host=localhost;Port=15437;Username=govai;Password=x"))));

        Assert.Contains("veritabanı adı", hata.Message, StringComparison.Ordinal);
    }

    // ═══════════════ Katman 2: ortam beyanı ═══════════════

    [Fact(DisplayName = "K4. Ortam beyanı yoksa komut durur")]
    public void Ortam_beyani_zorunludur()
    {
        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam(Baglanti(OnizlemeBaglantisi)), CozumlemeYok));

        Assert.Contains(EfMigrationTarget.EnvironmentVariable, hata.Message, StringComparison.Ordinal);

        // Hata, gerçek hedefin ne olduğunu söylemeli ki doğru beyan yazılabilsin.
        Assert.Contains("Preview", hata.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "K5. 'Önizlemeye gidiyorum' deyip üretime bağlanmak engellenir")]
    public void Yanlis_ortam_beyani_engellenir()
    {
        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(
                Ortam(Baglanti(UretimBaglantisi), Beyan("Preview"), Onay()),
                CozumlemeYok));

        Assert.Contains("uyuşmuyor", hata.Message, StringComparison.Ordinal);

        // Onay verilmiş olsa bile beyan tutmuyorsa geçilmez.
        Assert.Contains("ÜRETİM", hata.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "K6. Anlamsız ortam beyanı kabul edilmez")]
    public void Anlamsiz_beyan_kabul_edilmez()
    {
        Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(
                Ortam(Baglanti(OnizlemeBaglantisi), Beyan("evet")), CozumlemeYok));
    }

    [Fact(DisplayName = "K7. Doğru beyanla önizleme hedefi kabul edilir")]
    public void Onizleme_hedefi_serbesttir()
    {
        var hedef = EfMigrationTarget.Resolve(
            Ortam(Baglanti(OnizlemeBaglantisi), Beyan("Preview")), CozumlemeYok);

        Assert.Equal(EfMigrationTarget.TargetEnvironment.Preview, hedef.Environment);
        Assert.False(hedef.IsModelOnly);
        Assert.Equal(OnizlemeBaglantisi, hedef.ConnectionString);
        Assert.Contains("localhost:15437/govai", hedef.Description, StringComparison.Ordinal);
    }

    // ═══════════════ Katman 3: üretim onayı ═══════════════

    [Fact(DisplayName = "K8. 5180 veritabanına onaysız gidilemez")]
    public void Korunan_hedefe_onaysiz_gidilemez()
    {
        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(
                Ortam(Baglanti(UretimBaglantisi), Beyan("Production")), CozumlemeYok));

        Assert.Contains(EfMigrationTarget.ProductionConsentVariable, hata.Message, StringComparison.Ordinal);
        Assert.Contains("localhost:5432/govai", hata.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "K9. Yaklaşık veya yanlış onay değeri yeterli değildir")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("evet")]
    [InlineData("EVET")]
    [InlineData("EVET-5180-VERITABANINI-DEGISTIRME")]
    [InlineData("evet-5180-veritabanini-degistir")]
    [InlineData(" EVET-5180-VERITABANINI-DEGISTIR ")]
    public void Yaklasik_onay_yetmez(string onay)
    {
        // Baştaki/sondaki boşluk kırpılır; onun dışında tam eşleşme aranır.
        var beklenenGecerli = onay.Trim() == EfMigrationTarget.ProductionConsentValue;

        if (beklenenGecerli)
        {
            var hedef = EfMigrationTarget.Resolve(
                Ortam(Baglanti(UretimBaglantisi), Beyan("Production"),
                      (EfMigrationTarget.ProductionConsentVariable, onay)),
                CozumlemeYok);

            Assert.Equal(EfMigrationTarget.TargetEnvironment.Production, hedef.Environment);
            return;
        }

        Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(
                Ortam(Baglanti(UretimBaglantisi), Beyan("Production"),
                      (EfMigrationTarget.ProductionConsentVariable, onay)),
                CozumlemeYok));
    }

    [Fact(DisplayName = "K10. Tam onay ve doğru beyanla 5180 hedefi kabul edilir")]
    public void Acik_onayla_korunan_hedef_kabul_edilir()
    {
        var hedef = EfMigrationTarget.Resolve(
            Ortam(Baglanti(UretimBaglantisi), Beyan("Production"), Onay()), CozumlemeYok);

        Assert.Equal(EfMigrationTarget.TargetEnvironment.Production, hedef.Environment);
        Assert.Contains("localhost:5432/govai", hedef.Description, StringComparison.Ordinal);
    }

    // ═══════════════ Aynı hedefe ulaşan adlar ═══════════════

    [Theory(DisplayName = "K11. 5180'in Postgres'ine ulaşan her ad korunur")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("0.0.0.0")]
    [InlineData("host.docker.internal")]
    [InlineData("gateway.docker.internal")]
    [InlineData("docker.for.win.localhost")]
    [InlineData("govai-postgres-1")]
    [InlineData("govai_postgres_1")]
    [InlineData("govai-postgres")]
    [InlineData("postgres")]
    [InlineData("db")]
    public void Korunan_adres_yazim_degistirerek_atlatilamaz(string sunucu)
    {
        var baglanti = $"Host={sunucu};Port=5432;Database=govai;Username=govai;Password=x";

        // Beyan ve onay verilmese de üretim olarak sınıflandırılmalı.
        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(
                Ortam(Baglanti(baglanti), Beyan("Production")), CozumlemeYok));

        Assert.Contains(EfMigrationTarget.ProductionConsentVariable, hata.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "K12. Listede olmayan ama aynı makineye çözülen ad da korunur")]
    public void Cozumlenen_takma_ad_da_korunur()
    {
        // Listede yok; ama ad çözümlemesi döngü adresine çıkıyor.
        const string baglanti =
            "Host=benim-takma-adim.local;Port=5432;Database=govai;Username=govai;Password=x";

        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(
                Ortam(Baglanti(baglanti), Beyan("Production")), HerSeyYerel));

        Assert.Contains(EfMigrationTarget.ProductionConsentVariable, hata.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "K13. Varsayılan port 5432 açıkça yazılmasa da korunur")]
    public void Varsayilan_port_da_korunur()
    {
        // Port yazılmazsa Npgsql 5432 varsayar; kilit bu durumu da yakalamalı.
        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(
                Ortam(Baglanti("Host=localhost;Database=govai;Username=govai;Password=x"),
                      Beyan("Production")),
                CozumlemeYok));

        Assert.Contains(EfMigrationTarget.ProductionConsentVariable, hata.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "K14. Üretim sunucusundaki BAŞKA bir veritabanı da korunur")]
    public void Uretim_sunucusundaki_diger_veritabani_da_korunur()
    {
        // Aynı Postgres örneği; veritabanı adı farklı olsa da oraya şema yazmıyoruz.
        const string baglanti =
            "Host=localhost;Port=5432;Database=deneme;Username=govai;Password=x";

        Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(
                Ortam(Baglanti(baglanti), Beyan("Production")), CozumlemeYok));
    }

    [Fact(DisplayName = "K15. Uzak bir sunucu üretim sayılmaz")]
    public void Uzak_sunucu_uretim_sayilmaz()
    {
        const string baglanti =
            "Host=veritabani.ornek.test;Port=5432;Database=govai;Username=govai;Password=x";

        var hedef = EfMigrationTarget.Resolve(
            Ortam(Baglanti(baglanti), Beyan("Other")), CozumlemeYok);

        Assert.Equal(EfMigrationTarget.TargetEnvironment.Other, hedef.Environment);
    }

    // ═══════════════ Model-only ═══════════════

    [Fact(DisplayName = "K16. model-only kipi veritabanına erişemeyen bir hedefe bağlanır")]
    public void Model_only_kipi_veritabanina_erisemez()
    {
        // Ortam beyanı GEREKMEZ: bağlantı zaten hiçbir veritabanına gitmiyor.
        var hedef = EfMigrationTarget.Resolve(
            Ortam(Baglanti(EfMigrationTarget.ModelOnlyValue)));

        Assert.True(hedef.IsModelOnly);
        Assert.Equal(EfMigrationTarget.TargetEnvironment.ModelOnly, hedef.Environment);

        // ".invalid" hiçbir zaman çözümlenmez; bu dizeyle yanlışlıkla yazmak mümkün değil.
        Assert.Contains(".invalid", hedef.ConnectionString, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", hedef.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("5432", hedef.ConnectionString, StringComparison.Ordinal);
    }

    // ═══════════════ Gizli alanlar sızmaz ═══════════════

    [Fact(DisplayName = "K17. Parola ve kullanıcı adı hata metinlerine sızmaz")]
    public void Gizli_alanlar_hata_metnine_sizmaz()
    {
        const string parola = "cok-gizli-parola-42";
        const string kullanici = "gizli-kullanici";

        var baglanti = $"Host=localhost;Port=5432;Database=govai;Username={kullanici};Password={parola}";

        // Üç ayrı hata yolu: beyan yok, beyan yanlış, onay yok.
        foreach (var degiskenler in new[]
                 {
                     new[] { Baglanti(baglanti) },
                     [Baglanti(baglanti), Beyan("Preview")],
                     [Baglanti(baglanti), Beyan("Production")],
                 })
        {
            var hata = Assert.Throws<EfMigrationTargetException>(() =>
                EfMigrationTarget.Resolve(Ortam(degiskenler), CozumlemeYok));

            Assert.DoesNotContain(parola, hata.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(kullanici, hata.Message, StringComparison.Ordinal);
        }
    }

    [Fact(DisplayName = "K18. Başarılı çözümde de tanım gizli alan içermez")]
    public void Tanim_gizli_alan_icermez()
    {
        var hedef = EfMigrationTarget.Resolve(
            Ortam(Baglanti(OnizlemeBaglantisi), Beyan("Preview")), CozumlemeYok);

        // Bu tanım loglanıyor; parola ya da kullanıcı adı içermemeli.
        Assert.DoesNotContain("onizleme-parolasi", hedef.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", hedef.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username", hedef.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "K19. Bozuk bağlantı dizesi ham hâliyle yansıtılmaz")]
    public void Bozuk_baglanti_yansitilmaz()
    {
        const string bozuk = "Host=localhost;Port=abc;Password=sizmamali";

        var hata = Assert.Throws<EfMigrationTargetException>(() =>
            EfMigrationTarget.Resolve(Ortam(Baglanti(bozuk))));

        Assert.DoesNotContain("sizmamali", hata.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "K20. TryDescribe yalnızca sunucu, port ve veritabanını açar")]
    public void TryDescribe_gizli_alan_dondurmez()
    {
        var basarili = EfMigrationTarget.TryDescribe(
            OnizlemeBaglantisi, out var sunucu, out var port, out var veritabani, out var hata);

        Assert.True(basarili);
        Assert.Null(hata);
        Assert.Equal("localhost", sunucu);
        Assert.Equal(15437, port);
        Assert.Equal("govai", veritabani);
    }

    // ═══════════════ Sınıflandırma ═══════════════

    [Fact(DisplayName = "K21. Sınıflandırma ad çözümlemesi olmadan da çalışır")]
    public void Siniflandirma_cozumleme_olmadan_calisir()
    {
        Assert.Equal(
            EfMigrationTarget.TargetEnvironment.Production,
            EfMigrationTarget.Classify("govai-postgres-1", 5432, "govai"));

        Assert.Equal(
            EfMigrationTarget.TargetEnvironment.Preview,
            EfMigrationTarget.Classify("localhost", 15437, "govai"));

        Assert.Equal(
            EfMigrationTarget.TargetEnvironment.Other,
            EfMigrationTarget.Classify("uzak.ornek.test", 5432, "govai"));
    }

    [Fact(DisplayName = "K22. Ad çözümlemesi patlarsa liste denetimi ayakta kalır")]
    public void Cozumleme_patlarsa_liste_ayakta_kalir()
    {
        Func<string, IPAddress[]> patlayan = _ => throw new System.Net.Sockets.SocketException();

        // Çözümleme hata verse bile "localhost" listede olduğu için üretim sayılır.
        Assert.Equal(
            EfMigrationTarget.TargetEnvironment.Production,
            EfMigrationTarget.Classify("localhost", 5432, "govai", patlayan));
    }
}
