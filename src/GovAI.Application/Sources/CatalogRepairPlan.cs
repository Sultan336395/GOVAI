using GovAI.Domain.Common;

namespace GovAI.Application.Sources;

/// <summary>Bir onarım adımının türü.</summary>
public enum CatalogRepairAction
{
    /// <summary>Kayıt karantinaya alınır; silinmez, kanıtı korunur.</summary>
    Quarantine = 1,

    /// <summary>Kaydın başlığı belge sürümündeki gerçek başlıkla düzeltilir.</summary>
    RetitleFromDocument = 2
}

/// <summary>Onarımın hedefi: fırsat kataloğu mu, mevzuat kataloğu mu?</summary>
public enum CatalogRepairTarget
{
    Opportunity = 1,
    RegulatoryChange = 2
}

/// <summary>
/// Tek bir onarım adımının <b>kod sahipli</b> tanımı.
///
/// <para>
/// Neden kodda: bu adımlar üretim verisine dokunuyor ve gözden geçirilmeleri gerekiyor.
/// Veritabanından ya da bir arayüzden okunan bir onarım listesi, kimin ne zaman ne
/// eklediği belli olmadan çalışırdı. Kaynak kataloğunda aynı gerekçeyle aynı yol
/// izleniyor (<c>OfficialSourceCatalog</c>).
/// </para>
///
/// <para>
/// Kayıtlar kimlikle değil <b>eşleştirme deseniyle</b> bulunur: kimlikler ortamdan
/// ortama değişir, başlık ve adres deseni değişmez.
/// </para>
/// </summary>
public sealed record CatalogRepairStep
{
    /// <summary>Adımın sabit kodu; rapor ve testler bununla konuşur.</summary>
    public required string Code { get; init; }

    public required CatalogRepairTarget Target { get; init; }

    public required CatalogRepairAction Action { get; init; }

    /// <summary>Kaydın adresinde geçmesi gereken parça. Boşsa adrese bakılmaz.</summary>
    public string? UrlContains { get; init; }

    /// <summary>Kaydın başlığında geçmesi gereken parça (Türkçe katlanmış karşılaştırma).</summary>
    public string? TitleContains { get; init; }

    /// <summary>Karantina nedeni; <see cref="CatalogRepairAction.Quarantine"/> için zorunlu.</summary>
    public QuarantineReason Reason { get; init; } = QuarantineReason.None;

    /// <summary>Karantina notu. Kullanıcı ve inceleyici bunu okur.</summary>
    public string? Note { get; init; }

    /// <summary>Adımın neden var olduğu; dry-run raporunda gösterilir.</summary>
    public required string Rationale { get; init; }
}

/// <summary>
/// Bekleyen katalog onarımlarının tanımı (Faz 3).
///
/// <para>
/// İki grup var:
/// </para>
/// <list type="number">
///   <item>
///     <b>KOSGEB</b> — iki çöp kayıt. Biri destek <i>liste</i> sayfası, diğeri
///     yürürlükten kaldırılmış destekler sayfası. İkisi de aktif fırsat değil; katalogda
///     durdukları sürece firmalara "size uygun destek var" deniyor ve bağlantı bir liste
///     sayfasına götürüyor.
///   </item>
///   <item>
///     <b>SGK</b> — beş taslak kayıt. Üçü sağlık/SUT/ilaç ve gayrimenkul içeriği:
///     işveren mevzuatı değil. İkisinin başlığı menü başlığından alınmış
///     ("ÇALIŞAN VE İŞVEREN"); belge sürümündeki gerçek başlıkla düzeltilir.
///   </item>
/// </list>
///
/// <para>
/// SGK konu kararları <c>collector/konu.py</c> süzgecinin çıktısıdır; buradaki liste o
/// süzgecin kararını <b>tekrarlamaz</b>, uygular. İki tarafın ayrışmadığı
/// <c>test_sgk_onarim_plani.py</c> ile korunur.
/// </para>
/// </summary>
public static class CatalogRepairPlan
{
    /// <summary>KOSGEB çöp kayıtlarının karantina notu.</summary>
    public const string KosgebNote =
        "Liste sayfası veya yürürlükten kaldırılmış destek sayfası; aktif fırsat değildir.";

