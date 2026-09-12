using GovAI.Domain.Common;

namespace GovAI.Domain.Integrations;

/// <summary>
/// ERP'den çekilmiş tek bir süreç olayı.
///
/// <para>
/// <b>Ne işe yarar:</b> süreç madenciliğinin ham maddesidir. Profil anlık görüntüsü
/// (ciro, personel sayısı) firmanın <i>durumunu</i> söyler; olay günlüğü <i>nasıl
/// çalıştığını</i> söyler — hangi iş hangi sırayla, ne kadar sürede, hangi birimde
/// yapılıyor. Mevzuat değişikliğinin hangi süreci vuracağı ancak bu veriyle
/// hesaplanabilir. Bu kayıt olmadan nedensel ikiz (Regulatory Causal Twin) bir
/// algoritma sorunu değil, bir <b>veri yokluğu</b> sorunudur.
/// </para>
///
/// <para>
/// <b>Bu sınıf nedensel çıkarım YAPMAZ.</b> Yalnızca veriyi toplar ve saklar. Çıkarım
/// ayrı bir iştir ve yeterli gözlem birikmeden başlatılmamalıdır; birkaç haftalık
/// günlükle üretilen "etki" tahmini istatistik değil gürültüdür.
/// </para>
///
/// <para>
/// <b>Mahremiyet kararı:</b> <see cref="Resource"/> alanında <b>kişi adı tutulmaz</b>,
/// yalnızca ERP'nin kendi opak kodu tutulur. Süreç madenciliği için "aynı kaynak mı"
/// bilgisi yeterlidir; kimin yaptığı gerekmez. Ad tutmak, uyum analizi için toplanan
/// veriyi çalışan izlemeye çevirirdi — GOVAI'nin işi bu değildir ve KVKK açısından
/// ayrı bir hukuki dayanak gerektirirdi.
/// </para>
/// </summary>
public class ErpProcessEvent : Entity, ITenantScoped
{
    private ErpProcessEvent()
    {
    }

    public ErpProcessEvent(
        Guid tenantId,
        Guid companyId,
        string caseId,
        string activity,
        DateTimeOffset occurredAt,
        string? resource = null,
        string? department = null,
        string? externalId = null)
    {
        DomainException.ThrowIf(companyId == Guid.Empty, "Firma zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(caseId), "Vaka kimliği zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(activity), "Faaliyet adı zorunludur.");

        TenantId = tenantId;
        CompanyId = companyId;
        CaseId = Kirp(caseId)!;
        Activity = Kirp(activity)!;
        OccurredAt = occurredAt;
        Resource = Kirp(resource);
        Department = Kirp(department);
        ExternalId = Kirp(externalId);
    }

    public Guid TenantId { get; set; }

    public Guid CompanyId { get; private set; }

    /// <summary>
    /// Vaka kimliği: aynı süreç örneğine ait olayları birbirine bağlar (sipariş no,
    /// fatura no, işe alım talebi no). Süreç madenciliğinin üç zorunlu alanından biridir.
    /// </summary>
    public string CaseId { get; private set; } = string.Empty;

    /// <summary>Yapılan iş ("Teklif onaylandı", "Fatura kesildi"). İkinci zorunlu alan.</summary>
    public string Activity { get; private set; } = string.Empty;

    /// <summary>Olayın gerçekleştiği an. Üçüncü zorunlu alan; sıralamayı bu belirler.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// İşi yapan kaynağın ERP'deki <b>opak kodu</b>. Kişi adı DEĞİLDİR (bkz. sınıf notu).
    /// </summary>
    public string? Resource { get; private set; }

    /// <summary>Olayın geçtiği birim; mevzuat etkisinin hangi departmana düştüğünü gösterir.</summary>
    public string? Department { get; private set; }

    /// <summary>
    /// ERP'deki kayıt kimliği. Mükerrer çekmeyi önler: aynı olay iki turda gelse de
    /// tek satır kalır. Boşsa tekilleştirme vaka+faaliyet+zaman üçlüsüne düşer.
    /// </summary>
    public string? ExternalId { get; private set; }

    /// <summary>
    /// Aynı olayın iki kez kaydedilmesini önleyen anahtar.
    ///
    /// <para>
    /// ERP kimliği varsa o kullanılır; yoksa vaka + faaliyet + zaman üçlüsü. İkincisi
    /// kusursuz değildir (aynı saniyede aynı işin iki kez yapılması ayırt edilemez) ama
    /// alternatifi her turda günlüğü yeniden yazmaktır ve o, ölçümü tamamen bozar.
    /// </para>
    /// </summary>
    public string DeduplicationKey => ExternalId is { Length: > 0 }
        ? $"id:{ExternalId}"
        : $"vaka:{CaseId}|{Activity}|{OccurredAt.UtcDateTime:O}";

    private static string? Kirp(string? deger) =>
        string.IsNullOrWhiteSpace(deger) ? null : deger.Trim();
}
