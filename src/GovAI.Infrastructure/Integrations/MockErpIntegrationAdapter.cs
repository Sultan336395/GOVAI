using System.Collections.Concurrent;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Integrations;
using Microsoft.Extensions.Logging;

namespace GovAI.Infrastructure.Integrations;

/// <summary>
/// ERP entegrasyonunun sahte uyarlayıcısı (Faz 3).
///
/// <para>
/// IKPROF'un API bilgileri henüz gelmedi. Gerçek bağlantı kurulana kadar liman
/// (<see cref="IErpIntegrationPort"/>) bu uyarlayıcıyla doldurulur: sözleşme, doğrulama,
/// idempotency ve kiracı yalıtımı uçtan uca çalıştırılabilir ve test edilebilir hâle
/// gelir. Bağlantı geldiğinde yalnızca bu sınıfın yerine gerçek uyarlayıcı konur;
/// uygulama katmanı değişmez.
/// </para>
///
/// <para>
/// <see cref="IsLive"/> <b>false</b> döner ve bu bilinçlidir: kurulum "bağlantı kuruldu"
/// diye raporlanamaz. Sahteyi gerçek gibi göstermek, entegrasyonun çalıştığı sanılarak
/// canlıya alınmasına yol açar.
/// </para>
/// </summary>
public sealed class MockErpIntegrationAdapter(
    IDateTimeProvider clock,
    ILogger<MockErpIntegrationAdapter> logger) : IErpIntegrationPort
{
    /// <summary>Idempotency anahtarının hatırlanma süresi.</summary>
    public static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, SnapshotResult Result)> _seen = new();

    /// <summary>ERP kimliği → GOVAI şirketi eşlemesi. Gerçek uyarlayıcıda veritabanından gelir.</summary>
    private readonly ConcurrentDictionary<string, (Guid TenantId, Guid CompanyId, string TaxNumber)> _links = new();

    public string PartnerName => "IKPROF";

    /// <summary>Sahte uyarlayıcı asla canlı değildir.</summary>
    public bool IsLive => false;

    /// <summary>Testin ve panelin eşleme kurabilmesi için.</summary>
    public void Link(string externalCompanyId, Guid tenantId, Guid companyId, string taxNumber) =>
        _links[externalCompanyId] = (tenantId, companyId, taxNumber);

    public Task<SnapshotResult> ApplyCompanySnapshotAsync(
        string idempotencyKey,
        CompanySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Task.FromResult(new SnapshotResult(
                SnapshotOutcome.Rejected, null, "Idempotency-Key başlığı zorunludur."));
        }

        var now = clock.UtcNow;

        // Aynı anahtar ikinci kez gelirse İLK sonucun kopyası döner. Kuyruk yeniden
        // deneme yaptığında mükerrer kayıt oluşmamalıdır.
        if (_seen.TryGetValue(idempotencyKey, out var onceki) && now - onceki.At < IdempotencyWindow)
        {
            return Task.FromResult(onceki.Result with
            {
                Outcome = SnapshotOutcome.Duplicate,
                Message = "Bu istek daha önce işlendi; ikinci kayıt açılmadı.",
            });
        }

        var dogrulama = CompanySnapshotValidator.Validate(snapshot);

        if (!dogrulama.IsValid)
        {
            // Reddedilen istek idempotency defterine YAZILMAZ: gönderen düzeltip
            // aynı anahtarla tekrar denerse çalışmalıdır.
            return Task.FromResult(new SnapshotResult(SnapshotOutcome.Rejected, null, dogrulama.Reason!));
        }

        if (!_links.TryGetValue(snapshot.ExternalCompanyId, out var link))
        {
            // Eşleme yoksa şirket SESSİZCE OLUŞTURULMAZ: ERP'nin gönderdiği kimlikle
            // kiracıda yeni tüzel kişilik açmak, yanlış kiracıya veri yazmanın en kolay yolu.
            return Task.FromResult(new SnapshotResult(
                SnapshotOutcome.LinkNotFound,
                null,
                $"'{snapshot.ExternalCompanyId}' bir GOVAI şirketine bağlı değil. Eşleme "
                + "panelden kurulmalıdır; istek şirket oluşturmaz."));
        }

        if (!string.Equals(link.TaxNumber, snapshot.TaxNumber.Trim(), StringComparison.Ordinal))
        {
            // Yanlış eşleme, bir firmanın verisini bir başkasının kaydına taşır.
            return Task.FromResult(new SnapshotResult(
                SnapshotOutcome.Rejected,
                null,
                "Vergi numarası eşlemedeki şirketle tutmuyor; istek reddedildi."));
        }

        var sonuc = new SnapshotResult(
            SnapshotOutcome.Applied, link.CompanyId, "Şirket profili güncellendi (sahte uyarlayıcı).");

        _seen[idempotencyKey] = (now, sonuc);

        // Sır ve kişisel veri LOGLANMAZ: yalnızca hangi eşlemenin işlendiği yazılır.
        logger.LogInformation(
            "ERP anlık görüntüsü uygulandı. Ortak={Partner} Eşleme={External} Şirket={CompanyId}",
            PartnerName, snapshot.ExternalCompanyId, link.CompanyId);

        return Task.FromResult(sonuc);
    }

    /// <summary>
    /// Sahte uyarlayıcı sinyal üretmez. Boş liste dönmek, uydurma sinyal üretmekten
    /// iyidir: ERP tarafı "veri yok" ile "veri yanlış" arasında ayrım yapabilmelidir.
    /// </summary>
    public Task<IReadOnlyList<IntegrationSignal>> ListSignalsAsync(
        Guid companyId,
        DateTimeOffset? since,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IntegrationSignal>>([]);
}
