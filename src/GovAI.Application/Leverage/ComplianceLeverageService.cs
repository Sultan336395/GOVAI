using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Leverage;

namespace GovAI.Application.Leverage;

/// <summary>Bir eksiğin tek bir çağrıdaki karşılığı.</summary>
public sealed record OpportunityUpsideDto(
    Guid OpportunityId,
    string Title,
    decimal? MaxAmount,
    bool UnlockedByThisAlone,
    int RemainingGapCount);

/// <summary>Kapatılabilir bir eksik ve açtığı fırsatlar.</summary>
public sealed record ComplianceGapDto(
    string Key,
    string Requirement,
    GapKind Kind,
    string KindLabel,
    string? SuggestedAction,
    int UnlockCount,
    int AffectedCount,
    decimal? UnlockedAmount,
    bool AmountIsPartial,
    bool IsConditional,
    IReadOnlyList<OpportunityUpsideDto> Opportunities);

/// <summary>Firmanın uyum eksiklerinin fırsat karşılığı.</summary>
public sealed record ComplianceLeverageDto(
    Guid CompanyId,
    string CompanyName,
    int EvaluatedOpportunityCount,
    int GapCount,
    // Tek bir eksiğin kapatılmasıyla açılabilecek FARKLI çağrı sayısı; bir çağrı
    // birden çok eksikle açılabildiği için tekilleştirilir.
    int UnlockableOpportunityCount,
    // Açılabilecek çağrıların BİLİNEN toplam azami tutarı; tutarı olmayanlar hariç.
    decimal? UnlockableAmount,
    bool AmountIsPartial,
    IReadOnlyList<ComplianceGapDto> Gaps);

/// <summary>
/// Uyum eksiklerini fırsata çevirir.
///
/// <para>
/// Hesap <see cref="ComplianceLeverage"/> içindedir ve saftır; burada yalnızca yetki
/// doğrulanır, açık çağrılar okunur ve motor çalıştırılır.
/// </para>
///
/// <para>
/// Değerlendirme <b>yeniden hesaplanır</b>, kayıtlı skorlardan okunmaz: kayıtlı
/// değerlendirme yalnızca boyut düzeyinde saklanıyor, oysa "hangi koşul eksik"
/// sorusunun cevabı kural düzeyindedir. Aynı motor aynı girdiyle aynı sonucu verdiği
/// için bu yeniden hesap tutarsızlık üretmez (§2.1).
/// </para>
/// </summary>
public sealed class ComplianceLeverageService(
    ICompanyRepository companies,
    IOpportunityRepository opportunities,
    CompanyAccessGuard access,
    IDateTimeProvider clock)
{
    public async Task<ComplianceLeverageDto> GetAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var ozet = await access.LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);

        // Motor firmanın alt koleksiyonlarını (sertifika, NACE, lokasyon) okur.
        var firma = await companies.GetWithDetailsAsync(companyId, cancellationToken) ?? ozet;

        var now = clock.UtcNow;

        // Yalnızca AÇIK çağrılar: süresi dolmuş bir çağrı için yatırım önermek
        // yanıltıcı olurdu, bugün kapatılsa bile başvurulamaz.
        var acikCagrilar = await opportunities.ListForEvaluationAsync(now, null, cancellationToken);

        var sonuclar = new List<EligibilityOutcome>(acikCagrilar.Count);
        var katalog = new Dictionary<Guid, ComplianceLeverage.OpportunityBrief>(acikCagrilar.Count);

        foreach (var cagri in acikCagrilar)
        {
            cancellationToken.ThrowIfCancellationRequested();

            sonuclar.Add(EligibilityEngine.Evaluate(firma, cagri, now));

            katalog[cagri.Id] = new ComplianceLeverage.OpportunityBrief
            {
                Id = cagri.Id,
                Title = cagri.Title,
                MaxAmount = cagri.Budget?.MaxAmount,
            };
        }

        var eksikler = ComplianceLeverage.Analyze(sonuclar, katalog);

        // Bir çağrı birden çok eksikle açılabilir; toplamda kaç FARKLI çağrının
        // açılabileceği sorulur, eksik sayısı değil.
        var acilabilirler = eksikler
            .SelectMany(e => e.Opportunities.Where(o => o.UnlockedByThisAlone))
            .GroupBy(o => o.OpportunityId)
            .Select(g => g.First())
            .ToList();

        var bilinenTutarlar = acilabilirler.Where(o => o.MaxAmount is not null).ToList();

        return new ComplianceLeverageDto(
            firma.Id,
            firma.LegalName,
            acikCagrilar.Count,
            eksikler.Count,
            acilabilirler.Count,
            bilinenTutarlar.Count == 0 ? null : bilinenTutarlar.Sum(o => o.MaxAmount!.Value),
            acilabilirler.Count != bilinenTutarlar.Count,
            [.. eksikler.Select(Cevir)]);
    }

    private static ComplianceGapDto Cevir(ComplianceGap e) => new(
        e.Key,
        e.Requirement,
        e.Kind,
        GapLabels.Of(e.Kind),
        e.SuggestedAction,
        e.UnlockCount,
        e.AffectedCount,
        e.UnlockedAmount,
        e.AmountIsPartial,
        e.IsConditional,
        [.. e.Opportunities.Select(o => new OpportunityUpsideDto(
            o.OpportunityId, o.Title, o.MaxAmount, o.UnlockedByThisAlone, o.RemainingGapCount))]);
}

/// <summary>Eksik türlerinin Türkçe karşılıkları.</summary>
public static class GapLabels
{
    public static string Of(GapKind kind) => kind switch
    {
        GapKind.Declaration => "Beyan eksiği",
        GapKind.Capability => "Yetkinlik eksiği",
        GapKind.Document => "Belge eksiği",
        _ => "Eksik",
    };
}
