namespace GovAI.Domain.Common;

/// <summary>Firmanın hukuki yapısı; bazı çağrılar yalnızca belirli tüzel kişilikleri kabul eder.</summary>
public enum LegalType
{
    Unknown = 0,
    SoleProprietorship = 1,   // Şahıs işletmesi
    LimitedCompany = 2,       // Limited şirket
    JointStockCompany = 3,    // Anonim şirket
    Cooperative = 4,          // Kooperatif
    Association = 5,          // Dernek
    Foundation = 6,           // Vakıf
    PublicEntity = 7          // Kamu kurumu
}

/// <summary>KOBİ ölçeği; çalışan sayısı ve ciro/bilanço eşiklerinden türetilir.</summary>
public enum EnterpriseSize
{
    Micro = 1,    // &lt; 10 çalışan
    Small = 2,    // &lt; 50 çalışan
    Medium = 3,   // &lt; 250 çalışan
    Large = 4     // &gt;= 250 çalışan
}

/// <summary>Fırsatın hangi veri kaynağı ailesinden geldiği.</summary>
public enum SourceType
{
    OfficialGazette = 1,      // Resmî Gazete
    Ministry = 2,             // Bakanlık duyuruları
    DevelopmentAgency = 3,    // Kalkınma ajansları
    KosgebOrSimilar = 4,      // KOSGEB / TÜBİTAK benzeri destek kurumları
    TenderPortal = 5,         // EKAP vb. ihale portalları
    EuOrInternational = 6,    // AB ve uluslararası programlar
    Other = 99
}

/// <summary>Destek türü sınıflandırması (Modül 4 – Teşvik / Hibe / İhale ayrıştırması).</summary>
public enum SupportCategory
{
    EmploymentIncentive = 1,  // İstihdam teşviki
    InvestmentIncentive = 2,  // Yatırım teşviki
    Grant = 3,                // Hibe
    RndSupport = 4,           // Ar-Ge desteği
    DigitalTransformation = 5,// Dijitalleşme
    ExportSupport = 6,        // İhracat desteği
    GreenTransformation = 7,  // Yeşil dönüşüm
    Tender = 8,               // Kamu ihalesi
    Loan = 9,                 // Kredi / faiz desteği
    Other = 99
}

/// <summary>Kaynak tarama işinin son durumu.</summary>
public enum CrawlStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4
}

/// <summary>Ham dokümanın işlenme aşaması.</summary>
public enum DocumentProcessingStatus
{
    Raw = 0,          // Toplandı, henüz ayrıştırılmadı
    Parsed = 1,       // Metin çıkarıldı
    RulesExtracted = 2, // Koşullar çıkarıldı
    Failed = 3,
    Discarded = 4     // İlgisiz bulundu
}

/// <summary>Tek bir uygunluk kuralının firma verisi karşısındaki sonucu.</summary>
public enum RuleOutcome
{
    Satisfied = 1,    // Koşul sağlanıyor
    NotSatisfied = 2, // Koşul sağlanmıyor
    Unknown = 3,      // Firma verisi eksik, karar verilemedi
    NotApplicable = 4 // Bu firma için geçerli değil
}

/// <summary>Kuralın ihlal edilmesinin başvuruya etkisi.</summary>
public enum RuleSeverity
{
    Blocking = 1,   // Sağlanmazsa başvuru reddedilir
    Major = 2,      // Skoru ciddi düşürür
    Minor = 3,      // Bilgilendirici
    Bonus = 4       // Sağlanırsa avantaj sağlar
}

/// <summary>Kuralın hangi firma boyutunu sorguladığı; skorlama ağırlıkları buradan eşlenir.</summary>
public enum RuleDimension
{
    Sector = 1,               // NACE / faaliyet alanı
    Financial = 2,            // Ciro, bilanço, mali yeterlilik
    Employment = 3,           // Çalışan sayısı ve personel yapısı
    Documentation = 4,        // Belge ve sertifika
    Region = 5,               // Lokasyon / bölge
    TechnicalQualification = 6, // Teknik yeterlilik, referans, deneyim
    Timing = 7                // Takvim ve son başvuru tarihi
}

/// <summary>Uygunluk değerlendirmesinin özet kararı.</summary>
public enum EligibilityVerdict
{
    Eligible = 1,           // Uygun
    ConditionallyEligible = 2, // Eksikler kapatılırsa uygun
    NotEligible = 3,        // Uygun değil
    Indeterminate = 4       // Veri yetersiz
}

