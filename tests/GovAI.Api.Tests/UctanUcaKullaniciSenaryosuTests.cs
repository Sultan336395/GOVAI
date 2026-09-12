using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GovAI.Api.Tests;

/// <summary>
/// Uçtan uca kullanıcı senaryosu: bir danışmanın tek oturumda yaptığı iş.
///
/// <para>
/// Tek tek uçların çalışması, <b>akışın</b> çalıştığını göstermez. Sahada kırılan
/// şeyler genelde uçların kendisi değil aralarındaki bağlardır: skorlama bildirim
/// üretmez, rapor boş çıkar, rapordaki kimlik başka ekranda bulunamaz. Bu test o
/// bağları sırayla yürür ve her adımın çıktısını bir sonrakine <b>gerçekten</b>
/// taşır.
/// </para>
///
/// <para>
/// Gerçek HTTP hattından geçer: kimlik doğrulama, yetkilendirme, EF sorgu süzgeçleri
/// ve controller'lar üretimdeki gibi devrededir.
/// </para>
/// </summary>
public sealed class UctanUcaKullaniciSenaryosuTests(GovAiApiFactory factory)
    : IClassFixture<GovAiApiFactory>, IAsyncLifetime
{
    private readonly GovAiApiFactory _factory = factory;

    private HttpClient _kullanici = null!;
    private Guid _companyId;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();

        // 1. adım: kullanıcı giriş yapar. Buradan sonrası gerçek jetonla yürür.
        _kullanici = await _factory.CreateAuthenticatedClientAsync(_factory.TenantA);
        _companyId = _factory.TenantA.CompanyId;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> OkuAsync(string yol)
    {
        var cevap = await _kullanici.GetAsync(yol);

        Assert.True(cevap.IsSuccessStatusCode, $"{yol} → {cevap.StatusCode}");

        return await cevap.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact(DisplayName = "UU1. Giriş → profil → skorlama → eşleşmeler zinciri kopmaz")]
    public async Task Giristen_eslesmelere()
    {
        // Kimlik gerçekten kurulmuş mu?
        var ben = await OkuAsync("/api/auth/me");

        Assert.False(string.IsNullOrWhiteSpace(ben.GetProperty("email").GetString()));

        // Firma profili okunabiliyor mu?
        var firma = await OkuAsync($"/api/company-profile/{_companyId}");

        Assert.Equal(_companyId, firma.GetProperty("id").GetGuid());

        // Skorlama tetiklenir ve gerçekten değerlendirme üretir.
        var skorla = await _kullanici.PostAsync(
            $"/api/eligibility/companies/{_companyId}/rescore", null);

        skorla.EnsureSuccessStatusCode();

        var eslesmeler = await OkuAsync($"/api/eligibility/companies/{_companyId}/matches");
        var satirlar = (eslesmeler.ValueKind == JsonValueKind.Array
            ? eslesmeler
            : eslesmeler.GetProperty("items")).EnumerateArray().ToList();

        Assert.NotEmpty(satirlar);

        // Her satır sektör uyumunu taşımalı: yalnızca skor gösteren bir liste,
        // sektörü doğrulanamamış yüksek puanlı çağrıyı güvenli gibi gösterirdi.
        foreach (var satir in satirlar)
        {
            Assert.True(satir.TryGetProperty("sectorFit", out _));
        }
    }

    [Fact(DisplayName = "UU2. Değerlendirme AÇIKLANABİLİR: gerekçe ve dayanak taşır")]
    public async Task Degerlendirme_aciklanabilir()
    {
        // Ürünün ikinci iddiası. Skor gelip gerekçe gelmiyorsa iddia boştur.
        var detay = await OkuAsync($"/api/eligibility/{_factory.TenantA.AssessmentId}");

        Assert.True(detay.TryGetProperty("finalScore", out _));
        Assert.True(detay.TryGetProperty("verdict", out _));
        Assert.True(detay.TryGetProperty("dimensions", out var boyutlar));

        foreach (var boyut in boyutlar.EnumerateArray())
        {
            // Her boyut neden o puanı aldığını söylemeli.
            Assert.False(string.IsNullOrWhiteSpace(boyut.GetProperty("rationale").GetString()));
        }
    }

    [Fact(DisplayName = "UU3. Haftalık rapor ÜRETİLİR ve sekiz bölümü de taşır")]
    public async Task Haftalik_rapor_uretilir()
    {
        var rapor = await UretilmisRaporAsync();

        var icerik = rapor.GetProperty("content");

        foreach (var bolum in new[]
                 {
                     "header", "supports", "technologyTenders", "otherOpportunities",
                     "regulatoryChanges", "risks", "pastPeriodGaps", "deadlines", "todos", "notes",
                 })
        {
            Assert.True(icerik.TryGetProperty(bolum, out _), $"Bölüm eksik: {bolum}");
        }

        var baslik = icerik.GetProperty("header");

        Assert.Equal(_companyId, baslik.GetProperty("companyId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(baslik.GetProperty("companyName").GetString()));
    }

    [Fact(DisplayName = "UU4. Rapor KENDİ İÇİNDE tutarlı: boş bölümün sebebi yazılı")]
    public async Task Rapor_tutarli()
    {
        // Sahada görülen hata: rapor "uygun destek bulunamadı" derken aynı sayfada
        // bir çağrıyı acil iş olarak gösteriyordu.
        var icerik = (await UretilmisRaporAsync()).GetProperty("content");

        var destekSayisi = icerik.GetProperty("supports").GetArrayLength();
        var digerSayisi = icerik.GetProperty("otherOpportunities").GetArrayLength();
        var notlar = icerik.GetProperty("notes").EnumerateArray().ToList();

        if (destekSayisi == 0 && digerSayisi == 0)
        {
            // Boş bölüm sessizce geçilmez; sebebi raporun kendi notunda olmalı.
            Assert.NotEmpty(notlar);
        }

        // Riskler yalnızca listelenen çağrılardan türemeli; kapanmış çağrının eksiği
        // ayrı bölümde durur.
        Assert.True(icerik.TryGetProperty("pastPeriodGaps", out _));
    }

    [Fact(DisplayName = "UU5. Rapor PDF ve Excel olarak indirilebilir")]
    public async Task Rapor_disa_aktarilir()
    {
        var raporId = (await UretilmisRaporAsync()).GetProperty("id").GetGuid();

        foreach (var (bicim, tur, imza) in new[]
                 {
                     // PDF "%PDF", xlsx bir ZIP arşividir ve "PK" ile başlar.
                     ("pdf", "application/pdf", "%PDF"u8.ToArray()),
                     ("excel", "spreadsheet", "PK"u8.ToArray()),
                 })
        {
            var cevap = await _kullanici.GetAsync($"/api/reports/weekly/{raporId}/{bicim}");

            cevap.EnsureSuccessStatusCode();

            var icerik = await cevap.Content.ReadAsByteArrayAsync();

            // Boş bir dosya "indirildi" sayılmamalı.
            Assert.True(icerik.Length > 1000, $"{bicim} dosyası çok küçük: {icerik.Length} bayt");
            Assert.Contains(tur, cevap.Content.Headers.ContentType?.MediaType ?? string.Empty);

            // İçerik türü başlığı DOĞRU OLSA DA gövde hata sayfası olabilir; dosyanın
            // gerçekten o biçimde olduğu imzasından doğrulanır.
            Assert.True(
                icerik.Take(imza.Length).SequenceEqual(imza),
                $"{bicim} dosyası beklenen biçimde değil.");

            // Dosya adı verilmeli; yoksa tarayıcı "download" diye kaydeder.
            Assert.False(string.IsNullOrWhiteSpace(
                cevap.Content.Headers.ContentDisposition?.FileNameStar
                ?? cevap.Content.Headers.ContentDisposition?.FileName));
        }
    }

    [Fact(DisplayName = "UU6. Rapora soru sorulur, cevap gelir ve hak düşer")]
    public async Task Rapora_soru_sorulur()
    {
        var raporId = (await UretilmisRaporAsync()).GetProperty("id").GetGuid();

        var durum = await OkuAsync($"/api/reports/weekly/{raporId}/questions");

        Assert.Equal(5, durum.GetProperty("totalQuota").GetInt32());

        var sorular = durum.GetProperty("available").EnumerateArray().ToList();

        Assert.NotEmpty(sorular);

        var cevap = await _kullanici.PostAsJsonAsync(
            $"/api/reports/weekly/{raporId}/questions",
            new { questionKey = sorular[0].GetProperty("key").GetString() });

        cevap.EnsureSuccessStatusCode();

        var sonuc = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(4, sonuc.GetProperty("remainingCount").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(
            sonuc.GetProperty("inquiry").GetProperty("answerText").GetString()));
    }

    [Fact(DisplayName = "UU7. İhale takibe alınır ve dashboard hunisine YANSIR")]
    public async Task Takip_dashboarda_yansir()
    {
        // Ekranlar arası bağ: takip ekranında yapılan iş, genel bakışta görünmeli.
        var oncesi = (await OkuAsync($"/api/reports/companies/{_companyId}/dashboard/insights"))
            .GetProperty("funnel").GetProperty("tracked").GetInt32();

        var takip = await _kullanici.PostAsJsonAsync(
            $"/api/tenders/companies/{_companyId}",
            new { opportunityId = _factory.TenantA.OpportunityId, note = "Şartname okunuyor." });

        takip.EnsureSuccessStatusCode();

        var sonrasi = (await OkuAsync($"/api/reports/companies/{_companyId}/dashboard/insights"))
            .GetProperty("funnel").GetProperty("tracked").GetInt32();

        Assert.Equal(oncesi + 1, sonrasi);
    }

    [Fact(DisplayName = "UU8. Dashboard aksiyonlarının hepsi bir yere GÖTÜRÜR")]
    public async Task Dashboard_aksiyonlari_hedefli()
    {
        var icgoruler = await OkuAsync($"/api/reports/companies/{_companyId}/dashboard/insights");

        foreach (var aksiyon in icgoruler.GetProperty("actions").EnumerateArray())
        {
            var hedef = aksiyon.GetProperty("target").GetString();

            Assert.False(string.IsNullOrWhiteSpace(hedef));
            Assert.StartsWith("/", hedef, StringComparison.Ordinal);
        }

        // Profil göstergesi kullanıcıya sistemin iç terimlerini göstermemeli.
        foreach (var eksik in icgoruler.GetProperty("profile").GetProperty("missingLabels").EnumerateArray())
        {
            Assert.DoesNotContain("Workforce.", eksik.GetString());
            Assert.DoesNotContain("Financials.", eksik.GetString());
        }
    }

    [Fact(DisplayName = "UU9. Bildirimler görünür ve kanalı ne olursa olsun listede kalır")]
    public async Task Bildirimler_gorunur()
    {
        var liste = await OkuAsync($"/api/notifications?companyId={_companyId}&pageSize=100");

        Assert.True(liste.TryGetProperty("items", out var satirlar));

        foreach (var bildirim in satirlar.EnumerateArray())
        {
            // Kanal görünürlüğü belirlemez; e-posta kanallı bildirim de listede durur.
            Assert.False(string.IsNullOrWhiteSpace(bildirim.GetProperty("title").GetString()));
        }
    }

    [Fact(DisplayName = "UU10. Oturum kapanınca hiçbir ekran açılmaz")]
    public async Task Oturumsuz_erisim_yok()
    {
        using var anonim = _factory.CreateClient();

        foreach (var yol in new[]
                 {
                     "/api/auth/me",
                     $"/api/company-profile/{_companyId}",
                     $"/api/eligibility/companies/{_companyId}/matches",
                     $"/api/reports/companies/{_companyId}/dashboard",
                     $"/api/tenders/companies/{_companyId}",
                     "/api/notifications",
                 })
        {
            var cevap = await anonim.GetAsync(yol);

            Assert.Equal(HttpStatusCode.Unauthorized, cevap.StatusCode);
        }
    }

    /// <summary>
    /// Raporu üretir.
    ///
    /// <para>
    /// Aynı haftanın raporu zaten varsa ikinci kayıt açılmaz, mevcut kayıt güncellenir;
    /// bu yüzden testler arasında çağrılması geçmişi kopyalarla doldurmaz.
    /// </para>
    /// </summary>
    private async Task<JsonElement> UretilmisRaporAsync()
    {
        var cevap = await _kullanici.PostAsJsonAsync(
            $"/api/reports/weekly/companies/{_companyId}", new { });

        cevap.EnsureSuccessStatusCode();

        return await cevap.Content.ReadFromJsonAsync<JsonElement>();
    }
}
