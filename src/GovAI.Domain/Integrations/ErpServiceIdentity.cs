using GovAI.Domain.Common;

namespace GovAI.Domain.Integrations;

/// <summary>
/// Beyanı imzalayan anahtarın algoritması.
///
/// <para>
/// Yalnızca <b>asimetrik</b> algoritmalar vardır ve bu liste genişletilirken dikkat
/// edilmelidir: HMAC eklenirse GOVAI'nin sakladığı "açık" anahtar aynı zamanda imzalama
/// anahtarı olur ve saklanan değeri okuyabilen herkes geçerli beyan üretebilir.
/// Asimetrikte GOVAI yalnızca doğrulayabilir, imzalayamaz.
/// </para>
/// </summary>
public enum ErpSigningAlgorithm
{
    /// <summary>ECDSA P-256 + SHA-256. Tercih edilen: anahtar kısa, imza küçük.</summary>
    ES256 = 1,

    /// <summary>RSASSA-PKCS1-v1_5 + SHA-256. Eski ERP yığınlarıyla uyum için.</summary>
    RS256 = 2
}

/// <summary>
/// Bir servis kimliğinin açık anahtarı.
///
/// <para>
/// Bir kimliğin <b>birden çok</b> etkin anahtarı olabilir. Sebebi anahtar
/// değiştirmedir: yeni anahtar eklenip ERP ona geçtikten sonra eskisi iptal edilir.
/// Tek anahtar zorunlu olsaydı her değişimde entegrasyon kesintiye girerdi ve bu,
/// anahtarların hiç değiştirilmemesine yol açardı.
/// </para>
/// </summary>
public class ErpSigningKey : Entity, ITenantScoped
{
    private ErpSigningKey()
    {
    }

    public ErpSigningKey(
        Guid tenantId,
        Guid identityId,
        string keyId,
        ErpSigningAlgorithm algorithm,
        string publicKeyPem,
        DateTimeOffset addedAt)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(keyId), "Anahtar kimliği (kid) zorunludur.");
        DomainException.ThrowIf(
            string.IsNullOrWhiteSpace(publicKeyPem), "Açık anahtar zorunludur.");

        // Özel anahtarın yanlışlıkla yapıştırılması en olası ve en pahalı hatadır;
        // kabul edilirse GOVAI veritabanında ERP'nin imzalama anahtarı durur.
        DomainException.ThrowIf(
            publicKeyPem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase),
            "Buraya ÖZEL anahtar yapıştırılamaz; yalnızca açık anahtar (PUBLIC KEY) kabul edilir.");

        DomainException.ThrowIf(
            !publicKeyPem.Contains("BEGIN PUBLIC KEY", StringComparison.Ordinal),
            "Açık anahtar PEM biçiminde olmalıdır (BEGIN PUBLIC KEY).");

        TenantId = tenantId;
        ErpServiceIdentityId = identityId;
        KeyId = keyId.Trim();
        Algorithm = algorithm;
        PublicKeyPem = publicKeyPem.Trim();
        AddedAt = addedAt;
    }

    public Guid TenantId { get; set; }

    public Guid ErpServiceIdentityId { get; private set; }

    /// <summary>Beyanın başlığındaki <c>kid</c>. Hangi anahtarla doğrulanacağını söyler.</summary>
    public string KeyId { get; private set; } = string.Empty;

    public ErpSigningAlgorithm Algorithm { get; private set; }

    public string PublicKeyPem { get; private set; } = string.Empty;

    public DateTimeOffset AddedAt { get; private set; }

    /// <summary>İptal edilen anahtar silinmez: hangi anahtarın ne zaman kullanıldığı kayıtta kalır.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;
}

