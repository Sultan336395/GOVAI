using GovAI.Domain.Common;

namespace GovAI.Domain.Sources;

/// <summary>
/// Kaynağın kurumsal kimliği (Faz 2). Kim yayımlıyor, hangi yargı alanında, hangi dilde.
///
/// Sahipli (owned) tip olarak <c>sources</c> tablosunda kolonlara açılır; ayrı tablo
/// gerektirmez ve kaynak kaydından bağımsız yaşamaz.
/// </summary>
public sealed record SourceProfile(
    /// <summary>Yayımlayan resmî kurum (ör. "Gelir İdaresi Başkanlığı").</summary>
    string? Authority,
    /// <summary>Yargı alanı: ISO ülke kodu ya da "EU".</summary>
    string? Jurisdiction,
    /// <summary>Kurumun resmî alan adı. Tarama bu alan adının dışına çıkmaz.</summary>
    string? OfficialDomain,
    /// <summary>İçerik dili (ISO 639-1).</summary>
    string? Language)
{
    public static SourceProfile Empty => new(null, null, null, null);
}

/// <summary>
/// Kaynağın tarama yapılandırması (Faz 2).
///
/// Bu alanlar eskiden serbest biçimli <c>ConfigurationJson</c> içindeydi; oradan
/// çıkarılıp kolonlara alındılar çünkü tarama kararı bunlara bakıyor: seçicisi ya da
/// URL kalıbı olmayan bir kaynak <b>taranamaz</b> — aksi hâlde sitedeki her bağlantıyı
/// (menü, iletişim, hakkımızda) ilan sanarak toplar.
/// </summary>
public sealed record SourceCrawlPlan(
    /// <summary>Tarama başlangıç adresi. Boşsa kaynağın kök adresi kullanılır.</summary>
    string? StartUrl,
    /// <summary>Bağlantıların çıkarılacağı CSS seçicisi (ör. <c>a.duyuru-link</c>).</summary>
    string? ListSelector,
    /// <summary>İçerik gövdesinin CSS seçicisi.</summary>
    string? ContentSelector,
    /// <summary>Yalnızca bu kalıba uyan adresler izlenir (regex).</summary>
    string? UrlPattern,
    /// <summary>Tek taramada indirilecek en fazla sayfa.</summary>
    int MaxPages,
    /// <summary>Virgülle ayrılmış ek alan adları; resmî alan adının yanında izin verilenler.</summary>
    string? AllowedDomains,
    /// <summary>Virgülle ayrılmış kabul edilen medya türleri (ör. <c>text/html,application/pdf</c>).</summary>
    string? DocumentTypes)
{
    public static SourceCrawlPlan Empty => new(null, null, null, null, 1, null, null);

    /// <summary>
    /// Tarama için asgari yapılandırma var mı?
    ///
    /// Liste seçicisi <b>ve</b> URL kalıbından en az biri zorunludur. İkisi de yoksa
    /// tarayıcı neyin ilan neyin menü olduğunu ayırt edemez.
    /// </summary>
    public bool IsCrawlable =>
        !string.IsNullOrWhiteSpace(ListSelector) || !string.IsNullOrWhiteSpace(UrlPattern);
}
