using System.ComponentModel.DataAnnotations;

namespace GovAI.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "govai";

    [Required]
    public string Audience { get; set; } = "govai-api";

    public int AccessTokenMinutes { get; set; } = 60;
}

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Kural çıkarımı gibi yapılandırılmış çıktı gerektiren işler için model.</summary>
    public string ExtractionModel { get; set; } = "gpt-4.1";

    /// <summary>Yönetici özeti gibi metin üretimi için model.</summary>
    public string SummaryModel { get; set; } = "gpt-4.1-mini";

    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>API anahtarı yoksa servis devre dışı kalır; skorlama etkilenmez, yalnızca özet üretilmez.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "localhost:6379";

    public string InstanceName { get; set; } = "govai:";

    public bool Enabled { get; set; } = true;
}

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    /// <summary>Tüm GOVAI olayları bu topic exchange üzerinden dağıtılır.</summary>
    public string ExchangeName { get; set; } = "govai.events";

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// SMTP ayarları.
///
/// <para>
/// Parola <b>bu sınıfta varsayılan almaz</b> ve hiçbir yerde loglanmaz. Eksik
/// yapılandırmada sistem çalışmaya devam eder: bildirim panelde görünür, yalnızca
/// e-posta gitmez (<see cref="IsConfigured"/>).
/// </para>
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Kapalıyken gönderim hiç denenmez; bildirimler panelde kalır.</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    /// <summary>
    /// 587 için STARTTLS, 465 için baştan TLS. Şifresiz bağlantı desteklenmez:
    /// kimlik bilgisi ve personel verisi açık ağdan geçerdi.
    /// </summary>
    public bool UseStartTls { get; set; } = true;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "GOVAI";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Tek bir e-postadaki azami alıcı; kalabalık liste sunucuda reddedilir.</summary>
    public int MaxRecipients { get; set; } = 50;

    /// <summary>
    /// Dördü birden verilmezse gönderim yapılmaz. Kullanıcı adı bilerek zorunlu
    /// değildir: kurum içi röle sunucuları kimlik istemeyebilir.
    /// </summary>
    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Host)
        && Port > 0
        && !string.IsNullOrWhiteSpace(FromAddress);
}
