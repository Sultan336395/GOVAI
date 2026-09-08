using System.Net;
using System.Text;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Analysis;
using GovAI.Infrastructure.Ai;
using GovAI.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovAI.Application.Tests;

/// <summary>
/// Gerçek yapay zekâ sağlayıcısının davranış sözleşmesi (Faz 3 — kritik eksik 3).
///
/// Testler <b>gerçek modele çıkmaz</b>: ağ gerektirir, ücretlidir ve aynı girdiye
/// farklı cevap verir. Sınanan şey modelin zekâsı değil, sağlayıcının hata, zaman
/// aşımı, oran sınırı ve sınır davranışlarıdır — bunlar deterministik olarak
/// sahtelenebilir ve asıl risk oradadır.
///
/// Beş garanti sabitlenir:
/// 1. Anahtar yokken sistem kural tabanlı çalışmaya devam eder.
/// 2. Zaman aşımı ve oran sınırında kural sonucu korunur.
/// 3. Kalıcı hatada (401) boşuna yeniden denenmez.
/// 4. Devre kesici açıkken istek hiç yapılmaz.
/// 5. Anahtar hiçbir çıktıya, hataya veya duruma sızmaz.
/// </summary>
public class OpenAiAnalysisProviderTests
{
    private const string SahteAnahtar = "sk-test-BUNU-LOGLAMA-1234567890";

    private static AnalysisAiOptions Ayar(bool yapilandirilmis = true, int retry = 2) => new()
    {
        Provider = yapilandirilmis ? "openai" : "none",
        ApiKey = yapilandirilmis ? SahteAnahtar : string.Empty,
        Model = yapilandirilmis ? "test-model" : string.Empty,
        MaxRetries = retry,
        CircuitBreakerFailureThreshold = 2,
        CircuitBreakerCooldownSeconds = 60,
        MaxEvidenceChunks = 5,
        MaxInputTokens = 2000
    };

    private static AnalysisAiRequest Istek(int kanitSayisi = 1) => new()
    {
        Kind = AnalysisKind.Opportunity,
        TargetTitle = "Test Çağrısı",
        Criteria =
        [
            new CriterionResult
            {
                Code = CriterionCatalog.EmployeeCount,
                Name = "Çalışan sayısı",
                IsMandatory = false,
                Outcome = CriterionOutcome.Unknown,
                Rationale = "Veri eksik.",
                ScoreImpact = 0.5m,
                RuleSetVersion = AnalysisRuleSet.Current.Version,
                Group = ScoreGroup.Workforce
            }
        ],
        Evidence = Enumerable.Range(1, kanitSayisi).Select(i => new AnalysisEvidence
        {
            EvidenceChunkId = Guid.CreateVersion7(),
            DocumentVersionId = Guid.CreateVersion7(),
            Text = $"Başvuru sahibinin en az 10 çalışanı olmalıdır. ({i})",
            SequenceNumber = i
        }).ToList(),
        CompanyFacts = new Dictionary<string, string> { ["Çalışan sayısı"] = "42" },
        CorrelationId = "test-korelasyon"
    };

    private static OpenAiAnalysisProvider Saglayici(
        AnalysisAiOptions ayar,
        SahteHttpHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/v1/") };

        return new OpenAiAnalysisProvider(
            client, ayar, new AiCircuitBreaker(ayar), NullLogger<OpenAiAnalysisProvider>.Instance);
    }