    /// <summary>Sağlık/SUT içeriğinin karantina notu.</summary>
    public const string SgkHealthNote =
        "Sağlık Uygulama Tebliği / ilaç listesi içeriği; işveren mevzuatı değildir.";

    /// <summary>Gayrimenkul ilanının karantina notu.</summary>
    public const string SgkRealEstateNote =
        "Kurumun gayrimenkul satış ilanı; işveren mevzuatı değildir.";

    /// <summary>
    /// Onarım adımları, <b>özelden genele</b> sıralı.
    ///
    /// <para>
    /// Sıra önemlidir: bir kayıt en fazla bir adımla eşleşir ve ilk eşleşen kazanır.
    /// "Yürürlükten Kaldırılan Destekler" başlığı hem özel adımla hem genel "destekler"
    /// adımıyla eşleşiyor; özel adım önce gelmezse kayıt yanlış gerekçeyle raporlanırdı.
    /// </para>
    /// </summary>
    public static IReadOnlyList<CatalogRepairStep> Steps =>
    [
        new()
        {
            Code = "KOSGEB-YURURLUKTEN-KALDIRILAN",
            Target = CatalogRepairTarget.Opportunity,
            Action = CatalogRepairAction.Quarantine,
            UrlContains = "kosgeb.gov.tr",
            TitleContains = "yururlukten kaldirilan",
            Reason = QuarantineReason.InvalidSourcePage,
            Note = KosgebNote,
            Rationale = "Yürürlükten kaldırılmış destekler sayfası; başvurulabilir bir çağrı değil."
        },
        new()
        {
            Code = "KOSGEB-LISTE",
            Target = CatalogRepairTarget.Opportunity,
            Action = CatalogRepairAction.Quarantine,
            UrlContains = "kosgeb.gov.tr",
            TitleContains = "destekler",
            Reason = QuarantineReason.InvalidSourcePage,
            Note = KosgebNote,
            Rationale = "Destek liste sayfası tek bir çağrı değildir; bağlantı hangi destekten "
                        + "bahsedildiğini göstermez."
        },
        new()
        {
            Code = "SGK-SUT",
            Target = CatalogRepairTarget.RegulatoryChange,
            Action = CatalogRepairAction.Quarantine,
            TitleContains = "sut",
            Reason = QuarantineReason.NeedsManualReview,
            Note = SgkHealthNote,
            Rationale = "Sağlık Uygulama Tebliği içeriği işveren yükümlülüğü doğurmaz; "
                        + "işveren mevzuatı olarak yayımlanırsa danışman yanlış yönlendirilir."
        },
        new()
        {
            Code = "SGK-ILAC",
            Target = CatalogRepairTarget.RegulatoryChange,
            Action = CatalogRepairAction.Quarantine,
            TitleContains = "bedeli odenecek ilaclar",
            Reason = QuarantineReason.NeedsManualReview,
            Note = SgkHealthNote,
            Rationale = "İlaç listesi duyurusu işveren mevzuatı değildir."
        },
        new()
        {
            Code = "SGK-GAYRIMENKUL",
            Target = CatalogRepairTarget.RegulatoryChange,
            Action = CatalogRepairAction.Quarantine,
            TitleContains = "gayrimenkul satis",
            Reason = QuarantineReason.NeedsManualReview,
            Note = SgkRealEstateNote,
            Rationale = "Kurumun kendi gayrimenkul satışı; firmayı bağlayan bir düzenleme değil."
        },
        new()
        {
            Code = "SGK-MENU-BASLIGI",
            Target = CatalogRepairTarget.RegulatoryChange,
            Action = CatalogRepairAction.RetitleFromDocument,
            TitleContains = "calisan ve isveren",
            Rationale = "Başlık sayfanın menü başlığından alınmış; kaydın gerçek konusunu "
                        + "anlatmıyor. Belge sürümündeki başlıkla düzeltilir."
        }
    ];
}
