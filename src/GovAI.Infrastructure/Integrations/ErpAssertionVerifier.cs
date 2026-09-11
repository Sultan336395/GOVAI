using System.Security.Cryptography;
using System.Text.Json;
using GovAI.Domain.Integrations;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GovAI.Infrastructure.Integrations;

/// <summary>İmza doğrulamasının sonucu.</summary>
public sealed record ErpSignatureResult(
    bool IsValid,
    ErpAssertionRejection Rejection,
    string? Detail,
    ErpAssertionClaims? Claims,
    string? KeyId);

/// <summary>
/// ERP'nin imzaladığı beyanı doğrular.
///
/// <para>
/// Doğrulama <b>anahtarın kayıtlı algoritmasına göre</b> yapılır, beyanın başlığında
/// yazan algoritmaya göre değil. Bu ayrım JWT'nin en bilinen açığını kapatır: saldırgan
/// başlığa <c>alg: none</c> ya da <c>alg: HS256</c> yazıp, saklanan açık anahtarı HMAC
/// sırrı gibi kullanarak kendi imzasını geçerli kılabilirdi. Burada başlıktaki değer
/// yalnızca <b>kayıtlı olanla aynı mı</b> diye bakılır; farklıysa beyan reddedilir.
/// </para>
///
/// <para>
/// Hiçbir hata dışarıya ayrıntılı dönmez: hangi adımda takıldığını söylemek, saldırgana
/// geçerli beyan üretmeyi öğretmek olurdu. Ayrıntı yalnızca kayda yazılır.
/// </para>
/// </summary>
public sealed class ErpAssertionVerifier
{
    /// <summary>Kabul edilen algoritmalar. Asimetrik olmayan hiçbir değer yoktur.</summary>
    private static readonly Dictionary<ErpSigningAlgorithm, string> Algoritmalar = new()
    {
        [ErpSigningAlgorithm.ES256] = SecurityAlgorithms.EcdsaSha256,
        [ErpSigningAlgorithm.RS256] = SecurityAlgorithms.RsaSha256,
    };

    /// <summary>
    /// Beyanı ayrıştırır ve imzasını doğrular.
    /// </summary>
    /// <param name="assertion">ERP'nin ürettiği JWT.</param>
    /// <param name="keys">Kimliğin etkin anahtarları.</param>
    public ErpSignatureResult Verify(string assertion, IReadOnlyList<ErpSigningKey> keys)
    {
        if (string.IsNullOrWhiteSpace(assertion))
        {
            return Hata(ErpAssertionRejection.MissingClaim, "Beyan boş.");
        }

        JsonWebToken jwt;

        try
        {
            jwt = new JsonWebToken(assertion);
        }
        catch (Exception)
        {
            return Hata(ErpAssertionRejection.BadSignature, "Beyan ayrıştırılamadı.");
        }

        var kid = jwt.Kid;

        if (string.IsNullOrWhiteSpace(kid))
        {
            // kid olmadan hangi anahtarla doğrulanacağı bilinmez. Bütün anahtarları
            // tek tek denemek, iptal edilmiş bir anahtarın sessizce kullanılmasına ve
            // doğrulama maliyetinin anahtar sayısıyla çarpılmasına yol açardı.
            return Hata(ErpAssertionRejection.UnknownKey, "Beyan başlığında kid yok.");
        }

        var anahtar = keys.FirstOrDefault(k => k.KeyId == kid && k.IsActive);

        if (anahtar is null)
        {
            return Hata(ErpAssertionRejection.UnknownKey, $"Etkin anahtar yok: {kid}");
        }

        if (!Algoritmalar.TryGetValue(anahtar.Algorithm, out var beklenenAlg))
        {
            return Hata(ErpAssertionRejection.UnsupportedAlgorithm, "Anahtar algoritması desteklenmiyor.");
        }

        // Başlıktaki algoritma KAYITLI olanla birebir aynı olmalı. "none" ve HMAC
        // burada elenir; anahtarın kendi algoritması dışına çıkılamaz.
        if (!string.Equals(jwt.Alg, beklenenAlg, StringComparison.Ordinal))
        {
            return Hata(
                ErpAssertionRejection.UnsupportedAlgorithm,
                $"Beyan algoritması ({jwt.Alg}) anahtarınkiyle ({beklenenAlg}) uyuşmuyor.");
        }

        SecurityKey dogrulamaAnahtari;

        try
        {
            dogrulamaAnahtari = AnahtariOku(anahtar);
        }
        catch (Exception)
        {
            return Hata(ErpAssertionRejection.UnknownKey, "Kayıtlı açık anahtar okunamadı.");
        }

        if (!ImzaDogru(assertion, dogrulamaAnahtari, beklenenAlg))
        {
            return Hata(ErpAssertionRejection.BadSignature, "İmza doğrulanamadı.");
        }

        var claims = ClaimOku(jwt);

        if (claims is null)
        {
            return Hata(ErpAssertionRejection.MissingClaim, "Beyanda zorunlu alanlar eksik.");
        }

        return new ErpSignatureResult(true, ErpAssertionRejection.None, null, claims, kid);
    }

