namespace GovAI.Api.Infrastructure;

/// <summary>
/// ERP modülü kimlik doğrulamasının sabitleri.
///
/// <para>
/// Şema ve politika bilerek <b>ayrıdır</b>: ERP modülü jetonu ne rol ne kiracı kapsamı
/// taşır ve panelin uçlarında kabul edilmemelidir. Tek şema kullanılsaydı, dar yetkili
/// bir entegrasyon jetonu bütün API'yi açardı.
/// </para>
/// </summary>
public static class ErpModuleDefaults
{
    public const string Scheme = "ErpModule";

    /// <summary>Yalnızca ERP modülü uçlarında kullanılır.</summary>
    public const string Policy = "ErpModuleAccess";
}
