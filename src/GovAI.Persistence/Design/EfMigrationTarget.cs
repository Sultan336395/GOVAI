using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
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
/// <b>Üç katmanlı denetim:</b>
/// </para>
/// <list type="number">
///   <item><b>Hedef açıkça verilmeli.</b> <see cref="ConnectionVariable"/> yoksa komut durur;
///         sessiz bir varsayılana düşmez.</item>
///   <item><b>Ortam kimliği doğrulanmalı.</b> Çağıran <see cref="EnvironmentVariable"/> ile
///         hangi ortama gittiğini <i>beyan eder</i>; bağlantı dizesinden çözülen gerçek ortam
///         bununla eşleşmezse komut durur. "Önizlemeye gidiyorum" diyip üretime bağlanmak
///         mümkün değildir.</item>
///   <item><b>Üretim ayrıca onay ister.</b> 5180 için <see cref="ProductionConsentVariable"/>
///         tam değeriyle verilmelidir.</item>
/// </list>
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

    /// <summary>
    /// Çağıranın <b>beyan ettiği</b> ortam. Bağlantıdan çözülen gerçek ortamla
    /// eşleşmek zorundadır; yanlış beyan komutu durdurur.
    /// </summary>
    public const string EnvironmentVariable = "GOVAI_EF_ENVIRONMENT";

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

    /// <summary>5180 (müşterinin canlı ortamı) Postgres portu.</summary>
    public const int ProductionPort = 5432;

    /// <summary>5181 önizleme ortamının Postgres portu.</summary>
    public const int PreviewPort = 15437;

    /// <summary>Üretim veritabanının adı. Farklı bir ad, farklı bir veritabanı demektir.</summary>
    public const string ProductionDatabase = "govai";

    /// <summary>
    /// Geçici test veritabanı adının taşımak zorunda olduğu işaret.
    ///
    /// <para>
    /// Ad denetimi bilinçli olarak katı: "test niyetiyle çalıştırdım ama gerçek
    /// veritabanına gitti" hatası, adı okuyan bir kilit olmadan önlenemez. Port
    /// denetimi tek başına yetmez — aynı Postgres örneğinde hem gerçek hem geçici
    /// veritabanı bulunabilir.
    /// </para>
    /// </summary>
    public const string TestDatabaseMarker = "_test";

    /// <summary>Geçici test hedefi için beyan edilecek ortam adı.</summary>
    public const string EphemeralTestEnvironmentName = "EphemeralTest";

    /// <summary>
    /// Aynı makineye / aynı Postgres'e ulaşan sunucu adları.
    ///
    /// <para>
    /// Liste yalnızca metin karşılaştırması değildir: ad çözümlemesi de yapılır
    /// (bkz. <see cref="ResolvesToLocalMachine"/>), çünkü buraya yazılmamış bir ad da
    /// aynı hedefe çıkabilir. İkisi birlikte çalışır: liste, çözümleme yapılamayan
    /// container adlarını (host'ta çözülmez) yakalar; çözümleme ise listede olmayan
    /// takma adları yakalar.
    /// </para>
    /// </summary>
    private static readonly string[] LocalHostNames =
    [
        "localhost",
        "127.0.0.1",
        "::1",
        "[::1]",
        "0.0.0.0",
        // Container içinden host'a çıkan adlar
        "host.docker.internal",
        "gateway.docker.internal",
        "docker.for.win.localhost",
        "docker.for.mac.localhost",
        // 5180 compose yığınının Postgres'ine ulaşan adlar
        "govai-postgres-1",
        "govai_postgres_1",
        "govai-postgres",
        "postgres",
        "db",
    ];

    /// <summary>Hedef ortamın kimliği.</summary>
    public enum TargetEnvironment
    {
        /// <summary>Veritabanına bağlanılmaz.</summary>
        ModelOnly,

        /// <summary>5181 önizleme ortamı.</summary>
        Preview,

        /// <summary>5180 — müşterinin canlı verisi.</summary>
        Production,

        /// <summary>
        /// Geçici test veritabanı. Adı <see cref="TestDatabaseMarker"/> içermek
        /// <b>zorundadır</b>: yanlış hedefe migration uygulamanın en olası yolu, test
        /// niyetiyle çalıştırılan bir komutun gerçek bir veritabanına gitmesidir.
        /// </summary>
        EphemeralTest,

        /// <summary>Bunların dışında bir hedef.</summary>
        Other,
    }

    /// <summary>Çözülmüş hedef.</summary>
    public sealed record Target(
        string ConnectionString,
        string Description,
        TargetEnvironment Environment)
    {
        public bool IsModelOnly => Environment == TargetEnvironment.ModelOnly;
    }

    /// <summary>
    /// Ortam değişkenlerinden hedefi çözer.
    /// </summary>
    /// <param name="readVariable">Değişken okuyucu; testte sahtelenir.</param>
    /// <param name="resolveHost">
    /// Ad çözümleyici; testte sahtelenir. <c>null</c> dönerse ad çözülemedi demektir
    /// ve yalnızca <see cref="LocalHostNames"/> listesi geçerli olur.
    /// </param>
    /// <exception cref="EfMigrationTargetException">
    /// Bağlantı verilmediğinde, geçersiz olduğunda, beyan edilen ortam gerçek hedefle
    /// uyuşmadığında ya da korunan hedefe onaysız gidildiğinde atılır.
    /// </exception>
    public static Target Resolve(
        Func<string, string?> readVariable,
        Func<string, IPAddress[]>? resolveHost = null)
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
            return new Target(
                ModelOnlyConnectionString,
                "model-only (veritabanına bağlanılmaz)",
                TargetEnvironment.ModelOnly);
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

        var ortam = Classify(host, port, database, resolveHost);
        var description = $"{host}:{port}/{database} [{Etiket(ortam)}]";

        DogrulaOrtamBeyani(readVariable, ortam, host, port, database);

        if (ortam == TargetEnvironment.Production)
        {
            var consent = readVariable(ProductionConsentVariable)?.Trim();

            if (!string.Equals(consent, ProductionConsentValue, StringComparison.Ordinal))
            {
                throw new EfMigrationTargetException(ProtectedTargetMessage($"{host}:{port}/{database}"));
            }
        }

        return new Target(raw, description, ortam);
    }

    /// <summary>
    /// Bağlantının gerçekte hangi ortama gittiği. Karar <b>yalnızca metne</b> değil,
    /// ad çözümlemesine de dayanır.
    /// </summary>
    public static TargetEnvironment Classify(
        string host,
        int port,
        string database,
        Func<string, IPAddress[]>? resolveHost = null)
    {
        var yerel = IsLocalHostName(host) || ResolvesToLocalMachine(host, resolveHost);

        if (port == ProductionPort && yerel)
        {
            // Üretim portu + yerel makine: 5180'in Postgres'i. Veritabanı adı farklı olsa
            // bile aynı sunucudur; yanlışlıkla oraya şema yazmak istemiyoruz.
            return TargetEnvironment.Production;
        }

        if (port == PreviewPort && yerel)
        {
            return TargetEnvironment.Preview;
        }

        // Adı test işareti taşıyan veritabanı geçici test hedefi sayılır. Korunan
        // portlarda olamaz: orada "govai_test" adlı bir veritabanı da olsa aynı
        // sunucudur ve yanlışlıkla gerçek şemaya dokunma riski sürer.
        if (database.Contains(TestDatabaseMarker, StringComparison.OrdinalIgnoreCase)
            && port != ProductionPort
            && port != PreviewPort)
        {
            return TargetEnvironment.EphemeralTest;
        }

        return TargetEnvironment.Other;
    }

    /// <summary>
    /// Geçici test hedefi için güvenlik kilidi.
    ///
    /// <para>
    /// Betikler ve testler bunu doğrudan çağırır: bağlantı test hedefi <b>değilse</b>
    /// açıklayıcı bir hatayla durur. Böylece "izole test veritabanında çalıştırıyorum"
    /// diyen bir komut, gerçekte üretim ya da önizleme veritabanına bağlıysa hiçbir şey
    /// yapamaz.
    /// </para>
    /// </summary>
    public static void RequireEphemeralTestTarget(string connectionString)
    {
        if (!TryDescribe(connectionString, out var host, out var port, out var database, out var error))
        {
            throw new EfMigrationTargetException(
                $"Test hedefi çözümlenemedi: {error} "
                + "(Bağlantı dizesinin kendisi güvenlik gereği gösterilmez.)");
        }

        var ortam = Classify(host, port, database, SystemResolver);

        if (ortam != TargetEnvironment.EphemeralTest)
        {
            throw new EfMigrationTargetException(
                $"Bu komut yalnızca geçici test veritabanında çalışır. Çözülen hedef: "
                + $"{host}:{port}/{database} [{Etiket(ortam)}]. "
                + $"Test veritabanının adı '{TestDatabaseMarker}' içermeli ve portu "
                + $"{ProductionPort} veya {PreviewPort} OLMAMALIDIR.");
        }
    }

    /// <summary>Sunucu adı, bilinen yerel/container adlarından biri mi?</summary>
    public static bool IsLocalHostName(string host)
    {
        host = Normalize(host);

        return LocalHostNames.Any(ad => string.Equals(ad, host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ad, bu makinenin kendisine mi çözülüyor? Listede olmayan bir takma ad
    /// (<c>db.local</c>, <c>/etc/hosts</c> girdisi…) da aynı Postgres'e çıkabilir.
    /// Çözümleme başarısız olursa <c>false</c> döner; liste yine devrededir.
    /// </summary>
    public static bool ResolvesToLocalMachine(string host, Func<string, IPAddress[]>? resolveHost)
    {
        if (resolveHost is null)
        {
            return false;
        }

        try
        {
            return resolveHost(Normalize(host)).Any(IPAddress.IsLoopback);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            // Ad çözülemedi: liste tabanlı denetim geçerli kalır.
            return false;
        }
    }

    /// <summary>Varsayılan ad çözümleyici.</summary>
    public static IPAddress[] SystemResolver(string host) => Dns.GetHostAddresses(host);

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

    private static string Normalize(string host) =>
        host.Trim().Trim('[', ']').TrimEnd('.').ToLowerInvariant();

    private static string Etiket(TargetEnvironment ortam) => ortam switch
    {
        TargetEnvironment.ModelOnly => "model-only",
        TargetEnvironment.Preview => "önizleme",
        TargetEnvironment.Production => "ÜRETİM (5180)",
        TargetEnvironment.EphemeralTest => "geçici test veritabanı",
        _ => "diğer",
    };

    /// <summary>
    /// Çağıranın beyan ettiği ortam ile bağlantının gerçek hedefi tutuyor mu?
    ///
    /// Bu adım kazayı yakalar: "önizlemeye uyguluyorum" diyen bir komut aslında
    /// üretime bağlanıyorsa, onay değişkeni olsa bile burada durur.
    /// </summary>
    private static void DogrulaOrtamBeyani(
        Func<string, string?> readVariable,
        TargetEnvironment gercek,
        string host,
        int port,
        string database)
    {
        var beyan = readVariable(EnvironmentVariable)?.Trim();

        if (string.IsNullOrWhiteSpace(beyan))
        {
            throw new EfMigrationTargetException(
                $"""
                {EnvironmentVariable} tanımlı değil.

                Hangi ortama çalıştığını açıkça beyan etmelisin. Bağlantıdan çözülen
                gerçek hedef: {host}:{port}/{database} → {Etiket(gercek)}

                  $env:{EnvironmentVariable} = "{Deger(gercek)}"

                Beyan ile gerçek hedef tutmazsa komut durur. Hazır komutlar bunu
                senin için ayarlar: scripts/ef-migrate.ps1 ve scripts/ef-migrate.sh
                """);
        }

        if (!Enum.TryParse<TargetEnvironment>(beyan, ignoreCase: true, out var beyanEdilen)
            || beyanEdilen != gercek)
        {
            throw new EfMigrationTargetException(
                $"""
                Ortam beyanı hedefle uyuşmuyor.

                  Beyan edilen : {beyan}
                  Gerçek hedef : {host}:{port}/{database} → {Etiket(gercek)}

                Bu bir kazadır: bağlantı dizesi beyan ettiğin ortama gitmiyor.
                Ya bağlantıyı ya beyanı düzelt. Geçerli beyanlar: Preview, Production, Other.

                Veritabanı adının da doğru olduğundan emin ol — üretim veritabanı
                '{ProductionDatabase}' adını taşır.
                """);
        }
    }

    private static string Deger(TargetEnvironment ortam) => ortam.ToString();

    private static string MissingVariableMessage() =>
        $"""
        {ConnectionVariable} tanımlı değil.

        EF Core komutları GOVAI'de sessiz bir varsayılana DÜŞMEZ: appsettings içindeki
        bağlantı localhost:{ProductionPort}'dir ve orası çalışan 5180 ortamının veritabanıdır.
        Hedefi her zaman açıkça vermelisin.

          # Şemaya dokunmayan komutlar (migrations add, has-pending-model-changes, script)
          $env:{ConnectionVariable} = "{ModelOnlyValue}"

          # Önizleme (5181) veritabanına migration uygulamak
          $env:{ConnectionVariable} = "Host=localhost;Port={PreviewPort};Database=govai;Username=govai;Password=<parola>"
          $env:{EnvironmentVariable} = "Preview"

        Hazır komutlar: scripts/ef-migrate.ps1 ve scripts/ef-migrate.sh
        """;

    private static string ProtectedTargetMessage(string description) =>
        $"""
        Hedef korunan veritabanı: {description}

        Burası 5180 ortamının (deploy/docker-compose.yml) Postgres'idir; müşterinin
        gerçek şirket, kullanıcı ve değerlendirme verisi buradadır.

        Devam etmek gerçekten isteniyorsa ÖNCE yedek al, sonra onayı açıkça ver:

          $env:{ProductionConsentVariable} = "{ProductionConsentValue}"

        Önizleme ortamına çalışmak istiyorsan port {PreviewPort}'dir, {ProductionPort} değil.
        """;
}

/// <summary>EF hedefi güvenli biçimde çözülemedi.</summary>
public sealed class EfMigrationTargetException(string message) : Exception(message);
