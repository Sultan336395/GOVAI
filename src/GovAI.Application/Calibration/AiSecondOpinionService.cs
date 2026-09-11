using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Calibration;
using GovAI.Domain.Common;
using GovAI.Domain.Eligibility;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Calibration;

public sealed record AiSecondOpinionResultDto(
    int ExaminedCount,
    int RecordedCount,
    int AgreedCount,
    int DisagreedCount,
    int SkippedCount,
    /// <summary>Yapay zekâ devre dışıysa <c>false</c>; hiçbir görüş üretilmez.</summary>
    bool AiEnabled);

/// <summary>
/// Kural motorunun kararlarına <b>bağımsız ikinci görüş</b> toplar.
///
/// <para>
/// İkinci görüşü yapay zekâ verir ve bu görüş <b>hiçbir skoru değiştirmez</b>. İşi
/// taramadır: iki taraf ayrı yollardan aynı sonuca varıyorsa kayıt büyük olasılıkla
/// doğrudur, ayrılıyorsa insan bakmalıdır. Böylece danışman otuz kaydı tek tek okumak
/// yerine yalnızca ayrışan birkaçına bakar.
/// </para>
///
/// <para>
/// <b>Yapay zekâ görüşü ağırlık kalibrasyonunun ölçütü değildir.</b> Ağırlıkları modelin
/// görüşüne göre ayarlamak, sistemi gerçeğe değil modelin eğilimine kalibre etmek olurdu;
/// üstelik iki taraf da aynı metni okuduğu için aynı yanlışı birlikte yapabilirler.
/// Kalibrasyonun ölçütü insan görüşüdür ve rapor ikisini ayrı gösterir.
/// </para>
/// </summary>
public sealed class AiSecondOpinionService(
    IExpertVerdictRepository verdicts,
    IAssessmentRepository assessments,
    ICompanyRepository companies,
    IOpportunityRepository opportunities,
    IUnitOfWork unitOfWork,
    IAiExplanationClient ai,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    ILogger<AiSecondOpinionService> logger)
{
    /// <summary>
    /// Tek turda incelenecek azami değerlendirme sayısı.
    ///
    /// <para>
    /// Her görüş bir model çağrısıdır. Sınırsız bir tur, tek gecede binlerce çağrı
    /// üretip hem maliyeti hem hız sınırını patlatırdı. Kalanlar sonraki turda alınır;
    /// ölçüm zaten zamana yayılarak birikir.
    /// </para>
    /// </summary>
    private const int MaximumPerRun = 25;

    /// <summary>Bir firmanın görüşü olmayan değerlendirmeleri için ikinci görüş toplar.</summary>
    public async Task<AiSecondOpinionResultDto> ReviewCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var company = await access.LoadAccessibleAsync(companyId, CompanyPermission.Operate, cancellationToken);

        return await ReviewAsync(company.Id, company.LegalName, access.RequireTenant(), cancellationToken);
    }

    /// <summary>
    /// Kiracıdaki bütün firmalar için ikinci görüş toplar; gece turu bunu çağırır.
    ///
    /// <para>
    /// Bir firmanın hatası turu durdurmaz: tek bozuk profil yüzünden o gece hiçbir
    /// firmanın taranmaması, taramanın kendisini işlevsiz bırakırdı.
    /// </para>
    /// </summary>
    public async Task<AiSecondOpinionResultDto> ReviewTenantAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();
        var all = await companies.ListAsync(tenantId, cancellationToken);

        var incelenen = 0;
        var kaydedilen = 0;
        var uyumlu = 0;
        var ayrisan = 0;
        var atlanan = 0;
        var acik = true;

        foreach (var company in all)
        {
            try
            {
                var sonuc = await ReviewAsync(company.Id, company.LegalName, tenantId, cancellationToken);

                incelenen += sonuc.ExaminedCount;
                kaydedilen += sonuc.RecordedCount;
                uyumlu += sonuc.AgreedCount;
                ayrisan += sonuc.DisagreedCount;
                atlanan += sonuc.SkippedCount;
                acik = sonuc.AiEnabled;

                if (!acik)
                {
                    // Anahtar yoksa diğer firmaları denemek anlamsız; tur boşuna sürmez.
                    break;
                }
            }
            catch (Exception exception)
            {
                atlanan++;
                logger.LogError(
                    exception,
                    "İkinci görüş turunda firma atlandı. TenantId={TenantId} CompanyId={CompanyId}",
                    tenantId, company.Id);
            }
        }

        logger.LogInformation(
            "İkinci görüş turu bitti. TenantId={TenantId} İncelenen={Examined} Kaydedilen={Recorded} " +
            "Ayrışan={Disagreed} Atlanan={Skipped}",
            tenantId, incelenen, kaydedilen, ayrisan, atlanan);

        return new AiSecondOpinionResultDto(incelenen, kaydedilen, uyumlu, ayrisan, atlanan, acik);
    }

    private async Task<AiSecondOpinionResultDto> ReviewAsync(
        Guid companyId,
        string companyName,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var guncel = await assessments.ListLatestForCompanyAsync(companyId, cancellationToken);

        // Görüşü zaten olan değerlendirme yeniden sorulmaz: aynı vaka için ikinci model
        // çağrısı hem para harcar hem ölçüme yeni bilgi katmaz.
        var mevcutlar = await verdicts.ListAiReviewedAssessmentsAsync(companyId, cancellationToken);

        var bekleyen = guncel
            .Where(a => !mevcutlar.Contains(a.Id))
            .OrderByDescending(a => a.EvaluatedAt)
            .Take(MaximumPerRun)
            .ToList();

        if (bekleyen.Count == 0)
        {
            return new AiSecondOpinionResultDto(0, 0, 0, 0, 0, true);
        }

        var profil = await BuildProfileSummaryAsync(companyId, cancellationToken);

        var kaydedilen = 0;
        var uyumlu = 0;
        var ayrisan = 0;
        var atlanan = 0;

        foreach (var assessment in bekleyen)
        {
            var opportunity = await opportunities.GetWithRulesAsync(assessment.OpportunityId, cancellationToken);

            if (opportunity is null)
            {
                atlanan++;
                continue;
            }

            var gorus = await ai.ReviewEligibilityAsync(
                new SecondOpinionRequest
                {
                    OpportunityTitle = opportunity.Title,
                    OpportunityText = KosullarMetni(opportunity),
                    CompanyName = companyName,
                    CompanyProfileSummary = profil.Summary,
                    MissingProfileFields = profil.MissingFields,
                },
                cancellationToken);

            if (gorus is null)
            {
                // Anahtar yok ya da yanıt okunamadı. Görüş UYDURULMAZ; tur burada biter.
                logger.LogInformation(
                    "Yapay zekâ ikinci görüş üretmedi; kayıt açılmadı. CompanyId={CompanyId}", companyId);

                return new AiSecondOpinionResultDto(
                    bekleyen.Count, kaydedilen, uyumlu, ayrisan, atlanan + 1, AiEnabled: false);
            }

            await KaydetAsync(assessment, gorus, tenantId, cancellationToken);

            kaydedilen++;

            if (assessment.Verdict == gorus.Verdict)
            {
                uyumlu++;
            }
            else
            {
                ayrisan++;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AiSecondOpinionResultDto(bekleyen.Count, kaydedilen, uyumlu, ayrisan, atlanan, true);
    }

    private async Task KaydetAsync(
        Domain.Assessments.EligibilityAssessment assessment,
        AiSecondOpinionResult gorus,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // Model ayrışmanın SEBEBİNİ sınıflandıramaz: hangi düzeltmenin gerektiğini
        // (kural mı, veri mi, ağırlık mı) ancak insan söyleyebilir. Sebep alanı bu yüzden
        // "Other" kalır ve gerekçe metni nota yazılır.
        var sebep = assessment.Verdict == gorus.Verdict
            ? VerdictDisagreementReason.None
            : VerdictDisagreementReason.Other;

        var verdict = new ExpertVerdict(
            tenantId,
            assessment.CompanyId,
            assessment.OpportunityId,
            assessment.Id,
            assessment.Verdict,
            assessment.FinalScore,
            assessment.DataGapCount > 0,
            gorus.Verdict,
            sebep,
            gorus.Rationale,
            clock.UtcNow,
            gorus.ModelName,
            VerdictSource.Ai,
            gorus.ModelName,
            gorus.Confidence);

        await verdicts.AddAsync(verdict, cancellationToken);
    }

    /// <summary>
    /// Firma profilinin okunabilir özeti ve eksik alan listesi.
    ///
    /// <para>
    /// Eksik alanlar modele <b>açıkça</b> bildirilir. Bildirilmezse model bilinmeyeni
    /// "hayır" sayıp firmayı eler ve ürünün üçüncü iddiası (eksik veri elemez) ikinci
    /// görüş tarafında çiğnenmiş olur.
    /// </para>
    /// </summary>
    private async Task<(string Summary, IReadOnlyList<string> MissingFields)> BuildProfileSummaryAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var company = await companies.GetWithDetailsAsync(companyId, cancellationToken)
            ?? throw new NotFoundException("Firma", companyId);

        var satirlar = new List<string>();
        var eksikler = new List<string>();
        var asOf = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        foreach (var (alan, _) in CompanyFieldResolver.SupportedFields)
        {
            var deger = CompanyFieldResolver.Resolve(company, alan, asOf);
            var ad = CompanyFieldResolver.Label(alan);

            if (deger.Kind == FieldValueKind.Unknown)
            {
                eksikler.Add(ad);
                continue;
            }

            satirlar.Add($"- {ad}: {deger.Display()}");
        }

        return (string.Join("\n", satirlar), eksikler);
    }

    /// <summary>
    /// Modele verilecek çağrı metni: kuralların okunabilir hâli.
    ///
    /// <para>
    /// Ham belge metni yerine çıkarılmış koşullar verilir, çünkü belge çoğu zaman çok uzun
    /// ve koşullar dağınıktır. Kuralların <b>kaynak cümlesi</b> de eklenir: model böylece
    /// kuralın doğru çıkarılıp çıkarılmadığını görebilir ve yanlış çıkarımı yakalayabilir.
    /// </para>
    /// </summary>
    private static string KosullarMetni(Domain.Opportunities.Opportunity opportunity)
    {
        var satirlar = opportunity.Rules
            .Select(r => r.SourceExcerpt is { Length: > 0 } alinti
                ? $"- {r.HumanReadable}\n  (metindeki karşılığı: {alinti})"
                : $"- {r.HumanReadable}")
            .ToList();

        if (satirlar.Count == 0)
        {
            return opportunity.Summary ?? "Çağrı metninden koşul çıkarılamadı.";
        }

        var basliklar = new List<string> { $"Yayınlayan: {opportunity.Publisher}" };

        if (opportunity.Deadline is { } son)
        {
            basliklar.Add($"Son başvuru: {son:dd.MM.yyyy}");
        }

        return string.Join("\n", basliklar) + "\n\nKoşullar:\n" + string.Join("\n", satirlar);
    }
}
