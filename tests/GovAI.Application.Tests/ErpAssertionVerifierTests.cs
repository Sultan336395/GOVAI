using System.Security.Cryptography;
using System.Text;
using GovAI.Domain.Integrations;
using GovAI.Infrastructure.Integrations;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GovAI.Application.Tests;

/// <summary>
/// Beyan imzasının doğrulanması — gerçek anahtarlarla.
///
/// <para>
/// Buradaki testler kriptografiyi taklit etmez; gerçek ECDSA ve RSA anahtarları üretip
/// gerçek imza atar. Sebebi şudur: bu kodun koruduğu açıkların çoğu (alg karışıklığı,
/// yanlış anahtarla doğrulama) yalnızca gerçek imzayla ortaya çıkar.
/// </para>
/// </summary>
public class ErpAssertionVerifierTests
{
    private const string Istemci = "erp_ikprof_test";
    private const string Alici = "https://govai.test/api/erp-auth/token";

    private static readonly DateTimeOffset Simdi = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid KimlikId = Guid.CreateVersion7();

    private readonly ErpAssertionVerifier _dogrulayici = new();

    // ── Anahtar üretimi ─────────────────────────────────────────────────────

    private static (ECDsa Ozel, ErpSigningKey Kayit) EcAnahtar(string kid = "k1")
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var kayit = new ErpSigningKey(
            TenantId, KimlikId, kid, ErpSigningAlgorithm.ES256,
            ecdsa.ExportSubjectPublicKeyInfoPem(), Simdi);