/// <summary>Belge kontrol listesindeki bir kalemin durumu.</summary>
public enum DocumentStatus
{
    Missing = 0,
    Provided = 1,
    Expired = 2,
    NotRequired = 3
}

/// <summary>Bildirim tetikleyicisi (Modül 10).</summary>
public enum NotificationKind
{
    DeadlineApproaching = 1,  // Son başvuru tarihi yaklaşıyor
    NewMatch = 2,             // Yeni uygun fırsat eşleşti
    ScoreChanged = 3,         // Firma verisi değişti, skor güncellendi
    RegulationChanged = 4,    // Mevzuat / çağrı metni güncellendi
    DocumentMissing = 5,      // Eksik belge hatırlatması
    SystemAlert = 99
}

public enum NotificationChannel
{
    InApp = 1,
    Email = 2,
    Webhook = 3
}

/// <summary>
/// Kiracı (workspace) düzeyindeki roller ve platform işletim rolleri.
///
/// 1–5 arası değerler <b>kiracıya</b> aittir: bir müşterinin kendi çalışma alanındaki
/// yetkileridir ve ortak katalogda hiçbir yazma hakkı vermezler.
/// 10 ve üzeri değerler <b>platform işletimine</b> aittir; kiracı yöneticileri
/// tarafından atanamazlar (bkz. AuthenticationService.CreateUserAsync).
///
/// Şirket bazlı yetkiler bu enum'da değil, <see cref="CompanyRole"/> içindedir:
/// bir kullanıcının yetkisi şirketten şirkete değişebilir.
/// </summary>
public enum UserRole
{
    SuperAdmin = 1,        // Kiracı yöneticisi — kendi çalışma alanının tamamı
    CompanyManager = 2,    // Firma yöneticisi
    OperationUser = 3,     // Operasyon kullanıcısı
    Consultant = 4,        // Danışman
    ReadOnly = 5,          // Salt okuyucu

    /// <summary>Platform: kaynakları ve global fırsat kataloğunu yönetir.</summary>
    PlatformCatalogManager = 10,

    /// <summary>Platform: fırsat kuralını düzeltir ve danışman onayı verir.</summary>
    PlatformReviewer = 11,

    /// <summary>Platform: yalnızca worker'ın ingest / crawl / run uçları.</summary>
    SystemIngest = 12
}

/// <summary>
/// Kullanıcının <b>belirli bir şirketteki</b> yetkisi. Aynı kullanıcı bir şirkette
/// sahip, başka şirkette yalnızca görüntüleyici olabilir.
/// </summary>
public enum CompanyRole
{
    /// <summary>Şirketi ve üyeliklerini yönetir. Her şirkette en az bir aktif sahip bulunmalıdır.</summary>
    CompanyOwner = 1,

    /// <summary>Profil ve operasyon yönetimi. Sahip rolü atayamaz.</summary>
    CompanyManager = 2,

    /// <summary>Analiz, fırsat ve rapor işlemleri. Üyelik yönetemez.</summary>
    CompanyExpert = 3,

    /// <summary>Yalnızca görüntüleme.</summary>
    CompanyViewer = 4
}

/// <summary>Şirketin gruptaki veya ana şirkete göre konumu.</summary>
public enum CompanyRelationshipType
{
    /// <summary>Bağımsız; ana şirkete bağlı değil.</summary>
    Independent = 0,

    /// <summary>Grubun ana şirketi.</summary>
    HeadCompany = 1,

    /// <summary>Bağlı ortaklık (kontrol sahibi ana şirket).</summary>
    Subsidiary = 2,

    /// <summary>İştirak (azınlık payı).</summary>
    Affiliate = 3,

    /// <summary>
    /// Şube. Ayrı tüzel kişilik olmayan operasyonel şubeler için
    /// <c>CompanyLocation</c> kullanılmalıdır; bu değer yalnızca ayrı vergi
    /// numarası taşıyan tüzel şube kayıtları içindir.
    /// </summary>
    Branch = 4,

    /// <summary>Aynı grupta yer alan, doğrudan ana-bağlı ilişkisi olmayan şirket.</summary>
    GroupCompany = 5
}

/// <summary>Başka bir kiracıda kayıtlı vergi numarası için açılan bağlantı/doğrulama talebi.</summary>
public enum VerificationRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}
