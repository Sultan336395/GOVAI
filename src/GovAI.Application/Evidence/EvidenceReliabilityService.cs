using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Evidence;

namespace GovAI.Application.Evidence;

/// <summary>Tek bir kanıtın güvenilirlik değerlendirmesi.</summary>
public sealed record EvidenceReliabilityDto(
    string Label,
    EvidenceKind Kind,
    string KindLabel,
    EvidenceStatus Status,
    string StatusLabel,
    decimal? Score,
    int? AgeDays,
    int HalfLifeDays,
    DateOnly? WeakensOn,
    string Reason,
    bool IsDependable);

/// <summary>Firmanın kanıt portföyü ve özeti.</summary>
public sealed record EvidencePortfolioDto(
    Guid CompanyId,
    string CompanyName,
    DateOnly AsOf,
    int Total,
    int Dependable,
    int Weakening,
    int Undependable,
    int Expired,
    int Unknown,
    decimal? AverageScore,
    int NeedsAttention,
    IReadOnlyList<EvidenceReliabilityDto> Items);

/// <summary>
/// Kanıt eskime raporunu üretir.
///
/// <para>
/// Hesap <see cref="EvidenceHalfLife"/> ve <see cref="CompanyEvidenceCollector"/>
/// içindedir ve saftır; burada yalnızca yetki doğrulanır, firma yüklenir ve sonuç
/// dışarıya çevrilir.
/// </para>
/// </summary>
public sealed class EvidenceReliabilityService(
    ICompanyRepository companies,
    CompanyAccessGuard access,
    IDateTimeProvider clock)
{
    public async Task<EvidencePortfolioDto> GetAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var ozet = await access.LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);

        // Kanıtlar alt koleksiyonlardan (sertifika, mali yıl) gelir; yetki denetiminden
        // dönen özet kayıt bunları taşımaz.
        var firma = await companies.GetWithDetailsAsync(companyId, cancellationToken) ?? ozet;

        var bugun = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var kanitlar = CompanyEvidenceCollector.Collect(firma, bugun);
        var portfoy = EvidenceHalfLife.Summarize(kanitlar);

        return new EvidencePortfolioDto(
            firma.Id,
            firma.LegalName,
            bugun,
            portfoy.Total,
            portfoy.Dependable,
            portfoy.Weakening,
            portfoy.Undependable,
            portfoy.Expired,
            portfoy.Unknown,
            portfoy.AverageScore,
            portfoy.NeedsAttention,
            // Önce ilgi isteyenler: süresi dolmuş ve güvenilmez kanıtlar başa gelir,
            // ölçülemeyenler en sona. Ekranda ilk satır zaten aksiyon gereken satırdır.
            [.. kanitlar
                .OrderBy(SiraAnahtari)
                .ThenBy(k => k.Score ?? decimal.MaxValue)
                .Select(Cevir)]);
    }

    /// <summary>Aciliyet sırası; skor tek başına yetmez çünkü ölçülemeyenin skoru yoktur.</summary>
    private static int SiraAnahtari(EvidenceReliability k) => k.Status switch
    {
        EvidenceStatus.Gecersiz => 0,
        EvidenceStatus.Guvenilmez => 1,
        EvidenceStatus.Zayifliyor => 2,
        EvidenceStatus.Guvenilir => 3,
        _ => 4,
    };

    private static EvidenceReliabilityDto Cevir(EvidenceReliability k) => new(
        k.Label,
        k.Kind,
        EvidenceLabels.Of(k.Kind),
        k.Status,
        EvidenceLabels.Of(k.Status),
        k.Score,
        k.AgeDays,
        k.HalfLifeDays,
        k.WeakensOn,
        k.Reason,
        k.IsDependable);
}

/// <summary>Kanıt türü ve durumunun Türkçe karşılıkları.</summary>
public static class EvidenceLabels
{
    public static string Of(EvidenceKind kind) => kind switch
    {
        EvidenceKind.Certificate => "Sertifika / belge",
        EvidenceKind.FinancialStatement => "Mali veri",
        EvidenceKind.WorkforceDeclaration => "Profil beyanı",
        EvidenceKind.ErpSnapshot => "ERP eşitlemesi",
        EvidenceKind.RegulationClause => "Mevzuat hükmü",
        _ => "Bilinmeyen",
    };

    public static string Of(EvidenceStatus status) => status switch
    {
        EvidenceStatus.Guvenilir => "Güvenilir",
        EvidenceStatus.Zayifliyor => "Zayıflıyor",
        EvidenceStatus.Guvenilmez => "Güvenilmez",
        EvidenceStatus.Gecersiz => "Geçersiz",
        _ => "Hesaplanamıyor",
    };
}
