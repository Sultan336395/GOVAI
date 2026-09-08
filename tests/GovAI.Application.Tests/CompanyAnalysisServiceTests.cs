using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Analysis;
using GovAI.Application.Common;
using GovAI.Application.Regulatory;
using GovAI.Domain.Analysis;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Regulatory;
using GovAI.Domain.Sources;

namespace GovAI.Application.Tests;

/// <summary>
/// DeepTech analiz servisinin uygulama katmanı sözleşmesi (Faz 3 — Aşama 1).
///
/// Servisin işi karar vermek değil; veriyi toplamak, kiracı sınırını uygulamak ve
/// domain sonucunu ekran sözleşmesine çevirmektir. Bu testler dört şeyi sabitler:
///
/// 1. Başka kiracının firması analiz edilemez.
/// 2. Karantinadaki kayıt analiz edilmez — güvenilmeyen belgeden sonuç üretilmez.
/// 3. Model bağlı değilken çıktı bunu açıkça söyler; "hibrit çalışıyor" denmez.
/// 4. Ekrana giden hiçbir metin puanı "kazanma ihtimali" diye adlandırmaz.
/// </summary>
public class CompanyAnalysisServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "A1. Fırsat analizi kriterleri, puanı ve güveni birlikte döner")]
    public async Task Firsat_analizi_tam_doner()
    {
        var (service, company, opportunity, _) = Build();

        var sonuc = await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id);

        Assert.Equal(CriterionCatalog.OpportunityCriteria.Count, sonuc.Criteria.Count);
        Assert.NotEmpty(sonuc.Score.Components);
        Assert.NotEmpty(sonuc.Confidence.Factors);
        Assert.Equal(AnalysisRuleSet.Current.Version, sonuc.RuleSetVersion);
    }

    [Fact(DisplayName = "A2. Puan 'uygunluk puanı' olarak adlandırılır, kazanma ihtimali değil")]
    public async Task Puan_uygunluk_puani_olarak_adlandirilir()
    {
        var (service, company, opportunity, _) = Build();

        var sonuc = await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id);

        Assert.Equal("Uygunluk puanı", sonuc.Score.Label);

        var tumMetinler = sonuc.Criteria.Select(c => c.Rationale)
            .Concat(sonuc.Score.Components.Select(c => c.Rationale))
            .Concat([sonuc.Score.Label, sonuc.VerdictLabel]);

        Assert.All(tumMetinler, m =>
            Assert.DoesNotContain("kazanma", m, StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "A3. Model bağlı değilken yapay zekâ katkısı yok olarak bildirilir")]
    public async Task Model_yoksa_yapay_zeka_katkisi_bildirilmez()
    {
        var (service, company, opportunity, _) = Build();

        var sonuc = await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id);

        Assert.False(sonuc.HasAiContribution);

        var yapayZekaGuveni = sonuc.Confidence.Factors
            .Single(f => f.Code == ConfidenceFactors.AiEvidenceAgreement);

        Assert.True(yapayZekaGuveni.NotMeasured);
    }

    [Fact(DisplayName = "A4. Başka kiracının firması analiz edilemez")]
    public async Task Baska_kiracinin_firmasi_analiz_edilemez()
    {
        var (service, _, opportunity, _) = Build();
        var yabanci = new Company(Guid.CreateVersion7(), "Yabancı A.Ş.", "1231231231", LegalType.LimitedCompany);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.AnalyzeOpportunityAsync(yabanci.Id, opportunity.Id));
    }

    [Fact(DisplayName = "A5. Karantinadaki çağrı analiz edilmez")]
    public async Task Karantinadaki_cagri_analiz_edilmez()
    {
        var (service, company, opportunity, _) = Build();
        opportunity.Quarantine(QuarantineReason.InvalidSourcePage, "Kaynak sayfa geçersiz.");

        var hata = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AnalyzeOpportunityAsync(company.Id, opportunity.Id));

        Assert.Contains("Karantinadaki", hata.Errors.Values.SelectMany(v => v).Single());
    }

    [Fact(DisplayName = "A6. Kriter sonuçları Türkçe etiketle gelir; ham enum sızmaz")]
    public async Task Kriter_sonuclari_turkce_etiketli()
    {
        var (service, company, opportunity, _) = Build();

        var sonuc = await service.AnalyzeOpportunityAsync(company.Id, opportunity.Id);

        Assert.All(sonuc.Criteria, k =>
        {
            Assert.False(string.IsNullOrWhiteSpace(k.OutcomeLabel));
            Assert.DoesNotContain("NotApplicable", k.OutcomeLabel);
            Assert.DoesNotContain("ConflictingEvidence", k.OutcomeLabel);
        });
    }

    [Fact(DisplayName = "A7. Mevzuat etki analizi hukuki uyarı ve açık sorularla döner")]
    public async Task Mevzuat_etkisi_uyariyla_doner()
    {
        var (service, company, _, changes) = Build();
        var (change, _) = changes.SeedWithEvidence(
            "İşverenler aylık prim ve hizmet belgelerini vermek zorundadır.",
            Now);

        var sonuc = await service.AnalyzeRegulationAsync(company.Id, change.Id);

        Assert.Contains("hukuki görüş değildir", sonuc.LegalDisclaimer);
        Assert.NotEmpty(sonuc.Criteria);
        Assert.False(string.IsNullOrWhiteSpace(sonuc.ImpactLabel));
        Assert.False(sonuc.HasAiContribution);
    }

    [Fact(DisplayName = "A8. Karantinadaki mevzuat kaydı için etki analizi üretilmez")]
    public async Task Karantinadaki_mevzuat_analiz_edilmez()
    {
        var (service, company, _, changes) = Build();
        var (change, _) = changes.SeedWithEvidence("İşverenler bildirmek zorundadır.", Now);
        change.Quarantine(QuarantineReason.InvalidSourcePage, "Kaynak sayfa geçersiz.");

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.AnalyzeRegulationAsync(company.Id, change.Id));
    }

    [Fact(DisplayName = "A9. Mevzuat kanıtı gerçek kanıt parçası kimliğine bağlanır")]
    public async Task Mevzuat_kaniti_kimlige_baglanir()
    {
        var (service, company, _, changes) = Build();
        var (change, chunk) = changes.SeedWithEvidence("İşverenler bildirmek zorundadır.", Now);

        var sonuc = await service.AnalyzeRegulationAsync(company.Id, change.Id);

        var kanitli = sonuc.Criteria.Where(c => c.Evidence.Count > 0).ToList();

        Assert.NotEmpty(kanitli);
        Assert.All(kanitli, k => Assert.All(k.Evidence, e => Assert.Equal(chunk.Id, e.EvidenceChunkId)));
    }

    [Fact(DisplayName = "A10. Bulunamayan fırsat 404 üretir")]
    public async Task Bulunamayan_firsat_404()
    {
        var (service, company, _, _) = Build();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AnalyzeOpportunityAsync(company.Id, Guid.CreateVersion7()));
    }

    private static (CompanyAnalysisService Service, Company Company, Opportunity Opportunity,
        FakeRegulatoryChangeRepository Changes) Build()
    {
        var currentUser = new FakeCurrentUser();
        var tenantId = currentUser.TenantId!.Value;

        var company = new Company(tenantId, "Analiz Test A.Ş.", "5556667778", LegalType.LimitedCompany);
        company.UpdateIdentity("Analiz Test A.Ş.", LegalType.LimitedCompany, new DateOnly(2018, 6, 1));
        company.UpdateWorkforce(new Workforce(20, 7, 5, 3, 1, youngEmployeeMaxAge: 29));
        company.UpdateFinancials(new Financials(40_000_000m, 25_000_000m, 9_000_000m, 3_000_000m, "TRY", 2025));
        company.UpdateSectors("İmalat", null, null);
        company.ReplaceNaceCodes([new CompanyNaceCode("2562", isPrimary: true)]);
        company.ReplaceLocations([new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: true)]);

        var opportunity = new Opportunity(
            Guid.CreateVersion7(), SourceType.DevelopmentAgency, SupportCategory.Grant,
            "Analiz Test Programı", "Test Ajansı", Now.AddDays(-5));

        opportunity.SetSchedule(Now.AddDays(-5), Now.AddDays(50));
        opportunity.ReplaceRules(
        [
            new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10",
                RuleDimension.Employment, RuleSeverity.Major, "Asgari 10 çalışan.",
                sourceExcerpt: "Başvuru sahibinin en az 10 çalışanı olmalıdır."),
            new OpportunityRule("Company.NaceCodes", RuleOperator.NaceMatch, "25",
                RuleDimension.Sector, RuleSeverity.Major, "İmalat sektörü.")
        ], extractionConfidence: 0.9m);

        var companies = new FakeCompanyRepository();
        companies.Seed(company);

        var opportunities = new FakeOpportunityRepository();
        opportunities.Seed(opportunity);

        var memberships = new FakeUserCompanyRepository();
        memberships.Seed(tenantId, currentUser.UserId!.Value, company.Id);

        var changes = new FakeRegulatoryChangeRepository();

        var service = new CompanyAnalysisService(
            companies,
            opportunities,
            changes,
            new CompanyAccessGuard(companies, memberships, currentUser),
            new FixedClock(Now));

        return (service, company, opportunity, changes);
    }
}

