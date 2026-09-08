using GovAI.Domain.Analysis;

namespace GovAI.Application.Abstractions.Services;

/// <summary>
/// DeepTech analiz katmanının <b>sağlayıcıdan bağımsız</b> yapay zekâ arayüzü
/// (Faz 3 — Aşama 2).
///
/// <para>
/// Mevcut <see cref="IAiExplanationClient"/> serbest metin özeti ve kural taslağı
/// üretir; analiz katmanının ihtiyacı farklıdır: <b>kanıt kimliğine bağlı, katı şemalı
/// iddialar</b>. İkisi tek arayüzde birleştirilseydi özet üreten yol da kanıt
/// doğrulamasına tabi olurdu ve kural çıkarımı bozulurdu.
/// </para>
///
/// <para>
/// Üretim sağlayıcısı <b>yapılandırma olmadan çalışmaz</b>. Anahtar kaynak koda
/// yazılmaz; yapılandırma yoksa DI <see cref="AnalysisAiStatusDescriptions"/> ile
/// açıklanan "kullanılamıyor" sağlayıcısını bağlar ve sistem kural tabanlı çalışmaya
/// devam eder.
/// </para>
/// </summary>
public interface IAnalysisAiProvider
{
    /// <summary>Sağlayıcı adı (ör. <c>openai</c>, <c>fake</c>). Kayda yazılır.</summary>
    string ProviderName { get; }

    /// <summary>Gerçek bir model bağlantısı yapılandırılmış mı?</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Kanıtlara ve kural sonuçlarına dayanan yapılandırılmış iddialar üretir.
    ///
    /// <para>
    /// Hata fırlatmaz: erişilemeyen model <see cref="AiAnalysisStatus.AIUnavailable"/>,
    /// şemaya uymayan çıktı <see cref="AiAnalysisStatus.InvalidOutput"/> döner. Kural
    /// sonuçları her hâlükârda korunur.
    /// </para>
    /// </summary>
    Task<AiAnalysisOutput> AnalyzeAsync(AnalysisAiRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Modele giden istek.
///
/// <para>
/// <b>Kişisel veri taşımaz.</b> Firma tarafından yalnızca toplu sayılar ve kriter
/// sonuçları gider; çalışan adı, kimlik numarası, doğum tarihi, ücret ve banka bilgisi
/// bu kayda hiç girmez. Mali veriden yalnızca kriterin ihtiyaç duyduğu asgari alanlar
/// taşınır.
/// </para>
/// </summary>
public sealed record AnalysisAiRequest
{
    public required AnalysisKind Kind { get; init; }

    /// <summary>Çağrı ya da mevzuat başlığı.</summary>
    public required string TargetTitle { get; init; }

    /// <summary>Kural motorunun sonuçları; model bunları değiştiremez, açıklayabilir.</summary>
    public required IReadOnlyList<CriterionResult> Criteria { get; init; }

    /// <summary>Modelin dayanabileceği tek kanıt kümesi.</summary>
    public required IReadOnlyList<AnalysisEvidence> Evidence { get; init; }

    /// <summary>Firma profilinin kişisel veri içermeyen özeti.</summary>
    public required IReadOnlyDictionary<string, string> CompanyFacts { get; init; }

    /// <summary>Talimat içerdiği tespit edilen kanıt parçaları; modele uyarıyla verilir.</summary>
    public IReadOnlyList<Guid> SuspiciousChunkIds { get; init; } = [];

    public required string CorrelationId { get; init; }
}

/// <summary>Durum açıklamalarının tek kaynağı; ekranlar ve loglar aynı metni kullanır.</summary>
public static class AnalysisAiStatusDescriptions
{
    public const string NotConfigured =
        "Yapay zekâ sağlayıcısı yapılandırılmadı; sonuç yalnızca kural motorundan üretildi.";

    public const string Unreachable =
        "Yapay zekâ sağlayıcısına ulaşılamadı; sonuç yalnızca kural motorundan üretildi.";

    public const string InvalidOutput =
        "Yapay zekâ çıktısı beklenen şemaya uymadı; iddiaları kullanılmadı.";

    /// <summary>Ekranda düşük güvenli ya da modelsiz sonuçların yanında görünür.</summary>
    public const string RulesOnlyWarning =
        "Bu sonuç yalnızca resmî belgedeki kurallara göre hesaplandı; yapay zekâ açıklaması yok.";
}
