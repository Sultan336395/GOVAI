using GovAI.Domain.Common;
using GovAI.Domain.Eligibility;

namespace GovAI.Domain.Leverage;

/// <summary>
/// Eksiğin ne tür bir iş olduğu. Maliyeti ve kesinliği türe göre çok farklıdır.
/// </summary>
public enum GapKind
{
    /// <summary>
    /// Beyan eksiği: veri sistemde yok, bu yüzden koşul <b>değerlendirilemiyor</b>.
    ///
    /// <para>
    /// Kapatması en ucuz iştir — bir alanı doldurmak yeter. Ama sonucu <b>belirsizdir</b>:
    /// beyan girildiğinde cevap "sağlıyor" da çıkabilir "sağlamıyor" da. Bu yüzden
    /// beyan eksiğinin getirisi <b>koşulludur</b> ve öyle sunulmalıdır.
    /// </para>
    /// </summary>
    Declaration = 1,

    /// <summary>
    /// Yetkinlik eksiği: koşul değerlendirildi ve sağlanmıyor. Kapatmak gerçek bir
    /// yatırım ister (personel almak, ciroyu büyütmek, bölgeye tesis açmak).
    /// </summary>
    Capability = 2,

    /// <summary>Belge eksiği: temin edilebilir, süresi ve maliyeti öngörülebilir.</summary>
    Document = 3
}

/// <summary>Bir eksiğin kapatılmasının tek bir çağrıdaki karşılığı.</summary>
public sealed record OpportunityUpside
{
    public required Guid OpportunityId { get; init; }

    public required string Title { get; init; }

    /// <summary>Çağrının azami tutarı. Bilinmiyorsa <c>null</c> — uydurulmaz.</summary>
    public decimal? MaxAmount { get; init; }

    /// <summary>
    /// Bu eksik kapatılınca çağrıda <b>başka engel kalmıyor</b> mu?
    ///
    /// <para>
    /// Ayrım ürünün dürüstlüğüdür: üç engelden birini kapatıp "6 milyonluk hibe açıldı"
    /// demek yalan olurdu. Yalnızca bu bayrak açıkken "açılır" denir.
    /// </para>
    /// </summary>
    public required bool UnlockedByThisAlone { get; init; }

    /// <summary>Bu eksik kapatıldıktan sonra çağrıda kalacak diğer engel sayısı.</summary>
    public required int RemainingGapCount { get; init; }
}

/// <summary>Kapatılabilir bir eksik ve açtığı fırsatlar.</summary>
public sealed record ComplianceGap
{
    /// <summary>Eksiğin dayandığı alan ya da belge kodu; aynı eksik buna göre gruplanır.</summary>
    public required string Key { get; init; }

    /// <summary>Kullanıcıya gösterilecek koşul metni.</summary>
    public required string Requirement { get; init; }

    public required GapKind Kind { get; init; }

    /// <summary>Ne yapılması gerektiği; çağrı metninden gelir, uydurulmaz.</summary>
    public string? SuggestedAction { get; init; }

    /// <summary>Bu eksiğin etkilediği çağrılar.</summary>
    public required IReadOnlyList<OpportunityUpside> Opportunities { get; init; }

    /// <summary>Yalnızca bu eksik kapatılarak açılacak çağrı sayısı.</summary>
    public int UnlockCount => Opportunities.Count(o => o.UnlockedByThisAlone);

    /// <summary>Eksiğin etkilediği toplam çağrı sayısı (tek başına açmasa da).</summary>
    public int AffectedCount => Opportunities.Count;

    /// <summary>
    /// Yalnızca bu eksik kapatılarak açılacak çağrıların toplam azami tutarı.
    ///
    /// <para>
    /// Tutarı bilinmeyen çağrılar toplama <b>katılmaz</b>; bu yüzden sayı bir alt
    /// sınırdır. <see cref="AmountIsPartial"/> bunu söyler.
    /// </para>
    /// </summary>
    public decimal? UnlockedAmount
    {
        get
        {
            var tutarlilar = Opportunities
                .Where(o => o.UnlockedByThisAlone && o.MaxAmount is not null)
                .Select(o => o.MaxAmount!.Value)
                .ToList();

            return tutarlilar.Count == 0 ? null : tutarlilar.Sum();
        }
    }