    [Fact(DisplayName = "YZ1. Anahtar yokken sağlayıcı yapılandırılmamış sayılır")]
    public async Task Anahtar_yoksa_yapilandirilmamis()
    {
        var ayar = Ayar(yapilandirilmis: false);
        var handler = new SahteHttpHandler(_ => throw new InvalidOperationException("İstek yapılmamalıydı."));

        var saglayici = Saglayici(ayar, handler);
        var sonuc = await saglayici.AnalyzeAsync(Istek());

        Assert.False(saglayici.IsConfigured);
        Assert.Equal(AiAnalysisStatus.AIUnavailable, sonuc.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact(DisplayName = "YZ2. Eksik yapılandırma anahtarı GÖSTERMEDEN anlatılır")]
    public void Eksik_yapilandirma_anahtari_gostermez()
    {
        var eksik = new AnalysisAiOptions { Provider = "openai", ApiKey = string.Empty, Model = string.Empty };

        var aciklama = eksik.DescribeMissing();

        Assert.Contains(AnalysisAiOptions.ApiKeyVariable, aciklama);
        Assert.Contains(AnalysisAiOptions.ModelVariable, aciklama);
        Assert.DoesNotContain("sk-", aciklama);
    }

    [Fact(DisplayName = "YZ3. Kanıt yoksa model çağrılmaz")]
    public async Task Kanitsiz_istekte_model_cagrilmaz()
    {
        var handler = new SahteHttpHandler(_ => throw new InvalidOperationException("İstek yapılmamalıydı."));
        var saglayici = Saglayici(Ayar(), handler);

        var sonuc = await saglayici.AnalyzeAsync(Istek(kanitSayisi: 0));

        Assert.Equal(AiAnalysisStatus.AIUnavailable, sonuc.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact(DisplayName = "YZ4. Zaman aşımında kural sonucu korunur, hata fırlatılmaz")]
    public async Task Zaman_asiminda_hata_firlatilmaz()
    {
        var handler = new SahteHttpHandler(_ => throw new TaskCanceledException("zaman aşımı"));
        var saglayici = Saglayici(Ayar(retry: 1), handler);

        var sonuc = await saglayici.AnalyzeAsync(Istek());

        Assert.Equal(AiAnalysisStatus.Error, sonuc.Status);
        Assert.Empty(sonuc.Claims);
    }

    [Fact(DisplayName = "YZ5. Oran sınırında sınırlı sayıda yeniden denenir")]
    public async Task Oran_sinirinda_yeniden_denenir()
    {
        var handler = new SahteHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var saglayici = Saglayici(Ayar(retry: 2), handler);

        var sonuc = await saglayici.AnalyzeAsync(Istek());

        Assert.Equal(AiAnalysisStatus.Error, sonuc.Status);
        // İlk deneme + 2 yeniden deneme.
        Assert.Equal(3, handler.CallCount);
    }

    [Fact(DisplayName = "YZ6. Kalıcı hatada (401) yeniden denenmez")]
    public async Task Kalici_hatada_yeniden_denenmez()
    {
        // Yanlış anahtar tekrar denemekle düzelmez; yalnızca maliyet üretir.
        var handler = new SahteHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var saglayici = Saglayici(Ayar(retry: 3), handler);

        var sonuc = await saglayici.AnalyzeAsync(Istek());

        Assert.Equal(AiAnalysisStatus.Error, sonuc.Status);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact(DisplayName = "YZ7. Devre kesici açıldıktan sonra istek hiç yapılmaz")]
    public async Task Devre_kesici_istegi_durdurur()
    {
        var ayar = Ayar(retry: 0);
        var handler = new SahteHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var kesici = new AiCircuitBreaker(ayar);

        var saglayici = new OpenAiAnalysisProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/v1/") },
            ayar, kesici, NullLogger<OpenAiAnalysisProvider>.Instance);

        // Eşik 2: iki başarısız çağrı devreyi açar.
        await saglayici.AnalyzeAsync(Istek());
        await saglayici.AnalyzeAsync(Istek());

        var oncekiSayac = handler.CallCount;
        var ucuncu = await saglayici.AnalyzeAsync(Istek());

        Assert.True(kesici.IsOpen);
        Assert.Equal(AiAnalysisStatus.AIUnavailable, ucuncu.Status);
        Assert.Equal(oncekiSayac, handler.CallCount);
    }

    [Fact(DisplayName = "YZ8. Başarılı çağrı devre kesiciyi sıfırlar")]
    public void Basarili_cagri_devreyi_sifirlar()
    {
        var kesici = new AiCircuitBreaker(Ayar());

        kesici.RecordFailure();
        kesici.RecordSuccess();
        kesici.RecordFailure();

        Assert.False(kesici.IsOpen);
        Assert.True(kesici.AllowRequest());
    }

    [Fact(DisplayName = "YZ9. Anahtar istek gövdesine yazılmaz")]
    public async Task Anahtar_govdeye_yazilmaz()
    {
        string? govde = null;
        var handler = new SahteHttpHandler(istek =>
        {
            govde = istek.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Basarili("{\"claims\":[],\"summary\":\"ok\"}");
        });

        await Saglayici(Ayar(), handler).AnalyzeAsync(Istek());

        Assert.NotNull(govde);
        Assert.DoesNotContain(SahteAnahtar, govde);
    }

    [Fact(DisplayName = "YZ10. Kanıt sayısı yapılandırma sınırıyla kısıtlanır")]
    public async Task Kanit_sayisi_sinirlanir()
    {
        string? govde = null;
        var handler = new SahteHttpHandler(istek =>
        {
            govde = istek.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Basarili("{\"claims\":[],\"summary\":\"ok\"}");
        });

        // MaxEvidenceChunks = 5; 12 parça verilir.
        await Saglayici(Ayar(), handler).AnalyzeAsync(Istek(kanitSayisi: 12));

        var gonderilen = govde!.Split("evidenceChunkId=").Length - 1;
        Assert.Equal(5, gonderilen);
    }

    [Fact(DisplayName = "YZ11. Geçerli yanıt iddiaya çevrilir ve token miktarı kaydedilir")]
    public async Task Gecerli_yanit_isleniyor()
    {
        var istek = Istek();
        var parcaId = istek.Evidence[0].EvidenceChunkId;

        var handler = new SahteHttpHandler(_ => Basarili($$"""
            {
              "claims": [
                {
                  "claimType": "Clarification",
                  "criterionCode": "EMPLOYEE_COUNT",
                  "evidenceChunkId": "{{parcaId}}",
                  "quote": "en az 10 çalışanı olmalıdır",
                  "explanation": "Belge asgari çalışan sayısı istiyor.",
                  "confidence": 0.9
                }
              ],
              "summary": "Kısa özet."
            }
            """, promptTokens: 1200, completionTokens: 80));

        var sonuc = await Saglayici(Ayar(), handler).AnalyzeAsync(istek);

        Assert.Equal(AiAnalysisStatus.Succeeded, sonuc.Status);
        Assert.Single(sonuc.Claims);
        Assert.Equal(1200, sonuc.PromptTokens);
        Assert.Equal(80, sonuc.CompletionTokens);
        Assert.Equal("test-model", sonuc.ModelName);
    }

    [Fact(DisplayName = "YZ12. Bozuk JSON yanıtı InvalidOutput üretir")]
    public async Task Bozuk_yanit_invalid_output()
    {
        var handler = new SahteHttpHandler(_ => Basarili("bu JSON değil"));

        var sonuc = await Saglayici(Ayar(), handler).AnalyzeAsync(Istek());

        Assert.Equal(AiAnalysisStatus.InvalidOutput, sonuc.Status);
        Assert.Empty(sonuc.Claims);
    }

    [Fact(DisplayName = "YZ13. Model adı koda gömülü değil, yapılandırmadan gelir")]
    public async Task Model_adi_yapilandirmadan_gelir()
    {
        string? govde = null;
        var ayar = Ayar();
        ayar.Model = "farkli-model-2027";

        var handler = new SahteHttpHandler(istek =>
        {
            govde = istek.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Basarili("{\"claims\":[],\"summary\":\"ok\"}");
        });

        await Saglayici(ayar, handler).AnalyzeAsync(Istek());

        Assert.Contains("farkli-model-2027", govde);
    }

    [Fact(DisplayName = "YZ14. Ortam değişkenleri yapılandırmayı belirler")]
    public void Ortam_degiskenleri_okunur()
    {
        var degerler = new Dictionary<string, string?>
        {
            [AnalysisAiOptions.ProviderVariable] = "openai",
            [AnalysisAiOptions.ApiKeyVariable] = SahteAnahtar,
            [AnalysisAiOptions.ModelVariable] = "ortamdan-model",
            [AnalysisAiOptions.MaxEvidenceChunksVariable] = "7",
            [AnalysisAiOptions.EnabledVariable] = "true"
        };

        var ayar = AnalysisAiOptions.FromEnvironment(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            ad => degerler.GetValueOrDefault(ad));

        Assert.True(ayar.IsConfigured);
        Assert.Equal("ortamdan-model", ayar.Model);
        Assert.Equal(7, ayar.MaxEvidenceChunks);
    }

    [Fact(DisplayName = "YZ15. Özellik kapalıysa yapılandırma tam olsa da çalışmaz")]
    public void Ozellik_kapaliysa_calismaz()
    {
        var ayar = Ayar();
        ayar.Enabled = false;

        Assert.False(ayar.IsConfigured);
        Assert.Contains(AnalysisAiOptions.EnabledVariable, ayar.DescribeMissing());
    }

    private static HttpResponseMessage Basarili(
        string content,
        int promptTokens = 0,
        int completionTokens = 0)
    {
        var kacisli = System.Text.Json.JsonSerializer.Serialize(content);

        var govde = $$"""
            {
              "choices": [ { "message": { "content": {{kacisli}} } } ],
              "usage": { "prompt_tokens": {{promptTokens}}, "completion_tokens": {{completionTokens}}, "total_tokens": {{promptTokens + completionTokens}} }
            }
            """;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(govde, Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>Ağa çıkmayan, ne döneceği önceden yazılmış HTTP işleyicisi.</summary>
internal sealed class SahteHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> yanit) : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(yanit(request));
    }
}
