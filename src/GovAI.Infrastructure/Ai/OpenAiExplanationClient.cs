using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Common;
using GovAI.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovAI.Infrastructure.Ai;

/// <summary>
/// OpenAI Chat Completions API üzerinden çalışan AI Explanation Service uygulaması.
///
/// İki iş yapar ve ikisinde de karar mercii değildir:
/// 1) Resmî metinden kural taslağı çıkarır (JSON şeması zorunlu kılınır, alan adları beyaz listeye kısıtlanır).
/// 2) Hesaplanmış skoru Türkçe yönetici özetine çevirir.
/// </summary>
public sealed class OpenAiExplanationClient(
    HttpClient httpClient,
    IOptions<OpenAiOptions> options,
    ILogger<OpenAiExplanationClient> logger) : IAiExplanationClient
{
    private readonly OpenAiOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<RuleExtractionResult> ExtractRulesAsync(
        RuleExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            logger.LogWarning("OpenAI yapılandırılmamış; kural çıkarımı atlandı.");
            return new RuleExtractionResult { Rules = [], Documents = [], Confidence = 0m };
        }

        var fieldList = string.Join("\n", request.AllowedFields.Select(f => $"- {f.Key}: {f.Value}"));

        var systemPrompt = $"""
            Sen Türkiye'deki resmî teşvik, hibe ve ihale çağrılarını analiz eden bir uzmansın.
            Görevin, verilen çağrı metninden makine tarafından değerlendirilebilir başvuru koşullarını çıkarmaktır.

            Kurallar:
            - YALNIZCA aşağıdaki alan adlarını kullan. Listede olmayan bir alan adı ÜRETME:
            {fieldList}
            - operator şunlardan biri olmalı: Equals, NotEquals, GreaterThan, GreaterThanOrEqual, LessThan,
              LessThanOrEqual, In, NotIn, ContainsAll, ContainsAny, NaceMatch, IsTrue, IsFalse
            - dimension şunlardan biri olmalı: Sector, Financial, Employment, Documentation, Region,
              TechnicalQualification, Timing
            - severity şunlardan biri olmalı: Blocking (sağlanmazsa başvuru reddedilir), Major, Minor, Bonus
            - Sayısal değerleri nokta ondalık ayracıyla, binlik ayraç olmadan yaz (örn. 250000000).
            - Oranları 0..1 aralığında yaz (%30 → 0.30).
            - Liste değerlerini virgülle ayır (örn. "TR62,TR61").
            - sourceExcerpt alanına koşulun dayandığı orijinal cümleyi birebir koy.
            - Metinde açıkça yazmayan bir koşul UYDURMA. Emin değilsen o koşulu ekleme veya confidence değerini düşür.
            - Yanıtını yalnızca geçerli JSON olarak ver, başka hiçbir metin ekleme.
            """;

        var userPrompt = $$"""
            Çağrı başlığı: {{request.OpportunityTitle}}

            Çağrı metni:
            {{Truncate(request.NormalizedText, 40_000)}}

            Şu JSON şemasına göre yanıt ver:
            {
              "summary": "çağrının 2-3 cümlelik Türkçe özeti",
              "detectedCategory": "EmploymentIncentive|InvestmentIncentive|Grant|RndSupport|DigitalTransformation|ExportSupport|GreenTransformation|Tender|Loan|Other",
              "deadline": "ISO-8601 tarih veya null",
              "confidence": 0.0-1.0,
              "rules": [
                {"field":"...","operator":"...","value":"...","dimension":"...","severity":"...","humanReadable":"Türkçe koşul metni","sourceExcerpt":"...","confidence":0.0-1.0}
              ],
              "documents": [
                {"code":"BUYUK_HARF_KOD","name":"Belge adı","isMandatory":true,"issuingAuthority":"Kurum"}
              ]
            }
            """;

        var payload = await CompleteAsync(_options.ExtractionModel, systemPrompt, userPrompt, jsonMode: true, cancellationToken);
        if (payload is null)
        {
            return new RuleExtractionResult { Rules = [], Documents = [], Confidence = 0m };
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ExtractionPayload>(payload, JsonOptions);
            if (parsed is null)
            {
                return new RuleExtractionResult { Rules = [], Documents = [], Confidence = 0m };
            }

            // Model beyaz liste dışına çıkarsa o kural sessizce düşürülür; uydurma alan motora sızmaz.
            var validRules = parsed.Rules
                .Where(r => request.AllowedFields.ContainsKey(r.Field))
                .ToList();

            if (validRules.Count != parsed.Rules.Count)
            {
                logger.LogWarning(
                    "Kural çıkarımında {Dropped} adet geçersiz alan adı elendi. Başlık={Title}",
                    parsed.Rules.Count - validRules.Count, request.OpportunityTitle);
            }

            return new RuleExtractionResult
            {
                Rules = validRules,
                Documents = parsed.Documents,
                Confidence = Math.Clamp(parsed.Confidence, 0m, 1m),
                Summary = parsed.Summary,
                Deadline = parsed.Deadline,
                DetectedCategory = Enum.TryParse<SupportCategory>(parsed.DetectedCategory, ignoreCase: true, out var category)
                    ? category
                    : null,
                ModelName = _options.ExtractionModel
            };
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Kural çıkarım yanıtı JSON olarak ayrıştırılamadı.");
            return new RuleExtractionResult { Rules = [], Documents = [], Confidence = 0m };
        }
    }

    public async Task<AiSummaryResult> GenerateExecutiveSummaryAsync(
        ExecutiveSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return new AiSummaryResult(BuildFallbackSummary(request), "rule-based-fallback");
        }

        const string systemPrompt = """
            Sen kurumsal bir karar destek platformunun yönetici raporlarını yazan analistsin.
            Sana verilen uygunluk analizi sonucunu, bir şirket yöneticisinin 30 saniyede okuyup karar verebileceği
            Türkçe bir özete dönüştür.

            Kurallar:
            - Verilen sayıları ve kararı DEĞİŞTİRME; yalnızca anlat.
            - Sana verilmeyen hiçbir bilgiyi ekleme, tahmin yürütme.
            - 3-5 cümle yaz. Önce karar, sonra en belirleyici gerekçe, sonra somut aksiyon.
            - Yüzde ve tarih gibi değerleri olduğu gibi kullan.
            - Madde işareti kullanma, akıcı paragraf yaz.
            """;

        var userPrompt = $"""
            Firma: {request.CompanyName}
            Çağrı: {request.OpportunityTitle} ({request.Publisher})
            Karar: {request.Verdict}
            Uygunluk skoru: {request.FinalScore:0.0}/100
            Son başvuru: {request.Deadline?.ToString("dd.MM.yyyy") ?? "belirtilmemiş"}

            Boyut kırılımı:
            {Join(request.DimensionHighlights)}

            Elenme sebepleri:
            {Join(request.BlockingReasons)}

            Kapatılabilir eksikler:
            {Join(request.MissingConditions)}

            Eksik belgeler:
            {Join(request.MissingDocuments)}
            """;

        var text = await CompleteAsync(_options.SummaryModel, systemPrompt, userPrompt, jsonMode: false, cancellationToken);

        return string.IsNullOrWhiteSpace(text)
            ? new AiSummaryResult(BuildFallbackSummary(request), "rule-based-fallback")
            : new AiSummaryResult(text.Trim(), _options.SummaryModel);
    }

    /// <summary>Modele verilecek azami çağrı metni uzunluğu.</summary>
    private const int MaximumOpportunityTextLength = 12000;

    public async Task<AiSecondOpinionResult?> ReviewEligibilityAsync(
        SecondOpinionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Anahtar yoksa görüş UYDURULMAZ. Sahte bir ikinci görüş, ölçümü olduğundan iyi
        // ya da kötü gösterir ve kalibrasyonu bozar; hiç görüş vermemek dürüst olandır.
        if (!_options.IsConfigured)
        {
            return null;
        }

        const string systemPrompt = """
            Sen kamu teşvik, hibe ve ihale başvurularını değerlendiren kıdemli bir danışmansın.
            Sana bir çağrının resmî metni ve bir firmanın profili veriliyor.

            Görevin: firmanın bu çağrıya uygun olup olmadığına KENDİ BAŞINA karar vermek.

            Kurallar:
            1. Kararını yalnızca çağrı metninde YAZAN koşullara dayandır. Metinde olmayan bir
               şartı varsayma.
            2. Bilinmeyen bir firma bilgisi "hayır" DEĞİLDİR. Bir koşul, profilde o alan
               eksik olduğu için değerlendirilemiyorsa firmayı eleme; kararını
               "Indeterminate" yap ve gerekçende hangi bilginin eksik olduğunu yaz.
            3. Emin olmadığında yüksek güven bildirme. Güven değerin kararının ne kadar
               sağlam veriye dayandığını yansıtmalı.
            4. Gerekçen Türkçe, en fazla 3 cümle ve somut olmalı: hangi koşul, firmanın
               hangi değeriyle karşılaştırıldı.

            Karar seçenekleri:
            - "Eligible": firma koşulların tamamını karşılıyor.
            - "ConditionallyEligible": kapatılabilir eksikler var ama başvuru yapılabilir.
            - "NotEligible": firma bir zorunlu koşulu karşılamıyor.
            - "Indeterminate": eksik bilgi yüzünden karar verilemiyor.

            Yanıtı yalnızca şu JSON şemasıyla ver:
            {"verdict":"Eligible|ConditionallyEligible|NotEligible|Indeterminate",
             "rationale":"...","confidence":0.0}
            """;

        // Modele HESAPLANMIŞ SKOR VERİLMEZ. Verilseydi model onu onaylama eğilimine girer,
        // "bağımsız" görüş sistemin kendi cevabının yankısı olur ve ayrışma hiç görünmezdi.
        var userPrompt = $"""
            Çağrı: {request.OpportunityTitle}

            Çağrı metni:
            {Kirp(request.OpportunityText, MaximumOpportunityTextLength)}

            Firma: {request.CompanyName}

            Firma profili:
            {request.CompanyProfileSummary}

            Profilde EKSİK olan alanlar (bunlar "hayır" anlamına gelmez):
            {(request.MissingProfileFields.Count == 0
                ? "- yok"
                : string.Join("\n", request.MissingProfileFields.Select(a => $"- {a}")))}
            """;

        var text = await CompleteAsync(
            _options.ExtractionModel, systemPrompt, userPrompt, jsonMode: true, cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var kok = document.RootElement;

            if (!Enum.TryParse<EligibilityVerdict>(
                    kok.GetProperty("verdict").GetString(), ignoreCase: true, out var karar))
            {
                // Tanınmayan karar sessizce bir değere eşlenmez: yanlış eşleme, ölçümü
                // gerçekte olmayan bir uyum ya da ayrışmayla doldurur.
                logger.LogWarning("İkinci görüş tanınmayan karar döndürdü; kayıt açılmadı.");
                return null;
            }

            var guven = kok.TryGetProperty("confidence", out var g) && g.TryGetDecimal(out var d)
                ? Math.Clamp(d, 0m, 1m)
                : 0.5m;

            var gerekce = kok.TryGetProperty("rationale", out var r) ? r.GetString() : null;

            return new AiSecondOpinionResult
            {
                Verdict = karar,
                Rationale = string.IsNullOrWhiteSpace(gerekce) ? "Gerekçe üretilmedi." : gerekce.Trim(),
                Confidence = guven,
                ModelName = _options.ExtractionModel,
            };
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "İkinci görüş yanıtı JSON olarak ayrıştırılamadı.");
            return null;
        }
    }

    /// <summary>
    /// Çağrı metni uzun olabilir; modele tamamı yerine başı verilir.
    ///
    /// <para>
    /// Koşullar resmî metinlerde başta toplanır; sondaki ekler çoğunlukla biçimsel
    /// hükümlerdir. Kırpma yapılmazsa uzun bir belge istek sınırını aşar ve o vaka için
    /// ikinci görüş HİÇ üretilmez — eksik ölçüm, kısaltılmış bağlamdan kötüdür.
    /// </para>
    /// </summary>
    private static string Kirp(string metin, int azami) =>
        metin.Length <= azami ? metin : metin[..azami] + "\n[metin kırpıldı]";

    private async Task<string?> CompleteAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        bool jsonMode,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["temperature"] = jsonMode ? 0.0 : 0.3,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        if (jsonMode)
        {
            body["response_format"] = new { type = "json_object" };
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync("chat/completions", body, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("OpenAI çağrısı başarısız. Durum={Status} Yanıt={Error}", (int)response.StatusCode, Truncate(error, 500));
                return null;
            }

            var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, cancellationToken);
            return completion?.Choices.FirstOrDefault()?.Message?.Content;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "OpenAI servisine ulaşılamadı.");
            return null;
        }
    }

    /// <summary>
    /// AI kullanılamadığında üretilen deterministik özet.
    /// Ürünün AI'sız da çalışabilmesi tasarım gereğidir; skorlar zaten kural motorundan gelir.
    /// </summary>
    private static string BuildFallbackSummary(ExecutiveSummaryRequest request)
    {
        var verdict = request.Verdict switch
        {
            EligibilityVerdict.Eligible => "uygun",
            EligibilityVerdict.ConditionallyEligible => "şartlı uygun",
            EligibilityVerdict.NotEligible => "uygun değil",
            _ => "veri yetersizliği nedeniyle belirsiz"
        };

        var parts = new List<string>
        {
            $"{request.CompanyName}, \"{request.OpportunityTitle}\" çağrısı için {verdict} (skor {request.FinalScore:0.0}/100)."
        };

        if (request.BlockingReasons.Count > 0)
        {
            parts.Add($"Elenme sebebi: {string.Join("; ", request.BlockingReasons.Take(3))}.");
        }

        if (request.MissingConditions.Count > 0)
        {
            parts.Add($"Kapatılması gereken eksikler: {string.Join("; ", request.MissingConditions.Take(3))}.");
        }

        if (request.MissingDocuments.Count > 0)
        {
            parts.Add($"Eksik belgeler: {string.Join(", ", request.MissingDocuments.Take(5))}.");
        }

        if (request.Deadline is not null)
        {
            parts.Add($"Son başvuru tarihi {request.Deadline:dd.MM.yyyy}.");
        }

        return string.Join(" ", parts);
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(yok)" : string.Join("\n", values.Select(v => $"- {v}"));

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record ExtractionPayload
    {
        public string? Summary { get; init; }
        public string? DetectedCategory { get; init; }
        public DateTimeOffset? Deadline { get; init; }
        public decimal Confidence { get; init; }
        public List<ExtractedRule> Rules { get; init; } = [];
        public List<ExtractedDocument> Documents { get; init; } = [];
    }

    private sealed record ChatCompletionResponse(List<ChatChoice> Choices);

    private sealed record ChatChoice(ChatMessage? Message);

    private sealed record ChatMessage(string? Content);
}
