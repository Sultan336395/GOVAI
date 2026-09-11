using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Assessments;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Tenders;

namespace GovAI.Application.Dashboard;

/// <summary>
/// Dashboard'un dört ek bölümünü kurmak için gereken veriyi yükler.
///
/// <para>
/// Hesap <see cref="DashboardInsightsBuilder"/> içindedir ve saftır; burada yalnızca
/// yetki doğrulanır ve kayıtlar okunur. Ayrım bilinçlidir: kural yükleme kodunun
/// içinde dursaydı davranışı sınamak yedi sahte depo kurmayı gerektirirdi.
/// </para>
/// </summary>
public sealed class DashboardInsightsService(
    ICompanyRepository companies,
    IAssessmentRepository assessments,
    IOpportunityRepository opportunities,
    ITenderPursuitRepository pursuits,
    IWeeklyReportRepository reports,
    IReportInquiryRepository inquiries,
    IErpConnectionRepository erp,
    CompanyAccessGuard access,
    IDateTimeProvider clock)
{
    public async Task<DashboardInsightsDto> GetAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var company = await access.LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);

        // Profil doluluğu firmanın alt koleksiyonlarını (NACE, lokasyon, sertifika)
        // okur; yetki denetiminden dönen özet kayıt yetmez.
        var detayli = await companies.GetWithDetailsAsync(companyId, cancellationToken) ?? company;

        var degerlendirmeler = await assessments.ListLatestForCompanyAsync(companyId, cancellationToken);
        var takipler = await pursuits.ListForCompanyAsync(companyId, cancellationToken);
        var raporlar = await reports.ListForCompanyAsync(
            companyId, DashboardInsightsBuilder.TrendWeeks, cancellationToken);
        var sorular = await inquiries.ListRecentForCompanyAsync(
            companyId, DashboardInsightsBuilder.ActivityLimit, cancellationToken);
        var baglanti = await erp.GetByCompanyAsync(companyId, cancellationToken);

        return DashboardInsightsBuilder.Build(
            detayli,
            degerlendirmeler,
            takipler,
            raporlar,
            sorular,
            baglanti,
            await CagrilarAsync(degerlendirmeler, takipler, cancellationToken),
            clock.UtcNow);
    }

    /// <summary>Aksiyon ve hareket satırlarında başlık gösterebilmek için çağrı künyeleri.</summary>
    private async Task<IReadOnlyDictionary<Guid, Opportunity>> CagrilarAsync(
        IReadOnlyList<EligibilityAssessment> degerlendirmeler,
        IReadOnlyList<TenderPursuit> takipler,
        CancellationToken cancellationToken)
    {
        var kimlikler = degerlendirmeler.Select(a => a.OpportunityId)
            .Concat(takipler.Select(p => p.OpportunityId))
            .Distinct();

        var harita = new Dictionary<Guid, Opportunity>();

        foreach (var id in kimlikler)
        {
            var cagri = await opportunities.GetWithRulesAsync(id, cancellationToken);

            if (cagri is not null)
            {
                harita[id] = cagri;
            }
        }

        return harita;
    }
}
