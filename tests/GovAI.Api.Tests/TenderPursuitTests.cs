using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;
using GovAI.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// İhale başvuru takibi: yetki, kiracı sınırı ve süreç kaydı.
///
/// <para>
/// Takip müşteri verisidir. Üç sınır birlikte korunur: başka kiracı göremez, platform
/// rolleri hiç giremez ve görüntüleyici rolü süreci <b>değiştiremez</b> — kimin ne
/// zaman teklif verdiği izlenebilir kalmalıdır.
/// </para>
/// </summary>
public sealed class TenderPursuitTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _tenantAdmin = null!;
    private Guid _companyId;
    private Guid _opportunityId;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _companyId = _factory.TenantA.CompanyId;

        // Her test kendi çağrısını açar. Tek bir ortak çağrı paylaşılsaydı testler
        // birbirinin bıraktığı aşamayı görür ve sıraya bağımlı hâle gelirdi.
        _opportunityId = await CagriAcAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Kataloğa yeni bir açık çağrı ekler. Katalog kiracıdan bağımsızdır.</summary>
    private async Task<Guid> CagriAcAsync()
    {
        using var kapsam = _factory.Services.CreateScope();

        var context = kapsam.ServiceProvider.GetRequiredService<GovAiDbContext>();

        var cagri = new Opportunity(
            _factory.TenantA.SourceId,
            SourceType.Ministry,
            SupportCategory.Tender,
            $"Takip Testi İhalesi {Guid.CreateVersion7():N}",
            "Test Kurumu",
            DateTimeOffset.UtcNow.AddDays(-3));

        cagri.SetSchedule(DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(45));

        context.Opportunities.Add(cagri);
        await context.SaveChangesAsync();

        return cagri.Id;
    }

    private async Task<JsonElement> TahtaAsync(HttpClient? client = null) =>
        await (client ?? _tenantAdmin).GetFromJsonAsync<JsonElement>(
            $"/api/tenders/companies/{_companyId}");

    private async Task<HttpResponseMessage> TakibeAlAsync(
        HttpClient? client = null, string? not = null) =>
        await (client ?? _tenantAdmin).PostAsJsonAsync(
            $"/api/tenders/companies/{_companyId}",
            new { opportunityId = _opportunityId, note = not });

    private async Task<Guid> TakipAsync()
    {
        var cevap = await TakibeAlAsync();
        cevap.EnsureSuccessStatusCode();

        return (await cevap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> DurumAsync(
        Guid pursuitId, string durum, string? sonuc = null, string? not = null, HttpClient? client = null) =>
        await (client ?? _tenantAdmin).PostAsJsonAsync(
            $"/api/tenders/{pursuitId}/status",
            new { status = durum, outcome = sonuc, note = not });

    /// <summary>Tahtadaki, bu testin kendi çağrısına ait satır.</summary>
    private async Task<JsonElement> SatirAsync()
    {
        var tahta = await TahtaAsync();

        return tahta.GetProperty("pursuits").EnumerateArray()
            .Single(p => p.GetProperty("opportunityId").GetGuid() == _opportunityId);
    }

    /// <summary>Bir aşamanın tahtadaki satır sayısı.</summary>
    private static int Say(JsonElement tahta, Func<JsonElement, bool> kosul) =>
        tahta.GetProperty("pursuits").EnumerateArray().Count(kosul);

    [Fact(DisplayName = "İA1. Takibe alınan ihale tahtada incelemede görünür")]
    public async Task Takibe_alinan_ihale_gorunur()
    {
        var cevap = await TakibeAlAsync(not: "Şartname okunuyor.");
        cevap.EnsureSuccessStatusCode();

        var kayit = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Inceleniyor", kayit.GetProperty("status").GetString());
        Assert.Equal("İnceleniyor", kayit.GetProperty("statusLabel").GetString());
        Assert.False(kayit.GetProperty("isClosed").GetBoolean());

        // Çağrı künyesi takip kaydından değil kataloktan gelir; başlıksız satır kalmaz.
        Assert.False(string.IsNullOrWhiteSpace(kayit.GetProperty("title").GetString()));

        var satir = await SatirAsync();

        Assert.Equal("Inceleniyor", satir.GetProperty("status").GetString());
    }

    [Fact(DisplayName = "İA2. Aynı ihale iki kez takibe alınınca İKİNCİ kayıt açılmaz")]
    public async Task Ayni_ihale_tek_kayit()
    {
        var bir = await TakibeAlAsync();
        var iki = await TakibeAlAsync();

        bir.EnsureSuccessStatusCode();
        iki.EnsureSuccessStatusCode();

        var birId = (await bir.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var ikiId = (await iki.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(birId, ikiId);

        var tahta = await TahtaAsync();

        Assert.Equal(1, Say(tahta, p => p.GetProperty("opportunityId").GetGuid() == _opportunityId));
    }

    [Fact(DisplayName = "İA3. Aşama değişikliği geçmişe yazılır ve kim yaptığı kalır")]
    public async Task Asama_gecmise_yazilir()
    {
        var takipId = await TakipAsync();

        (await DurumAsync(takipId, "Hazirlaniyor", not: "Teklif dosyası açıldı."))
            .EnsureSuccessStatusCode();

        var cevap = await DurumAsync(takipId, "TeklifVerildi");
        cevap.EnsureSuccessStatusCode();

        var kayit = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("TeklifVerildi", kayit.GetProperty("status").GetString());

        var gecmis = kayit.GetProperty("history").EnumerateArray().ToList();

        Assert.Equal(3, gecmis.Count);
        Assert.Equal("Inceleniyor", gecmis[0].GetProperty("toStatus").GetString());
        Assert.Equal("TeklifVerildi", gecmis[2].GetProperty("toStatus").GetString());

        foreach (var satir in gecmis)
        {
            Assert.False(string.IsNullOrWhiteSpace(satir.GetProperty("by").GetString()));
        }
    }

    [Fact(DisplayName = "İA4. Sonuçlanan ihale SONUÇSUZ kaydedilemez")]
    public async Task Sonuc_zorunlu()
    {
        var takipId = await TakipAsync();

        var cevap = await DurumAsync(takipId, "Sonuclandi");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, cevap.StatusCode);

        var satir = await SatirAsync();

        Assert.Equal("Inceleniyor", satir.GetProperty("status").GetString());
    }

    [Fact(DisplayName = "İA5. Sonuç kazanıldı/kaybedildi olarak AYRI sayılır")]
    public async Task Sonuc_ayri_sayilir()
    {
        var takipId = await TakipAsync();

        var cevap = await DurumAsync(takipId, "Sonuclandi", "Kazanildi", "Sözleşme imzalandı.");
        cevap.EnsureSuccessStatusCode();

        var kayit = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Kazanildi", kayit.GetProperty("outcome").GetString());
        Assert.Equal("Kazanıldı", kayit.GetProperty("outcomeLabel").GetString());
        Assert.True(kayit.GetProperty("isClosed").GetBoolean());

        // Sayaç tahtanın kendi satırlarıyla tutmalı: kazanılan ile kaybedilen aynı
        // rakamda toplanırsa ekran başarıyı da başarısızlığı da aynı sayıyla gösterir.
        var tahta = await TahtaAsync();
        var sayaclar = tahta.GetProperty("counts");

        Assert.Equal(
            Say(tahta, p => p.TryGetProperty("outcome", out var o) && o.GetString() == "Kazanildi"),
            sayaclar.GetProperty("kazanildi").GetInt32());

        Assert.Equal(
            Say(tahta, p => p.TryGetProperty("outcome", out var o) && o.GetString() == "Kaybedildi"),
            sayaclar.GetProperty("kaybedildi").GetInt32());

        Assert.True(sayaclar.GetProperty("kazanildi").GetInt32() >= 1);
    }

    [Fact(DisplayName = "İA6. Takip sistemin DEĞERLENDİRMESİNİ taşır ama onu değiştirmez")]
    public async Task Degerlendirme_gosterilir_degismez()
    {
        // Firma bir ihaleyi işaretleyerek kendi puanını yükseltemez (CLAUDE.md §2.1).
        var oncesi = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/eligibility/{_factory.TenantA.AssessmentId}");

        var takipId = await TakipAsync();

        (await DurumAsync(takipId, "Hazirlaniyor")).EnsureSuccessStatusCode();

        var sonrasi = await _tenantAdmin.GetFromJsonAsync<JsonElement>(
            $"/api/eligibility/{_factory.TenantA.AssessmentId}");

        Assert.Equal(
            oncesi.GetProperty("finalScore").GetDecimal(),
            sonrasi.GetProperty("finalScore").GetDecimal());

        Assert.Equal(
            oncesi.GetProperty("verdict").GetString(),
            sonrasi.GetProperty("verdict").GetString());
    }

    [Fact(DisplayName = "İA7. Görüntüleyici takibi OKUR ama değiştiremez")]
    public async Task Goruntuleyici_degistiremez()
    {
        var takipId = await TakipAsync();
        var okuyucu = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA.ViewerEmail);

        var oku = await okuyucu.GetAsync($"/api/tenders/companies/{_companyId}");
        var ekle = await TakibeAlAsync(okuyucu);
        var degistir = await DurumAsync(takipId, "Hazirlaniyor", client: okuyucu);

        Assert.Equal(HttpStatusCode.OK, oku.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ekle.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, degistir.StatusCode);
    }

    [Fact(DisplayName = "İA8. Başka kiracı takibi göremez ve değiştiremez")]
    public async Task Baska_kiraci_erisemez()
    {
        var takipId = await TakipAsync();
        var digerKiraci = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);

        var oku = await digerKiraci.GetAsync($"/api/tenders/companies/{_companyId}");
        var degistir = await DurumAsync(takipId, "Hazirlaniyor", client: digerKiraci);

        Assert.NotEqual(HttpStatusCode.OK, oku.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, degistir.StatusCode);
    }

    [Fact(DisplayName = "İA9. Platform rolleri takip ekranına GİREMEZ")]
    public async Task Platform_rolleri_giremez()
    {
        foreach (var eposta in new[]
                 {
                     GovAiApiFactory.PlatformCatalogEmail,
                     GovAiApiFactory.PlatformReviewerEmail,
                 })
        {
            var client = await _factory.CreateAuthenticatedClientAsync(eposta);

            var oku = await client.GetAsync($"/api/tenders/companies/{_companyId}");

            Assert.Equal(HttpStatusCode.Forbidden, oku.StatusCode);
        }
    }

    [Fact(DisplayName = "İA10. Var olmayan çağrı takibe alınamaz")]
    public async Task Olmayan_cagri_takibe_alinamaz()
    {
        var cevap = await _tenantAdmin.PostAsJsonAsync(
            $"/api/tenders/companies/{_companyId}",
            new { opportunityId = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.NotFound, cevap.StatusCode);
    }
}
