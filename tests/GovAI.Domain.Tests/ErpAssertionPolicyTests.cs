using GovAI.Domain.Common;
using GovAI.Domain.Integrations;

namespace GovAI.Domain.Tests;

/// <summary>
/// İmzası doğrulanmış beyanın içeriğinin kabul edilebilirliği.
///
/// <para>
/// Bu testler bir saldırı listesidir. İmza doğruluğu tek başına yetmez: geçerli imzalı
/// ama süresi dolmuş, ömrü uzatılmış, başka alıcıya yazılmış ya da yeniden oynatılmış
/// bir beyan da reddedilmelidir.
/// </para>
/// </summary>
public class ErpAssertionPolicyTests
{
    private const string Istemci = "erp_ikprof_test";
    private const string Alici = "https://govai.yuppi.cloud/api/erp-auth/token";

    private static readonly DateTimeOffset Simdi = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static ErpAssertionClaims Beyan(
        string? iss = Istemci,
        string? sub = "ikprof-user-42",
        string? aud = Alici,
        string? jti = "01a0-benzersiz",
        int omurSaniye = 60,
        int uretimKaymasiSaniye = 0)
    {
        var iat = Simdi.AddSeconds(uretimKaymasiSaniye);

        return new ErpAssertionClaims
        {
            Issuer = iss!,
            Subject = sub!,
            Audience = aud!,
            TokenId = jti!,
            IssuedAt = iat,
            ExpiresAt = iat.AddSeconds(omurSaniye),
        };
    }

    private static ErpAssertionResult Degerlendir(
        ErpAssertionClaims beyan,
        bool etkin = true,
        bool gorulmus = false,
        DateTimeOffset? simdi = null) =>
        ErpAssertionPolicy.Evaluate(beyan, Istemci, Alici, etkin, gorulmus, simdi ?? Simdi);

    [Fact(DisplayName = "EB1. Geçerli beyan kabul edilir")]
    public void Gecerli_beyan_kabul_edilir()
    {
        var sonuc = Degerlendir(Beyan());

        Assert.True(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.None, sonuc.Rejection);
    }

