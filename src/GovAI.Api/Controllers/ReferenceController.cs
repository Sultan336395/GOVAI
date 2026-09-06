using GovAI.Api.Infrastructure;
using GovAI.Application.Reference;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Controllers;

/// <summary>
/// <c>/api/reference</c> — form alanlarını besleyen referans listeleri.
///
/// <para>
/// Sektör ve NACE kodu artık serbest metin değildir; kullanıcı yazmaz, listeden seçer.
/// Liste tek yerde (<see cref="ActivityCatalog"/>) durur: arayüz öneriyi buradan alır,
/// kayıt doğrulaması da aynı katalogdan yapılır. İki taraf ayrı listelerden beslenseydi
/// arayüzde geçerli görünen bir seçim sunucuda reddedilirdi.
/// </para>
///
/// <para>Katalog müşteri verisi içermez; yalnızca oturum açmış kullanıcılara açıktır.</para>
/// </summary>
[ApiController]
[Route("api/reference")]
[Authorize(Policy = Policies.Read)]
[Produces("application/json")]
public sealed class ReferenceController : ControllerBase
{
    /// <summary>
    /// Faaliyet sektörleri. <c>q</c> boş veya kısaysa tüm liste döner — sektör listesi
    /// kısadır, kullanıcı hiç yazmadan da gezinebilmelidir.
    /// </summary>
    [HttpGet("sectors")]
    public ActionResult<IReadOnlyList<SectorOptionDto>> Sectors([FromQuery] string? q) =>
        Ok(ActivityCatalog.SearchSectors(q)
            .Select(s => new SectorOptionDto(s.Name, s.Divisions))
            .ToList());

    /// <summary>
    /// NACE Rev. 2 kod önerileri. Sorgu koda ("256") da tanıma ("yazılım") da uyar.
    ///
    /// <para>
    /// <c>sector</c> birden çok kez verilebilir ve liste o sektörlerin kodlarıyla
    /// <b>sınırlanır</b>. Arayüz ana sektörü (diğer kodlar için alt sektörleri de)
    /// gönderir; böylece kullanıcı sektörüne ait olmayan bir kodu görmez ve seçemez.
    /// Kayıt doğrulaması da aynı kısıtı uygular.
    /// </para>
    /// </summary>
    [HttpGet("nace")]
    public ActionResult<IReadOnlyList<NaceOptionDto>> Nace(
        [FromQuery] string? q,
        [FromQuery] string[]? sector) =>
        Ok(ActivityCatalog.SearchNace(q, sector)
            .Select(n => new NaceOptionDto(n.Code, n.Title, n.Sector))
            .ToList());
}

public sealed record SectorOptionDto(string Name, IReadOnlyList<string> Divisions);

public sealed record NaceOptionDto(string Code, string Title, string Sector);
