namespace GovAI.Api.Infrastructure;

/// <summary>
/// Yetkilendirme politikası adları. Rol listeleri <c>Program.cs</c> içinde tek yerde tanımlanır;
/// controller'lar yalnızca bu sabitleri kullanır.
/// </summary>
public static class Policies
{
    /// <summary>Kiracı yönetimi, kullanıcı açma, kaynak tanımlama gibi sistem işleri.</summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>Firma kartı oluşturma/güncelleme, ERP eşitleme.</summary>
    public const string ManageCompany = "ManageCompany";

    /// <summary>Skor tetikleme, senaryo çalıştırma, kural düzeltme gibi operasyonel işler.</summary>
    public const string Operate = "Operate";

    /// <summary>Salt okuyucu dahil tüm oturum açmış kullanıcılar.</summary>
    public const string Read = "Read";

    /// <summary>
    /// Ortak katalog <b>tanımı</b>: kaynak ekleme/güncelleme ve fırsat kaydı.
    /// Yalnızca platform işletimi. Kiracı SuperAdmin'i buraya dahil <b>değildir</b> —
    /// katalog tüm kiracılarca paylaşıldığı için bir müşterinin yöneticisi diğerlerinin
    /// verisini değiştiremez (Faz 1).
    /// </summary>
    public const string PlatformCatalog = "PlatformCatalog";

    /// <summary>
    /// Fırsat kuralı düzeltme ve danışman onayı. Katalog tanımını değiştirmez;
    /// yalnızca içerik kalitesine dokunur.
    /// </summary>
    public const string PlatformReview = "PlatformReview";

    /// <summary>
    /// Worker'ın veri toplama yolu: crawl tetikleme, ham doküman bırakma, tarama
    /// sonucu bildirme ve ayrıştırılmış fırsatı kaydetme.
    /// Kullanıcı, rol, kiracı veya şirket yönetemez; şirket raporlarını okuyamaz.
    /// </summary>
    public const string SystemIngest = "SystemIngest";

    /// <summary>
    /// Kiracıya ait şirket verisi (rapor, skor, simülasyon). Platform rolleri
    /// bilinçli olarak <b>dışarıdadır</b>: veri toplama kimliği müşteri verisini görmez.
    /// </summary>
    public const string CompanyData = "CompanyData";

    /// <summary>
    /// Yeniden skorlama tetikleme. Hem kiracı operasyon kullanıcısının ("Yeniden skorla"
    /// düğmesi) hem de gece toplu turunu çalıştıran worker'ın ihtiyacı olduğu için iki
    /// tarafı da kapsayan tek politika. Skor <b>okuma</b> yetkisi vermez.
    /// </summary>
    public const string Rescore = "Rescore";
}