        return (ecdsa, kayit);
    }

    private static (RSA Ozel, ErpSigningKey Kayit) RsaAnahtar(string kid = "r1")
    {
        var rsa = RSA.Create(2048);

        var kayit = new ErpSigningKey(
            TenantId, KimlikId, kid, ErpSigningAlgorithm.RS256,
            rsa.ExportSubjectPublicKeyInfoPem(), Simdi);

        return (rsa, kayit);
    }

    // ── Beyan üretimi ───────────────────────────────────────────────────────

    private static string Imzala(
        SecurityKey anahtar,
        string algoritma,
        string kid,
        string? iss = Istemci,
        string? sub = "ikprof-user-42",
        string? aud = Alici,
        string? jti = null,
        int omurSaniye = 60,
        string? ad = null)
    {
        var iat = Simdi.ToUnixTimeSeconds();

        var payload = new Dictionary<string, object>
        {
            ["iss"] = iss!,
            ["sub"] = sub!,
            ["aud"] = aud!,
            ["jti"] = jti ?? Guid.CreateVersion7().ToString(),
            ["iat"] = iat,
            ["exp"] = iat + omurSaniye,
        };

        if (ad is not null)
        {
            payload["name"] = ad;
        }

        // kid başlığa SigningCredentials'daki anahtardan yazılır; kütüphane
        // AdditionalHeaderClaims içinde kid kabul etmez.
        anahtar.KeyId = kid;

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = payload,
            SigningCredentials = new SigningCredentials(anahtar, algoritma),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    // ── Testler ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "EI1. ES256 ile imzalanmış geçerli beyan doğrulanır")]
    public void Es256_gecerli_beyan()
    {
        var (ozel, kayit) = EcAnahtar();

        var beyan = Imzala(new ECDsaSecurityKey(ozel), SecurityAlgorithms.EcdsaSha256, "k1", ad: "Ayşe Yılmaz");

        var sonuc = _dogrulayici.Verify(beyan, [kayit]);

        Assert.True(sonuc.IsValid);
        Assert.Equal(Istemci, sonuc.Claims!.Issuer);
        Assert.Equal("ikprof-user-42", sonuc.Claims.Subject);
        Assert.Equal("Ayşe Yılmaz", sonuc.Claims.SubjectName);
        Assert.Equal(ErpPrincipalKind.User, sonuc.Claims.Kind);
        Assert.Equal("k1", sonuc.KeyId);
    }

    [Fact(DisplayName = "EI2. RS256 ile imzalanmış geçerli beyan doğrulanır")]
    public void Rs256_gecerli_beyan()
    {
        var (ozel, kayit) = RsaAnahtar();

        var beyan = Imzala(new RsaSecurityKey(ozel), SecurityAlgorithms.RsaSha256, "r1");

        Assert.True(_dogrulayici.Verify(beyan, [kayit]).IsValid);
    }

    [Fact(DisplayName = "EI3. BAŞKA anahtarla imzalanmış beyan reddedilir")]
    public void Baska_anahtarla_imzali_reddedilir()
    {
        var (_, kayitliAnahtar) = EcAnahtar("k1");
        var (saldirganinOzeli, _) = EcAnahtar("k1");

        var beyan = Imzala(new ECDsaSecurityKey(saldirganinOzeli), SecurityAlgorithms.EcdsaSha256, "k1");

        var sonuc = _dogrulayici.Verify(beyan, [kayitliAnahtar]);

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.BadSignature, sonuc.Rejection);
    }

    [Fact(DisplayName = "EI4. ALGORİTMA KARIŞTIRMA: açık anahtarı HMAC sırrı gibi kullanmak REDDEDİLİR")]
    public void Alg_karistirma_reddedilir()
    {
        // JWT'nin en bilinen açığı: saldırgan başlığa HS256 yazar ve saklanan AÇIK
        // anahtarı HMAC sırrı olarak kullanır. Açık anahtar herkese açık olduğu için
        // imza "geçerli" çıkardı. Başlıktaki algoritma kayıtlıyla karşılaştırılmasaydı
        // bu test kırmızı olurdu.
        var (_, kayit) = EcAnahtar("k1");

        var hmacAnahtari = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(kayit.PublicKeyPem));

        var beyan = Imzala(hmacAnahtari, SecurityAlgorithms.HmacSha256, "k1");

        var sonuc = _dogrulayici.Verify(beyan, [kayit]);

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.UnsupportedAlgorithm, sonuc.Rejection);
    }

    [Fact(DisplayName = "EI5. alg=none REDDEDİLİR")]
    public void Alg_none_reddedilir()
    {
        var (_, kayit) = EcAnahtar("k1");

        // İmzasız beyan elle kurulur: kütüphane böyle bir jeton üretmez.
        var basli = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT","kid":"k1"}""");
        var govde = Base64UrlEncoder.Encode(
            $$"""{"iss":"{{Istemci}}","sub":"x","aud":"{{Alici}}","jti":"j1","iat":{{Simdi.ToUnixTimeSeconds()}},"exp":{{Simdi.AddMinutes(1).ToUnixTimeSeconds()}}}""");

        var sonuc = _dogrulayici.Verify($"{basli}.{govde}.", [kayit]);

        Assert.False(sonuc.IsValid);
        Assert.NotEqual(ErpAssertionRejection.None, sonuc.Rejection);
    }

    [Fact(DisplayName = "EI6. kid YOKSA reddedilir; anahtarlar tek tek DENENMEZ")]
    public void Kid_yoksa_reddedilir()
    {
        // Bütün anahtarları denemek, iptal edilmiş bir anahtarın sessizce kullanılmasına
        // ve maliyetin anahtar sayısıyla çarpılmasına yol açardı.
        var (ozel, kayit) = EcAnahtar("k1");

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                ["iss"] = Istemci,
                ["sub"] = "x",
                ["aud"] = Alici,
                ["jti"] = "j1",
                ["iat"] = Simdi.ToUnixTimeSeconds(),
                ["exp"] = Simdi.AddMinutes(1).ToUnixTimeSeconds(),
            },
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(ozel), SecurityAlgorithms.EcdsaSha256),
        };

        var beyan = new JsonWebTokenHandler().CreateToken(descriptor);

        var sonuc = _dogrulayici.Verify(beyan, [kayit]);

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.UnknownKey, sonuc.Rejection);
    }

    [Fact(DisplayName = "EI7. İPTAL EDİLMİŞ anahtarla imzalanmış beyan reddedilir")]
    public void Iptal_edilmis_anahtar_reddedilir()
    {
        var (ozel, kayit) = EcAnahtar("k1");

        kayit.Revoke(Simdi.AddHours(1));

        var beyan = Imzala(new ECDsaSecurityKey(ozel), SecurityAlgorithms.EcdsaSha256, "k1");

        var sonuc = _dogrulayici.Verify(beyan, [kayit]);

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.UnknownKey, sonuc.Rejection);
    }

    [Fact(DisplayName = "EI8. Anahtar DEĞİŞİMİ sırasında iki anahtar da çalışır")]
    public void Anahtar_degisimi_kesintisiz()
    {
        var (eskiOzel, eskiKayit) = EcAnahtar("eski");
        var (yeniOzel, yeniKayit) = EcAnahtar("yeni");

        var eskiBeyan = Imzala(new ECDsaSecurityKey(eskiOzel), SecurityAlgorithms.EcdsaSha256, "eski");
        var yeniBeyan = Imzala(new ECDsaSecurityKey(yeniOzel), SecurityAlgorithms.EcdsaSha256, "yeni");

        Assert.True(_dogrulayici.Verify(eskiBeyan, [eskiKayit, yeniKayit]).IsValid);
        Assert.True(_dogrulayici.Verify(yeniBeyan, [eskiKayit, yeniKayit]).IsValid);
    }

    [Fact(DisplayName = "EI9. GÖVDESİ değiştirilmiş beyan reddedilir")]
    public void Govde_degistirilirse_reddedilir()
    {
        var (ozel, kayit) = EcAnahtar("k1");

        var beyan = Imzala(new ECDsaSecurityKey(ozel), SecurityAlgorithms.EcdsaSha256, "k1");
        var parcalar = beyan.Split('.');

        // Özneyi değiştirip imzayı olduğu gibi bırak.
        var bozukGovde = Base64UrlEncoder.Encode(
            $$"""{"iss":"{{Istemci}}","sub":"baska-kullanici","aud":"{{Alici}}","jti":"j1","iat":{{Simdi.ToUnixTimeSeconds()}},"exp":{{Simdi.AddMinutes(1).ToUnixTimeSeconds()}}}""");

        var sonuc = _dogrulayici.Verify($"{parcalar[0]}.{bozukGovde}.{parcalar[2]}", [kayit]);

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.BadSignature, sonuc.Rejection);
    }

    [Fact(DisplayName = "EI10. Zorunlu alanı eksik beyan reddedilir")]
    public void Eksik_alan_reddedilir()
    {
        var (ozel, kayit) = EcAnahtar("k1");

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                ["iss"] = Istemci,
                ["aud"] = Alici,
                ["iat"] = Simdi.ToUnixTimeSeconds(),
                ["exp"] = Simdi.AddMinutes(1).ToUnixTimeSeconds(),
            },
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(ozel) { KeyId = "k1" }, SecurityAlgorithms.EcdsaSha256),
        };

        var sonuc = _dogrulayici.Verify(new JsonWebTokenHandler().CreateToken(descriptor), [kayit]);

        Assert.False(sonuc.IsValid);
        Assert.Equal(ErpAssertionRejection.MissingClaim, sonuc.Rejection);
    }

    [Fact(DisplayName = "EI11. Bozuk metin çökmeye YOL AÇMAZ")]
    public void Bozuk_metin_cokmez()
    {
        var (_, kayit) = EcAnahtar("k1");

        foreach (var bozuk in new[] { "", "   ", "abc", "a.b", "a.b.c.d", "...." })
        {
            var sonuc = _dogrulayici.Verify(bozuk, [kayit]);

            Assert.False(sonuc.IsValid);
        }
    }

    [Fact(DisplayName = "EI12. sub == iss ise SERVİS kimliği olarak okunur")]
    public void Servis_beyani()
    {
        var (ozel, kayit) = EcAnahtar("k1");

        var beyan = Imzala(
            new ECDsaSecurityKey(ozel), SecurityAlgorithms.EcdsaSha256, "k1", sub: Istemci);

        var sonuc = _dogrulayici.Verify(beyan, [kayit]);

        Assert.True(sonuc.IsValid);
        Assert.Equal(ErpPrincipalKind.Service, sonuc.Claims!.Kind);
    }

    [Fact(DisplayName = "EI13. Hata ayrıntısı dışarıya SIZDIRILMAZ biçimde sınıflandırılır")]
    public void Hata_siniflandirilir()
    {
        // Dışarıya yalnızca sınıflandırma döner; hangi adımda takıldığını anlatmak
        // saldırgana geçerli beyan üretmeyi öğretmek olurdu.
        var (_, kayit) = EcAnahtar("k1");

        var sonuc = _dogrulayici.Verify("bozuk.jwt.metni", [kayit]);

        Assert.False(sonuc.IsValid);
        Assert.NotNull(sonuc.Detail);
        Assert.Null(sonuc.Claims);
    }
}
