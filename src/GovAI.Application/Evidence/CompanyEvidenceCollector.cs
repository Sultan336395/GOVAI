using GovAI.Domain.Companies;
using GovAI.Domain.Evidence;

namespace GovAI.Application.Evidence;

/// <summary>
/// Firmanın profilindeki verileri <b>kanıt</b> olarak toplar.
///
/// <para>
/// Saf ve test edilebilir: veritabanına, saate ve ağa dokunmaz. Girdi firma nesnesi ve
/// değerlendirme günü, çıktı kanıt listesidir. Toplama mantığı yükleme mantığından
/// ayrıldı çünkü asıl kırılgan kısım burası — hangi alanın hangi tarihe dayandığı ve
/// hangisinin tarihinin bilinmediği.
/// </para>
///
/// <para>
/// <b>Tarih uydurulmaz.</b> Bir alanın ne zaman girildiği bilinmiyorsa kanıt tarihsiz
/// bırakılır ve motor ona <see cref="EvidenceStatus.Bilinmiyor"/> der. Kayıt tarihini
/// "herhâlde şirket kurulduğunda girilmiştir" gibi bir varsayımla doldurmak, ölçülemeyen
/// bir şeyi ölçülmüş gibi göstermek olurdu.
/// </para>
/// </summary>
public static class CompanyEvidenceCollector
{
    /// <summary>Firmanın tüm kanıtlarını değerlendirir.</summary>
    public static IReadOnlyList<EvidenceReliability> Collect(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);

        return Snapshot(company)
            .Select(k => EvidenceHalfLife.Evaluate(k, asOf))
            .ToList();
    }

    /// <summary>Ham kanıt kayıtları; motora verilmeden önceki hâl.</summary>
    public static IReadOnlyList<EvidenceSnapshot> Snapshot(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        var kanitlar = new List<EvidenceSnapshot>();

        kanitlar.AddRange(Sertifikalar(company));
        kanitlar.AddRange(MaliVeriler(company));
        kanitlar.Add(PersonelBeyani(company));

        var erp = ErpAnlikGoruntusu(company);

        if (erp is not null)
        {
            kanitlar.Add(erp);
        }

        return kanitlar;
    }

    /// <summary>
    /// Sertifikalar. Geçerlilik tarihi olanlar kalan süreye, olmayanlar veriliş
    /// tarihine göre değerlendirilir.
    /// </summary>
    private static IEnumerable<EvidenceSnapshot> Sertifikalar(Company company) =>
        company.Certificates.Select(s => new EvidenceSnapshot
        {
            Kind = EvidenceKind.Certificate,
            Label = string.IsNullOrWhiteSpace(s.Name) ? s.Code : s.Name,
            ObservedOn = s.IssuedOn,
            ValidUntil = s.ValidUntil,
        });

    /// <summary>
    /// Mali yıl kayıtları.
    ///
    /// <para>
    /// Gözlem tarihi olarak <b>mali yılın sonu</b> alınır, kaydın güncellenme zamanı
    /// değil. Sebep: 2023 yılına ait bir bilanço bugün sisteme girilse bile 2023'ün
    /// verisidir; girildiği güne bakmak üç yıllık veriyi "bugün elde edilmiş" gibi
    /// gösterirdi.
    /// </para>
    /// </summary>
    private static IEnumerable<EvidenceSnapshot> MaliVeriler(Company company) =>
        company.AnnualFinancials
            .Where(f => !f.IsEmpty)
            .Select(f => new EvidenceSnapshot
            {
                Kind = EvidenceKind.FinancialStatement,
                Label = $"{f.FiscalYear} mali yılı",
                ObservedOn = new DateOnly(f.FiscalYear, 12, 31),
                Assurance = Guvence(f.VerificationStatus),
            });

    private static EvidenceAssurance Guvence(FinancialVerificationStatus durum) => durum switch
    {
        FinancialVerificationStatus.Verified => EvidenceAssurance.Verified,
        FinancialVerificationStatus.DocumentChecked => EvidenceAssurance.DocumentChecked,
        _ => EvidenceAssurance.SelfDeclared,
    };

    /// <summary>
    /// Personel beyanı.
    ///
    /// <para>
    /// Profil hiç güncellenmemişse <see cref="Company.UpdatedAt"/> boş olur ve kanıt
    /// <b>tarihsiz</b> kalır. Kayıt oluşturma tarihine düşmek cazip ama yanlış olurdu:
    /// firma kurulduğunda girilen personel sayısı bugün hâlâ doğru diye varsayılamaz,
    /// ama "yanlış" da denemez — bilinmiyordur.
    /// </para>
    /// </summary>
    private static EvidenceSnapshot PersonelBeyani(Company company) => new()
    {
        Kind = EvidenceKind.WorkforceDeclaration,
        Label = "Personel ve profil beyanı",
        ObservedOn = company.UpdatedAt is { } guncelleme
            ? DateOnly.FromDateTime(guncelleme.UtcDateTime)
            : null,
    };

    /// <summary>
    /// ERP anlık görüntüsü. Hiç eşitleme yapılmamışsa kanıt <b>üretilmez</b>.
    ///
    /// <para>
    /// Tarihsiz bir ERP kanıtı üretmek, entegrasyonu olmayan firmada "ERP verisi var
    /// ama tazeliği bilinmiyor" demek olurdu. Olmayan bir kanıt hakkında konuşulmaz.
    /// </para>
    /// </summary>
    private static EvidenceSnapshot? ErpAnlikGoruntusu(Company company) =>
        company.LastSyncedAt is { } esitleme
            ? new EvidenceSnapshot
            {
                Kind = EvidenceKind.ErpSnapshot,
                Label = "ERP eşitlemesi",
                ObservedOn = DateOnly.FromDateTime(esitleme.UtcDateTime),
            }
            : null;
}
