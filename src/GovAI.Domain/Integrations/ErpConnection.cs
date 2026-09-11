using GovAI.Domain.Common;

namespace GovAI.Domain.Integrations;

/// <summary>
/// Bir firmanın kendi ERP sistemine kurulan bağlantı.
///
/// <para>
/// Mevcut <c>/erp-sync</c> ucu <b>itme</b> yoluyla çalışır: ERP tarafındaki bir geliştirici
/// veriyi GOVAI'ye gönderir. Sahada bu çoğu zaman hiç kurulmuyor — firmanın ERP'sine
/// kod yazacak kimse olmuyor. Bu kayıt <b>çekme</b> yolunu açar: GOVAI, firmanın izin
/// verdiği uçtan veriyi kendisi okur ve profil kendiliğinden güncel kalır.
/// </para>
///
/// <para>
/// Bağlantı <b>yalnızca okur</b>. ERP'ye yazan bir yol bilerek yoktur: müşterinin
/// muhasebe ve bordro kayıtlarına yazma yetkisi istemek, entegrasyonun riskini
/// faydasının çok ötesine taşır.
/// </para>
/// </summary>
public class ErpConnection : AggregateRoot, IAuditable, ITenantScoped
{
    private ErpConnection()
    {
    }

    public ErpConnection(
        Guid tenantId,
        Guid companyId,
        ErpVendor vendor,
        string baseUrl,
        ErpAuthMode authMode,
        string protectedSecret,
        bool isOnPremise,
        string? fieldMapJson = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(baseUrl), "ERP adresi zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(protectedSecret), "ERP kimlik bilgisi zorunludur.");

        DomainException.ThrowIf(
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var adres)
            || (adres.Scheme != Uri.UriSchemeHttp && adres.Scheme != Uri.UriSchemeHttps),
            "ERP adresi geçerli bir http veya https adresi olmalıdır.");

