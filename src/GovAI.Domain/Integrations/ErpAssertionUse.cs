using GovAI.Domain.Common;

namespace GovAI.Domain.Integrations;

/// <summary>
/// Kullanılmış bir beyanın kaydı (tekrar önleme).
///
/// <para>
/// Kayıt <b>veritabanındadır</b>, önbellekte değil. Önbellekte olsaydı, Redis kapalı ya
/// da erişilemez olduğunda tekrar koruması sessizce kalkardı — güvenlik kontrolünün en
/// kötü başarısızlık biçimi budur: hata vermeden devre dışı kalmak.
/// </para>
///
/// <para>
/// Atomiklik benzersiz indeksten gelir: aynı <c>(ClientId, TokenId)</c> ikinci kez
/// eklenemez. "Önce bak, sonra yaz" yaklaşımı iki eşzamanlı isteğin aradaki boşlukta
/// ikisinin de geçmesine izin verirdi.
/// </para>
///
/// <para>
/// Kiracıya bağlı <b>değildir</b>: bu kayıt müşteri verisi değil, kimlik doğrulamanın
/// iç izidir ve jeton ucu oturumsuz çalıştığı için kiracı süzgecine takılmamalıdır.
/// </para>
/// </summary>
public class ErpAssertionUse : Entity
{
    private ErpAssertionUse()
    {
    }

    public ErpAssertionUse(string clientId, string tokenId, DateTimeOffset usedAt, DateTimeOffset expiresAt)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(clientId), "İstemci kimliği zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(tokenId), "Beyan kimliği zorunludur.");

        ClientId = clientId;
        TokenId = tokenId;
        UsedAt = usedAt;
        ExpiresAt = expiresAt;
    }

    public string ClientId { get; private set; } = string.Empty;

    /// <summary>Beyanın <c>jti</c> alanı.</summary>
    public string TokenId { get; private set; } = string.Empty;

    public DateTimeOffset UsedAt { get; private set; }

    /// <summary>Bu andan sonra kayıt gereksizdir; beyan zaten süresi dolmuş sayılır.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }
}
