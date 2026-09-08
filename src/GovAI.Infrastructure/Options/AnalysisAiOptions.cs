using Microsoft.Extensions.Configuration;

namespace GovAI.Infrastructure.Options;

/// <summary>
/// DeepTech analiz katmanının yapay zekâ yapılandırması (Faz 3 — Aşama 2 tamamlaması).
///
/// <para>
/// Değerler <b>ortam değişkeninden</b> okunur. Anahtar kaynak koda, compose dosyasına,
/// <c>.env.example</c>'a, loga veya veritabanına yazılmaz; bu sınıf da anahtarı hiçbir
/// açıklama metnine koymaz.
/// </para>
///
/// <para>
/// Model adı koda gömülmez: <see cref="Model"/> yapılandırmadan gelir. Sağlayıcı model
/// adlarını sık değiştiriyor; kodda sabit bir ad, sürüm emekliye ayrıldığında sistemi
/// durdururdu.
/// </para>
/// </summary>
public sealed class AnalysisAiOptions
{
    public const string SectionName = "AnalysisAi";

    // ── Ortam değişkeni adları ──
    public const string ProviderVariable = "GOVAI_AI_PROVIDER";
    public const string ApiKeyVariable = "GOVAI_AI_API_KEY";
    public const string ModelVariable = "GOVAI_AI_MODEL";
    public const string BaseUrlVariable = "GOVAI_AI_BASE_URL";
    public const string EnabledVariable = "GOVAI_AI_ENABLED";
    public const string TimeoutVariable = "GOVAI_AI_TIMEOUT_SECONDS";
    public const string MaxInputTokensVariable = "GOVAI_AI_MAX_INPUT_TOKENS";
    public const string MaxOutputTokensVariable = "GOVAI_AI_MAX_OUTPUT_TOKENS";
    public const string MaxEvidenceChunksVariable = "GOVAI_AI_MAX_EVIDENCE_CHUNKS";
    public const string MaxRetriesVariable = "GOVAI_AI_MAX_RETRIES";

    /// <summary>Sağlayıcı adı. Bugün yalnızca <c>openai</c> desteklenir.</summary>
    public string Provider { get; set; } = "none";

    /// <summary>API anahtarı. <b>Yalnızca ortam değişkeninden</b> gelir.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model adı; yapılandırmadan gelir, koda gömülmez.</summary>
    public string Model { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Özellik anahtarı: yapılandırma tam olsa bile kapatılabilir.</summary>
    public bool Enabled { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Modele gönderilecek azami giriş uzunluğu (token). Sözcük başına yaklaşık dört
    /// karakter varsayılarak karakter sınırına çevrilir; kesin sayım için ayrı bir
    /// tokenizer bağımlılığı gerekirdi ve <b>üst sınır</b> için gereksizdir.
    /// </summary>
    public int MaxInputTokens { get; set; } = 8000;

    public int MaxOutputTokens { get; set; } = 1500;

    /// <summary>Modele gönderilecek azami kanıt parçası sayısı.</summary>
    public int MaxEvidenceChunks { get; set; } = 20;

    /// <summary>Geçici hatada azami yeniden deneme. Kalıcı hatada hiç denenmez.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Devre kesici bu kadar ardışık hatadan sonra açılır.</summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 3;

    /// <summary>Devre açıkken bu süre boyunca hiç istek yapılmaz.</summary>
    public int CircuitBreakerCooldownSeconds { get; set; } = 120;

    /// <summary>Yaklaşık karakter sınırı; token sınırının karakter karşılığı.</summary>
    public int MaxInputCharacters => MaxInputTokens * 4;

    /// <summary>
    /// Gerçek bir model bağlantısı kurulabilir mi?
    ///
    /// <para>
    /// Üçü birden gerekir: sağlayıcı seçilmiş, anahtar verilmiş ve model adı verilmiş
    /// olmalı. Biri eksikse sistem kural tabanlı çalışır ve bunu açıkça söyler —
    /// eksik yapılandırmayla "hibrit analiz aktif" göstermek en yanıltıcı davranıştır.
    /// </para>
    /// </summary>
    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Provider)
        && !string.Equals(Provider, "none", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Model);

    /// <summary>
    /// Neyin eksik olduğunu <b>anahtarı göstermeden</b> anlatır. Teslim raporunda ve
    /// başlangıç logunda kullanılır.
    /// </summary>
    public string DescribeMissing()
    {
        var eksik = new List<string>();

        if (!Enabled)
        {
            return $"{EnabledVariable} kapalı.";
        }

        if (string.IsNullOrWhiteSpace(Provider) || string.Equals(Provider, "none", StringComparison.OrdinalIgnoreCase))
        {
            eksik.Add(ProviderVariable);
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            eksik.Add(ApiKeyVariable);
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            eksik.Add(ModelVariable);
        }

        return eksik.Count == 0
            ? "Yapılandırma tam."
            : "Eksik ortam değişkenleri: " + string.Join(", ", eksik);
    }

    /// <summary>
    /// Ortam değişkenlerinden okur.
    ///
    /// <para>
    /// Yapılandırma bölümü de desteklenir (test ve yerel geliştirme için), ama ortam
    /// değişkeni her zaman önceliklidir: dağıtımda secret oradan gelir.
    /// </para>
    /// </summary>
    public static AnalysisAiOptions FromEnvironment(
        IConfiguration configuration,
        Func<string, string?>? readVariable = null)
    {
        var oku = readVariable ?? Environment.GetEnvironmentVariable;
        var options = configuration.GetSection(SectionName).Get<AnalysisAiOptions>() ?? new AnalysisAiOptions();

        Metin(oku, ProviderVariable, v => options.Provider = v);
        Metin(oku, ApiKeyVariable, v => options.ApiKey = v);
        Metin(oku, ModelVariable, v => options.Model = v);
        Metin(oku, BaseUrlVariable, v => options.BaseUrl = v);
        Sayi(oku, TimeoutVariable, v => options.TimeoutSeconds = v);
        Sayi(oku, MaxInputTokensVariable, v => options.MaxInputTokens = v);
        Sayi(oku, MaxOutputTokensVariable, v => options.MaxOutputTokens = v);
        Sayi(oku, MaxEvidenceChunksVariable, v => options.MaxEvidenceChunks = v);
        Sayi(oku, MaxRetriesVariable, v => options.MaxRetries = v);

        if (oku(EnabledVariable) is { Length: > 0 } enabled && bool.TryParse(enabled, out var acik))
        {
            options.Enabled = acik;
        }

        return options;
    }

    private static void Metin(Func<string, string?> oku, string ad, Action<string> ata)
    {
        if (oku(ad) is { Length: > 0 } deger)
        {
            ata(deger.Trim());
        }
    }

    private static void Sayi(Func<string, string?> oku, string ad, Action<int> ata)
    {
        if (oku(ad) is { Length: > 0 } deger && int.TryParse(deger, out var sayi) && sayi > 0)
        {
            ata(sayi);
        }
    }
}
