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
/// Faz 2 – resmî belge hattının veri garantileri.
///
/// Tümü gerçek HTTP hattı üzerinden koşar. Canlı web sayfasına bağımlılık yoktur:
/// belgeler ingest ucundan verilir, böylece CI resmî kurum sitelerine erişmek zorunda
/// kalmaz ve o siteler gereksiz yüklenmez.
/// </summary>
public sealed class SourcePipelineTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _ingest = null!;    // SystemIngest — worker kimliği
    private HttpClient _catalog = null!;   // PlatformCatalogManager
    private HttpClient _tenantAdmin = null!; // kiracı yöneticisi

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        _ingest = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.SystemIngestEmail);
        _catalog = await _factory.CreateAuthenticatedClientAsync(GovAiApiFactory.PlatformCatalogEmail);
        _tenantAdmin = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Testin kendi kaynağını açar; başka testlerin belgeleriyle karışmasın.</summary>
    private async Task<Guid> CreateSourceAsync(string name)
    {
        var response = await _catalog.PostAsJsonAsync("/api/sources", new
        {
            name,
            type = "Ministry",
            baseUrl = "https://kurum.gov.tr",
            cronExpression = "0 6 * * *",
            configurationJson = "{\"linkSelector\":\"a.ilan\"}"
        });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> IngestAsync(
        Guid sourceId,
        string url,
        string title,
        string content,
        string? charset = "utf-8")
    {
        var response = await _ingest.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url,
            title,
            rawContent = content,
            mediaType = "text/html",
            canonicalUrl = url,
            charset,
            httpStatusCode = 200
        });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<T> QueryAsync<T>(Func<GovAiDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovAiDbContext>();
        return await query(db);
    }

    private const string GercekIcerik =
        "Şirketlerin Ar-Ge harcamalarına ilişkin usul ve esaslar hakkında tebliğ. " +
        "Bu tebliğ, 5746 sayılı Kanun kapsamında yapılan harcamaların belgelendirilmesine " +
        "ve indirim olarak dikkate alınmasına ilişkin uygulama esaslarını düzenler. " +
        "Yürürlük tarihi yayımı izleyen ayın ilk günüdür ve geriye dönük uygulanmaz.";

    // ═══════════════ Tekrar kaydetme ve sürümleme ═══════════════

    [Fact(DisplayName = "Faz2-A. Aynı URL ve aynı içerik ikinci kez kaydedilmez")]
    public async Task Ayni_url_ve_icerik_tekrar_kaydedilmez()
    {
        var sourceId = await CreateSourceAsync("Tekrar Testi");
        const string url = "https://kurum.gov.tr/ilan/tekrar-1";

        var ilk = await IngestAsync(sourceId, url, "Ar-Ge Tebliği", GercekIcerik);
        var ikinci = await IngestAsync(sourceId, url, "Ar-Ge Tebliği", GercekIcerik);

        Assert.True(ilk.GetProperty("isNew").GetBoolean());
        Assert.False(ikinci.GetProperty("isNew").GetBoolean());
        Assert.False(ikinci.GetProperty("contentChanged").GetBoolean());

        // Aynı içerik ikinci bir sürüm açmaz.
        Assert.Equal(1, ilk.GetProperty("revision").GetInt32());
        Assert.Equal(1, ikinci.GetProperty("revision").GetInt32());

        var documentId = ilk.GetProperty("documentId").GetGuid();
        var versiyonSayisi = await QueryAsync(db =>
            db.SourceDocumentVersions.CountAsync(v => v.SourceDocumentId == documentId));

        Assert.Equal(1, versiyonSayisi);
    }

    [Fact(DisplayName = "Faz2-B. İçerik değişince yeni belge sürümü oluşur ve eskisi durur")]
    public async Task Icerik_degisince_yeni_surum_olusur()
    {
        var sourceId = await CreateSourceAsync("Sürüm Testi");
        const string url = "https://kurum.gov.tr/ilan/surum-1";

        var ilk = await IngestAsync(sourceId, url, "Tebliğ", GercekIcerik);
        var ikinci = await IngestAsync(
            sourceId, url, "Tebliğ (değişik)", GercekIcerik + " Ek madde: geçici 2. madde eklenmiştir.");

        Assert.True(ikinci.GetProperty("contentChanged").GetBoolean());
        Assert.Equal(2, ikinci.GetProperty("revision").GetInt32());

        var documentId = ilk.GetProperty("documentId").GetGuid();

        var versiyonlar = await QueryAsync(db => db.SourceDocumentVersions
            .Where(v => v.SourceDocumentId == documentId)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync());

        // İki ayrı sürüm; eskisinin üstüne YAZILMAZ.
        Assert.Equal(2, versiyonlar.Count);
        Assert.Equal([1, 2], versiyonlar.Select(v => v.VersionNumber));
        Assert.NotEqual(versiyonlar[0].RawContentHash, versiyonlar[1].RawContentHash);

        // Eski sürümün içeriği hâlâ okunabilir — kanıt kaybolmaz.
        Assert.Contains("5746 sayılı Kanun", versiyonlar[0].RawContent, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Faz2-C. Belge sürümü charset ve nihai adresi kanıt olarak saklar")]
    public async Task Surum_charset_ve_kanonik_adresi_saklar()
    {
        var sourceId = await CreateSourceAsync("Kanıt Testi");
        const string url = "https://kurum.gov.tr/ilan/kanit-1";

        var sonuc = await IngestAsync(sourceId, url, "Tebliğ", GercekIcerik, charset: "windows-1254");
        var versionId = sonuc.GetProperty("documentVersionId").GetGuid();

        var version = await QueryAsync(db => db.SourceDocumentVersions.SingleAsync(v => v.Id == versionId));

        Assert.Equal("windows-1254", version.Charset);
        Assert.Equal(url, version.CanonicalUrl);
        Assert.Equal(200, version.HttpStatusCode);
        Assert.Equal(64, version.RawContentHash.Length);

        // Türkçe karakterler bozulmadan saklanır.
        Assert.Contains("Şirketlerin", version.RawContent, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Faz2-D. Ayrıştırma sonucu kanıt parçalarını üretir ve sürüme bağlar")]
    public async Task Ayristirma_kanit_parcalarini_uretir()
    {
        var sourceId = await CreateSourceAsync("Parça Testi");
        var sonuc = await IngestAsync(
            sourceId, "https://kurum.gov.tr/ilan/parca-1", "Tebliğ", GercekIcerik);

        var documentId = sonuc.GetProperty("documentId").GetGuid();
        var versionId = sonuc.GetProperty("documentVersionId").GetGuid();

        // Parçalar elle yazılmaz: parser worker'ın kullandığı uç çağrılır.
        var parse = await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new
            {
                status = "Parsed",
                normalizedText = GercekIcerik,
                title = "Ar-Ge Tebliği",
                language = "tr",
                pageCount = 1,
                chunks = new[]
                {
                    new
                    {
                        sequenceNumber = 0,
                        text = "Bu tebliğ, 5746 sayılı Kanun kapsamında yapılan harcamaların belgelendirilmesine ilişkindir.",
                        startOffset = 60,
                        endOffset = 152,
                        pageNumber = (int?)1,
                        sectionTitle = (string?)"Kapsam",
                        paragraphNumber = (int?)1
                    },
                    // Anlamsız parça: kaydedilmemeli.
                    new
                    {
                        sequenceNumber = 1,
                        text = "  ",
                        startOffset = 0,
                        endOffset = 2,
                        pageNumber = (int?)null,
                        sectionTitle = (string?)null,
                        paragraphNumber = (int?)null
                    }
                }
            });

        parse.EnsureSuccessStatusCode();
        var parseResult = await parse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Parsed", parseResult.GetProperty("status").GetString());
        Assert.Equal(1, parseResult.GetProperty("chunkCount").GetInt32());

        var chunk = await QueryAsync(db => db.DocumentEvidenceChunks
            .SingleAsync(c => c.DocumentVersionId == versionId));

        // Parçadan sürüme, sürümden belgeye, belgeden kaynağa zincir kuruluyor.
        Assert.Equal("Kapsam", chunk.SectionTitle);
        Assert.Equal(1, chunk.PageNumber);
        Assert.Equal(64, chunk.TextHash.Length);

        var zincir = await QueryAsync(db => db.SourceDocumentVersions
            .Where(v => v.Id == chunk.DocumentVersionId)
            .Select(v => new { v.SourceDocumentId, v.CanonicalUrl, v.ParseStatus, v.NormalizedTextHash })
            .SingleAsync());

        Assert.Equal(documentId, zincir.SourceDocumentId);
        Assert.Equal("https://kurum.gov.tr/ilan/parca-1", zincir.CanonicalUrl);
        Assert.Equal(DocumentParseStatus.Parsed, zincir.ParseStatus);
        Assert.Equal(64, zincir.NormalizedTextHash!.Length);
    }

    [Fact(DisplayName = "Faz2-D2. Taranmış PDF NeedsOcr olur ve uydurma metin üretilmez")]
    public async Task Taranmis_pdf_needs_ocr_olur()
    {
        var sourceId = await CreateSourceAsync("OCR Testi");
        var sonuc = await IngestAsync(
            sourceId, "https://kurum.gov.tr/ilan/ocr-1", "Taranmış Tebliğ", GercekIcerik);

        var documentId = sonuc.GetProperty("documentId").GetGuid();

        var parse = await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new { status = "NeedsOcr", pageCount = 3, error = "Taranmış PDF; metin katmanı yok." });

        parse.EnsureSuccessStatusCode();
        var parseResult = await parse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("NeedsOcr", parseResult.GetProperty("status").GetString());
        Assert.Equal(0, parseResult.GetProperty("chunkCount").GetInt32());

        var version = await QueryAsync(db => db.SourceDocumentVersions
            .SingleAsync(v => v.SourceDocumentId == documentId));

        Assert.True(version.RequiresOcr);
        Assert.Null(version.NormalizedText);

        // Belge silinmez; incelemeye alınır.
        var document = await QueryAsync(db => db.SourceDocuments.SingleAsync(d => d.Id == documentId));
        Assert.True(document.IsQuarantined);
    }

    [Fact(DisplayName = "Faz2-D3. Ayrıştırma hatası belgeyi silmez, karantinaya alır")]
    public async Task Ayristirma_hatasi_belgeyi_silmez()
    {
        var sourceId = await CreateSourceAsync("Ayrıştırma Hatası Testi");
        var sonuc = await IngestAsync(
            sourceId, "https://kurum.gov.tr/ilan/hata-1", "Bozuk Belge", GercekIcerik);

        var documentId = sonuc.GetProperty("documentId").GetGuid();

        var parse = await _ingest.PostAsJsonAsync(
            $"/api/sources/documents/{documentId}/parse-result",
            new { status = "Failed", error = "Ayrıştırma boş metin üretti." });

        parse.EnsureSuccessStatusCode();

        var document = await QueryAsync(db => db.SourceDocuments.SingleAsync(d => d.Id == documentId));

        Assert.Equal(QuarantineReason.ParserFailed, document.QuarantineReason);

        // Kayıt DURUYOR — silinmedi.
        var varMi = await QueryAsync(db => db.SourceDocuments.AnyAsync(d => d.Id == documentId));
        Assert.True(varMi);
    }

    // ═══════════════ Karantina ═══════════════

    [Fact(DisplayName = "Faz2-E. Zorunlu alanı eksik kayıt karantinaya gider")]
    public async Task Eksik_alanli_kayit_karantinaya_gider()
    {
        var sourceId = await CreateSourceAsync("Eksik Alan Testi");

        var sonuc = await IngestAsync(
            sourceId, "https://kurum.gov.tr/ilan/eksik-1", title: "", content: GercekIcerik);

        Assert.Equal("MissingRequiredFields", sonuc.GetProperty("quarantine").GetString());

        var documentId = sonuc.GetProperty("documentId").GetGuid();
        var document = await QueryAsync(db => db.SourceDocuments.SingleAsync(d => d.Id == documentId));

        // Silinmez — durur ve incelenebilir.
        Assert.True(document.IsQuarantined);
        Assert.Equal(QuarantineReason.MissingRequiredFields, document.QuarantineReason);
    }

    [Theory(DisplayName = "Faz2-F. Menü ve kurumsal sayfalar fırsat olarak kaydedilmez")]
    [InlineData("https://kurum.gov.tr/iletisim", "İletişim")]
    [InlineData("https://kurum.gov.tr/hakkimizda", "Hakkımızda")]
    [InlineData("https://kurum.gov.tr/tarihce", "Tarihçe")]
    [InlineData("https://kurum.gov.tr/kvkk", "KVKK Aydınlatma Metni")]
    public async Task Alakasiz_sayfalar_karantinaya_alinir(string url, string title)
    {
        var sourceId = await CreateSourceAsync($"Alakasız {title}");

        var sonuc = await IngestAsync(sourceId, url, title, GercekIcerik);

        Assert.Equal("InvalidSourcePage", sonuc.GetProperty("quarantine").GetString());
    }

    [Fact(DisplayName = "Faz2-G. Gerçek ilan karantinaya alınmaz")]
    public async Task Gercek_ilan_karantinaya_alinmaz()
    {
        var sourceId = await CreateSourceAsync("Geçerli İlan Testi");

        var sonuc = await IngestAsync(
            sourceId, "https://kurum.gov.tr/ilan/2026-14", "Ar-Ge Tebliği", GercekIcerik);

        Assert.Equal("None", sonuc.GetProperty("quarantine").GetString());
    }

    [Fact(DisplayName = "Faz2-H. Çok kısa içerik insana bırakılır, sessizce atılmaz")]
    public async Task Cok_kisa_icerik_incelemeye_gider()
    {
        var sourceId = await CreateSourceAsync("Kısa İçerik Testi");

        var sonuc = await IngestAsync(
            sourceId, "https://kurum.gov.tr/ilan/kisa-1", "Duyuru", "Kısa duyuru.");

        Assert.Equal("NeedsManualReview", sonuc.GetProperty("quarantine").GetString());
    }

    // ═══════════════ Veri kalitesi: NotProvided ═══════════════

    private async Task<JsonElement> UpsertOpportunityAsync(Guid sourceId, object payload)
    {
        var response = await _ingest.PostAsJsonAsync("/api/opportunities", payload);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact(DisplayName = "Faz2-M. Bulunamayan alanlar NotProvided olur, tahmin edilmez")]
    public async Task Bulunamayan_alanlar_notprovided_olur()
    {
        var sourceId = await CreateSourceAsync("Eksik Alan Fırsatı");

        var sonuc = await UpsertOpportunityAsync(sourceId, new
        {
            sourceId,
            sourceType = "Ministry",
            supportCategory = "Grant",
            title = "Yalnızca başlığı olan çağrı",
            publisher = "Test Kurumu",
            publishedAt = DateTimeOffset.UtcNow,
            summary = GercekIcerik,
            sourceUrl = "https://kurum.gov.tr/cagri/eksik"
            // deadline, budget, eligibleApplicant, geography, sector,
            // programmeType, officialDocumentUrl BİLEREK verilmedi.
        });

        var availability = sonuc.GetProperty("fieldAvailability");

        Assert.Equal("NotProvided", availability.GetProperty("deadline").GetString());
        Assert.Equal("NotProvided", availability.GetProperty("budget").GetString());
        Assert.Equal("NotProvided", availability.GetProperty("currency").GetString());
        Assert.Equal("NotProvided", availability.GetProperty("eligibleApplicant").GetString());
        Assert.Equal("NotProvided", availability.GetProperty("geography").GetString());
        Assert.Equal("NotProvided", availability.GetProperty("sector").GetString());
        Assert.Equal("NotProvided", availability.GetProperty("programmeType").GetString());
        Assert.Equal("NotProvided", availability.GetProperty("officialDocumentUrl").GetString());

        // Değerler UYDURULMAZ. API null alanları hiç yazmaz (WhenWritingNull), bu yüzden
        // alan ya yoktur ya da null'dır; ikisi de "değer üretilmedi" demektir.
        Assert.True(!sonuc.TryGetProperty("deadline", out var dl) || dl.ValueKind == JsonValueKind.Null);
        Assert.True(!sonuc.TryGetProperty("budget", out var bt) || bt.ValueKind == JsonValueKind.Null);
    }

    [Fact(DisplayName = "Faz2-N. Çıkarılan alanlar Provided olur")]
    public async Task Cikarilan_alanlar_provided_olur()
    {
        var sourceId = await CreateSourceAsync("Dolu Alan Fırsatı");

        var sonuc = await UpsertOpportunityAsync(sourceId, new
        {
            sourceId,
            sourceType = "Ministry",
            supportCategory = "Grant",
            title = "Tam künyeli çağrı",
            publisher = "Test Kurumu",
            publishedAt = DateTimeOffset.UtcNow,
            summary = GercekIcerik,
            sourceUrl = "https://kurum.gov.tr/cagri/tam",
            deadline = DateTimeOffset.UtcNow.AddDays(45),
            budget = new { minAmount = 100_000m, maxAmount = 1_000_000m, currency = "TRY", supportRate = 0.6m },
            eligibleApplicant = "KOBİ",
            geography = "TR62",
            sector = "Makine imalatı",
            programmeType = "KOBİGEL",
            officialDocumentUrl = "https://kurum.gov.tr/cagri/tam/belge.pdf"
        });

        var availability = sonuc.GetProperty("fieldAvailability");

        foreach (var alan in new[]
                 {
                     "deadline", "budget", "currency", "eligibleApplicant",
                     "geography", "sector", "programmeType", "officialDocumentUrl"
                 })
        {
            Assert.Equal("Provided", availability.GetProperty(alan).GetString());
        }
    }

    [Fact(DisplayName = "Faz2-O. Sürekli açık çağrıda son başvuru NotApplicable olur")]
    public async Task Surekli_acik_cagri_notapplicable_olur()
    {
        var sourceId = await CreateSourceAsync("Sürekli Açık Çağrı");

        var sonuc = await UpsertOpportunityAsync(sourceId, new
        {
            sourceId,
            sourceType = "Ministry",
            supportCategory = "Grant",
            title = "Sürekli açık çağrı",
            publisher = "Test Kurumu",
            publishedAt = DateTimeOffset.UtcNow,
            summary = GercekIcerik,
            sourceUrl = "https://kurum.gov.tr/cagri/surekli",
            isContinuouslyOpen = true
        });

        // "Eksik veri" ile "uygulanamaz" AYRI şeylerdir.
        Assert.Equal("NotApplicable", sonuc.GetProperty("fieldAvailability").GetProperty("deadline").GetString());
        Assert.Equal("NotProvided", sonuc.GetProperty("fieldAvailability").GetProperty("budget").GetString());
    }

    [Fact(DisplayName = "Faz2-P. Zorunlu alanı eksik fırsat karantinaya gider ve katalogda görünmez")]
    public async Task Zorunlu_alani_eksik_firsat_karantinaya_gider()
    {
        var sourceId = await CreateSourceAsync("Zorunlu Alan Testi");

        var sonuc = await UpsertOpportunityAsync(sourceId, new
        {
            sourceId,
            sourceType = "Ministry",
            supportCategory = "Grant",
            title = "İçeriksiz kayıt",
            publisher = "Test Kurumu",
            publishedAt = DateTimeOffset.UtcNow
            // summary ve sourceUrl YOK → zorunlu alan eksik.
        });

        var opportunityId = sonuc.GetProperty("id").GetGuid();

        var opportunity = await QueryAsync(db => db.Opportunities
            .IgnoreQueryFilters()
            .SingleAsync(o => o.Id == opportunityId));

        Assert.Equal(QuarantineReason.MissingRequiredFields, opportunity.QuarantineReason);
        Assert.Contains("içerik", opportunity.QuarantineNote!, StringComparison.Ordinal);

        // Katalogda GÖRÜNMEZ.
        var katalog = await _tenantAdmin.GetFromJsonAsync<JsonElement>("/api/opportunities?pageSize=100");
        var basliklar = katalog.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("title").GetString()).ToList();

        Assert.DoesNotContain("İçeriksiz kayıt", basliklar);
    }

    [Fact(DisplayName = "Faz2-R. Karantinadaki fırsat skorlanmaz")]
    public async Task Karantinadaki_firsat_skorlanmaz()
    {
        var sourceId = await CreateSourceAsync("Skorlama Karantina Testi");

        var sonuc = await UpsertOpportunityAsync(sourceId, new
        {
            sourceId,
            sourceType = "Ministry",
            supportCategory = "Grant",
            title = "Karantinaya alınacak çağrı",
            publisher = "Test Kurumu",
            publishedAt = DateTimeOffset.UtcNow,
            summary = GercekIcerik,
            sourceUrl = "https://kurum.gov.tr/cagri/karantina",
            deadline = DateTimeOffset.UtcNow.AddDays(30)
        });

        var opportunityId = sonuc.GetProperty("id").GetGuid();

        await QueryAsync(async db =>
        {
            var o = await db.Opportunities.SingleAsync(x => x.Id == opportunityId);
            o.Quarantine(QuarantineReason.InvalidSourcePage, "Menü sayfası.");
            await db.SaveChangesAsync();
            return true;
        });

        // Yeniden skorlama çalıştırılır; karantinadaki çağrı değerlendirmeye GİRMEZ.
        var rescore = await _tenantAdmin.PostAsync(
            $"/api/eligibility/companies/{_factory.TenantA.CompanyId}/rescore", null);

        rescore.EnsureSuccessStatusCode();

        var degerlendirmeVarMi = await QueryAsync(db => db.Assessments
            .IgnoreQueryFilters()
            .AnyAsync(a => a.OpportunityId == opportunityId));

        Assert.False(degerlendirmeVarMi, "Karantinadaki fırsat skorlanmamalı.");
    }

    // ═══════════════ Yetki ═══════════════

    [Fact(DisplayName = "Faz2-I. Kiracı kullanıcısı kaynak yapılandırmasını değiştiremez")]
    public async Task Kiraci_kullanicisi_kaynak_degistiremez()
    {
        var sourceId = await CreateSourceAsync("Yetki Testi");

        var olustur = await _tenantAdmin.PostAsJsonAsync("/api/sources", new
        {
            name = "Kiracının kaynağı",
            type = "Ministry",
            baseUrl = "https://baska.gov.tr",
            cronExpression = "0 6 * * *"
        });

        var guncelle = await _tenantAdmin.PutAsJsonAsync($"/api/sources/{sourceId}", new
        {
            name = "Ele geçirildi",
            type = "Ministry",
            baseUrl = "https://kotu.example",
            cronExpression = "0 6 * * *"
        });

        var kapat = await _tenantAdmin.PostAsJsonAsync($"/api/sources/{sourceId}/enabled", new { enabled = false });

        Assert.Equal(HttpStatusCode.Forbidden, olustur.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, guncelle.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, kapat.StatusCode);

        // Kaynak değişmedi.
        var source = await QueryAsync(db => db.Sources.SingleAsync(s => s.Id == sourceId));
        Assert.Equal("Yetki Testi", source.Name);
        Assert.Equal("https://kurum.gov.tr", source.BaseUrl);
    }

    [Fact(DisplayName = "Faz2-J. Kiracı kullanıcısı belge bırakamaz")]
    public async Task Kiraci_kullanicisi_belge_birakamaz()
    {
        var sourceId = await CreateSourceAsync("Belge Yetkisi Testi");

        var response = await _tenantAdmin.PostAsJsonAsync("/api/sources/documents", new
        {
            sourceId,
            url = "https://kurum.gov.tr/sahte",
            title = "Sahte belge",
            rawContent = GercekIcerik,
            mediaType = "text/html"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Faz2-K. SystemIngest kullanıcı ve şirket verisine erişemez")]
    public async Task SystemIngest_kullanici_ve_sirket_verisine_erisemez()
    {
        var kullanicilar = await _ingest.GetAsync("/api/admin/users");
        var sirketler = await _ingest.GetAsync("/api/companies");
        var uyeler = await _ingest.GetAsync($"/api/companies/{_factory.TenantA.CompanyId}/members");
        var rapor = await _ingest.GetAsync($"/api/reports/companies/{_factory.TenantA.CompanyId}/dashboard");

        Assert.Equal(HttpStatusCode.Forbidden, kullanicilar.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sirketler.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, uyeler.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rapor.StatusCode);
    }

    // ═══════════════ Mevzuat / fırsat ayrımı ═══════════════

    [Fact(DisplayName = "Faz2-L. Mevzuat fırsat tablosuna, fırsat mevzuat tablosuna yazılmaz")]
    public async Task Mevzuat_ve_firsat_tablolari_ayridir()
    {
        var sourceId = await CreateSourceAsync("Ayrım Testi");
        var sonuc = await IngestAsync(
            sourceId, "https://kurum.gov.tr/ilan/ayrim-1", "Vergi Tebliği", GercekIcerik);

        var documentId = sonuc.GetProperty("documentId").GetGuid();
        var versionId = sonuc.GetProperty("documentVersionId").GetGuid();

        await QueryAsync(async db =>
        {
            db.RegulatoryChanges.Add(new GovAI.Domain.Regulatory.RegulatoryChange(
                sourceId, documentId, versionId,
                jurisdiction: "TR",
                authority: "Gelir İdaresi Başkanlığı",
                domain: GovAI.Domain.Common.RegulationDomain.Tax,
                changeType: GovAI.Domain.Common.RegulatoryChangeType.Communique,
                title: "Ar-Ge Harcamaları Tebliği",
                officialUrl: "https://kurum.gov.tr/ilan/ayrim-1",
                contentHash: new string('a', 64),
                detectedAt: DateTimeOffset.UtcNow));

            await db.SaveChangesAsync();
            return true;
        });

        // Mevzuat kaydı fırsat kataloğunda GÖRÜNMEZ.
        var firsatlar = await _tenantAdmin.GetFromJsonAsync<JsonElement>("/api/opportunities?pageSize=100");
        var basliklar = firsatlar.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("title").GetString())
            .ToList();

        Assert.DoesNotContain("Ar-Ge Harcamaları Tebliği", basliklar);

        // İki tablo birbirinden bağımsız.
        var mevzuatSayisi = await QueryAsync(db => db.RegulatoryChanges.CountAsync());
        var firsatSayisi = await QueryAsync(db => db.Opportunities.CountAsync());

        Assert.True(mevzuatSayisi >= 1);
        Assert.True(firsatSayisi >= 1);

        var firsatBasliklari = await QueryAsync(db => db.Opportunities.Select(o => o.Title).ToListAsync());
        Assert.DoesNotContain("Ar-Ge Harcamaları Tebliği", firsatBasliklari);
    }

    // ═══════════════ Karantina inceleme ═══════════════

    [Fact(DisplayName = "Faz2-S. Karantina kararı mevcut değerlendirmeyi günceller, silmez")]
    public async Task Karantina_assessment_durumunu_gunceller()
    {
        var sourceId = await CreateSourceAsync("Değerlendirme Etkisi Testi");

        var belge = await IngestAsync(
            sourceId, "https://kurum.gov.tr/ilan/etki-1", "Ar-Ge Destek Çağrısı", GercekIcerik);

        var documentId = belge.GetProperty("documentId").GetGuid();

        var firsat = await UpsertOpportunityAsync(sourceId, new
        {
            sourceId,
            sourceDocumentId = documentId,
            sourceType = "Ministry",
            supportCategory = "Grant",
            title = "Ar-Ge Destek Çağrısı",
            publisher = "Test Kurumu",
            publishedAt = DateTimeOffset.UtcNow,
            summary = GercekIcerik,
            sourceUrl = "https://kurum.gov.tr/ilan/etki-1",
            deadline = DateTimeOffset.UtcNow.AddDays(45)
        });

        var opportunityId = firsat.GetProperty("id").GetGuid();

        // Önce skorlanır: elimizde geçerli bir değerlendirme olsun.
        var rescore = await _tenantAdmin.PostAsync(
            $"/api/eligibility/companies/{_factory.TenantA.CompanyId}/rescore", null);
        rescore.EnsureSuccessStatusCode();

        var oncekiSayi = await QueryAsync(db => db.Assessments
            .IgnoreQueryFilters()
            .CountAsync(a => a.OpportunityId == opportunityId && a.IsLatest));

        Assert.True(oncekiSayi > 0, "Karantina öncesinde geçerli bir değerlendirme bulunmalı.");

        // İnceleyici kaydı karantinaya alır.
        var reviewer = await _factory.CreateAuthenticatedClientAsync(
            GovAiApiFactory.PlatformReviewerEmail);

        var reddet = await reviewer.PostAsJsonAsync(
            $"/api/quarantine/{documentId}/reject",
            new { reason = "InvalidSourcePage", note = "Kurumsal menü sayfası." });

        Assert.Equal(HttpStatusCode.NoContent, reddet.StatusCode);

        var sonrakiGecerli = await QueryAsync(db => db.Assessments
            .IgnoreQueryFilters()
            .CountAsync(a => a.OpportunityId == opportunityId && a.IsLatest));

        var toplam = await QueryAsync(db => db.Assessments
            .IgnoreQueryFilters()
            .CountAsync(a => a.OpportunityId == opportunityId));

        // Değerlendirme SİLİNMEZ; yalnızca "en güncel" işareti kalkar.
        Assert.Equal(0, sonrakiGecerli);
        Assert.Equal(oncekiSayi, toplam);
    }

    [Fact(DisplayName = "Faz2-T. PlatformReviewer karantinayı yönetir, kiracı kullanıcısı yönetemez")]
    public async Task PlatformReviewer_karantina_yonetebilir()
    {
        var sourceId = await CreateSourceAsync("İnceleyici Yetkisi Testi");

        // Gerçek bir ilan kullanılır: rapor modunun hiçbir şeyi değiştirmediği ancak
        // temiz bir kayıtla gösterilebilir. (Kurumsal menü sayfası zaten ingest sırasında
        // ön elemeden karantinaya düşer; Faz2-N bunu ayrıca doğruluyor.)
        var belge = await IngestAsync(
            sourceId, "https://kurum.gov.tr/ilan/inceleyici-1", "Ar-Ge Destek Çağrısı", GercekIcerik);

        var documentId = belge.GetProperty("documentId").GetGuid();

        var reviewer = await _factory.CreateAuthenticatedClientAsync(
            GovAiApiFactory.PlatformReviewerEmail);

        // Rapor modu hiçbir şeyi değiştirmez.
        var rapor = await reviewer.PostAsync("/api/quarantine/triage?apply=false", null);
        rapor.EnsureSuccessStatusCode();

        var raporGovde = await rapor.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, raporGovde.GetProperty("applied").GetInt32());

        var raporSonrasi = await QueryAsync(db => db.SourceDocuments
            .SingleAsync(d => d.Id == documentId));

        Assert.Equal(QuarantineReason.None, raporSonrasi.QuarantineReason);

        // Karantinaya alma ve geri çıkarma da inceleyicinin yetkisindedir.
        var reddet = await reviewer.PostAsJsonAsync(
            $"/api/quarantine/{documentId}/reject",
            new { reason = "NeedsManualReview", note = "İnceleme bekliyor." });

        var liste = await reviewer.GetAsync("/api/quarantine");
        var onayla = await reviewer.PostAsync($"/api/quarantine/{documentId}/approve", null);

        Assert.Equal(HttpStatusCode.NoContent, reddet.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liste.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, onayla.StatusCode);

        var cikarilan = await QueryAsync(db => db.SourceDocuments
            .SingleAsync(d => d.Id == documentId));

        Assert.Equal(QuarantineReason.None, cikarilan.QuarantineReason);

        // Kiracı kullanıcısı bu uçların hiçbirine giremez.
        var kiraciListe = await _tenantAdmin.GetAsync("/api/quarantine");
        var kiraciTriyaj = await _tenantAdmin.PostAsync("/api/quarantine/triage?apply=true", null);
        var kiraciReddet = await _tenantAdmin.PostAsJsonAsync(
            $"/api/quarantine/{documentId}/reject", new { reason = "Duplicate", note = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, kiraciListe.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, kiraciTriyaj.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, kiraciReddet.StatusCode);
    }
}
