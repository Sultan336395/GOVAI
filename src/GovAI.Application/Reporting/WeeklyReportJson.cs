using System.Text.Json;
using System.Text.Json.Serialization;

namespace GovAI.Application.Reporting;

/// <summary>
/// Rapor gövdesinin serileştirme ayarları.
///
/// <para>
/// Yazan ve okuyan taraf <b>aynı ayarı</b> kullanmak zorundadır. Ayarlar iki yerde ayrı
/// tanımlansaydı biri değiştiğinde gövde sessizce okunamaz hâle gelirdi — kalibrasyon
/// modülünde tam olarak bu olmuştu (bkz. <c>AssessmentDetailSnapshot</c>).
/// </para>
/// </summary>
public static class WeeklyReportJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}
