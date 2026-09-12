using System.Net;
using System.Net.Http.Json;
using GovAI.Domain.Companies;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Kanıt eskime raporu — uçtan uca.
///
/// <para>
/// Alan testleri motoru, bu testler <b>hattı</b> sınar: gerçek firma verisi okunuyor mu,
/// yetki ve kiracı sınırı duruyor mu, ve sonuç ekranın kullanabileceği biçimde mi
/// geliyor. Motor doğru olsa da firma yanlış yüklenirse rapor yanlış çıkar.
/// </para>
/// </summary>
public sealed class EvidenceReliabilityTests(GovAiApiFactory factory)
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

    private async Task VeritabaniAsync(Func<GovAiDbContext, Task> islem)
    {
        using var kapsam = _factory.Services.CreateScope();

        await islem(kapsam.ServiceProvider.GetRequiredService<GovAiDbContext>());
    }

    private async Task<PortfoyYaniti> PortfoyAsync(HttpClient? istemci = null)
    {
        var yanit = await (istemci ?? _tenantAdmin).GetAsync($"/api/evidence/companies/{_companyId}");
        yanit.EnsureSuccessStatusCode();

        return (await yanit.Content.ReadFromJsonAsync<PortfoyYaniti>())!;
    }

    /// <summary>Firmanın sertifikalarını verilen listeyle değiştirir.</summary>
    private Task SertifikalariAyarlaAsync(params CompanyCertificate[] sertifikalar) =>
        VeritabaniAsync(async db =>
        {
            // Kiracı süzgeci arka plan kapsamında kimliği çözemez; test kurulumu
            // süzgeci bilerek atlar.
            var firma = await db.Companies
                .IgnoreQueryFilters()
                .Include(c => c.Certificates)
                .FirstAsync(c => c.Id == _companyId);

            firma.ReplaceCertificates(sertifikalar);

            await db.SaveChangesAsync();
        });

    // ── Yetki ve kiracı sınırı ──────────────────────────────────────────────

    [Fact(DisplayName = "KG1. Kimliksiz istek reddedilir")]
    public async Task Kimliksiz_reddedilir()
    {
        var anonim = _factory.CreateClient();

        var yanit = await anonim.GetAsync($"/api/evidence/companies/{_companyId}");

        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    [Fact(DisplayName = "KG2. BAŞKA KİRACI firmanın kanıtlarını göremez")]
    public async Task Diger_kiraci_goremez()
    {
        // Kanıt portföyü firmanın belge ve mali durumunu ele verir; kiracı sınırının
        // burada da durduğu ayrıca sınanmalı.
        var digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        var yanit = await digerKiraci.GetAsync($"/api/evidence/companies/{_companyId}");

        Assert.True(
            yanit.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Beklenen 404/403, gelen {(int)yanit.StatusCode}.");
    }

    // ── Gerçek veri hattı ───────────────────────────────────────────────────

    [Fact(DisplayName = "KG3. Portföy firmanın gerçek sertifikalarını içerir")]
    public async Task Gercek_sertifikalar_gelir()
    {
        var bugun = DateOnly.FromDateTime(DateTime.UtcNow);

        await SertifikalariAyarlaAsync(
            new CompanyCertificate("ISO9001", "ISO 9001 Kalite", bugun.AddYears(-1), bugun.AddYears(2)));

        var portfoy = await PortfoyAsync();

        Assert.Contains(portfoy.Items, k => k.Label.Contains("ISO 9001", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "KG4. Süresi DOLMUŞ belge geçersiz olarak raporlanır")]
    public async Task Suresi_dolmus_gecersiz()
    {
        var bugun = DateOnly.FromDateTime(DateTime.UtcNow);

        await SertifikalariAyarlaAsync(
            new CompanyCertificate("ISO14001", "ISO 14001 Çevre", bugun.AddYears(-4), bugun.AddDays(-3)));

        var portfoy = await PortfoyAsync();
        var kanit = portfoy.Items.Single(k => k.Label.Contains("ISO 14001", StringComparison.Ordinal));

        Assert.Equal("Geçersiz", kanit.StatusLabel);
        Assert.False(kanit.IsDependable);
        Assert.True(portfoy.Expired >= 1);
    }

    [Fact(DisplayName = "KG5. Yakında BİTECEK belge zayıflıyor olarak raporlanır")]
    public async Task Bitecek_belge_zayifliyor()
    {
        // Ürünün asıl iddiası burada görünür: belge duruyor ama artık tam güvenilir
        // değil. "Var/yok" gösteren bir sistem bunu söyleyemezdi.
        var bugun = DateOnly.FromDateTime(DateTime.UtcNow);

        await SertifikalariAyarlaAsync(
            new CompanyCertificate("ISO27001", "ISO 27001 Bilgi Güvenliği", bugun.AddYears(-3), bugun.AddDays(20)));

        var portfoy = await PortfoyAsync();
        var kanit = portfoy.Items.Single(k => k.Label.Contains("ISO 27001", StringComparison.Ordinal));

        Assert.NotEqual("Geçersiz", kanit.StatusLabel);
        Assert.NotEqual("Güvenilir", kanit.StatusLabel);
        Assert.True(kanit.Score < 1m);
    }

    // ── Sıralama ve kullanılabilirlik ───────────────────────────────────────

    [Fact(DisplayName = "KG6. ACİL olanlar listenin başında gelir")]
    public async Task Acil_olanlar_basta()
    {
        var bugun = DateOnly.FromDateTime(DateTime.UtcNow);

        await SertifikalariAyarlaAsync(
            new CompanyCertificate("A", "Sağlam Belge", bugun.AddMonths(-1), bugun.AddYears(3)),
            new CompanyCertificate("B", "Bitmiş Belge", bugun.AddYears(-5), bugun.AddDays(-1)));

        var portfoy = await PortfoyAsync();

        Assert.Equal("Bitmiş Belge", portfoy.Items[0].Label);
    }

    [Fact(DisplayName = "KG7. Her kanıt bir GEREKÇE taşır")]
    public async Task Gerekce_gelir()
    {
        // Ekranda "güvenilmez" yazıp sebebini söylememek, açıklanabilirlik iddiasını
        // arayüzde kırardı.
        var portfoy = await PortfoyAsync();

        Assert.NotEmpty(portfoy.Items);
        Assert.All(portfoy.Items, k => Assert.False(string.IsNullOrWhiteSpace(k.Reason)));
    }

    [Fact(DisplayName = "KG8. Özet sayıları liste ile tutar")]
    public async Task Ozet_listeyle_tutar()
    {
        var bugun = DateOnly.FromDateTime(DateTime.UtcNow);

        await SertifikalariAyarlaAsync(
            new CompanyCertificate("A", "Sağlam Belge", bugun.AddMonths(-1), bugun.AddYears(3)),
            new CompanyCertificate("B", "Bitmiş Belge", bugun.AddYears(-5), bugun.AddDays(-1)));

        var portfoy = await PortfoyAsync();

        Assert.Equal(portfoy.Items.Count, portfoy.Total);
        Assert.Equal(portfoy.Items.Count(k => k.StatusLabel == "Geçersiz"), portfoy.Expired);
        Assert.Equal(portfoy.Items.Count(k => k.IsDependable), portfoy.Dependable);
    }

    [Fact(DisplayName = "KG9. Ölçülemeyen kanıt ortalamayı DÜŞÜRMEZ")]
    public async Task Olculemeyen_ortalamayi_dusurmez()
    {
        // Seed firmasının profil beyanı tarihsizdir; sıfır sayılsaydı ortalama
        // haksız yere düşerdi.
        var bugun = DateOnly.FromDateTime(DateTime.UtcNow);

        await SertifikalariAyarlaAsync(
            new CompanyCertificate("A", "Sağlam Belge", bugun.AddMonths(-1), bugun.AddYears(3)));

        var portfoy = await PortfoyAsync();

        if (portfoy.Unknown > 0 && portfoy.AverageScore is { } ortalama)
        {
            Assert.True(ortalama > 0m, "Ölçülemeyen kanıtlar ortalamaya sıfır olarak karışmış.");
        }
    }

    private sealed record PortfoyYaniti(
        Guid CompanyId,
        string CompanyName,
        DateOnly AsOf,
        int Total,
        int Dependable,
        int Weakening,
        int Undependable,
        int Expired,
        int Unknown,
        decimal? AverageScore,
        int NeedsAttention,
        IReadOnlyList<KanitYaniti> Items);

    private sealed record KanitYaniti(
        string Label,
        string KindLabel,
        string StatusLabel,
        decimal? Score,
        int? AgeDays,
        int HalfLifeDays,
        DateOnly? WeakensOn,
        string Reason,
        bool IsDependable);
}
