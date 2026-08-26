using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace GovAI.Persistence.Design;

/// <summary>
/// <c>dotnet ef</c> komutlarının hangi veritabanına bağlanacağına karar veren kural.
///
/// <para>
/// <b>Neden var:</b> EF Core, açık bir bağlantı verilmezse başlangıç projesinin
/// yapılandırmasına düşer. GOVAI'de <c>appsettings.Development.json</c> içindeki
/// bağlantı <c>localhost:5432</c>'dir — yani <b>çalışan 5180 ortamının</b> Postgres'i.
/// Bu yüzden <c>database update</c> gibi bir komut, hiçbir uyarı vermeden müşterinin
/// canlı veritabanının şemasını değiştirebilir. Faz 2'de tam olarak bu oldu:
/// iki migration istemeden 5180'e uygulandı.
/// </para>
///
/// <para>
/// <b>Kural:</b> hedef yalnızca <c>GOVAI_EF_CONNECTION_STRING</c> ile verilir.
/// Değişken yoksa komut açıklayıcı bir hatayla durur; sessiz bir varsayılana düşmez.
/// Hedef korunan bir adresse (5180'in Postgres'i) ayrıca <c>GOVAI_EF_ALLOW_PRODUCTION</c>
/// onayı istenir.
/// </para>
///
/// <para>
/// Bağlantı dizesi <b>hiçbir yerde bütün olarak yazılmaz</b>: ne hatada, ne logda.
/// Yalnızca sunucu, port ve veritabanı adı gösterilir; kullanıcı adı ve parola asla.
/// </para>
/// </summary>
public static class EfMigrationTarget
{
    /// <summary>Hedefi belirleyen zorunlu değişken.</summary>
    public const string ConnectionVariable = "GOVAI_EF_CONNECTION_STRING";

    /// <summary>Korunan hedefe dokunmak için gereken açık onay değişkeni.</summary>
    public const string ProductionConsentVariable = "GOVAI_EF_ALLOW_PRODUCTION";

    /// <summary>Onay değişkeninin kabul edilen tek değeri. Yanlışlıkla yazılamayacak kadar açık.</summary>
    public const string ProductionConsentValue = "EVET-5180-VERITABANINI-DEGISTIR";

    /// <summary>
    /// Veritabanına hiç bağlanmadan çalışan komutlar için (<c>migrations add</c>,
    /// <c>has-pending-model-changes</c>, <c>migrations script</c>). CI bunu kullanır.
    /// </summary>
    public const string ModelOnlyValue = "model-only";

    /// <summary>
    /// Model-only kipinde kullanılan, kasıtlı olarak <b>erişilemez</b> bağlantı.
    /// <c>.invalid</c> IANA tarafından hiçbir zaman çözümlenmemek üzere ayrılmıştır,
    /// dolayısıyla bu dizeyle yanlışlıkla bir veritabanına yazmak mümkün değildir.
    /// </summary>
    public const string ModelOnlyConnectionString =
        "Host=govai-ef-model-only.invalid;Port=1;Database=govai_model_only;" +
        "Username=model-only;Password=model-only;Timeout=1;Command Timeout=1";

    /// <summary>
    /// Korunan hedefler: 5180 ortamının Postgres'i. Sunucu adı ve port çiftiyle
    /// eşleşir, çünkü ayırt edici olan budur — veritabanı adı her ortamda "govai".
    /// </summary>
    private static readonly (string Host, int Port)[] ProtectedTargets =
    [
        ("localhost", 5432),
        ("127.0.0.1", 5432),
        ("::1", 5432),
        ("[::1]", 5432),
    ];

    /// <summary>Çözülmüş hedef.</summary>
    public sealed record Target(string ConnectionString, string Description, bool IsModelOnly);

    /// <summary>
    /// Ortam değişkenlerinden hedefi çözer.
    /// </summary>
    /// <param name="readVariable">Değişken okuyucu; testte sahtelenir.</param>
    /// <exception cref="EfMigrationTargetException">
    /// Bağlantı verilmediğinde, geçersiz olduğunda ya da korunan hedefe onaysız
    /// gidildiğinde atılır.
    /// </exception>
    public static Target Resolve(Func<string, string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(readVariable);

        var raw = readVariable(ConnectionVariable);

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new EfMigrationTargetException(MissingVariableMessage());
        }

