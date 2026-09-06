using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Fırsat listesinin sektöre göre sıralanması (Faz 2).
///
/// <para>
/// Sahada görülen hata: makine imalatı yapan bir firmanın "fırsatlarım" listesinde
/// "Didi soğuk çay nakliye" ve "Sivas YHT Garı buz çözme" ihaleleri en üstte, 88 puanla
/// ve "uygun" kararıyla duruyordu. Sebep motorun sektör boyutunu kuralsız çağrılarda tam
/// puan saymasıydı.
/// </para>
///
/// <para>
/// Bu testler düzeltmeyi <b>API sınırında</b> sabitler: sıralama sunucuda ve sayfalama
/// öncesinde yapılmalıdır, aksi hâlde ilk sayfa doğru, ikinci sayfa yanlış görünür.
/// Uyumsuz kayıt listeden ÇIKARILMAZ; etiketiyle en altta kalır.
/// </para>
/// </summary>
public sealed class SectorRankingTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _tenantA = null!;
    private Guid _companyId;

    /// <summary>Sektörü tutan çağrı: makine imalatı (NACE 28/25).</summary>
    private Guid _uyumluId;

    /// <summary>Sektörü tutmayan çağrı: nakliye (NACE 49/52).</summary>
    private Guid _uyumsuzId;

    /// <summary>Sektör kuralı hiç olmayan çağrı.</summary>
    private Guid _belirsizId;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _tenantA = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _companyId = _factory.TenantA.CompanyId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();

            // Firma makine imalatçısıdır; NACE kodu olmadan sektör hiç doğrulanamaz.
            var company = await db.Companies
                .IgnoreQueryFilters()
                .SingleAsync(c => c.Id == _companyId);
            company.ReplaceNaceCodes([new CompanyNaceCode("2562", isPrimary: true)]);

            _uyumluId = Ekle(db, "Talaşlı İmalat Tezgâhı Alınacaktır", "28,25");
            _uyumsuzId = Ekle(db, "Didi Soğuk Çay Nakliye İşi İhale Edilecektir", "49.41,49.39,52.29");
            _belirsizId = Ekle(db, "Yatırımcılara Duyuru", nace: null);

            await db.SaveChangesAsync();
        }

        var rescore = await _tenantA.PostAsync($"/api/eligibility/companies/{_companyId}/rescore", null);
        rescore.EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Ortak kataloğa bir ihale ekler; <paramref name="nace"/> yoksa sektör kuralı da yoktur.</summary>
    private Guid Ekle(GovAiDbContext db, string baslik, string? nace)
    {
        var opportunity = new Opportunity(
            _factory.TenantA.SourceId,
            SourceType.OfficialGazette,
            SupportCategory.Tender,
            baslik,
            "Test İdaresi",
            DateTimeOffset.UtcNow.AddDays(-2));

        opportunity.SetSchedule(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(30));

        if (nace is not null)
        {
            opportunity.ReplaceRules(
            [
                new OpportunityRule(
                    "Company.NaceCodes", RuleOperator.NaceMatch, nace,
                    RuleDimension.Sector, RuleSeverity.Major, $"İlan konusu NACE {nace} alanındadır.")
            ], extractionConfidence: 0.7m);
        }

        db.Opportunities.Add(opportunity);

        return opportunity.Id;
    }

    private async Task<List<JsonElement>> ListeAsync(string sorgu = "")
    {
        var response = await _tenantA.GetAsync(
            $"/api/eligibility/companies/{_companyId}/matches?pageSize=100{sorgu}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("items").EnumerateArray().ToList();
    }

    private static string SektorUyumu(JsonElement satir) => satir.GetProperty("sectorFit").GetString()!;

    private static int Sira(List<JsonElement> liste, Guid opportunityId) =>
        liste.FindIndex(x => x.GetProperty("opportunityId").GetGuid() == opportunityId);

    [Fact(DisplayName = "SR1. Liste satırı sektör uyumunu taşır")]
    public async Task Liste_satiri_sektor_uyumunu_tasir()
    {
        var liste = await ListeAsync();

        Assert.NotEmpty(liste);
        Assert.All(liste, satir => Assert.True(satir.TryGetProperty("sectorFit", out _)));
    }

    [Fact(DisplayName = "SR2. Sektörü tutan çağrı listenin başında, tutmayan en sonda")]
    public async Task Siralama_sektor_uyumuna_gore_yapilir()
    {
        var liste = await ListeAsync();

        var uyumlu = Sira(liste, _uyumluId);
        var belirsiz = Sira(liste, _belirsizId);
        var uyumsuz = Sira(liste, _uyumsuzId);

        Assert.True(uyumlu >= 0 && belirsiz >= 0 && uyumsuz >= 0, "Üç çağrı da listede olmalı.");
        Assert.True(uyumlu < belirsiz, $"Uyumlu ({uyumlu}) doğrulanamayanın ({belirsiz}) üstünde olmalı.");
        Assert.True(belirsiz < uyumsuz, $"Doğrulanamayan ({belirsiz}) uyumsuzun ({uyumsuz}) üstünde olmalı.");
    }

    [Fact(DisplayName = "SR3. Etiketler doğru: uyumlu / doğrulanamadı / uyumsuz")]
    public async Task Etiketler_dogru_atanir()
    {
        var liste = await ListeAsync();

        Assert.Equal("Matched", SektorUyumu(liste[Sira(liste, _uyumluId)]));
        Assert.Equal("Unverified", SektorUyumu(liste[Sira(liste, _belirsizId)]));
        Assert.Equal("NotMatched", SektorUyumu(liste[Sira(liste, _uyumsuzId)]));
    }

    [Fact(DisplayName = "SR4. Sektörü tutmayan kayıt listeden GİZLENMEZ")]
    public async Task Uyumsuz_kayit_gizlenmez()
    {
        // Kullanıcının seçimi buydu: uyumsuz kayıt en alta iner ve etiketlenir, silinmez.
        // İsteyen bakabilmelidir.
        var liste = await ListeAsync();

        Assert.True(Sira(liste, _uyumsuzId) >= 0);
    }

    [Fact(DisplayName = "SR5. Son başvuruya göre sıralamada da sektör birincil kalır")]
    public async Task Son_basvuru_siralamasinda_da_sektor_birincil()
    {
        var liste = await ListeAsync("&sort=DeadlineAscending");

        Assert.True(Sira(liste, _uyumluId) < Sira(liste, _uyumsuzId));
    }

    [Fact(DisplayName = "SR6. Değerlendirme tarihine göre sıralamada da sektör birincil kalır")]
    public async Task Tarih_siralamasinda_da_sektor_birincil()
    {
        var liste = await ListeAsync("&sort=EvaluatedAtDescending");

        Assert.True(Sira(liste, _uyumluId) < Sira(liste, _uyumsuzId));
    }

    [Fact(DisplayName = "SR7. Sıralama sayfalamadan ÖNCE yapılır")]
    public async Task Siralama_sayfalamadan_once_yapilir()
    {
        // Bellekte sıralansaydı ilk sayfa doğru, sonraki sayfalar yanlış görünürdü.
        // İlk sayfanın ilk kaydı, tüm listenin ilk kaydıyla aynı olmalı.
        var tamamı = await ListeAsync();

        var response = await _tenantA.GetAsync(
            $"/api/eligibility/companies/{_companyId}/matches?page=1&pageSize=1");
        response.EnsureSuccessStatusCode();

        var ilkSayfa = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();

        Assert.Single(ilkSayfa);
        Assert.Equal(
            tamamı[0].GetProperty("opportunityId").GetGuid(),
            ilkSayfa[0].GetProperty("opportunityId").GetGuid());
    }

    [Fact(DisplayName = "SR8. Uyumsuz çağrı hâlâ değerlendirilebilir — eleme değil sıralama")]
    public async Task Uyumsuz_cagri_elenmez()
    {
        var liste = await ListeAsync();
        var uyumsuz = liste[Sira(liste, _uyumsuzId)];

        Assert.NotEqual("NotEligible", uyumsuz.GetProperty("verdict").GetString());
        Assert.True(uyumsuz.GetProperty("finalScore").GetDecimal() > 0m);
    }

    [Fact(DisplayName = "SR9. Detay ekranı da sektör uyumunu gösterir")]
    public async Task Detay_ekrani_sektor_uyumunu_gosterir()
    {
        var liste = await ListeAsync();
        var assessmentId = liste[Sira(liste, _uyumsuzId)].GetProperty("assessmentId").GetGuid();

        var response = await _tenantA.GetAsync($"/api/eligibility/{assessmentId}");
        response.EnsureSuccessStatusCode();

        var detay = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("NotMatched", detay.GetProperty("sectorFit").GetString());
    }
}
