using System.Net;
using System.Net.Http.Json;
using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Uyumu fırsata çeviren motor — uçtan uca.
///
/// <para>
/// Alan testleri hesabı sınar; bunlar <b>hattı</b> sınar: gerçek firma ve gerçek çağrı
/// kataloğu üzerinden çalışıyor mu, süresi dolmuş çağrılar dışarıda mı, ve kiracı
/// sınırı duruyor mu. Eksik listesi firmanın zayıf noktalarını gösterir; başka kiracıya
/// sızması en az skorun sızması kadar kötüdür.
/// </para>
/// </summary>
public sealed class ComplianceLeverageEndpointTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _tenantAdmin = null!;
    private Guid _companyId;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _companyId = _factory.TenantA.CompanyId;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> VeritabaniAsync<T>(Func<GovAiDbContext, Task<T>> islem)
    {
        using var kapsam = _factory.Services.CreateScope();

        return await islem(kapsam.ServiceProvider.GetRequiredService<GovAiDbContext>());
    }

    private async Task<KaldiracYaniti> KaldiracAsync(HttpClient? istemci = null)
    {
        var yanit = await (istemci ?? _tenantAdmin).GetAsync($"/api/leverage/companies/{_companyId}");
        yanit.EnsureSuccessStatusCode();

        return (await yanit.Content.ReadFromJsonAsync<KaldiracYaniti>())!;
    }

    /// <summary>
    /// Firmada olmayan bir belgeyi zorunlu kılan, açık bir çağrı açar.
    ///
    /// <para>
    /// Kod BÜYÜK HARFE çevrilerek üretilir: <c>DocumentRequirement</c> kodu normalleştirir
    /// ve eksik anahtarı o biçimde oluşur. Küçük harfle aranırsa eksik bulunamaz.
    /// </para>
    /// </summary>
    private Task<Guid> BelgeIsteyenCagriAsync(string belgeKodu, decimal? tutar, int kalanGun = 30) =>
        VeritabaniAsync(async db =>
        {
            var cagri = new Opportunity(
                _factory.TenantA.SourceId,
                SourceType.KosgebOrSimilar,
                SupportCategory.Grant,
                $"Kaldıraç Çağrısı {Guid.CreateVersion7():N}",
                "KOSGEB",
                DateTimeOffset.UtcNow.AddDays(-5));

            // Yayın tarihi son başvurudan sonra olamaz (alan kuralı); süresi dolmuş
            // çağrı kurgularken yayını yeterince geriye almak gerekir.
            var yayin = DateTimeOffset.UtcNow.AddDays(Math.Min(-5, kalanGun - 5));

            cagri.SetSchedule(yayin, DateTimeOffset.UtcNow.AddDays(kalanGun));

            if (tutar is not null)
            {
                cagri.SetBudget(new BudgetRange(0m, tutar.Value, "TRY", 0.5m));
            }

            cagri.ReplaceDocumentChecklist(
                [new DocumentRequirement(belgeKodu, $"{belgeKodu} belgesi", isMandatory: true)]);

            db.Opportunities.Add(cagri);
            await db.SaveChangesAsync();

            return cagri.Id;
        });

    // ── Yetki ve kiracı sınırı ──────────────────────────────────────────────

    [Fact(DisplayName = "UK1. Kimliksiz istek reddedilir")]
    public async Task Kimliksiz_reddedilir()
    {
        var yanit = await _factory.CreateClient().GetAsync($"/api/leverage/companies/{_companyId}");

        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    [Fact(DisplayName = "UK2. BAŞKA KİRACI firmanın eksiklerini göremez")]
    public async Task Diger_kiraci_goremez()
    {
        var digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        var yanit = await digerKiraci.GetAsync($"/api/leverage/companies/{_companyId}");

        Assert.True(
            yanit.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Beklenen 404/403, gelen {(int)yanit.StatusCode}.");
    }

    // ── Gerçek hat ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "UK3. Eksik belge, açtığı çağrıyla birlikte listelenir")]
    public async Task Eksik_belge_listelenir()
    {
        var kod = $"BELGE{Guid.CreateVersion7():N}"[..12].ToUpperInvariant();
        var cagriId = await BelgeIsteyenCagriAsync(kod, 4_000_000m);

        var kaldirac = await KaldiracAsync();
        var eksik = kaldirac.Gaps.SingleOrDefault(g => g.Key == $"belge:{kod}");

        Assert.NotNull(eksik);
        Assert.Equal("Belge eksiği", eksik.KindLabel);
        Assert.Contains(eksik.Opportunities, o => o.OpportunityId == cagriId);
    }

    [Fact(DisplayName = "UK4. SÜRESİ DOLMUŞ çağrı hesaba katılmaz")]
    public async Task Suresi_dolmus_cagri_haric()
    {
        // Bugün kapatılsa bile başvurulamayacak bir çağrı için yatırım önermek
        // yanıltıcı olurdu.
        var kod = $"GECMIS{Guid.CreateVersion7():N}"[..12].ToUpperInvariant();
        await BelgeIsteyenCagriAsync(kod, 9_000_000m, kalanGun: -10);

        var kaldirac = await KaldiracAsync();

        Assert.DoesNotContain(kaldirac.Gaps, g => g.Key == $"belge:{kod}");
    }

    [Fact(DisplayName = "UK5. Aynı belge birden çok çağrıyı birden açar")]
    public async Task Ayni_belge_coklu_cagri()
    {
        // Ürünün asıl vaadi: "bu tek belgeyi al, iki çağrıya birden gir."
        var kod = $"ORTAK{Guid.CreateVersion7():N}"[..12].ToUpperInvariant();
        await BelgeIsteyenCagriAsync(kod, 1_000_000m);
        await BelgeIsteyenCagriAsync(kod, 2_000_000m);

        var kaldirac = await KaldiracAsync();
        var eksik = kaldirac.Gaps.Single(g => g.Key == $"belge:{kod}");

        Assert.True(eksik.AffectedCount >= 2, $"Beklenen en az 2 çağrı, gelen {eksik.AffectedCount}.");
    }

    [Fact(DisplayName = "UK6. Tutar toplamı yalnızca AÇILAN çağrılardan gelir")]
    public async Task Tutar_yalnizca_acilanlardan()
    {
        var kod = $"TUTAR{Guid.CreateVersion7():N}"[..12].ToUpperInvariant();
        await BelgeIsteyenCagriAsync(kod, 3_000_000m);

        var kaldirac = await KaldiracAsync();
        var eksik = kaldirac.Gaps.Single(g => g.Key == $"belge:{kod}");

        if (eksik.UnlockCount == 0)
        {
            // Çağrıda başka engel de varsa tutar bildirilmemelidir.
            Assert.Null(eksik.UnlockedAmount);
        }
        else
        {
            Assert.NotNull(eksik.UnlockedAmount);
        }
    }

    // ── Dürüstlük ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "UK7. 'Açılır' denen her çağrıda başka engel KALMAZ")]
    public async Task Acilanlarda_engel_kalmaz()
    {
        // Sözleşmenin özü: UnlockedByThisAlone doğruysa kalan engel sıfır olmalı.
        var kaldirac = await KaldiracAsync();

        foreach (var eksik in kaldirac.Gaps)
        {
            foreach (var cagri in eksik.Opportunities.Where(o => o.UnlockedByThisAlone))
            {
                Assert.Equal(0, cagri.RemainingGapCount);
            }
        }
    }

    [Fact(DisplayName = "UK8. Beyan eksikleri KOŞULLU işaretlenir")]
    public async Task Beyan_eksikleri_kosullu()
    {
        var kaldirac = await KaldiracAsync();

        foreach (var eksik in kaldirac.Gaps.Where(g => g.KindLabel == "Beyan eksiği"))
        {
            Assert.True(eksik.IsConditional, $"'{eksik.Requirement}' koşullu işaretlenmemiş.");
        }
    }

    [Fact(DisplayName = "UK9. Özet sayıları eksik listesiyle tutar")]
    public async Task Ozet_tutar()
    {
        var kod = $"OZET{Guid.CreateVersion7():N}"[..12].ToUpperInvariant();
        await BelgeIsteyenCagriAsync(kod, 5_000_000m);

        var kaldirac = await KaldiracAsync();

        Assert.Equal(kaldirac.Gaps.Count, kaldirac.GapCount);

        var farkliAcilanlar = kaldirac.Gaps
            .SelectMany(g => g.Opportunities.Where(o => o.UnlockedByThisAlone))
            .Select(o => o.OpportunityId)
            .Distinct()
            .Count();

        Assert.Equal(farkliAcilanlar, kaldirac.UnlockableOpportunityCount);
    }

    [Fact(DisplayName = "UK10. Her eksik bir KOŞUL METNİ taşır")]
    public async Task Kosul_metni_var()
    {
        // "Eksiğiniz var" deyip ne olduğunu söylememek aksiyon üretmez.
        var kaldirac = await KaldiracAsync();

        Assert.All(kaldirac.Gaps, g => Assert.False(string.IsNullOrWhiteSpace(g.Requirement)));
    }

    private sealed record KaldiracYaniti(
        Guid CompanyId,
        string CompanyName,
        int EvaluatedOpportunityCount,
        int GapCount,
        int UnlockableOpportunityCount,
        decimal? UnlockableAmount,
        bool AmountIsPartial,
        IReadOnlyList<EksikYaniti> Gaps);

    private sealed record EksikYaniti(
        string Key,
        string Requirement,
        string KindLabel,
        string? SuggestedAction,
        int UnlockCount,
        int AffectedCount,
        decimal? UnlockedAmount,
        bool AmountIsPartial,
        bool IsConditional,
        IReadOnlyList<CagriYaniti> Opportunities);

    private sealed record CagriYaniti(
        Guid OpportunityId,
        string Title,
        decimal? MaxAmount,
        bool UnlockedByThisAlone,
        int RemainingGapCount);
}
