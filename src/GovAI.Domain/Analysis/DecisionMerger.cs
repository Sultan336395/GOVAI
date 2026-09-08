using GovAI.Domain.Common;

namespace GovAI.Domain.Analysis;

/// <summary>
/// Kural sonucu ile yapay zekâ iddiası arasındaki uyuşmazlık kaydı.
/// Çelişki gizlenmez: hangi kriterde, kural ne dedi, model ne dedi.
/// </summary>
public sealed record RuleAiConflict
{
    public required string CriterionCode { get; init; }

    public required CriterionOutcome RuleOutcome { get; init; }

    public required AiClaimType ClaimType { get; init; }

    public required string Note { get; init; }
}

/// <summary>
/// Birleştirmenin sonucu: kriterler, kabul/ret listesi ve iki tarafın katkısı ayrı ayrı.
/// </summary>
public sealed record MergedAnalysis
{
    public required IReadOnlyList<CriterionResult> Criteria { get; init; }

    public required IReadOnlyList<ValidatedClaim> Claims { get; init; }

    public required IReadOnlyList<RuleAiConflict> Conflicts { get; init; }

    public required AiAnalysisStatus AiStatus { get; init; }

    /// <summary>Kabul edilen iddiaların kanıtla doğrulanma oranı; güven bileşenini besler.</summary>
    public decimal? AiEvidenceAgreement { get; init; }

    public IReadOnlyList<ValidatedClaim> AcceptedClaims =>
        Claims.Where(c => c.Accepted).ToList();

    public IReadOnlyList<ValidatedClaim> RejectedClaims =>
        Claims.Where(c => !c.Accepted).ToList();

    /// <summary>
    /// Kullanıcıya gösterilebilecek yapay zekâ açıklamaları. Kanıtsız iddia buraya
    /// <b>hiç girmez</b>; reddedilen iddia ekranda görünmez.
    /// </summary>
    public IReadOnlyList<string> AiExplanations =>
        AcceptedClaims.Select(c => c.Claim.Explanation).ToList();

    public bool HasAiContribution => AiStatus == AiAnalysisStatus.Succeeded && AcceptedClaims.Count > 0;
}

/// <summary>
/// Kural sonuçlarını yapay zekâ iddialarıyla birleştirir (Faz 3 — Aşama 2).
///
/// <para>
/// Sıralama tek yönlüdür: <b>kural önce gelir</b>. Model, kural sonucunu değiştiremez;
/// yalnızca eksik kalanı kanıt göstererek kapatmayı önerebilir ve sonuçları
/// açıklayabilir. Bu, ürünün ticari savunma hattının doğrudan karşılığıdır —
/// karar deterministik kalmazsa "aynı girdi aynı çıktı" iddiası düşer.
/// </para>
///
/// <para>Dört kural hiçbir koşulda esnetilmez:</para>
/// <list type="number">
/// <item>Zorunlu bir kriterin <c>NotMet</c> sonucu model tarafından değiştirilemez.</item>
/// <item><c>Unknown</c> kriter ancak <b>geçerli kanıtla</b> çözülebilir; kanıtsız <c>Met</c> olmaz.</item>
/// <item>Kural ile model çelişirse kural kazanır ve çelişki kaydedilir.</item>
/// <item>Model hatası kural sonuçlarını silmez.</item>
/// </list>
/// </summary>
public static class DecisionMerger
{
    /// <summary>
    /// Modelin bir <c>Unknown</c> kriteri çözebilmesi için gereken asgari güven.
    ///
    /// <para>
    /// Eşik yüksek tutuldu: yanlış çözülen bir kriter, kullanıcıyı uygun olmadığı bir
    /// çağrıya başvurmaya yönlendirir. "Bilmiyorum" demek, yanlış bilmekten iyidir.
    /// </para>
    /// </summary>
    public const decimal ResolutionConfidenceThreshold = 0.80m;