        raw = raw.Trim();

        if (string.Equals(raw, ModelOnlyValue, StringComparison.OrdinalIgnoreCase))
        {
            return new Target(ModelOnlyConnectionString, "model-only (veritabanına bağlanılmaz)", true);
        }

        if (!TryDescribe(raw, out var host, out var port, out var database, out var parseError))
        {
            throw new EfMigrationTargetException(
                $"{ConnectionVariable} çözümlenemedi: {parseError}\n" +
                "Beklenen biçim: Host=...;Port=...;Database=...;Username=...;Password=...\n" +
                "(Bağlantı dizesinin kendisi güvenlik gereği burada gösterilmez.)");
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            throw new EfMigrationTargetException(
                $"{ConnectionVariable} içinde veritabanı adı yok. Hedef veritabanı açıkça yazılmalıdır.");
        }

        var description = $"{host}:{port}/{database}";

        if (IsProtected(host, port))
        {
            var consent = readVariable(ProductionConsentVariable)?.Trim();

            if (!string.Equals(consent, ProductionConsentValue, StringComparison.Ordinal))
            {
                throw new EfMigrationTargetException(ProtectedTargetMessage(description));
            }
        }

        return new Target(raw, description, false);
    }

    /// <summary>Hedef, 5180 ortamının veritabanı mı?</summary>
    public static bool IsProtected(string host, int port) =>
        ProtectedTargets.Any(t =>
            t.Port == port && string.Equals(t.Host, host, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Bağlantı dizesini <b>gizli alanları dışarıda bırakarak</b> tanımlar.
    /// Kullanıcı adı ve parola hiçbir koşulda dönmez.
    /// </summary>
    public static bool TryDescribe(
        string connectionString,
        out string host,
        out int port,
        out string database,
        [NotNullWhen(false)] out string? error)
    {
        host = string.Empty;
        port = 0;
        database = string.Empty;

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);

            host = builder.Host ?? string.Empty;
            port = builder.Port;
            database = builder.Database ?? string.Empty;

            if (string.IsNullOrWhiteSpace(host))
            {
                error = "sunucu (Host) belirtilmemiş";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // Hata metni bağlantı dizesinin parçalarını içerebilir; dışarı sızdırmıyoruz.
            error = "biçim geçersiz";
            return false;
        }
    }

    private static string MissingVariableMessage() =>
        $"""
        {ConnectionVariable} tanımlı değil.

        EF Core komutları GOVAI'de sessiz bir varsayılana DÜŞMEZ: appsettings içindeki
        bağlantı localhost:5432'dir ve orası çalışan 5180 ortamının veritabanıdır.
        Hedefi her zaman açıkça vermelisin.

          # Şemaya dokunmayan komutlar (migrations add, has-pending-model-changes, script)
          $env:{ConnectionVariable} = "{ModelOnlyValue}"

          # Önizleme (5181) veritabanına migration uygulamak
          $env:{ConnectionVariable} = "Host=localhost;Port=15437;Database=govai;Username=govai;Password=<parola>"

        Hazır komutlar: scripts/ef-migrate.ps1 ve scripts/ef-migrate.sh
        """;

    private static string ProtectedTargetMessage(string description) =>
        $"""
        Hedef korunan veritabanı: {description}

        Burası 5180 ortamının (deploy/docker-compose.yml) Postgres'idir; müşterinin
        gerçek şirket, kullanıcı ve değerlendirme verisi buradadır.

        Devam etmek gerçekten isteniyorsa ÖNCE yedek al, sonra onayı açıkça ver:

          $env:{ProductionConsentVariable} = "{ProductionConsentValue}"

        Önizleme ortamına çalışmak istiyorsan port 15437'dir, 5432 değil.
        """;
}

/// <summary>EF hedefi güvenli biçimde çözülemedi.</summary>
public sealed class EfMigrationTargetException(string message) : Exception(message);
