using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Application.Notifications;
using GovAI.Application.Companies;
using GovAI.Domain.Common;
using GovAI.Domain.Integrations;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Integrations;

public sealed record ErpPullResultDto(
    Guid CompanyId,
    ErpSyncStatus Status,
    /// <summary>Güncellenen profil bölümleri; boşsa profil değişmedi.</summary>
    IReadOnlyList<string> UpdatedSections,
    /// <summary>Eşlemede aranıp ERP yanıtında bulunamayan alanlar.</summary>
    IReadOnlyList<string> MissingFields,
    string Message);

public sealed record ErpPullBatchResultDto(
    int ConnectionCount,
    int SucceededCount,
    int NoChangeCount,
    int FailedCount);

/// <summary>
/// Firmanın ERP'sinden profil verisini <b>çeker</b>.
///
/// <para>
/// Çekilen veri doğrudan kaydedilmez: mevcut <c>SyncFromErpAsync</c> hattından geçer.
/// Böylece doğrulama, sektör–NACE tutarlılığı, profil sürümleme, denetim kaydı ve
/// yeniden skorlama tetiklemesi tek yerde kalır. İkinci bir yazma yolu açmak, bu
/// kontrollerin birinin ERP yolunda atlanması demek olurdu.
/// </para>
///
/// <para>
/// ERP'de bulunamayan alan <b>eksik</b> sayılır, sıfır yazılmaz. Aksi hâlde entegrasyon
/// firmayı "hiç kadın çalışanı yok" diye kaydeder ve ürünün üçüncü iddiası ERP yolunda
/// çiğnenirdi (bkz. <c>docs/adr/0003</c>).
/// </para>
/// </summary>
public sealed class ErpPullService(
    IErpConnectionRepository connections,
    ICompanyRepository companies,
    INotificationRecipientRepository recipients,
    CompanyProfileService profiles,
    IErpDataSource dataSource,
    IUnitOfWork unitOfWork,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    ILogger<ErpPullService> logger)
{
    /// <summary>Tek bir firmanın ERP'sinden veri çeker.</summary>
    public async Task<ErpPullResultDto> PullCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        // Profil değiştirme yetkisi aranır: bu çağrı firmanın kayıtlı verisini günceller.
        await access.LoadAccessibleAsync(companyId, CompanyPermission.ManageProfile, cancellationToken);

        var connection = await connections.GetByCompanyAsync(companyId, cancellationToken)
            ?? throw new NotFoundException("ERP bağlantısı", companyId);

        var sonuc = await PullAsync(connection, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return sonuc;
    }

    /// <summary>
    /// Kiracıdaki bütün etkin bağlantıları çeker; gece turu bunu çağırır.
    ///
    /// <para>
    /// Bir bağlantının hatası turu durdurmaz. Aksi hâlde tek bir erişilemeyen ERP, o gece
    /// hiçbir firmanın profilinin güncellenmemesine yol açardı.
    /// </para>
    /// </summary>
    public async Task<ErpPullBatchResultDto> PullTenantAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();
        var hepsi = await connections.ListEnabledAsync(cancellationToken);

        var basarili = 0;
        var degismeyen = 0;
        var hatali = 0;

        foreach (var connection in hepsi)
        {
            try
            {
                var sonuc = await PullAsync(connection, cancellationToken);

                switch (sonuc.Status)
                {
                    case ErpSyncStatus.Succeeded: basarili++; break;
                    case ErpSyncStatus.NoChange: degismeyen++; break;
                    default: hatali++; break;
                }
            }
            catch (Exception exception)
            {
                hatali++;
                connection.RecordFailure(clock.UtcNow, "Beklenmeyen hata; kayıtlara bakın.");

                logger.LogError(
                    exception,
                    "ERP çekme turunda bağlantı atlandı. TenantId={TenantId} CompanyId={CompanyId}",
                    tenantId, connection.CompanyId);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "ERP çekme turu bitti. TenantId={TenantId} Bağlantı={Count} Başarılı={Ok} " +
            "Değişmeyen={NoChange} Hatalı={Failed}",
            tenantId, hepsi.Count, basarili, degismeyen, hatali);

        return new ErpPullBatchResultDto(hepsi.Count, basarili, degismeyen, hatali);
    }

    /// <summary>
    /// ERP'de tanımlı bildirim sorumlularını mevcut kayıtlarla uzlaştırır.
    ///
    /// <para>
    /// Alıcılar profil hattından (<c>SyncFromErpAsync</c>) geçmez: profil kural
    /// motorunun okuduğu veridir ve sürümlenir, alıcı listesi ise skoru hiç
    /// etkilemeyen bir iletişim ayarıdır. Birleştirilseydi sorumlu değişikliği
    /// profil sürümünü artırır ve gereksiz yere bütün çağrıları yeniden skorlatırdı.
    /// </para>
    /// </summary>
    private async Task<RecipientSyncResult> AliciarıUzlastirAsync(
        ErpConnection connection,
        ErpSnapshot goruntu,
        CancellationToken cancellationToken)
    {
        if (goruntu.NotificationRecipients is null)
        {
            return RecipientSyncResult.None;
        }

        var mevcut = await recipients.ListForCompanyAsync(connection.CompanyId, cancellationToken);

        var sonuc = NotificationRecipientSync.Reconcile(
            connection.TenantId,
            connection.CompanyId,
            mevcut,
            goruntu.NotificationRecipients
                .Select(a => new IncomingRecipient(a.Email, a.FullName, a.Role, a.ExternalId))
                .ToList());

        foreach (var yeni in sonuc.Added)
        {
            await recipients.AddAsync(yeni, cancellationToken);
        }

        if (sonuc.Changed || sonuc.Invalid.Count > 0)
        {
            logger.LogInformation(
                "ERP bildirim sorumluları uzlaştırıldı. CompanyId={CompanyId} Eklenen={Added} "
                + "YenidenEtkin={Reactivated} Pasifleşen={Deactivated} Geçersiz={Invalid}",
                connection.CompanyId, sonuc.Added.Count, sonuc.Reactivated, sonuc.Deactivated,
                sonuc.Invalid.Count);
        }

        return sonuc;
    }

    private async Task<ErpPullResultDto> PullAsync(
        ErpConnection connection,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        ErpSnapshot goruntu;

        try
        {
            goruntu = await dataSource.FetchAsync(connection, cancellationToken);
        }
        catch (ErpFetchException exception)
        {
            // Mesaj uyarlayıcıda zaten ayıklandı; gövde ya da kimlik bilgisi taşımaz.
            connection.RecordFailure(now, exception.Message);

            logger.LogWarning(
                "ERP'den veri okunamadı. CompanyId={CompanyId} Ürün={Vendor}",
                connection.CompanyId, connection.Vendor);

            return new ErpPullResultDto(
                connection.CompanyId, ErpSyncStatus.Failed, [], [], exception.Message);
        }

        // Bildirim sorumluları profilden AYRI uzlaştırılır ve profil verisi boş olsa
        // da işlenir: ERP'sinde yalnızca sorumlu listesi tanımlı bir firmada, profil
        // alanları bulunamadı diye sorumlular da atlanırsa firma bildirimsiz kalırdı.
        var aliciSonucu = await AliciarıUzlastirAsync(connection, goruntu, cancellationToken);

        if (goruntu.IsEmpty)
        {
            // ERP ulaşıldı ama eşlemedeki alanların hiçbiri bulunamadı. Bu bir arıza
            // olabilir de olmayabilir de; profil DEĞİŞTİRİLMEZ ve durum açıkça yazılır.
            var mesaj = goruntu.MissingFields.Count > 0
                ? $"ERP yanıtında beklenen alanlar bulunamadı: {string.Join(", ", goruntu.MissingFields)}. " +
                  "Alan eşlemesini kontrol edin."
                : "ERP yanıtında profil verisi bulunamadı.";

            connection.RecordFailure(now, mesaj);

            return new ErpPullResultDto(
                connection.CompanyId, ErpSyncStatus.Failed, [], goruntu.MissingFields, mesaj);
        }

        var company = await companies.GetWithDetailsAsync(connection.CompanyId, cancellationToken)
            ?? throw new NotFoundException("Firma", connection.CompanyId);

        var istek = ToSyncRequest(company.TaxNumber, connection.Vendor, goruntu, company);

        var sonuc = await profiles.SyncFromErpAsync(istek, cancellationToken);

        if (sonuc.UpdatedSections.Count == 0)
        {
            connection.RecordNoChange(now);

            return new ErpPullResultDto(
                connection.CompanyId, ErpSyncStatus.NoChange, [], goruntu.MissingFields,
                "ERP'den gelen veri mevcut profille aynı.");
        }

        var basariMesaji = goruntu.MissingFields.Count == 0
            ? $"Güncellenen bölümler: {string.Join(", ", sonuc.UpdatedSections)}."
            : $"Güncellenen bölümler: {string.Join(", ", sonuc.UpdatedSections)}. " +
              $"ERP'de bulunamayan alanlar: {string.Join(", ", goruntu.MissingFields)}.";

        connection.RecordSuccess(now, basariMesaji);

        logger.LogInformation(
            "ERP'den profil güncellendi. CompanyId={CompanyId} Bölümler={Sections}",
            connection.CompanyId, string.Join(",", sonuc.UpdatedSections));

        return new ErpPullResultDto(
            connection.CompanyId, ErpSyncStatus.Succeeded, sonuc.UpdatedSections,
            goruntu.MissingFields, basariMesaji);
    }

    /// <summary>
    /// Anlık görüntüyü mevcut eşitleme isteğine çevirir.
    ///
    /// <para>
    /// <b>Yalnızca ERP'den gelen alanlar gönderilir.</b> Bir bölümün tamamı boşsa o bölüm
    /// <c>null</c> bırakılır ve mevcut profil değeri korunur: ERP bordro modülü kullanmayan
    /// bir firmada personel verisi gelmez ve elle girilmiş doğru veri ERP yüzünden
    /// silinemez.
    /// </para>
    /// </summary>
    private static ErpSyncRequest ToSyncRequest(
        string taxNumber,
        ErpVendor vendor,
        ErpSnapshot goruntu,
        Domain.Companies.Company mevcut)
    {
        var personelVar = goruntu.EmployeeCount is not null
            || goruntu.WomenEmployeeCount is not null
            || goruntu.YoungEmployeeCount is not null
            || goruntu.RAndDEmployeeCount is not null
            || goruntu.DisabledEmployeeCount is not null;

        var maliVar = goruntu.AnnualRevenue is not null
            || goruntu.BalanceSize is not null
            || goruntu.Equity is not null
            || goruntu.ExportRevenue is not null;

        return new ErpSyncRequest
        {
            TaxNumber = taxNumber,
            SourceSystem = vendor.ToString(),

            // Eksik gelen alt alanlar mevcut değerden tamamlanır; ERP'nin bilmediği bir
            // sayı, kayıtlı doğru sayıyı sıfırlamamalıdır.
            Workforce = personelVar
                ? new WorkforceDto(
                    goruntu.EmployeeCount ?? mevcut.Workforce.EmployeeCount,
                    goruntu.WomenEmployeeCount ?? mevcut.Workforce.WomenEmployeeCount,
                    goruntu.YoungEmployeeCount ?? mevcut.Workforce.YoungEmployeeCount,
                    goruntu.RAndDEmployeeCount ?? mevcut.Workforce.RAndDEmployeeCount,
                    goruntu.DisabledEmployeeCount ?? mevcut.Workforce.DisabledEmployeeCount)
                : null,

            Financials = maliVar
                ? new FinancialsDto(
                    goruntu.AnnualRevenue ?? mevcut.Financials.AnnualRevenue,
                    goruntu.BalanceSize ?? mevcut.Financials.BalanceSize,
                    goruntu.Equity ?? mevcut.Financials.Equity,
                    goruntu.ExportRevenue ?? mevcut.Financials.ExportRevenue,
                    mevcut.Financials.Currency,
                    mevcut.Financials.FiscalYear)
                : null,

            Certificates = goruntu.Certificates.Count > 0
                ? goruntu.Certificates
                    // ERP çoğu zaman yalnızca kodu ve geçerliliği tutar; ad alanı koddan
                    // türetilir ve belge adresi uydurulmaz.
                    .Select(c => new CertificateDto(c.Code, c.Code, null, c.ValidUntil, null))
                    .ToList()
                : null,
        };
    }
}
