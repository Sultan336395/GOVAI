using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovAI.Domain.Common;
using GovAI.Domain.Sources;
using GovAI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovAI.Api.Tests;

/// <summary>
/// Fırsat detay ekranının veri garantileri (Faz 2).
///
/// <para>
/// Detay ekranı kullanıcının karar verdiği yerdir: buradaki her bilgi resmî bir
/// belgeye dayanmak, dayanmayan her alan "Resmî kaynakta belirtilmemiş" görünmek
/// zorundadır. Bu testler ekranın gösterebileceği veriyi API sınırında sabitler.
/// </para>
/// </summary>
public sealed class OpportunityDetailTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _catalog = null!;   // PlatformCatalogManager — yazma yetkisi
    private HttpClient _ingest = null!;    // SystemIngest — worker
    private HttpClient _tenantA = null!;
    private HttpClient _tenantB = null!;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _catalog = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformCatalogEmail);
        _ingest = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);
        _tenantA = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _tenantB = await _factory.CreateAuthenticatedClientAsync(_factory.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> QueryAsync<T>(Func<GovAiDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        return await query(db);
    }

    /// <summary>
    /// API <c>null</c> alanları gövdeye hiç yazmaz (<c>WhenWritingNull</c>). "Yok" ile
    /// "null" bu yüzden aynı anlama gelir; testler ikisini de kabul etmelidir.
    /// </summary>
    private static bool Bos(JsonElement govde, string alan) =>
        !govde.TryGetProperty(alan, out var deger) || deger.ValueKind == JsonValueKind.Null;

    private static string? Metin(JsonElement govde, string alan) =>
        govde.TryGetProperty(alan, out var deger) && deger.ValueKind == JsonValueKind.String
            ? deger.GetString()
            : null;

    /// <summary>Resmî alan adı tanımlı bir ihale kaynağı açar.</summary>
    private async Task<Guid> IhaleKaynagiAsync(string name)
    {
        var response = await _catalog.PostAsJsonAsync("/api/sources", new
        {
            name,
            type = "TenderPortal",
            baseUrl = "https://www.resmigazete.gov.tr",
            cronExpression = "0 6 * * *"
        });

        response.EnsureSuccessStatusCode();
        var sourceId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Künye ve tarama planı: resmî alan adı olmadan bağlantı DOĞRULANAMAZ.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        var source = await db.Sources.SingleAsync(s => s.Id == sourceId);

        source.Describe(
            SourceCategory.Tender,
            new SourceProfile("Resmî Gazete", "TR", "resmigazete.gov.tr", "tr"));

        source.PlanCrawl(new SourceCrawlPlan(
            "https://www.resmigazete.gov.tr/ilanlar/",
            "a.ilan",
            "#icerik",
            @"/ilanlar/eskiilanlar/\d{4}/\d{2}/\d{8}-\d+\.htm$",
            3,
            "resmigazete.gov.tr",
            "text/html"));

        source.MarkVerified(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        return sourceId;
    }

    private async Task<Guid> FirsatAsync(
        Guid sourceId,
        string title,
        string sourceUrl,
        DateTimeOffset? deadline = null)
    {
        var response = await _catalog.PostAsJsonAsync("/api/opportunities", new
        {
            sourceId,
            sourceType = "TenderPortal",
            supportCategory = "Tender",
            title,
            publisher = "Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğü",
            summary = "Taşınmaz satış ihalesi; şartname bedeli ve teminat ilanda belirtilmiştir.",
            sourceUrl,
            // Yayın tarihi son başvurudan önce olmalı; süresi geçmiş çağrı senaryosu
            // için de geçerli bir geçmiş yayın tarihi gerekir.
            publishedAt = (deadline ?? DateTimeOffset.UtcNow).AddDays(-30),
            deadline,
            rules = Array.Empty<object>(),
            documentChecklist = Array.Empty<object>()
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    // ═══════════════ Bölüm başlığı fırsat olmaz ═══════════════

    [Theory(DisplayName = "FD1. Toplu bölüm başlığından fırsat kaydı açılamaz")]
    [InlineData("ARTIRMA, EKSİLTME VE İHALE İLÂNLARI")]
    [InlineData("ÇEŞİTLİ İLÂNLAR")]
    [InlineData("İLAN BÖLÜMÜ")]
    public async Task Bolum_basligindan_firsat_acilamaz(string baslik)
    {
        var sourceId = await IhaleKaynagiAsync($"Bölüm Başlığı Testi {baslik.Length}");

        var response = await _catalog.PostAsJsonAsync("/api/opportunities", new
        {
            sourceId,
            sourceType = "TenderPortal",
            supportCategory = "Tender",
            title = baslik,
            publisher = "Resmî Gazete",
            publishedAt = DateTimeOffset.UtcNow,
            rules = Array.Empty<object>(),
            documentChecklist = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var govde = await response.Content.ReadAsStringAsync();
        Assert.Contains("bölüm", govde, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "FD2. Gerçek tekil ihale kaydı açılır")]
    public async Task Gercek_tekil_ihale_acilir()
    {
        var sourceId = await IhaleKaynagiAsync("Tekil İhale Testi");

        var id = await FirsatAsync(
            sourceId,
            "Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğünden: TAŞINMAZ SATILACAKTIR",
            "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-1.htm",
            DateTimeOffset.UtcNow.AddDays(20));

        var detay = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{id}");

        Assert.Equal("Tender", detay.GetProperty("supportCategory").GetString());
        Assert.True(detay.GetProperty("isOpen").GetBoolean());
        Assert.Contains("TAŞINMAZ SATILACAKTIR", detay.GetProperty("title").GetString()!, StringComparison.Ordinal);
    }

    // ═══════════════ Resmî kaynak bağlantısı ═══════════════

    [Fact(DisplayName = "FD3. Resmî alan adındaki bağlantı doğrulanmış gösterilir")]
    public async Task Resmi_baglanti_dogrulanir()
    {
        var sourceId = await IhaleKaynagiAsync("Bağlantı Doğrulama Testi");
        const string adres = "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-2.htm";

        var id = await FirsatAsync(sourceId, "İhale ilânı: HİZMET ALINACAKTIR", adres);

        var detay = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{id}");
        var kanit = detay.GetProperty("provenance");

        Assert.Equal(adres, kanit.GetProperty("officialUrl").GetString());
        Assert.True(Bos(kanit, "officialUrlRejectionReason"), "Doğrulanan bağlantıda ret sebebi olmamalı.");
        Assert.Equal("resmigazete.gov.tr", kanit.GetProperty("officialDomain").GetString());
    }

    [Fact(DisplayName = "FD4. Resmî olmayan alan adı kaynak olarak gösterilmez")]
    public async Task Resmi_olmayan_baglanti_gosterilmez()
    {
        var sourceId = await IhaleKaynagiAsync("Sahte Bağlantı Testi");

        var id = await FirsatAsync(
            sourceId,
            "İhale ilânı: MAL ALINACAKTIR",
            "https://resmigazete.gov.tr.kotu-site.com/ilan/1");

        var detay = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{id}");
        var kanit = detay.GetProperty("provenance");

        // Düğme HİÇ gösterilmez; sebebi kayıtta durur.
        Assert.True(Bos(kanit, "officialUrl"), "Doğrulanmayan adres gövdede yer almamalı.");
        Assert.Contains(
            "resmî alan adında",
            Metin(kanit, "officialUrlRejectionReason")!,
            StringComparison.Ordinal);
    }

    // ═══════════════ Eksik alanlar ═══════════════

    [Fact(DisplayName = "FD5. Bulunmayan alanlar tahmin edilmez, eksik olarak işaretlenir")]
    public async Task Eksik_alanlar_isaretlenir()
    {
        var sourceId = await IhaleKaynagiAsync("Eksik Alan Testi");

        // Bütçe, son başvuru ve coğrafya verilmedi.
        var id = await FirsatAsync(
            sourceId,
            "İhale ilânı: YAPIM İŞİ İHALE EDİLECEKTİR",
            "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-3.htm");

        var detay = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{id}");

        Assert.True(Bos(detay, "budget"), "Bütçe uydurulmamalı.");
        Assert.True(Bos(detay, "deadline"), "Son başvuru tarihi uydurulmamalı.");

        // Alanın neden boş olduğu ayrıca kayıtlıdır: uydurulmaz.
        var durum = detay.GetProperty("fieldAvailability");
        Assert.Equal("NotProvided", durum.GetProperty("budget").GetString());
        Assert.Equal("NotProvided", durum.GetProperty("deadline").GetString());
    }

    // ═══════════════ Süresi geçmiş çağrı ═══════════════

    [Fact(DisplayName = "FD6. Son başvuru tarihi geçmiş fırsat açık gösterilmez")]
    public async Task Suresi_gecmis_firsat_acik_gosterilmez()
    {
        var sourceId = await IhaleKaynagiAsync("Süre Testi");

        var id = await FirsatAsync(
            sourceId,
            "İhale ilânı: KİRAYA VERİLECEKTİR",
            "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-4.htm",
            DateTimeOffset.UtcNow.AddDays(-3));

        var detay = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{id}");

        Assert.False(detay.GetProperty("isOpen").GetBoolean());
        Assert.True(detay.GetProperty("daysUntilDeadline").GetInt32() < 0);
    }

    // ═══════════════ Karantina ═══════════════

    [Fact(DisplayName = "FD7. Karantinadaki fırsat detay ekranında açılmaz")]
    public async Task Karantinadaki_firsat_detayi_acilmaz()
    {
        var sourceId = await IhaleKaynagiAsync("Karantina Testi");

        var id = await FirsatAsync(
            sourceId,
            "İhale ilânı: DANIŞMANLIK HİZMETİ ALINACAKTIR",
            "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-5.htm");

        // Önce görünüyor.
        Assert.Equal(HttpStatusCode.OK, (await _tenantA.GetAsync($"/api/opportunities/{id}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
            var firsat = await db.Opportunities.IgnoreQueryFilters().SingleAsync(o => o.Id == id);
            firsat.Quarantine(QuarantineReason.InvalidSourcePage, "Liste sayfası.");
            await db.SaveChangesAsync();
        }

        // Karantinaya alındıktan sonra bağlantı elden ele dolaşsa bile açılmaz.
        var sonra = await _tenantA.GetAsync($"/api/opportunities/{id}");
        Assert.Equal(HttpStatusCode.NotFound, sonra.StatusCode);
    }

    // ═══════════════ Şirket yalıtımı ═══════════════

    [Fact(DisplayName = "FD8. Başka şirkete ait eşleşme adres değiştirilerek görüntülenemez")]
    public async Task Baska_sirketin_eslesmesi_goruntulenemez()
    {
        var kiraciAdegerlendirme = _factory.TenantA.AssessmentId;

        // Kendi eşleşmesini görebiliyor.
        var kendi = await _tenantA.GetAsync($"/api/eligibility/{kiraciAdegerlendirme}");
        Assert.Equal(HttpStatusCode.OK, kendi.StatusCode);

        // Başkasınınkini adresi bilse bile göremiyor.
        var baskasi = await _tenantB.GetAsync($"/api/eligibility/{kiraciAdegerlendirme}");
        Assert.True(
            baskasi.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Beklenen 404/403, gelen {(int)baskasi.StatusCode}.");
    }

    // ═══════════════ Kanıt zinciri ═══════════════

    [Fact(DisplayName = "FD9. Kanıt parçaları belge sürümü ve hash ile birlikte gelir")]
    public async Task Kanit_parcalari_kunyesiyle_gelir()
    {
        var sourceId = await IhaleKaynagiAsync("Kanıt Zinciri Testi");
        const string adres = "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-6.htm";

        // Belgeyi gerçek hattan geçir: ingest → parse-result → kanıt parçaları.
        var belge = await _ingest.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url = adres,
            title = "Mersin Su ve Kanalizasyon İdaresinden: TAŞINMAZ SATILACAKTIR",
            rawContent =
                "Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğünden: TAŞINMAZ SATILACAKTIR. "
                + "İhale 15/09/2026 tarihinde saat 10.00'da İdare binasında yapılacaktır. "
                + "Muhammen bedel 4.250.000,00 TL, geçici teminat %3 oranındadır. "
                + "İhaleye katılacakların şartnamede belirtilen belgeleri sunmaları zorunludur.",
            mediaType = "text/html",
            canonicalUrl = adres,
            charset = "utf-8",
            httpStatusCode = 200
        });

        belge.EnsureSuccessStatusCode();
        var documentId = (await belge.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();

        var parse = await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new
            {
                status = "Parsed",
                normalizedText =
                    "Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğünden: TAŞINMAZ SATILACAKTIR. "
                    + "İhale 15/09/2026 tarihinde saat 10.00'da İdare binasında yapılacaktır. "
                    + "Muhammen bedel 4.250.000,00 TL, geçici teminat %3 oranındadır.",
                pageCount = 1,
                chunks = new[]
                {
                    new
                    {
                        sequenceNumber = 1,
                        pageNumber = 1,
                        sectionTitle = "İhale konusu",
                        paragraphNumber = 1,
                        text = "Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğünden: TAŞINMAZ SATILACAKTIR.",
                        startOffset = 0,
                        endOffset = 78
                    }
                }
            });

        parse.EnsureSuccessStatusCode();

        // Fırsat kaydı bu belgeye bağlanır.
        var firsat = await _catalog.PostAsJsonAsync("/api/opportunities", new
        {
            sourceId,
            sourceDocumentId = documentId,
            sourceType = "TenderPortal",
            supportCategory = "Tender",
            title = "Mersin Su ve Kanalizasyon İdaresinden: TAŞINMAZ SATILACAKTIR",
            publisher = "Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğü",
            summary = "Taşınmaz satış ihalesi.",
            sourceUrl = adres,
            publishedAt = DateTimeOffset.UtcNow.AddDays(-1),
            rules = Array.Empty<object>(),
            documentChecklist = Array.Empty<object>()
        });

        firsat.EnsureSuccessStatusCode();
        var id = (await firsat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var detay = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{id}");
        var kanit = detay.GetProperty("provenance");

        Assert.Equal(documentId, kanit.GetProperty("documentId").GetGuid());
        Assert.Equal(1, kanit.GetProperty("documentVersion").GetInt32());
        Assert.Equal(adres, kanit.GetProperty("officialUrl").GetString());
        Assert.Equal(64, kanit.GetProperty("contentHash").GetString()!.Length);

        var parcalar = kanit.GetProperty("evidence");
        Assert.True(parcalar.GetArrayLength() >= 1, "En az bir kanıt parçası olmalı.");

        var ilk = parcalar[0];
        Assert.Equal(1, ilk.GetProperty("pageNumber").GetInt32());
        Assert.Equal("İhale konusu", ilk.GetProperty("sectionTitle").GetString());
        Assert.Equal(64, ilk.GetProperty("textHash").GetString()!.Length);
        Assert.True(ilk.GetProperty("endOffset").GetInt32() > ilk.GetProperty("startOffset").GetInt32());
        Assert.Contains("TAŞINMAZ SATILACAKTIR", ilk.GetProperty("text").GetString()!, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "FD10. Aynı belge iki kez işlendiğinde mükerrer fırsat oluşmaz")]
    public async Task Ayni_belge_mukerrer_firsat_olusturmaz()
    {
        var sourceId = await IhaleKaynagiAsync("Mükerrer Fırsat Testi");
        const string adres = "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-7.htm";

        var documentId = Guid.CreateVersion7();

        var belge = await _ingest.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url = adres,
            title = "TCDD 3. Bölge Müdürlüğünden: HİZMET ALINACAKTIR",
            rawContent =
                "TCDD 3. Bölge Müdürlüğünden: HİZMET ALINACAKTIR. İhale 20/09/2026 tarihinde "
                + "yapılacaktır. Şartname bedeli 500,00 TL olup İdareden temin edilebilir. "
                + "Katılım için yeterlik belgeleri sunulması zorunludur.",
            mediaType = "text/html",
            canonicalUrl = adres,
            charset = "utf-8",
            httpStatusCode = 200
        });

        belge.EnsureSuccessStatusCode();
        documentId = (await belge.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();

        object Payload() => new
        {
            sourceId,
            sourceDocumentId = documentId,
            sourceType = "TenderPortal",
            supportCategory = "Tender",
            title = "TCDD 3. Bölge Müdürlüğünden: HİZMET ALINACAKTIR",
            publisher = "TCDD 3. Bölge Müdürlüğü",
            summary = "Hizmet alım ihalesi.",
            sourceUrl = adres,
            publishedAt = DateTimeOffset.UtcNow.AddDays(-1),
            rules = Array.Empty<object>(),
            documentChecklist = Array.Empty<object>()
        };

        var ilk = await _catalog.PostAsJsonAsync("/api/opportunities", Payload());
        var ikinci = await _catalog.PostAsJsonAsync("/api/opportunities", Payload());

        ilk.EnsureSuccessStatusCode();
        ikinci.EnsureSuccessStatusCode();

        var ilkId = (await ilk.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var ikinciId = (await ikinci.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(ilkId, ikinciId);

        var sayi = await QueryAsync(db => db.Opportunities
            .IgnoreQueryFilters()
            .CountAsync(o => o.SourceDocumentId == documentId));

        Assert.Equal(1, sayi);
    }

    [Fact(DisplayName = "FD12. Düzelen kurum adı katalogda güncellenir")]
    public async Task Kurum_adi_tazelenir()
    {
        // İhaleyi açan idare belgenin içinde yazılıdır ve ayrıştırıcı onu sonradan
        // çıkarabilir hâle gelebilir. Kurum yalnızca kayıt AÇILIRKEN yazılsaydı,
        // düzelme kullanıcıya hiç ulaşmazdı.
        var sourceId = await IhaleKaynagiAsync("Kurum Tazeleme Testi");
        const string adres = "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/09/20260906-3-9.pdf";

        var belge = await _ingest.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url = adres,
            title = "TAŞINMAZ SATILACAKTIR",
            rawContent =
                "TAŞINMAZ SATILACAKTIR. Çay İşletmeleri Genel Müdürlüğünden: İhale "
                + "18/09/2026 tarihinde yapılacaktır. Şartname İdareden temin edilir.",
            mediaType = "text/html",
            canonicalUrl = adres,
            charset = "utf-8",
            httpStatusCode = 200
        });

        belge.EnsureSuccessStatusCode();
        var documentId = (await belge.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("documentId").GetGuid();

        object Payload(string kurum) => new
        {
            sourceId,
            sourceDocumentId = documentId,
            sourceType = "TenderPortal",
            supportCategory = "Tender",
            title = "TAŞINMAZ SATILACAKTIR",
            publisher = kurum,
            summary = "Taşınmaz satış ihalesi.",
            sourceUrl = adres,
            publishedAt = DateTimeOffset.UtcNow.AddDays(-1),
            rules = Array.Empty<object>(),
            documentChecklist = Array.Empty<object>()
        };

        // İlk yakalanış: idare çıkarılamadı, kaynağın adı yazıldı.
        var ilk = await _catalog.PostAsJsonAsync("/api/opportunities", Payload("Resmî Gazete İhale İlanları"));
        ilk.EnsureSuccessStatusCode();
        var id = (await ilk.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Yeniden ayrıştırma: gerçek idare çıkarıldı.
        var ikinci = await _catalog.PostAsJsonAsync(
            "/api/opportunities", Payload("Çay İşletmeleri Genel Müdürlüğünden"));
        ikinci.EnsureSuccessStatusCode();

        // Aynı kayıt güncellendi, mükerrer açılmadı.
        Assert.Equal(id, (await ikinci.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

        var detay = await _tenantA.GetFromJsonAsync<JsonElement>($"/api/opportunities/{id}");
        Assert.Equal("Çay İşletmeleri Genel Müdürlüğünden", detay.GetProperty("publisher").GetString());
    }

    [Fact(DisplayName = "FD11. İhale kaydı mevzuat listesine karışmaz")]
    public async Task Ihale_mevzuata_karismaz()
    {
        var sourceId = await IhaleKaynagiAsync("Ayrım Testi");

        var id = await FirsatAsync(
            sourceId,
            "İhale ilânı: ARAÇ KİRALAMA HİZMETİ ALINACAKTIR",
            "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-8.htm");

        // Fırsat kataloğunda var.
        Assert.Equal(HttpStatusCode.OK, (await _tenantA.GetAsync($"/api/opportunities/{id}")).StatusCode);

        // Mevzuat kayıtlarına girmemiş.
        var mevzuatSayisi = await QueryAsync(db => db.RegulatoryChanges
            .IgnoreQueryFilters()
            .CountAsync(r => r.SourceId == sourceId));

        Assert.Equal(0, mevzuatSayisi);
    }
}