    public static MergedAnalysis Merge(
        IReadOnlyList<CriterionResult> ruleCriteria,
        AiAnalysisOutput ai,
        IReadOnlyList<AnalysisEvidence> evidence,
        AnalysisRuleSet? ruleSet = null)
    {
        ArgumentNullException.ThrowIfNull(ruleCriteria);
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(evidence);

        var rules = ruleSet ?? AnalysisRuleSet.Current;

        // Model kullanılamıyorsa kural sonuçları OLDUĞU GİBİ kalır. Bu, "model hatası
        // kesin kural sonuçlarını silmez" kuralının uygulandığı tek satırdır.
        if (!ai.IsUsable)
        {
            return new MergedAnalysis
            {
                Criteria = ruleCriteria,
                Claims = [],
                Conflicts = [],
                AiStatus = ai.Status,
                AiEvidenceAgreement = null
            };
        }

        var byId = evidence.ToDictionary(e => e.EvidenceChunkId);
        var byCode = ruleCriteria.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        var injection = PromptInjectionGuard.Scan(evidence);

        var validated = ai.Claims
            .Select(claim => Validate(claim, byId, byCode, injection))
            .ToList();

        var conflicts = new List<RuleAiConflict>();
        var criteria = ruleCriteria.ToList();

        foreach (var kabul in validated.Where(v => v.Accepted))
        {
            var index = criteria.FindIndex(c =>
                string.Equals(c.Code, kabul.Claim.CriterionCode, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                continue;
            }

            var kriter = criteria[index];

            if (IsConflicting(kriter, kabul.Claim))
            {
                conflicts.Add(new RuleAiConflict
                {
                    CriterionCode = kriter.Code,
                    RuleOutcome = kriter.Outcome,
                    ClaimType = kabul.Claim.ClaimType,
                    Note = $"Kural sonucu \"{kriter.Outcome}\" iken model bunun aksini savundu. "
                           + "Kural sonucu korundu."
                });

                continue;
            }

            if (kriter.Outcome == CriterionOutcome.Unknown
                && kabul.Claim.ClaimType == AiClaimType.ResolvesUnknown
                && kabul.Claim.Confidence >= ResolutionConfidenceThreshold)
            {
                criteria[index] = kriter with
                {
                    Outcome = CriterionOutcome.Met,
                    ScoreImpact = 1m,
                    Rationale = kriter.Rationale
                                + $" Eksik bilgi, belgedeki şu ifadeyle kapatıldı: {kabul.Claim.Explanation}",
                    MissingOrConflictExplanation = null,
                    Evidence = [.. kriter.Evidence, ToEvidence(byId[kabul.Claim.EvidenceChunkId], kabul.Claim.Quote)],
                    RuleSetVersion = rules.Version
                };
            }
        }

        var kabulEdilen = validated.Count(v => v.Accepted);

        return new MergedAnalysis
        {
            Criteria = criteria,
            Claims = validated,
            Conflicts = conflicts,
            AiStatus = ai.Status,
            AiEvidenceAgreement = validated.Count == 0 ? null : (decimal)kabulEdilen / validated.Count
        };
    }

    /// <summary>
    /// Tek bir iddianın doğrulanması. Sıra bilinçli: önce yapısal geçerlilik, sonra
    /// kanıta bağlılık, en sonda yetki sınırı. Yapısı bozuk bir iddianın yetkisini
    /// tartışmak anlamsızdır.
    /// </summary>
    private static ValidatedClaim Validate(
        AiClaim claim,
        IReadOnlyDictionary<Guid, AnalysisEvidence> evidence,
        IReadOnlyDictionary<string, CriterionResult> criteria,
        InjectionScanResult injection)
    {
        if (string.IsNullOrWhiteSpace(claim.Explanation))
        {
            return Reject(claim, ClaimRejectionReason.EmptyExplanation, "İddia açıklaması boş.");
        }

        if (claim.Confidence is < 0m or > 1m)
        {
            return Reject(claim, ClaimRejectionReason.InvalidConfidence,
                $"Güven değeri 0–1 aralığında değil: {claim.Confidence}.");
        }

        if (!CriterionCatalog.IsKnown(claim.CriterionCode))
        {
            return Reject(claim, ClaimRejectionReason.UnknownCriterion,
                $"Katalogda olmayan kriter kodu: {claim.CriterionCode}.");
        }

        if (!evidence.TryGetValue(claim.EvidenceChunkId, out var parca))
        {
            return Reject(claim, ClaimRejectionReason.UnknownEvidence,
                "İddianın gösterdiği kanıt parçası bu analizin kanıt kümesinde yok.");
        }

        if (!DeterministicValidators.QuoteAppears(claim.Quote ?? string.Empty, parca.Text))
        {
            return Reject(claim, ClaimRejectionReason.QuoteNotFound,
                "İddiadaki alıntı, gösterilen kanıt parçasında bulunamadı.");
        }

        var tumMetin = string.Join('\n', evidence.Values.Select(e => e.Text));
        if (!DeterministicValidators.FactsAreGrounded(claim.Explanation, tumMetin, out var dogrulanamayan))
        {
            return Reject(claim, ClaimRejectionReason.UnverifiableFact, dogrulanamayan!);
        }

        foreach (var adres in DeterministicValidators.ExtractUrls(claim.Explanation))
        {
            if (!DeterministicValidators.IsOfficialUrl(adres))
            {
                return Reject(claim, ClaimRejectionReason.InvalidOfficialUrl,
                    "Açıklamada resmî olmayan veya geçersiz bir bağlantı var.");
            }
        }

        // Talimat taşıyan bir parçaya dayanan iddia karar değiştiremez. Metin kanıt
        // olarak kalır ama "beni uygun göster" yazan bir paragraf sonucu belirleyemez.
        if (injection.SuspiciousChunkIds.Contains(claim.EvidenceChunkId)
            && claim.ClaimType != AiClaimType.Clarification)
        {
            return Reject(claim, ClaimRejectionReason.UnsupportedResolution,
                "İddia, otomatik değerlendirmeyi yönlendirmeye çalışan bir bölüme dayanıyor.");
        }

        var kriter = criteria[claim.CriterionCode];

        if (kriter.IsMandatoryFailure && claim.ClaimType == AiClaimType.SupportsCriterion)
        {
            return Reject(claim, ClaimRejectionReason.OverridesMandatoryFailure,
                "Zorunlu bir kriterin sağlanmadığı sonucu yapay zekâ tarafından değiştirilemez.");
        }

        if (kriter.Outcome == CriterionOutcome.Unknown
            && claim.ClaimType == AiClaimType.ResolvesUnknown
            && claim.Confidence < ResolutionConfidenceThreshold)
        {
            return Reject(claim, ClaimRejectionReason.UnsupportedResolution,
                $"Eksik bilgiyi kapatmak için gereken güven eşiği ({ResolutionConfidenceThreshold:0.00}) "
                + $"karşılanmıyor: {claim.Confidence:0.00}.");
        }

        return new ValidatedClaim { Claim = claim, Accepted = true };
    }

    /// <summary>Model, kuralın verdiği kesin kararın tersini mi savunuyor?</summary>
    private static bool IsConflicting(CriterionResult criterion, AiClaim claim) =>
        (criterion.Outcome == CriterionOutcome.Met && claim.ClaimType == AiClaimType.ContradictsCriterion)
        || (criterion.Outcome == CriterionOutcome.NotMet && claim.ClaimType == AiClaimType.SupportsCriterion);

    private static ValidatedClaim Reject(AiClaim claim, ClaimRejectionReason reason, string note) =>
        new()
        {
            Claim = claim,
            Accepted = false,
            RejectionReason = reason,
            RejectionNote = note
        };

    private static CriterionEvidence ToEvidence(AnalysisEvidence parca, string? quote) => new()
    {
        EvidenceChunkId = parca.EvidenceChunkId,
        DocumentVersionId = parca.DocumentVersionId,
        Excerpt = string.IsNullOrWhiteSpace(quote) ? parca.Text : quote,
        Locator = parca.SectionTitle ?? $"Paragraf {parca.SequenceNumber}"
    };
}