/// <summary>
/// Bir şirketin ERP'sinin GOVAI'ye tanıttığı servis kimliği.
///
/// <para>
/// <b>Düz makine anahtarı değildir.</b> GOVAI hiçbir sır saklamaz; yalnızca ERP'nin
/// <b>açık</b> anahtarını tutar. ERP her istekte kısa ömürlü, imzalı bir beyan üretir ve
/// onu jetona çevirir. Paylaşılan bir gizli anahtar olsaydı: veritabanı yedeğini,
/// logları ya da yapılandırmayı okuyabilen herkes o şirket adına konuşabilirdi ve
/// sızıntı fark edilmeden süresiz kullanılabilirdi.
/// </para>
///
/// <para>
/// Kimlik <b>tek bir şirkete</b> bağlıdır. Kiracı ve şirket her zaman <b>bu kayıttan</b>
/// okunur, beyanın gövdesinden değil — beyandan okunsaydı ERP kendi beyanına başka bir
/// şirketin kimliğini yazıp o şirketin verisini isteyebilirdi.
/// </para>
/// </summary>
public class ErpServiceIdentity : AggregateRoot, IAuditable, ITenantScoped
{
    private readonly List<ErpSigningKey> _keys = [];

    /// <summary>Beyanın azami ömrü. Uzun ömürlü beyan, çalınınca uzun süre kullanılır.</summary>
    public const int MaximumAssertionLifetimeSeconds = 300;

    /// <summary>Saat kayması payı. Geniş tutmak beyanın ömrünü fiilen uzatır.</summary>
    public const int ClockSkewSeconds = 60;

    private ErpServiceIdentity()
    {
    }

    public ErpServiceIdentity(
        Guid tenantId,
        Guid companyId,
        string clientId,
        string displayName,
        DateTimeOffset createdAt)
    {
        DomainException.ThrowIf(companyId == Guid.Empty, "Firma zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(clientId), "İstemci kimliği zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(displayName), "Tanım adı zorunludur.");

        TenantId = tenantId;
        CompanyId = companyId;
        ClientId = clientId.Trim();
        DisplayName = displayName.Trim();
        CreatedAt = createdAt;
        IsEnabled = true;
    }

    public Guid TenantId { get; set; }

    /// <summary>Kimliğin konuşabileceği <b>tek</b> şirket.</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>
    /// Beyanın <c>iss</c> alanı. Gizli değildir ve gizli olmasına gerek yoktur:
    /// tek başına hiçbir şeye yetmez, imza olmadan işe yaramaz.
    /// </summary>
    public string ClientId { get; private set; } = string.Empty;

    /// <summary>Operatöre görünen ad (ör. "IKPROF — test.ikprof.com").</summary>
    public string DisplayName { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    /// <summary>Son başarılı jeton alışı. Kullanılmayan kimliği fark etmeye yarar.</summary>
    public DateTimeOffset? LastTokenIssuedAt { get; private set; }

    public IReadOnlyCollection<ErpSigningKey> Keys => _keys.AsReadOnly();

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public IEnumerable<ErpSigningKey> ActiveKeys => _keys.Where(k => k.IsActive);

    public void AddKey(ErpSigningKey key)
    {
        DomainException.ThrowIf(
            _keys.Any(k => k.KeyId == key.KeyId && k.IsActive),
            $"Bu anahtar kimliği (kid) zaten etkin: {key.KeyId}");

        _keys.Add(key);
    }

    /// <summary>
    /// Anahtarı iptal eder.
    ///
    /// <para>
    /// Son etkin anahtar iptal edilebilir ve bu bilinçlidir: sızıntı şüphesinde
    /// entegrasyonu durdurmak, çalışır tutmaktan önemlidir. Kimlik anahtarsız kalır ve
    /// hiçbir beyanı doğrulanamaz.
    /// </para>
    /// </summary>
    public void RevokeKey(string keyId, DateTimeOffset at)
    {
        var anahtar = _keys.FirstOrDefault(k => k.KeyId == keyId && k.IsActive)
            ?? throw new DomainException($"Etkin anahtar bulunamadı: {keyId}");

        anahtar.Revoke(at);
    }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;

    public void RecordTokenIssued(DateTimeOffset at) => LastTokenIssuedAt = at;
}
