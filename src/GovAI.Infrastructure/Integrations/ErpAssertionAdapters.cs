using GovAI.Application.Integrations;
using GovAI.Domain.Integrations;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GovAI.Infrastructure.Integrations;

/// <summary>
/// Uygulama katmanının imza limanı. Kriptografi <see cref="ErpAssertionVerifier"/>
/// içindedir; bu sınıf yalnızca sonucu uygulama tiplerine çevirir.
/// </summary>
public sealed class ErpAssertionReader(ErpAssertionVerifier verifier) : IErpAssertionReader
{
    public (bool Ok, ErpAssertionRejection Rejection, string? Detail, ErpAssertionClaims? Claims) Read(
        string assertion,
        IReadOnlyList<ErpSigningKey> keys)
    {
        var sonuc = verifier.Verify(assertion, keys);

        return (sonuc.IsValid, sonuc.Rejection, sonuc.Detail, sonuc.Claims);
    }

    /// <summary>
    /// <c>iss</c> alanını <b>imzayı doğrulamadan</b> okur.
    ///
    /// <para>
    /// Bu, güvenlik kararı değildir ve olamaz: hangi kimliğin anahtarlarıyla
    /// doğrulanacağını bulmak için önce iddia edilen kimliğe bakmak gerekir. Okunan
    /// değer yalnızca kaydı <b>aramak</b> için kullanılır; kabul, imza doğrulandıktan
    /// sonra verilir ve <c>iss</c> orada kayıtla ayrıca karşılaştırılır.
    /// </para>
    /// </summary>
    public string? PeekIssuer(string assertion)
    {
        if (string.IsNullOrWhiteSpace(assertion))
        {
            return null;
        }

        try
        {
            var jwt = new JsonWebToken(assertion);

            return jwt.TryGetPayloadValue<string>("iss", out var iss) && !string.IsNullOrWhiteSpace(iss)
                ? iss
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
