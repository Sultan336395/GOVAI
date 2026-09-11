namespace GovAI.Domain.Integrations;

/// <summary>
/// ERP yanıtındaki alan adlarının GOVAI alanlarına eşlemesi.
///
/// <para>
/// Aynı bilgi her üründe başka adla durur: bir sistemde <c>yillikCiro</c>, başkasında
/// <c>annual_revenue</c>, bir üçüncüsünde <c>Ciro.Toplam</c>. Sabit kodlanmış tek bir şema
/// sahada tutmaz; eşleme bağlantı bazında değiştirilebilir olmalıdır.
/// </para>
///
/// <para>
/// Eşleme <b>nokta yolu</b> kullanır: <c>"personel.kadin"</c> iç içe nesneye iner. Yol
/// bulunamazsa alan <b>eksik</b> sayılır — sıfır sayılmaz. Bu ayrım ürünün üçüncü
/// iddiasıdır: eksik veri firmayı elemez (bkz. <c>docs/adr/0003</c>). ERP'de olmayan bir
/// alanı 0 olarak yazmak, firmayı "hiç kadın çalışanı yok" diye kaydetmek olurdu.
/// </para>
/// </summary>
public sealed record ErpFieldMap
{
    /// <summary>Yıllık ciro.</summary>
    public string? AnnualRevenue { get; init; }

    /// <summary>Bilanço (aktif) büyüklüğü.</summary>
    public string? BalanceSize { get; init; }

    public string? Equity { get; init; }

    public string? ExportRevenue { get; init; }

    public string? EmployeeCount { get; init; }

    public string? WomenEmployeeCount { get; init; }

    public string? YoungEmployeeCount { get; init; }

    public string? RAndDEmployeeCount { get; init; }

    public string? DisabledEmployeeCount { get; init; }

    /// <summary>Firmanın genç çalışan sayarken kullandığı azami yaş.</summary>
    public string? YoungEmployeeMaxAge { get; init; }

    /// <summary>Sertifika/belge listesi. Dizi ya da virgülle ayrılmış metin olabilir.</summary>
    public string? Certificates { get; init; }

    /// <summary>Sertifika listesi nesne dizisiyse, koda karşılık gelen alan.</summary>
    public string? CertificateCodeField { get; init; }

    /// <summary>Sertifika geçerlilik tarihi alanı.</summary>
    public string? CertificateValidUntilField { get; init; }

    /// <summary>
    /// Mevzuat/hibe bildirimlerini alacak kişilerin listesi.
    ///
    /// <para>
    /// Şirket bu listeyi <b>kendi ERP'sinde</b> tanımlar; GOVAI'de hesap açılmaz.
    /// Bölüm eşlemede tanımlı değilse alıcılar çekilmez ve mevcut tanımlar olduğu gibi
    /// kalır — ERP'sinde bu modül olmayan bir firmada elle girilmiş doğru liste
    /// silinmemelidir (§2.2.4 ile aynı gerekçe).
    /// </para>
    /// </summary>
    public string? NotificationRecipients { get; init; }

    /// <summary>Alıcı listesindeki e-posta alanı. Alıcının tek zorunlu alanıdır.</summary>
    public string? RecipientEmailField { get; init; }

    /// <summary>Alıcı adı alanı. Yoksa yalnızca adres kullanılır.</summary>
    public string? RecipientNameField { get; init; }

    /// <summary>Alıcı görevi alanı (ör. "Mali İşler Müdürü"). Yalnızca gösterim içindir.</summary>
    public string? RecipientRoleField { get; init; }

    /// <summary>ERP'deki personel/kişi kimliği; kişi yeniden adlandırılsa da eşleşme korunur.</summary>
    public string? RecipientExternalIdField { get; init; }

    /// <summary>
    /// Üreticinin varsayılan eşlemesi.
    ///
    /// <para>
    /// Bunlar <b>başlangıç değeridir, garanti değildir</b>: aynı ürünün sürümleri ve
    /// müşteriye özel alanlar farklılık gösterir. Bağlantı kurulurken deneme çağrısı
    /// yapılır ve hangi alanların bulunduğu kullanıcıya gösterilir; eşleme gerekiyorsa
    /// elle düzeltilir.
    /// </para>
    /// </summary>
    public static ErpFieldMap Default(ErpVendor vendor) => vendor switch
    {
        // Logo ve Netsis Türkçe alan adlarıyla yayımlıyor.
        ErpVendor.Logo or ErpVendor.Netsis or ErpVendor.Mikro => new ErpFieldMap
        {
            AnnualRevenue = "mali.yillikCiro",
            BalanceSize = "mali.bilancoBuyuklugu",
            Equity = "mali.ozkaynak",
            ExportRevenue = "mali.ihracatCirosu",
            EmployeeCount = "personel.toplam",
            WomenEmployeeCount = "personel.kadin",
            YoungEmployeeCount = "personel.genc",
            RAndDEmployeeCount = "personel.arge",
            DisabledEmployeeCount = "personel.engelli",
            YoungEmployeeMaxAge = "personel.gencUstYas",
            Certificates = "belgeler",
            CertificateCodeField = "kod",
            CertificateValidUntilField = "gecerlilikTarihi",
            NotificationRecipients = "bildirimSorumlulari",
            RecipientEmailField = "eposta",
            RecipientNameField = "adSoyad",
            RecipientRoleField = "gorev",
            RecipientExternalIdField = "personelKodu",
        },

        // SAP ve Nebim İngilizce alan adlarıyla yayımlıyor.
        ErpVendor.Sap or ErpVendor.Nebim => new ErpFieldMap
        {
            AnnualRevenue = "financials.annualRevenue",
            BalanceSize = "financials.balanceSheetTotal",
            Equity = "financials.equity",
            ExportRevenue = "financials.exportRevenue",
            EmployeeCount = "workforce.totalHeadcount",
            WomenEmployeeCount = "workforce.femaleHeadcount",
            YoungEmployeeCount = "workforce.youngHeadcount",
            RAndDEmployeeCount = "workforce.rndHeadcount",
            DisabledEmployeeCount = "workforce.disabledHeadcount",
            YoungEmployeeMaxAge = "workforce.youngMaxAge",
            Certificates = "certificates",
            CertificateCodeField = "code",
            CertificateValidUntilField = "validUntil",
            NotificationRecipients = "notificationContacts",
            RecipientEmailField = "email",
            RecipientNameField = "fullName",
            RecipientRoleField = "title",
            RecipientExternalIdField = "employeeCode",
        },

        // Ürün bilinmiyorsa varsayım yapılmaz: eşleme bağlantıda tanımlanır.
        _ => new ErpFieldMap(),
    };

    /// <summary>Eşlemede tanımlı alan var mı? Tamamen boş eşleme veri çekemez.</summary>
    public bool IsEmpty =>
        AnnualRevenue is null && BalanceSize is null && Equity is null && ExportRevenue is null
        && EmployeeCount is null && WomenEmployeeCount is null && YoungEmployeeCount is null
        && RAndDEmployeeCount is null && DisabledEmployeeCount is null && Certificates is null;
}