        TenantId = tenantId;
        CompanyId = companyId;
        Vendor = vendor;
        BaseUrl = baseUrl.Trim().TrimEnd('/');
        AuthMode = authMode;
        ProtectedSecret = protectedSecret;
        IsOnPremise = isOnPremise;
        FieldMapJson = fieldMapJson;
        IsEnabled = true;
    }

    public Guid TenantId { get; set; }

    public Guid CompanyId { get; private set; }

    public ErpVendor Vendor { get; private set; }

    /// <summary>ERP'nin veri sunduğu adresin kökü.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public ErpAuthMode AuthMode { get; private set; }

    /// <summary>
    /// Kimlik bilgisinin <b>şifrelenmiş</b> hâli.
    ///
    /// <para>
    /// Parola gibi özetlenemez (hash), çünkü ERP'ye gönderilmesi gerekir; bu yüzden geri
    /// çevrilebilir biçimde şifrelenir. Açık hâli hiçbir yerde saklanmaz, loglanmaz ve
    /// API yanıtında dönmez — alan adı bunu okuyan herkese hatırlatsın diye
    /// <c>Protected</c> ile başlar.
    /// </para>
    /// </summary>
    public string ProtectedSecret { get; private set; } = string.Empty;

    /// <summary>
    /// ERP şirket ağının içinde mi?
    ///
    /// <para>
    /// Bu bir kolaylık ayarı değil, <b>güvenlik kararıdır</b>. GOVAI kullanıcıdan gelen
    /// adreslere giderken özel IP bloklarını engeller (SSRF koruması). Ama kurum içi bir
    /// Logo ya da Netsis sunucusu tam da özel IP'dedir; engeli topyekûn açmak yerine
    /// firma yöneticisi bu bağlantının kurum içi olduğunu <b>açıkça</b> beyan eder ve
    /// beyan kayda geçer.
    /// </para>
    ///
    /// <para>
    /// Bulut metadata adresi (169.254.169.254) bu beyanla dahi açılmaz: orası bir ERP
    /// değil, sunucunun kendi kimlik bilgilerinin durduğu yerdir.
    /// </para>
    /// </summary>
    public bool IsOnPremise { get; private set; }

    /// <summary>
    /// ERP yanıtındaki alanların GOVAI alanlarına eşlemesi.
    ///
    /// <para>
    /// Boşsa üreticinin varsayılan eşlemesi kullanılır. Aynı ürünün farklı sürümleri ve
    /// müşteriye özel alanlar yüzünden eşlemenin <b>bağlantı bazında</b>
    /// değiştirilebilmesi gerekir; sabit kodlanmış tek bir şema sahada tutmaz.
    /// </para>
    /// </summary>
    public string? FieldMapJson { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset? LastRunAt { get; private set; }

    public ErpSyncStatus LastRunStatus { get; private set; } = ErpSyncStatus.NeverRun;

    /// <summary>
    /// Son çalıştırmanın açıklaması.
    ///
    /// <para>
    /// Hata metni <b>ayıklanmış</b> yazılır: ERP'nin döndürdüğü gövde kimlik bilgisi veya
    /// personel verisi içerebilir ve bu alan panelde görünür.
    /// </para>
    /// </summary>
    public string? LastRunMessage { get; private set; }

    public int ConsecutiveFailureCount { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Üst üste bu kadar başarısızlıkta bağlantı kendiliğinden durur.</summary>
    public const int FailureLimit = 5;

    public void UpdateEndpoint(
        ErpVendor vendor,
        string baseUrl,
        ErpAuthMode authMode,
        bool isOnPremise,
        string? fieldMapJson)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(baseUrl), "ERP adresi zorunludur.");

        Vendor = vendor;
        BaseUrl = baseUrl.Trim().TrimEnd('/');
        AuthMode = authMode;
        IsOnPremise = isOnPremise;
        FieldMapJson = fieldMapJson;
    }

    /// <summary>Kimlik bilgisi yalnızca yenisi verildiğinde değişir; boş gönderim eskisini silmez.</summary>
    public void ReplaceSecret(string protectedSecret)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(protectedSecret), "ERP kimlik bilgisi zorunludur.");

        ProtectedSecret = protectedSecret;
    }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;

    public void RecordSuccess(DateTimeOffset at, string message)
    {
        LastRunAt = at;
        LastRunStatus = ErpSyncStatus.Succeeded;
        LastRunMessage = Kirp(message);
        ConsecutiveFailureCount = 0;
    }

    /// <summary>
    /// Başarısızlığı kaydeder ve sınır aşılırsa bağlantıyı durdurur.
    ///
    /// <para>
    /// Durdurma bilinçlidir: yanlış kimlik bilgisiyle her gece denemeye devam etmek,
    /// müşterinin ERP'sinde hesabı kilitletir. Bağlantı kendiliğinden açılmaz; firma
    /// yöneticisi sorunu görüp yeniden açar.
    /// </para>
    /// </summary>
    public void RecordFailure(DateTimeOffset at, string message)
    {
        LastRunAt = at;
        LastRunStatus = ErpSyncStatus.Failed;
        LastRunMessage = Kirp(message);
        ConsecutiveFailureCount++;

        if (ConsecutiveFailureCount >= FailureLimit)
        {
            IsEnabled = false;
        }
    }

    /// <summary>ERP ulaşıldı ama yeni veri yoktu. Bu bir arıza değildir.</summary>
    public void RecordNoChange(DateTimeOffset at)
    {
        LastRunAt = at;
        LastRunStatus = ErpSyncStatus.NoChange;
        LastRunMessage = "ERP'den gelen veri mevcut profille aynı; değişiklik yapılmadı.";
        ConsecutiveFailureCount = 0;
    }

    private static string Kirp(string metin) =>
        metin.Length <= 1000 ? metin : metin[..1000];
}

/// <summary>
/// Desteklenen ERP ürünleri.
///
/// <para>
/// Ürün adı yalnızca <b>varsayılan alan eşlemesini</b> seçer; bağlantının kendisi her
/// durumda JSON konuşan bir uca gider. Böylece listede olmayan bir ürün de
/// <see cref="GenericRest"/> ile ve kendi eşlemesiyle bağlanabilir — müşteriyi ürün
/// listesine hapsetmemek gerekir.
/// </para>
/// </summary>
public enum ErpVendor
{
    /// <summary>Ürün fark etmeksizin JSON dönen bir uç; eşleme bağlantıda tanımlanır.</summary>
    GenericRest = 0,

    Logo = 1,
    Netsis = 2,
    Sap = 3,
    Mikro = 4,
    Nebim = 5,
}

/// <summary>ERP'ye kimlik sunma biçimi.</summary>
public enum ErpAuthMode
{
    /// <summary>Kimlik bir başlıkta gönderilir (ör. <c>X-API-Key</c>).</summary>
    ApiKeyHeader = 1,

    /// <summary><c>Authorization: Bearer ...</c></summary>
    BearerToken = 2,

    /// <summary><c>Authorization: Basic ...</c> — kimlik "kullanıcı:parola" biçiminde saklanır.</summary>
    BasicAuth = 3,
}

public enum ErpSyncStatus
{
    NeverRun = 0,
    Succeeded = 1,
    NoChange = 2,
    Failed = 3,
}