/// <summary>Etki analizi testleri için sade mevzuat deposu.</summary>
internal sealed class FakeRegulatoryChangeRepository : IRegulatoryChangeRepository
{
    private readonly Dictionary<Guid, RegulatoryChange> _changes = [];
    private readonly Dictionary<Guid, List<DocumentEvidenceChunk>> _chunks = [];

    public (RegulatoryChange Change, DocumentEvidenceChunk Chunk) SeedWithEvidence(string metin, DateTimeOffset now)
    {
        var versionId = Guid.CreateVersion7();

        var change = new RegulatoryChange(
            Guid.CreateVersion7(), Guid.CreateVersion7(), versionId,
            "TR", "Sosyal Güvenlik Kurumu", RegulationDomain.SocialSecurity,
            RegulatoryChangeType.Communique, "Test Tebliği",
            "https://www.sgk.gov.tr/duyuru/detay/1", new string('a', 64), now.AddDays(-10));

        change.Describe(null, now.AddDays(-10), now.AddDays(-5), null);

        var chunk = new DocumentEvidenceChunk(
            versionId, 1, metin, 0, metin.Length,
            pageNumber: 1, sectionTitle: "Genel Hükümler", paragraphNumber: 1);

        _changes[change.Id] = change;
        _chunks[versionId] = [chunk];

        return (change, chunk);
    }

    public Task<RegulatoryChange?> GetAsync(Guid changeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_changes.GetValueOrDefault(changeId));

    public Task<IReadOnlyList<DocumentEvidenceChunk>> ListEvidenceAsync(
        Guid documentVersionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DocumentEvidenceChunk>>(
            _chunks.GetValueOrDefault(documentVersionId) ?? []);

    public Task<bool> ExistsAsync(Guid documentVersionId, string contentHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task AddAsync(RegulatoryChange change, CancellationToken cancellationToken = default)
    {
        _changes[change.Id] = change;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RegulatoryChangeSummaryDto>> ListPublishableAsync(
        RegulationDomain? domain,
        string? jurisdiction,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RegulatoryChangeSummaryDto>>([]);

    public Task<RegulatoryChangeDetailDto?> GetDetailAsync(Guid changeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<RegulatoryChangeDetailDto?>(null);
}
