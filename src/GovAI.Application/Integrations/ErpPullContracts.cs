using GovAI.Domain.Integrations;

namespace GovAI.Application.Integrations;

/// <summary>
/// ERP'den okunan ham değerler.
///
/// <para>
/// Her alan <b>nullable</b>'dır ve bu bilinçlidir: ERP'de bulunamayan alan <c>null</c>
/// kalır, sıfır yazılmaz. Sıfır yazmak firmayı "hiç kadın çalışanı yok" diye kaydetmek
/// olurdu ve ürünün üçüncü iddiasını (eksik veri elemez) ERP yolunda çiğnerdi.
/// </para>
/// </summary>
public sealed record ErpSnapshot
{
    public decimal? AnnualRevenue { get; init; }

    public decimal? BalanceSize { get; init; }

    public decimal? Equity { get; init; }

    public decimal? ExportRevenue { get; init; }

    public int? EmployeeCount { get; init; }

    public int? WomenEmployeeCount { get; init; }

    public int? YoungEmployeeCount { get; init; }

    public int? RAndDEmployeeCount { get; init; }

    public int? DisabledEmployeeCount { get; init; }

    public int? YoungEmployeeMaxAge { get; init; }

    /// <summary>ERP'de kayıtlı belge/sertifikalar.</summary>
    public IReadOnlyList<ErpCertificate> Certificates { get; init; } = [];

    /// <summary>Eşlemede aranıp ERP yanıtında <b>bulunamayan</b> alanlar; kullanıcıya gösterilir.</summary>
    public IReadOnlyList<string> MissingFields { get; init; } = [];

    /// <summary>Hiçbir alan okunamadıysa profile dokunulmaz.</summary>
    public bool IsEmpty =>
        AnnualRevenue is null && BalanceSize is null && Equity is null && ExportRevenue is null
        && EmployeeCount is null && Certificates.Count == 0;
}

public sealed record ErpCertificate(string Code, DateOnly? ValidUntil);

/// <summary>
/// ERP'den veri okuyan liman.
///
/// <para>
/// Uygulama katmanı HTTP'yi tanımaz; uyarlayıcı altyapıdadır. Böylece çekme mantığı
/// gerçek bir ERP olmadan da test edilebilir.
/// </para>
/// </summary>
public interface IErpDataSource
{
    /// <summary>
    /// Bağlantıdan anlık görüntü okur.
    ///
    /// <para>
    /// Ağ, kimlik ya da biçim hatasında <see cref="ErpFetchException"/> atar. Hata
    /// mesajı <b>ayıklanmıştır</b>: ERP'nin döndürdüğü gövde kimlik bilgisi veya personel
    /// verisi içerebilir ve bu mesaj panelde görünür.
    /// </para>
    /// </summary>
    Task<ErpSnapshot> FetchAsync(ErpConnection connection, CancellationToken cancellationToken = default);
}

/// <summary>ERP'den veri okunamadı. Mesajı kullanıcıya gösterilebilir olmalıdır.</summary>
public sealed class ErpFetchException(string message) : Exception(message);

/// <summary>Kimlik bilgisini geri çevrilebilir biçimde şifreler.</summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}