    [Fact(DisplayName = "EB2. Süresi DOLMUŞ beyan reddedilir")]
    public void Suresi_dolmus_reddedilir()
    {
        var beyan = Beyan(omurSaniye: 60);

        var sonuc = Degerlendir(beyan, simdi: Simdi.AddMinutes(5));

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.Expired, sonuc.Rejection);
    }

    [Fact(DisplayName = "EB3. ÖMRÜ UZUN beyan, imzası geçerli olsa da reddedilir")]
    public void Uzun_omurlu_beyan_reddedilir()
    {
        // Bu, düz makine anahtarına dönmenin en kolay yoludur: ERP bir kez bir yıllık
        // beyan üretip her yerde kullanırdı. Çalındığında da bir yıl geçerli olurdu.
        var sonuc = Degerlendir(Beyan(omurSaniye: 86_400));

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.LifetimeTooLong, sonuc.Rejection);
    }

    [Fact(DisplayName = "EB4. Azami ömür sınırındaki beyan kabul edilir")]
    public void Azami_omur_kabul_edilir()
    {
        var sonuc = Degerlendir(
            Beyan(omurSaniye: ErpServiceIdentity.MaximumAssertionLifetimeSeconds));

        Assert.True(sonuc.IsValid);
    }

    [Fact(DisplayName = "EB5. TEKRAR oynatılan beyan reddedilir")]
    public void Tekrar_oynatilan_reddedilir()
    {
        // Ağdan yakalanan bir beyan, ömrü dolana kadar sınırsız kez kullanılabilirdi.
        var sonuc = Degerlendir(Beyan(), gorulmus: true);

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.Replayed, sonuc.Rejection);
    }

    [Fact(DisplayName = "EB6. jti YOKSA reddedilir")]
    public void Jti_yoksa_reddedilir()
    {
        // jti olmadan tekrar saldırısı tespit edilemez.
        var sonuc = Degerlendir(Beyan(jti: ""));

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.MissingClaim, sonuc.Rejection);
    }

    [Fact(DisplayName = "EB7. BAŞKA ALICIYA yazılmış beyan reddedilir")]
    public void Alici_uyusmazsa_reddedilir()
    {
        // ERP'nin başka bir servis için ürettiği geçerli imzalı beyan GOVAI'de
        // kullanılamamalı.
        var sonuc = Degerlendir(Beyan(aud: "https://baska-servis.example/token"));

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.AudienceMismatch, sonuc.Rejection);
    }

    [Fact(DisplayName = "EB8. BAŞKA KİMLİĞİN iss değeriyle gelen beyan reddedilir")]
    public void Iss_uyusmazsa_reddedilir()
    {
        var sonuc = Degerlendir(Beyan(iss: "erp_baska_firma"));

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.IssuerMismatch, sonuc.Rejection);
    }

    [Fact(DisplayName = "EB9. DEVRE DIŞI kimliğin beyanı reddedilir")]
    public void Devre_disi_kimlik_reddedilir()
    {
        // İptal, jetonların kısa ömrü sayesinde dakikalar içinde etkili olur.
        var sonuc = Degerlendir(Beyan(), etkin: false);

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.IdentityDisabled, sonuc.Rejection);
    }

    [Fact(DisplayName = "EB10. GELECEK TARİHLİ beyan reddedilir")]
    public void Gelecek_tarihli_reddedilir()
    {
        // Saat kaymasının ötesinde ileri tarih, ömrü fiilen uzatma girişimidir.
        var sonuc = Degerlendir(Beyan(uretimKaymasiSaniye: 600));

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.NotYetValid, sonuc.Rejection);
    }

    [Fact(DisplayName = "EB11. Küçük saat kayması TOLERE edilir")]
    public void Kucuk_saat_kaymasi_tolere_edilir()
    {
        // Sunucu saatleri birkaç saniye kayar; bunu reddetmek entegrasyonu
        // aralıklarla ve sebepsizce kırardı.
        var ileri = Degerlendir(Beyan(uretimKaymasiSaniye: 30));
        var geri = Degerlendir(Beyan(omurSaniye: 30), simdi: Simdi.AddSeconds(45));

        Assert.True(ileri.IsValid);
        Assert.True(geri.IsValid);
    }

    [Fact(DisplayName = "EB12. sub == iss ise SERVİS, farklıysa KULLANICI")]
    public void Ozne_turu_dogru_belirlenir()
    {
        Assert.Equal(ErpPrincipalKind.Service, Beyan(sub: Istemci).Kind);
        Assert.Equal(ErpPrincipalKind.User, Beyan(sub: "ikprof-user-42").Kind);
    }

    [Fact(DisplayName = "EB13. sub YOKSA reddedilir")]
    public void Sub_yoksa_reddedilir()
    {
        var sonuc = Degerlendir(Beyan(sub: ""));

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.MissingClaim, sonuc.Rejection);
    }

    [Fact(DisplayName = "EB14. Tekrar penceresi beyan ömründen KISA olamaz")]
    public void Tekrar_penceresi_yeterince_uzun()
    {
        // Kısa olsaydı, ömrü dolmamış bir beyan pencere kapandıktan sonra yeniden
        // kullanılabilirdi.
        Assert.True(
            ErpAssertionPolicy.ReplayWindow
            >= TimeSpan.FromSeconds(ErpServiceIdentity.MaximumAssertionLifetimeSeconds));
    }
}

/// <summary>
/// Servis kimliği ve anahtarlarının alan modeli.
///
/// <para>
/// Korunan ana kural şudur: GOVAI <b>hiçbir sır saklamaz</b>. Yalnızca açık anahtar
/// tutulur; veritabanı yedeğini okuyan biri o şirket adına konuşamaz.
/// </para>
/// </summary>
public class ErpServiceIdentityTests
{
    private static readonly DateTimeOffset Simdi = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private const string AcikAnahtar =
        "-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE\n-----END PUBLIC KEY-----";

