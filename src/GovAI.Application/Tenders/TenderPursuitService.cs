using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Application.Reporting;
using GovAI.Domain.Assessments;
using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Tenders;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Tenders;

/// <summary>
/// İhale başvuru takibi.
///
/// <para>
/// Takip firmanın <b>kendi beyanıdır</b> ve skoru, kararı, sıralamayı etkilemez. Aksi
/// hâlde firma bir ihaleyi işaretleyerek kendi puanını yükseltebilirdi; determinizm ve
/// açıklanabilirlik iddialarının ikisi de düşerdi (CLAUDE.md §2.1).
/// </para>
///
/// <para>
/// Takibe alma <see cref="CompanyPermission.Operate"/> ister, okuma
/// <see cref="CompanyPermission.Read"/>. Görüntüleyici rolünün başvuru sürecini
/// değiştirebilmesi, süreçte kimin ne dediğini izlenemez hâle getirirdi.
/// </para>
/// </summary>
public sealed class TenderPursuitService(
    ITenderPursuitRepository pursuits,
    IOpportunityRepository opportunities,
    IAssessmentRepository assessments,
    IUnitOfWork unitOfWork,
    CompanyAccessGuard access,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<TenderPursuitService> logger)
{
    public async Task<TenderBoardDto> GetBoardAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        await access.LoadAccessibleAsync(companyId, CompanyPermission.Read, cancellationToken);

        var kayitlar = await pursuits.ListForCompanyAsync(companyId, cancellationToken);
        var satirlar = await ToDtoAsync(companyId, kayitlar, cancellationToken);

        return new TenderBoardDto(companyId, satirlar, Say(satirlar));
    }

    /// <summary>
    /// İhaleyi takibe alır.
    ///
    /// <para>
    /// Zaten takipteyse yeni kayıt açılmaz, mevcut kayıt döner. İkinci satır hangisinin
    /// geçerli olduğunu belirsizleştirir ve aynı ihale ekranda iki kez görünürdü.
    /// </para>
    /// </summary>
    public async Task<TenderPursuitDto> StartAsync(
        Guid companyId,
        StartTenderPursuitRequest request,
        CancellationToken cancellationToken = default)
    {
        await access.LoadAccessibleAsync(companyId, CompanyPermission.Operate, cancellationToken);

        var cagri = await opportunities.GetWithRulesAsync(request.OpportunityId, cancellationToken)
            ?? throw new NotFoundException("Çağrı", request.OpportunityId);

        // Karantinadaki kayıt yayımlanabilir değildir; üzerine başvuru süreci kurmak,
        // firmayı doğruluğu henüz teyit edilmemiş bir ilana yatırım yaptırmak olurdu.
        DomainException.ThrowIf(
            !cagri.IsPublishable,
            "Bu çağrı incelemede olduğu için takibe alınamaz.");

        var mevcut = await pursuits.GetForOpportunityAsync(
            companyId, request.OpportunityId, cancellationToken);

        if (mevcut is not null)
        {
            return (await ToDtoAsync(companyId, [mevcut], cancellationToken))[0];
        }

        var kayit = new TenderPursuit(
            access.RequireTenant(),
            companyId,
            request.OpportunityId,
            clock.UtcNow,
            Kullanici(),
            request.Note);

        kayit.AssignOwner(request.Owner);

        await pursuits.AddAsync(kayit, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "İhale takibe alındı. CompanyId={CompanyId} OpportunityId={OpportunityId}",
            companyId, request.OpportunityId);

        return (await ToDtoAsync(companyId, [kayit], cancellationToken))[0];
    }

    /// <summary>Aşamayı değiştirir ve geçmişe satır yazar.</summary>
    public async Task<TenderPursuitDto> ChangeStatusAsync(
        Guid pursuitId,
        ChangeTenderPursuitRequest request,
        CancellationToken cancellationToken = default)
    {
        var kayit = await pursuits.GetAsync(pursuitId, cancellationToken)
            ?? throw new NotFoundException("İhale takibi", pursuitId);

        await access.LoadAccessibleAsync(kayit.CompanyId, CompanyPermission.Operate, cancellationToken);

        kayit.ChangeStatus(
            request.Status, request.Outcome, request.Note, clock.UtcNow, Kullanici());

        if (request.Owner is not null)
        {
            kayit.AssignOwner(request.Owner);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "İhale takibi güncellendi. PursuitId={PursuitId} Durum={Status}",
            pursuitId, request.Status);

        return (await ToDtoAsync(kayit.CompanyId, [kayit], cancellationToken))[0];
    }

    private string Kullanici() =>
        currentUser.Email ?? currentUser.UserId?.ToString() ?? "bilinmiyor";

    private async Task<IReadOnlyList<TenderPursuitDto>> ToDtoAsync(
        Guid companyId,
        IReadOnlyList<TenderPursuit> kayitlar,
        CancellationToken cancellationToken)
    {
        if (kayitlar.Count == 0)
        {
            return [];
        }

        var simdi = clock.UtcNow;
        var sonuc = new List<TenderPursuitDto>(kayitlar.Count);

        foreach (var kayit in kayitlar)
        {
            // Çağrı tek tek okunur: süresi geçmiş bir ihalenin takibi ekranda kalmalıdır.
            // "Teklif verildi, sonuç bekleniyor" satırı son başvuru tarihi geçtiği anda
            // kaybolsaydı süreç yarıda görünmez olurdu.
            var cagri = await opportunities.GetWithRulesAsync(kayit.OpportunityId, cancellationToken);

            if (cagri is null)
            {
                continue;
            }

            var degerlendirme = await assessments.GetLatestAsync(
                companyId, kayit.OpportunityId, cancellationToken);

            sonuc.Add(Olustur(kayit, cagri, degerlendirme, simdi));
        }

        return sonuc;
    }

    private static TenderPursuitDto Olustur(
        TenderPursuit kayit,
        Opportunity cagri,
        EligibilityAssessment? degerlendirme,
        DateTimeOffset simdi)
    {
        var kalanGun = cagri.Deadline is { } son
            ? (int)Math.Floor((son - simdi).TotalDays)
            : (int?)null;

        return new TenderPursuitDto(
            kayit.Id,
            kayit.OpportunityId,
            cagri.Title,
            cagri.Publisher,
            cagri.SupportCategory,
            CategoryLabels.Of(cagri.SupportCategory),
            cagri.SourceUrl,
            cagri.Deadline,
            kalanGun,
            kayit.Status,
            TenderLabels.Of(kayit.Status),
            kayit.Outcome,
            kayit.Outcome is { } s ? TenderLabels.Of(s) : null,
            kayit.Note,
            kayit.Owner,
            kayit.StartedAt,
            kayit.StatusChangedAt,
            kayit.IsClosed,
            kalanGun is < 0 && !kayit.IsClosed,
            degerlendirme is null
                ? null
                : new TenderAssessmentDto(
                    degerlendirme.FinalScore,
                    degerlendirme.Verdict,
                    VerdictLabels.Of(degerlendirme.Verdict),
                    degerlendirme.SectorFit,
                    SectorFitLabels.Of(degerlendirme.SectorFit),
                    degerlendirme.MissingConditionCount,
                    degerlendirme.MissingMandatoryDocumentCount,
                    degerlendirme.EvaluatedAt),
            kayit.Events
                .OrderBy(e => e.At)
                .Select(e => new TenderPursuitEventDto(
                    e.FromStatus,
                    e.FromStatus is { } f ? TenderLabels.Of(f) : null,
                    e.ToStatus,
                    TenderLabels.Of(e.ToStatus),
                    e.Outcome,
                    e.Outcome is { } o ? TenderLabels.Of(o) : null,
                    e.Note,
                    e.At,
                    e.By))
                .ToList());
    }

    private static TenderBoardCountsDto Say(IReadOnlyList<TenderPursuitDto> satirlar) =>
        new(
            satirlar.Count(p => p.Status == TenderPursuitStatus.Inceleniyor),
            satirlar.Count(p => p.Status == TenderPursuitStatus.Hazirlaniyor),
            satirlar.Count(p => p.Status == TenderPursuitStatus.TeklifVerildi),
            satirlar.Count(p => p.Outcome == TenderOutcome.Kazanildi),
            satirlar.Count(p => p.Outcome == TenderOutcome.Kaybedildi),
            satirlar.Count(p => p.Outcome == TenderOutcome.Iptal),
            satirlar.Count(p => p.Status == TenderPursuitStatus.Vazgecildi),
            satirlar.Count(p => p.IsOverdue));
}
