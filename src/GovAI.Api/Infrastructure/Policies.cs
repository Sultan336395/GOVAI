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
    /// Ortak çağrı/kaynak kataloğuna <b>yazma</b>. Katalog kiracıya özel değildir:
    /// bir kiracının yaptığı değişikliği tüm kiracılar görür. Bu yüzden sıradan
    /// operasyon kullanıcısına açık olamaz (Faz 0B).
    ///
    /// Okuma tarafı <see cref="Read"/> ile herkese açık kalır.
    /// </summary>
    public const string CatalogWrite = "CatalogWrite";
}