    /// <summary>Açılacak çağrıların bir kısmının tutarı bilinmiyor mu?</summary>
    public bool AmountIsPartial =>
        Opportunities.Any(o => o.UnlockedByThisAlone && o.MaxAmount is null);

    /// <summary>
    /// Getiri <b>koşullu</b> mu? Beyan eksiklerinde evet: alan doldurulduğunda cevap
    /// olumsuz da çıkabilir ve çağrı açılmayabilir.
    /// </summary>
    public bool IsConditional => Kind == GapKind.Declaration;
}

/// <summary>
/// Uyumu fırsata çeviren motor.
///
/// <para>
/// <b>Çözdüğü problem:</b> uyum araçları "şu koşulu sağlamıyorsun" der ve orada durur.
/// Firma için asıl soru ise "hangisini kapatırsam ne kazanırım"dır. Bu motor eksikleri
/// tersine çevirir: her eksik için, kapatılırsa hangi çağrıların açılacağını ve ne
/// kadar tutar anlamına geldiğini hesaplar.
/// </para>
///
/// <para>
/// <b>Neden GOVAI'de anlamlı:</b> yalnızca uyuma bakan bir sistem bu hesabı yapamaz,
/// çünkü elinde fırsat ve ihale tarafı yoktur. Buradaki bağ, iki tarafın aynı motorda
/// olmasından doğar.
/// </para>
///
/// <para>
/// <b>Dürüstlük kuralları:</b> üç engelden birini kapatıp "hibe açıldı" denmez
/// (<see cref="OpportunityUpside.UnlockedByThisAlone"/>); tutarı bilinmeyen çağrı
/// toplama katılmaz (<see cref="ComplianceGap.AmountIsPartial"/>); beyan eksiğinin
/// getirisi koşullu işaretlenir (<see cref="ComplianceGap.IsConditional"/>) çünkü
/// alan doldurulduğunda cevap olumsuz da çıkabilir.
/// </para>
///
/// <para>
/// Saf ve deterministiktir: §2.1 gereği model çağrısı, ağ ve rastgelelik yoktur.
/// </para>
/// </summary>
public static class ComplianceLeverage
{
    /// <summary>Çağrı künyesi; motor fırsat nesnesine değil bu özete bağlıdır.</summary>
    public sealed record OpportunityBrief
    {
        public required Guid Id { get; init; }

        public required string Title { get; init; }

        public decimal? MaxAmount { get; init; }
    }

