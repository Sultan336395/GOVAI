using System.Text.Json;
using System.Text.Json.Nodes;

namespace GovAI.Persistence.Seed;

/// <summary>
/// Kaynağın serbest yapılandırma gövdesine katalog değerlerini <b>ekleyen</b> yardımcı (Faz 2).
///
/// <para>
/// Buradaki tek iş arşiv şablonunu yerleştirmektir, ama üç kural yüzünden düz bir
/// atama yapılamaz:
/// </para>
///
/// <list type="number">
///   <item>
///     <b>Birleştirir, ezmez.</b> Gövde operatörün panelden girdiği başka anahtarları da
///     taşıyabilir. Tüm gövdeyi yeniden yazmak o anahtarları sessizce siler.
///   </item>
///   <item>
///     <b>Sürümlüdür.</b> Katalogdaki şablon ileride düzeltilebilir. Hangi sürümün
///     yazıldığı gövdede durur; yalnızca katalog sürümü büyükse yeniden yazılır.
///   </item>
///   <item>
///     <b>Operatör değerine dokunmaz.</b> Sürüm damgası olmayan bir şablon elle
///     girilmiş demektir; seed onu olduğu gibi bırakır.
///   </item>
/// </list>
///
/// <para>
/// Tamamen yereldir: ağa çıkmaz, tarih okumaz, rastgelelik içermez. Aynı girdi her
/// zaman aynı çıktıyı verir — bu yüzden seed her açılışta güvenle çalıştırılabilir.
/// </para>
/// </summary>
internal static class SourceConfigurationJson
{
    /// <summary>Şablonun kendisi.</summary>
    internal const string ArchiveUrlTemplateKey = "archiveUrlTemplate";

    /// <summary>Şablonu hangi katalog sürümünün yazdığı.</summary>
    internal const string ArchiveUrlTemplateVersionKey = "archiveUrlTemplateVersion";

    /// <summary>Uygulama sonucunun ne olduğu; seed bunu sayıp loglar.</summary>
    internal enum Outcome
    {
        /// <summary>Anahtar yoktu, eklendi.</summary>
        Added,

        /// <summary>Eski sürüm vardı, katalog sürümüne yükseltildi.</summary>
        Upgraded,

        /// <summary>Zaten güncel; gövde değişmedi.</summary>
        AlreadyCurrent,

        /// <summary>Sürüm damgası yok — elle girilmiş sayılır, dokunulmadı.</summary>
        OperatorValueKept,
    }

    /// <summary>
    /// Arşiv şablonunu gövdeye yerleştirir.
    /// </summary>
    /// <param name="currentJson">Kaynağın mevcut gövdesi; <c>null</c> veya bozuk olabilir.</param>
    /// <param name="template">Katalogdaki şablon.</param>
    /// <param name="version">Katalogdaki şablon sürümü.</param>
    /// <param name="result">
    /// Yazılacak yeni gövde. Sonuç <see cref="Outcome.AlreadyCurrent"/> veya
    /// <see cref="Outcome.OperatorValueKept"/> ise <paramref name="currentJson"/> aynen döner
    /// ve <b>yazma yapılmamalıdır</b>.
    /// </param>
    internal static Outcome ApplyArchiveUrlTemplate(
        string? currentJson,
        string template,
        int version,
        out string? result)
    {
        var govde = Parse(currentJson);

        var mevcutSablon = govde[ArchiveUrlTemplateKey]?.GetValue<string>();
        var mevcutSurum = OkunanSurum(govde[ArchiveUrlTemplateVersionKey]);

        if (mevcutSablon is null)
        {
            govde[ArchiveUrlTemplateKey] = template;
            govde[ArchiveUrlTemplateVersionKey] = version;
            result = govde.ToJsonString();
            return Outcome.Added;
        }

        // Şablon var ama damga yok: bu değeri seed yazmadı. Operatörün emeği korunur.
        if (mevcutSurum is null)
        {
            result = currentJson;
            return Outcome.OperatorValueKept;
        }

        if (mevcutSurum >= version)
        {
            result = currentJson;
            return Outcome.AlreadyCurrent;
        }

        govde[ArchiveUrlTemplateKey] = template;
        govde[ArchiveUrlTemplateVersionKey] = version;
        result = govde.ToJsonString();
        return Outcome.Upgraded;
    }

    /// <summary>
    /// Gövdeyi okur. Bozuk ya da nesne olmayan gövde <b>hata vermez</b>: seed her açılışta
    /// çalışır ve tek bir kaynağın bozuk ayarı yüzünden uygulamanın açılmaması kabul edilemez.
    /// Böyle bir gövde boş sayılır ve üzerine yazılır.
    /// </summary>
    private static JsonObject Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Damga sayı da olabilir metin de; ikisi de okunur, gerisi yok sayılır.</summary>
    private static int? OkunanSurum(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValueKind() switch
            {
                JsonValueKind.Number => node.GetValue<int>(),
                JsonValueKind.String when int.TryParse(node.GetValue<string>(), out var s) => s,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