    private static ErpServiceIdentity Kimlik() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "erp_ikprof", "IKPROF", Simdi);

    private static ErpSigningKey Anahtar(ErpServiceIdentity kimlik, string kid = "k1") =>
        new(kimlik.TenantId, kimlik.Id, kid, ErpSigningAlgorithm.ES256, AcikAnahtar, Simdi);

    [Fact(DisplayName = "SK1. ÖZEL anahtar kabul EDİLMEZ")]
    public void Ozel_anahtar_reddedilir()
    {
        // En olası ve en pahalı hata: özel anahtarın yanlışlıkla yapıştırılması.
        var kimlik = Kimlik();

        var hata = Assert.Throws<DomainException>(() => new ErpSigningKey(
            kimlik.TenantId, kimlik.Id, "k1", ErpSigningAlgorithm.ES256,
            "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----", Simdi));

        Assert.Contains("ÖZEL", hata.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SK2. PEM olmayan anahtar reddedilir")]
    public void Pem_olmayan_reddedilir()
    {
        var kimlik = Kimlik();

        Assert.Throws<DomainException>(() => new ErpSigningKey(
            kimlik.TenantId, kimlik.Id, "k1", ErpSigningAlgorithm.ES256, "duz-metin", Simdi));
    }

    [Fact(DisplayName = "SK3. kid zorunludur")]
    public void Kid_zorunludur()
    {
        var kimlik = Kimlik();

        Assert.Throws<DomainException>(() => new ErpSigningKey(
            kimlik.TenantId, kimlik.Id, "  ", ErpSigningAlgorithm.ES256, AcikAnahtar, Simdi));
    }

    [Fact(DisplayName = "SK4. Aynı kid iki kez ETKİN olamaz")]
    public void Ayni_kid_iki_kez_olamaz()
    {
        var kimlik = Kimlik();

        kimlik.AddKey(Anahtar(kimlik));

        Assert.Throws<DomainException>(() => kimlik.AddKey(Anahtar(kimlik)));
    }

    [Fact(DisplayName = "SK5. Anahtar DEĞİŞİMİ kesinti yaratmaz: ikisi birden etkin olabilir")]
    public void Anahtar_degisimi_kesintisiz()
    {
        // Tek anahtar zorunlu olsaydı her değişim kesinti demek olurdu; bu da
        // anahtarların hiç değiştirilmemesine yol açardı.
        var kimlik = Kimlik();

        kimlik.AddKey(Anahtar(kimlik, "eski"));
        kimlik.AddKey(Anahtar(kimlik, "yeni"));

        Assert.Equal(2, kimlik.ActiveKeys.Count());

        kimlik.RevokeKey("eski", Simdi.AddDays(1));

        Assert.Single(kimlik.ActiveKeys);
        Assert.Equal("yeni", kimlik.ActiveKeys.Single().KeyId);
    }

    [Fact(DisplayName = "SK6. İptal edilen anahtar SİLİNMEZ")]
    public void Iptal_edilen_silinmez()
    {
        var kimlik = Kimlik();

        kimlik.AddKey(Anahtar(kimlik, "k1"));
        kimlik.RevokeKey("k1", Simdi.AddDays(1));

        Assert.Single(kimlik.Keys);
        Assert.Empty(kimlik.ActiveKeys);
    }

    [Fact(DisplayName = "SK7. SON anahtar da iptal edilebilir")]
    public void Son_anahtar_iptal_edilebilir()
    {
        // Sızıntı şüphesinde entegrasyonu durdurmak, çalışır tutmaktan önemlidir.
        var kimlik = Kimlik();

        kimlik.AddKey(Anahtar(kimlik, "k1"));

        var hata = Record.Exception(() => kimlik.RevokeKey("k1", Simdi));

        Assert.Null(hata);
        Assert.Empty(kimlik.ActiveKeys);
    }

    [Fact(DisplayName = "SK8. Olmayan anahtarın iptali hata verir")]
    public void Olmayan_anahtar_iptali_hata()
    {
        var kimlik = Kimlik();

        Assert.Throws<DomainException>(() => kimlik.RevokeKey("yok", Simdi));
    }

    [Fact(DisplayName = "SK9. Kimlik TEK bir şirkete bağlıdır ve şirket zorunludur")]
    public void Sirket_zorunludur()
    {
        Assert.Throws<DomainException>(() => new ErpServiceIdentity(
            Guid.CreateVersion7(), Guid.Empty, "erp_x", "X", Simdi));
    }

    [Fact(DisplayName = "SK10. Kimlik kaydında SIR alanı yoktur")]
    public void Sir_alani_yoktur()
    {
        // Düz makine anahtarından ayrıldığımız nokta: saklanan hiçbir değer tek başına
        // beyan üretmeye yetmez.
        var alanlar = typeof(ErpServiceIdentity).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(alanlar, a => a.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(alanlar, a => a.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(alanlar, a => a.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
    }
}
