using GovAI.Domain.Common;

namespace GovAI.Domain.Notifications;

/// <summary>Alıcı tanımının nereden geldiği.</summary>
public enum RecipientSource
{
    /// <summary>Şirketin ERP'sinden çekildi. Asıl yol budur.</summary>
    ErpPull = 1,

    /// <summary>
    /// Panelden elle tanımlandı. ERP'sinde bildirim sorumlusu modülü olmayan ya da
    /// henüz ERP bağlantısı kurmamış firmalar için; yoksa o firmalara hiç e-posta
    /// gidemezdi.
    /// </summary>
    Manual = 2
}

/// <summary>
/// Bir şirketin mevzuat/hibe bildirimlerini alacak kişi.
///
/// <para>
/// Alıcı bir <b>GOVAI kullanıcısı değildir</b>: hesabı, parolası ve oturumu yoktur.
/// Müşteri çalışanları GOVAI'ye giriş yapmaz; kendi ERP'lerindeki GOVAI modülünü
/// kullanır. Bu yüzden alıcı listesi kullanıcı tablosundan türetilemez — türetilseydi
/// e-posta almak için herkese GOVAI hesabı açmak gerekirdi.
/// </para>
///
/// <para>
/// Kayıt <b>şirkete</b> bağlıdır (<see cref="TenantId"/> + <see cref="CompanyId"/>).
/// Kişiye değil şirkete bağlı olması bilinçlidir: sorumlu kişi değiştiğinde bildirim
/// hattı kopmaz, ERP'deki tanım güncellenir ve yeni kişi almaya başlar.
/// </para>
/// </summary>
public class NotificationRecipient : AggregateRoot, IAuditable, ITenantScoped
{
    private NotificationRecipient()
    {
    }

    public NotificationRecipient(
        Guid tenantId,
        Guid companyId,
        string email,
        string? fullName,
        string? role,
        RecipientSource source,
        string? externalId = null)
    {
        DomainException.ThrowIf(companyId == Guid.Empty, "Firma zorunludur.");
        DomainException.ThrowIf(!IsValidEmail(email), $"Geçersiz e-posta adresi: {email}");

        TenantId = tenantId;
        CompanyId = companyId;
        Email = Normalize(email);
        FullName = Kirp(fullName);
        Role = Kirp(role);
        Source = source;
        ExternalId = Kirp(externalId);
        IsActive = true;
    }

    public Guid TenantId { get; set; }

    public Guid CompanyId { get; private set; }

    /// <summary>
    /// Küçük harfe indirilmiş ve kırpılmış adres.
    ///
    /// <para>
    /// Normalleştirme tekilleştirmenin dayanağıdır: ERP'den bir turda
    /// <c>Ayse@firma.com</c>, sonrakinde <c>ayse@firma.com</c> gelirse aynı kişi iki
    /// kayıt açar ve her bildirimi iki kez alırdı.
    /// </para>
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    public string? FullName { get; private set; }

    /// <summary>Görevi (ör. "Mali İşler Müdürü"). Yalnızca gösterim içindir.</summary>
    public string? Role { get; private set; }

    public RecipientSource Source { get; private set; }

    /// <summary>ERP'deki kimliği. Kişi ERP'de yeniden adlandırılsa da eşleşme korunur.</summary>
    public string? ExternalId { get; private set; }

    /// <summary>
    /// Pasif alıcıya gönderim yapılmaz ama kayıt <b>silinmez</b>: geçmiş bir
    /// bildirimin kime gittiği sorusunun cevabı kayıtta kalmalıdır.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>ERP turu bir alıcıyı yeniden bildirdiğinde künyesini tazeler.</summary>
    public void Update(string? fullName, string? role, string? externalId)
    {
        FullName = Kirp(fullName);
        Role = Kirp(role);
        ExternalId = Kirp(externalId) ?? ExternalId;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Kaynağını değiştirir.
    ///
    /// <para>
    /// Elle tanımlanmış bir alıcı ERP'den de gelirse kayıt ERP'ye <b>devredilir</b>:
    /// tek bir doğruluk kaynağı kalır ve ERP'de silindiğinde burada da pasifleşir.
    /// </para>
    /// </summary>
    public void ChangeSource(RecipientSource source) => Source = source;

    /// <summary>
    /// Adres doğrulaması bilerek <b>dar</b>: tek <c>@</c>, iki yanı dolu, boşluksuz ve
    /// alan adında en az bir nokta. Geniş bir kalıp geçersiz adresleri kabul eder ve
    /// hata ancak SMTP sunucusunda, gönderim anında ortaya çıkardı.
    /// </summary>
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var temiz = email.Trim();

        if (temiz.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var parcalar = temiz.Split('@');

        return parcalar.Length == 2
               && parcalar[0].Length > 0
               && parcalar[1].Length > 2
               && parcalar[1].Contains('.', StringComparison.Ordinal)
               && !parcalar[1].StartsWith('.')
               && !parcalar[1].EndsWith('.');
    }

    /// <summary>Türkçe büyük/küçük harf tuzağına düşmemek için değişmez kültür kullanılır.</summary>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static string? Kirp(string? metin) =>
        string.IsNullOrWhiteSpace(metin) ? null : metin.Trim();
}