    /// <summary>
    /// Bir firmanın tüm değerlendirmelerinden kapatılabilir eksikleri çıkarır.
    ///
    /// <para>
    /// Değerlendirilmiş ama <b>uygun bulunmuş</b> çağrılarda eksik aranmaz; onlarda
    /// kapatılacak bir şey yoktur. Süresi dolmuş çağrılar da dışarıdadır: bugün
    /// kapatılamayacak bir eksik için yatırım önermek yanıltıcı olurdu.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ComplianceGap> Analyze(
        IReadOnlyCollection<EligibilityOutcome> outcomes,
        IReadOnlyDictionary<Guid, OpportunityBrief> opportunities)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(opportunities);

        var toplananlar = new Dictionary<string, GapAccumulator>(StringComparer.Ordinal);

        foreach (var sonuc in outcomes)
        {
            if (!opportunities.TryGetValue(sonuc.OpportunityId, out var cagri))
            {
                // Künyesi olmayan çağrı için başlık ve tutar uydurulmaz; atlanır.
                continue;
            }

            var eksikler = EksikleriCikar(sonuc);

            if (eksikler.Count == 0)
            {
                continue;
            }

            foreach (var eksik in eksikler)
            {
                var kova = toplananlar.TryGetValue(eksik.Key, out var mevcut)
                    ? mevcut
                    : toplananlar[eksik.Key] = new GapAccumulator(eksik);

                kova.Ekle(new OpportunityUpside
                {
                    OpportunityId = cagri.Id,
                    Title = cagri.Title,
                    MaxAmount = cagri.MaxAmount,
                    // Bu eksik kapatılınca geriye kalan engel sayısı.
                    RemainingGapCount = eksikler.Count - 1,
                    UnlockedByThisAlone = eksikler.Count == 1,
                });
            }
        }

        return [.. toplananlar.Values
            .Select(k => k.Sonuclandir())
            // Önce tek başına en çok çağrı açan, sonra en yüksek tutar, sonra en çok
            // çağrıya dokunan. Sıralama "en az işle en çok kazanç" niyetini yansıtır.
            .OrderByDescending(g => g.UnlockCount)
            .ThenByDescending(g => g.UnlockedAmount ?? 0m)
            .ThenByDescending(g => g.AffectedCount)
            .ThenBy(g => g.Requirement, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Bir değerlendirmedeki kapatılabilir eksikler.
    ///
    /// <para>
    /// Üç kaynak birleşir: eleyen koşullar, sağlanmayan koşullar ve veri boşlukları.
    /// Eleyen koşul da kapatılabilir — "bölge dışındasın" kapatılamaz ama "belgen yok"
    /// kapatılabilir; ayrımı tür değil, koşulun kendisi belirler ve motor bunu
    /// <b>varsaymaz</b>: hepsini listeler, kararı kullanıcı verir.
    /// </para>
    /// </summary>
    private static List<GapSeed> EksikleriCikar(EligibilityOutcome sonuc)
    {
        var eksikler = new List<GapSeed>();

        foreach (var kural in sonuc.RuleEvaluations)
        {
            if (kural.NeedsData)
            {
                eksikler.Add(new GapSeed(
                    $"alan:{kural.Field}",
                    kural.Requirement,
                    GapKind.Declaration,
                    kural.SuggestedAction));

                continue;
            }

            if (kural.Outcome == RuleOutcome.NotSatisfied)
            {
                eksikler.Add(new GapSeed(
                    $"alan:{kural.Field}",
                    kural.Requirement,
                    GapKind.Capability,
                    kural.SuggestedAction));
            }
        }

        foreach (var belge in sonuc.DocumentChecklist)
        {
            // Elde olan belge eksik değildir. Süresi dolmuş olan ise eksiktir ve
            // yenilenebilir — Evidence Half-Life'ın bulduğu durum tam olarak budur.
            if (!belge.IsMandatory || belge.Status is DocumentStatus.Provided or DocumentStatus.NotRequired)
            {
                continue;
            }

            eksikler.Add(new GapSeed(
                $"belge:{belge.Code}",
                belge.Name,
                GapKind.Document,
                belge.Action));
        }

        // Aynı çağrıda aynı anahtar iki kez sayılmaz; sayılsaydı "kalan engel sayısı"
        // şişer ve hiçbir eksik "tek başına açar" görünmezdi.
        return [.. eksikler.DistinctBy(e => e.Key, StringComparer.Ordinal)];
    }

    private readonly record struct GapSeed(string Key, string Requirement, GapKind Kind, string? Action);

    private sealed class GapAccumulator(GapSeed seed)
    {
        private readonly List<OpportunityUpside> _cagrilar = [];

        public void Ekle(OpportunityUpside cagri) => _cagrilar.Add(cagri);

        public ComplianceGap Sonuclandir() => new()
        {
            Key = seed.Key,
            Requirement = seed.Requirement,
            Kind = seed.Kind,
            SuggestedAction = seed.Action,
            // Önce tek başına açılanlar, sonra tutarı yüksek olanlar.
            Opportunities = [.. _cagrilar
                .OrderByDescending(o => o.UnlockedByThisAlone)
                .ThenByDescending(o => o.MaxAmount ?? 0m)
                .ThenBy(o => o.Title, StringComparer.Ordinal)],
        };
    }
}