    private static bool ImzaDogru(string assertion, SecurityKey key, string algoritma)
    {
        var parcalar = assertion.Split('.');

        if (parcalar.Length != 3)
        {
            return false;
        }

        var imzalanan = System.Text.Encoding.ASCII.GetBytes($"{parcalar[0]}.{parcalar[1]}");

        byte[] imza;

        try
        {
            imza = Base64UrlEncoder.DecodeBytes(parcalar[2]);
        }
        catch (Exception)
        {
            return false;
        }

        var saglayici = CryptoProviderFactory.Default.CreateForVerifying(key, algoritma);

        try
        {
            return saglayici.Verify(imzalanan, imza);
        }
        finally
        {
            CryptoProviderFactory.Default.ReleaseSignatureProvider(saglayici);
        }
    }

    private static SecurityKey AnahtariOku(ErpSigningKey anahtar)
    {
        if (anahtar.Algorithm == ErpSigningAlgorithm.ES256)
        {
            var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(anahtar.PublicKeyPem);

            return new ECDsaSecurityKey(ecdsa) { KeyId = anahtar.KeyId };
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(anahtar.PublicKeyPem);

        return new RsaSecurityKey(rsa) { KeyId = anahtar.KeyId };
    }

    /// <summary>Beyandaki alanları okur. Zorunlu bir alan eksikse <c>null</c> döner.</summary>
    private static ErpAssertionClaims? ClaimOku(JsonWebToken jwt)
    {
        var iss = Metin(jwt, "iss");
        var sub = Metin(jwt, "sub");
        var aud = jwt.Audiences.FirstOrDefault();
        var jti = Metin(jwt, "jti");

        if (iss is null || sub is null || aud is null || jti is null)
        {
            return null;
        }

        if (!Zaman(jwt, "iat", out var iat) || !Zaman(jwt, "exp", out var exp))
        {
            return null;
        }

        return new ErpAssertionClaims
        {
            Issuer = iss,
            Subject = sub,
            Audience = aud,
            TokenId = jti,
            IssuedAt = iat,
            ExpiresAt = exp,
            SubjectName = Metin(jwt, "name"),
        };
    }

    private static string? Metin(JsonWebToken jwt, string ad) =>
        jwt.TryGetPayloadValue<string>(ad, out var deger) && !string.IsNullOrWhiteSpace(deger)
            ? deger
            : null;

    /// <summary>
    /// Unix saniyesi olarak yazılmış zaman alanı.
    ///
    /// <para>
    /// İki gösterim de kabul edilir: kütüphane bazı yollarda değeri doğrudan sayı,
    /// bazılarında ham JSON öğesi olarak verir. Yalnızca birini beklemek, imzası
    /// tamamen geçerli bir beyanı "alan eksik" diye reddetmeye yol açıyordu.
    /// </para>
    /// </summary>
    private static bool Zaman(JsonWebToken jwt, string ad, out DateTimeOffset deger)
    {
        deger = default;

        if (jwt.TryGetPayloadValue<long>(ad, out var saniye))
        {
            deger = DateTimeOffset.FromUnixTimeSeconds(saniye);

            return true;
        }

        if (jwt.TryGetPayloadValue<JsonElement>(ad, out var ham)
            && ham.ValueKind == JsonValueKind.Number
            && ham.TryGetInt64(out var hamSaniye))
        {
            deger = DateTimeOffset.FromUnixTimeSeconds(hamSaniye);

            return true;
        }

        return false;
    }

    private static ErpSignatureResult Hata(ErpAssertionRejection sebep, string ayrinti) =>
        new(false, sebep, ayrinti, null, null);
}
