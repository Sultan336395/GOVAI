using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;

namespace GovAI.Application.Opportunities;

/// <summary>
/// Bir fırsat kuralını, dayandığı resmî kanıt parçasına bağlayan <b>tek</b> karar noktası.
///
/// <para>
/// İki yol bunu kullanır: yeni ayrıştırmada <c>OpportunityService</c>, mevcut kayıtlar
/// için <c>RuleEvidenceBackfillService</c>. Mantık ortak tutulur; iki ayrı kopya
/// zamanla ayrışır ve aynı belge iki yolda farklı kanıta bağlanırdı.
/// </para>
///
/// <para>
/// Bağ <b>bulunamazsa boş döner</b>. Tahmin edilmez: yanlış parçaya bağlanan bir kural,
/// kullanıcıya "belgenin şu yerinde yazıyor" der ve orada yazmaz — kanıtsız kalmaktan
/// daha kötüdür.
/// </para>
/// </summary>
public static class RuleEvidenceBinder
{
    /// <summary>
    /// Alıntıyla eşleştirme için asgari uzunluk. Kısa alıntı ("10 çalışan") belgenin
    /// birçok yerinde geçer ve yanlış parçaya bağlanır.
    /// </summary>
    public const int MinimumExcerptLength = 20;

    /// <summary>
    /// Alıntı bu kadardan fazla parçayla eşleşirse bağ <b>kurulmaz</b>.
    ///
    /// <para>
    /// Karakter aralığı kesindir; alıntı eşleşmesi olasılıksaldır. Bir alıntı belgenin
    /// dört ayrı yerinde geçiyorsa hangisinin kaynak olduğu bilinmiyordur — hepsini
    /// bağlamak "kanıt" sayısını şişirir, doğruluğu artırmaz.
    /// </para>
    /// </summary>
    public const int MaximumExcerptMatches = 3;

    /// <summary>
    /// Kuralın hangi parçalara dayandığını bulur.
    ///
    /// <para>
    /// Önce karakter aralığı çakışması aranır — değerin gerçekten yazdığı parça budur ve
    /// <see cref="RuleEvidenceRole.ValueSource"/> olarak işaretlenir. Aralık yoksa (eski
    /// kayıtlarda hiç yoktur) alıntı metni parçalarda aranır; bu daha zayıf bir bağdır ve
    /// <see cref="RuleEvidenceRole.ConditionText"/> olur.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(DocumentEvidenceChunk Chunk, RuleEvidenceRole Role)> Match(
        IReadOnlyList<DocumentEvidenceChunk> chunks,
        string? sourceExcerpt,
        int? startOffset,
        int? endOffset)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        if (startOffset is { } baslangic && endOffset is { } bitis && bitis > baslangic)
        {
            var cakisan = chunks
                .Where(c => c.StartOffset < bitis && baslangic < c.EndOffset)
                .Select(c => (c, RuleEvidenceRole.ValueSource))
                .ToList();

            if (cakisan.Count > 0)
            {
                return cakisan;
            }
        }

        if (string.IsNullOrWhiteSpace(sourceExcerpt))
        {
            return [];
        }

        var aranan = TurkceMetin.Katla(sourceExcerpt.Trim());

        if (aranan.Length < MinimumExcerptLength)
        {
            return [];
        }

        var eslesen = chunks
            .Where(c => TurkceMetin.Katla(c.Text).Contains(aranan, StringComparison.Ordinal)
                        || aranan.Contains(TurkceMetin.Katla(c.Text), StringComparison.Ordinal))
            .ToList();

        // Belirsiz eşleşme kanıt değildir.
        if (eslesen.Count == 0 || eslesen.Count > MaximumExcerptMatches)
        {
            return [];
        }

        return eslesen.Select(c => (c, RuleEvidenceRole.ConditionText)).ToList();
    }

    /// <summary>
    /// Bulunan parçaları kurala bağlar ve <b>yeni eklenen</b> bağ sayısını döner.
    ///
    /// <para>
    /// <see cref="OpportunityRule.AttachEvidence"/> aynı parçayı aynı rolle iki kez
    /// eklemez; bu yüzden ikinci koşu sıfır döner. Sayıyı buradan almak, "kaç bağ
    /// gerçekten kuruldu" sorusunu tahmine bırakmaz.
    /// </para>
    /// </summary>
    public static int Attach(
        OpportunityRule rule,
        SourceDocumentVersion version,
        IReadOnlyList<(DocumentEvidenceChunk Chunk, RuleEvidenceRole Role)> matches,
        DateTimeOffset now)
    {
        var oncekiSayi = rule.Evidence.Count;

        foreach (var (chunk, role) in matches)
        {
            rule.AttachEvidence(new OpportunityRuleEvidence(
                chunk.Id,
                version.Id,
                role,
                chunk.StartOffset,
                chunk.EndOffset,
                now,
                chunk.PageNumber,
                chunk.SectionTitle));
        }

        return rule.Evidence.Count - oncekiSayi;
    }
}
