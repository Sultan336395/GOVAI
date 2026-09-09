using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Opportunities;

/// <summary>
/// Teşvik – Hibe – İhale Sınıflandırma Modülü'nün (Modül 4) use-case servisi.
/// Fırsat kataloğunun okunması, worker'lardan gelen çağrıların kaydı ve danışman onayı burada yönetilir.
/// </summary>
public sealed class OpportunityService(
    IOpportunityRepository opportunities,
    ISourceRepository sources,
    ISourceDocumentRepository documents,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    IEventPublisher events,
    ICurrentUser currentUser,
    ILogger<OpportunityService> logger)
{
    public async Task<PagedResult<OpportunitySummaryDto>> SearchAsync(OpportunityQuery query, CancellationToken cancellationToken = default)
    {
        var page = await opportunities.SearchAsync(query, cancellationToken);
        var now = clock.UtcNow;

        return new PagedResult<OpportunitySummaryDto>(
            page.Items.Select(o => ToSummary(o, now)).ToList(),
            page.TotalCount,
            page.Page,
            page.PageSize);
    }

    public async Task<OpportunityDetailDto> GetAsync(Guid opportunityId, CancellationToken cancellationToken = default)
    {
        var opportunity = await opportunities.GetWithRulesAsync(opportunityId, cancellationToken)
                          ?? throw new NotFoundException("Fırsat", opportunityId);

        // Karantinadaki kayıt KİRACI kullanıcısına detay ekranında da AÇILMAZ. Listeden
        // gizleyip detayını açık bırakmak korumayı işe yaramaz kılar: bağlantı elden ele
        // dolaşabilir.
        //
        // Platform inceleme rolleri istisnadır ve bu bir gevşetme değil, rolün işidir:
        // karantinadan çıkarma kararı ancak kaydın ne olduğu görülerek verilebilir.
        // İstisna olmadan inceleyicinin elindeki tek bilgi başlık ve adres kalıyordu.
        if (!opportunity.IsPublishable && !IsPlatformReviewer())
        {
            throw new NotFoundException("Fırsat", opportunityId);
        }

        var provenance = await opportunities.GetProvenanceAsync(opportunityId, cancellationToken);
        var evidenceContext = await opportunities.GetRuleEvidenceContextAsync(opportunityId, cancellationToken);

        return ToDetail(opportunity, clock.UtcNow, provenance, evidenceContext);
    }

    /// <summary>
    /// Kullanıcı, ortak kataloğu denetleyen platform rollerinden biri mi?
    ///
    /// <para>
    /// <see cref="UserRole.SystemIngest"/> BİLEREK dışarıdadır: worker kimliği belge
    /// bırakır, karantina incelemez. Ele geçirilen bir worker kimliğinin elenmiş
    /// kayıtların içeriğini okuyabilmesi için sebep yok.
    /// </para>
    /// </summary>
    private bool IsPlatformReviewer() =>
        currentUser.Role is UserRole.PlatformReviewer or UserRole.PlatformCatalogManager;

    /// <summary>
    /// Çağrıyı oluşturur ya da aynı kaynak dokümandan gelen kaydı günceller.
    /// Parser worker'ı aynı ilanı yeniden işlediğinde kopya kayıt oluşmaz.
    /// </summary>
    public async Task<OpportunityDetailDto> UpsertAsync(UpsertOpportunityRequest request, CancellationToken cancellationToken = default)
    {
        var source = await sources.GetAsync(request.SourceId, cancellationToken)
            ?? throw new NotFoundException("Kaynak", request.SourceId);

        // Mevzuat fırsat değildir: mevzuata başvurulmaz, uyulur. Worker da bu ayrımı
        // gözetiyor, ama ona GÜVENİLMEZ — mesajda kategori alanı eksik kalırsa ya da
        // başka bir istemci gelirse ortak katalog kirlenmemeli. Bu kayıt
        // RegulatoryChange olarak açılır (bkz. SourceService.TryRecordRegulatoryChangeAsync).
        if (IsRegulationSource(source.Category))
        {
            throw new ValidationException(
                nameof(request.SourceId),
                $"'{source.Name}' bir mevzuat kaynağıdır; fırsat kataloğuna kayıt açamaz. "
                + "Bu belge mevzuat değişikliği olarak kaydedilir.");
        }

        // Bölüm başlığı bir ilan DEĞİLDİR. Resmî Gazete'nin ilan sayfası onlarca ayrı
        // ilanı tek sayfada yayımlar; sayfanın başlığı bunların ortak başlığıdır.
        // Böyle bir kayıt açılırsa şirkete "size uygun ihale var" denir ve bağlantı bir
        // liste sayfasına götürür — hangi ihaleden bahsedildiği belli olmaz.
        if (SectionHeading.IsCollective(request.Title))
        {
            throw new ValidationException(nameof(request.Title), SectionHeading.Explanation(request.Title));
        }

        Opportunity? opportunity = null;
        if (request.SourceDocumentId is not null)
        {
            opportunity = await opportunities.GetBySourceDocumentAsync(request.SourceDocumentId.Value, cancellationToken);
        }

        var isNew = opportunity is null;
        if (opportunity is null)
        {
            opportunity = new Opportunity(
                request.SourceId,
                request.SourceType,
                request.SupportCategory,
                request.Title,
                request.Publisher,
                request.PublishedAt);

            await opportunities.AddAsync(opportunity, cancellationToken);
        }

        // Başlık da tazelenir: aynı belgenin düzeltilmiş yeni sürümü geldiğinde
        // katalogdaki bozuk başlık yerinde kalmamalı.
        opportunity.RefreshTitle(request.Title);
        opportunity.RefreshPublisher(request.Publisher);
        opportunity.RefreshLegalBasis(request.LegalBasis);

        opportunity.ReplaceBudgetItems(
            request.BudgetItems.Select(b => new BudgetItem(
                b.Type, b.Amount, b.Currency, b.Excerpt, b.StartOffset, b.EndOffset)),
            request.BudgetRates.Select(r => new BudgetRate(
                r.Type, r.Rate, r.Excerpt, r.StartOffset, r.EndOffset)));
        opportunity.Describe(request.Summary, request.SourceUrl, request.SourceDocumentId);
        opportunity.SetSchedule(request.PublishedAt, request.Deadline);
        opportunity.SetBudget(request.Budget is null
            ? null
            : new BudgetRange(request.Budget.MinAmount, request.Budget.MaxAmount, request.Budget.Currency, request.Budget.SupportRate));

        opportunity.ReplaceRules(request.Rules.Select(ToDomain), request.RuleExtractionConfidence);
        await BindRuleEvidenceAsync(opportunity, request, cancellationToken);
        opportunity.ReplaceDocumentChecklist(request.DocumentChecklist.Select(ToDomain));

        // Faz 2: bulunamayan alanlar tahmin edilmez, durumları kaydedilir.
        opportunity.SetFieldAvailability(BuildAvailability(request));

        // Zorunlu veri doğrulaması: asgari alanları taşımayan kayıt kataloğa girmez,
        // karantinaya alınır ve PlatformReviewer tarafından incelenir.
        var eksik = MissingMandatoryFields(request);
        if (eksik.Count > 0)
        {
            opportunity.Quarantine(
                QuarantineReason.MissingRequiredFields,
                "Eksik zorunlu alan: " + string.Join(", ", eksik));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Fırsat {Action}. OpportunityId={OpportunityId} Kural={RuleCount} Güven={Confidence}",
            isNew ? "oluşturuldu" : "güncellendi", opportunity.Id, request.Rules.Count, request.RuleExtractionConfidence);

        // Yeni/değişmiş çağrı, tüm firmalar için yeniden skorlama tetikler.
        await events.PublishAsync(
            QueueNames.ScoringRequested,
            new { OpportunityId = opportunity.Id, RequestedAt = clock.UtcNow, Reason = isNew ? "OpportunityCreated" : "OpportunityUpdated" },
            cancellationToken);

        return ToDetail(opportunity, clock.UtcNow);
    }

    /// <summary>
    /// Ayrıştırılmış kuralları belgedeki kanıt parçalarına bağlar (Faz 3).
    ///
    /// <para>
    /// Bağlama <b>sunucuda</b> yapılır çünkü worker kanıt parçalarının kimliklerini
    /// bilmez — parçalar bu API tarafından oluşturulur. Worker yalnızca koşulun metindeki
    /// karakter aralığını bildirir; eşleşme aralık çakışmasıyla, o başarısız olursa
    /// alıntı metniyle kurulur.
    /// </para>
    ///
    /// <para>
    /// Aralığı da alıntısı da tutmayan kural <b>kanıtsız kalır ve silinmez</b>: eski
    /// kayıtlar ve elle girilmiş kurallar deterministik motorda çalışmaya devam eder.
    /// Yalnızca yapay zekâ o kural hakkında "belgede yazıyor" diyemez.
    /// </para>
    /// </summary>
    private async Task BindRuleEvidenceAsync(
        Opportunity opportunity,
        UpsertOpportunityRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourceDocumentId is null || opportunity.Rules.Count == 0)
        {
            return;
        }

        var document = await documents.GetWithVersionsAsync(request.SourceDocumentId.Value, cancellationToken);

        var version = document?.Versions
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

        if (version is null || version.Chunks.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        var istekler = request.Rules.ToList();
        var kurallar = opportunity.Rules.ToList();

        // ReplaceRules sırayı korur; istek ile kural aynı indekste eşleşir.
        for (var i = 0; i < kurallar.Count && i < istekler.Count; i++)
        {
            var eslesen = RuleEvidenceBinder.Match(
                version.Chunks,
                istekler[i].SourceExcerpt,
                istekler[i].StartOffset,
                istekler[i].EndOffset);

            RuleEvidenceBinder.Attach(kurallar[i], version, eslesen, now);
        }
    }


    /// <summary>
    /// Hangi alanın neden boş olduğunu belirler.
    ///
    /// Değer varsa <c>Provided</c>. Yoksa <c>NotProvided</c> — yani "resmî kaynakta
    /// yazmıyor". Tek istisna sürekli açık çağrılardaki son başvuru tarihidir: orada
    /// eksik veri yoktur, alan zaten uygulanamaz.
    /// </summary>
    private static OpportunityFieldAvailability BuildAvailability(UpsertOpportunityRequest request)
    {
        static FieldAvailability State(bool hasValue) =>
            hasValue ? FieldAvailability.Provided : FieldAvailability.NotProvided;

        var deadline = request.IsContinuouslyOpen
            ? FieldAvailability.NotApplicable
            : State(request.Deadline is not null);

        var budgetVar = request.Budget is not null
            && (request.Budget.MinAmount is not null || request.Budget.MaxAmount is not null);

        return new OpportunityFieldAvailability(
            deadline,
            State(budgetVar),
            State(!string.IsNullOrWhiteSpace(request.Budget?.Currency)),
            State(!string.IsNullOrWhiteSpace(request.EligibleApplicant)),
            State(!string.IsNullOrWhiteSpace(request.Geography)),
            State(!string.IsNullOrWhiteSpace(request.Sector)),
            State(!string.IsNullOrWhiteSpace(request.ProgrammeType)),
            State(!string.IsNullOrWhiteSpace(request.OfficialDocumentUrl)));
    }

    /// <summary>
    /// Kataloğa girmek için zorunlu asgari alanlar. Bunlar olmadan kayıt bir şirkete
    /// "şuna başvurabilirsin" demek için yeterli değildir.
    /// </summary>
    private static List<string> MissingMandatoryFields(UpsertOpportunityRequest request)
    {
        var eksik = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Title)) eksik.Add("başlık");
        if (string.IsNullOrWhiteSpace(request.SourceUrl)) eksik.Add("kaynak adresi");
        if (string.IsNullOrWhiteSpace(request.Publisher)) eksik.Add("kaynak kurum");
        if (string.IsNullOrWhiteSpace(request.Summary)) eksik.Add("içerik");
        if (request.PublishedAt == default) eksik.Add("yayın tarihi");

        return eksik;
    }

    /// <summary>Danışman, otomatik çıkarılan bir kuralı düzeltir. Elle düzeltilen kurallar korunur.</summary>
    public async Task<OpportunityDetailDto> OverrideRuleAsync(
        Guid opportunityId,
        Guid ruleId,
        OverrideRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await opportunities.GetWithRulesAsync(opportunityId, cancellationToken)
                          ?? throw new NotFoundException("Fırsat", opportunityId);

        var rule = opportunity.Rules.FirstOrDefault(r => r.Id == ruleId)
                   ?? throw new NotFoundException("Kural", ruleId);

        rule.OverrideManually(request.Operator, request.Value, request.Severity, request.HumanReadable);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await events.PublishAsync(
            QueueNames.ScoringRequested,
            new { OpportunityId = opportunityId, RequestedAt = clock.UtcNow, Reason = "RuleOverridden" },
            cancellationToken);

        return ToDetail(opportunity, clock.UtcNow);
    }

    /// <summary>Danışman onayı; skor güvenini tavana taşır.</summary>
    public async Task<OpportunityDetailDto> MarkReviewedAsync(Guid opportunityId, CancellationToken cancellationToken = default)
    {
        var opportunity = await opportunities.GetWithRulesAsync(opportunityId, cancellationToken)
                          ?? throw new NotFoundException("Fırsat", opportunityId);

        opportunity.MarkReviewed();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDetail(opportunity, clock.UtcNow);
    }

    private static OpportunityRule ToDomain(UpsertRuleDto dto) => new(
        dto.Field,
        dto.Operator,
        dto.Value,
        dto.Dimension,
        dto.Severity,
        dto.HumanReadable,
        dto.SourceExcerpt,
        dto.Confidence);

    private static DocumentRequirement ToDomain(DocumentRequirementDto dto) =>
        new(dto.Code, dto.Name, dto.IsMandatory, dto.IssuingAuthority, dto.Notes);

    public static OpportunitySummaryDto ToSummary(Opportunity opportunity, DateTimeOffset now) => new(
        opportunity.Id,
        opportunity.Title,
        opportunity.Publisher,
        opportunity.SourceType,
        opportunity.SupportCategory,
        opportunity.PublishedAt,
        opportunity.Deadline,
        opportunity.DaysUntilDeadline(now),
        opportunity.Budget?.MaxAmount,
        opportunity.Budget?.Currency,
        opportunity.IsReviewedByConsultant,
        opportunity.Rules.Count,
        opportunity.DocumentChecklist.Count);

    /// <summary>
    /// Kanıt bağlantısını ekran sözleşmesine çevirir. Bağlam verilmediyse metin ve özet
    /// boş kalır — uydurulmaz; bağlantının kendisi (parça kimliği, sürüm, aralık) yine
    /// de görünür.
    /// </summary>
    private static RuleEvidenceDto ToEvidenceDto(
        OpportunityRuleEvidence evidence,
        IReadOnlyDictionary<Guid, RuleEvidenceContext>? context)
    {
        var bulunan = context is not null && context.TryGetValue(evidence.EvidenceChunkId, out var bilgi)
            ? bilgi
            : null;

        return new RuleEvidenceDto(
            evidence.EvidenceChunkId,
            evidence.DocumentVersionId,
            evidence.Role,
            RuleEvidenceLabels.Of(evidence.Role),
            evidence.StartOffset,
            evidence.EndOffset,
            evidence.PageNumber,
            evidence.SectionTitle,
            bulunan?.Text ?? string.Empty,
            bulunan?.TextHash ?? string.Empty,
            bulunan?.OfficialUrl,
            bulunan?.DocumentVersionNumber);
    }

    public static OpportunityDetailDto ToDetail(
        Opportunity opportunity,
        DateTimeOffset now,
        OpportunityProvenanceDto? provenance = null,
        IReadOnlyDictionary<Guid, RuleEvidenceContext>? evidenceContext = null) => new(
        opportunity.Id,
        opportunity.Title,
        opportunity.Publisher,
        opportunity.Summary,
        opportunity.SourceUrl,
        opportunity.SourceType,
        opportunity.SupportCategory,
        opportunity.PublishedAt,
        opportunity.Deadline,
        opportunity.DaysUntilDeadline(now),
        opportunity.Budget is null
            ? null
            : new BudgetDto(opportunity.Budget.MinAmount, opportunity.Budget.MaxAmount, opportunity.Budget.Currency, opportunity.Budget.SupportRate),
        opportunity.BudgetItems.Select(b => new BudgetItemDto(
            b.Type, BudgetLabels.Of(b.Type), b.Amount, b.Currency, b.Excerpt,
            b.StartOffset, b.EndOffset, b.NeedsReview)).ToList(),
        opportunity.BudgetRates.Select(r => new BudgetRateDto(
            r.Type, BudgetLabels.Of(r.Type), r.Rate, r.Excerpt,
            r.StartOffset, r.EndOffset, r.NeedsReview)).ToList(),
        opportunity.LegalBasis,
        opportunity.RuleExtractionConfidence,
        opportunity.IsReviewedByConsultant,
        opportunity.Rules.Select(r => new OpportunityRuleDto(
            r.Id, r.Field, r.Operator, r.Value, r.Dimension, r.Severity, r.HumanReadable,
            r.SourceExcerpt, r.Confidence, r.IsManuallyOverridden,
            r.Evidence.Select(e => ToEvidenceDto(e, evidenceContext)).ToList(),
            r.SupportsAiClaims)).ToList(),
        opportunity.DocumentChecklist.Select(d => new DocumentRequirementDto(d.Code, d.Name, d.IsMandatory, d.IssuingAuthority, d.Notes)).ToList(),
        new FieldAvailabilityDto(
            opportunity.FieldAvailability.Deadline.ToString(),
            opportunity.FieldAvailability.Budget.ToString(),
            opportunity.FieldAvailability.Currency.ToString(),
            opportunity.FieldAvailability.EligibleApplicant.ToString(),
            opportunity.FieldAvailability.Geography.ToString(),
            opportunity.FieldAvailability.Sector.ToString(),
            opportunity.FieldAvailability.ProgrammeType.ToString(),
            opportunity.FieldAvailability.OfficialDocumentUrl.ToString()),
        // Süresi geçmiş çağrı açık gibi gösterilmez.
        IsOpen: opportunity.IsOpenOn(now),
        Provenance: provenance,
        QuarantineReason: opportunity.QuarantineReason,
        QuarantineNote: opportunity.QuarantineNote);

    /// <summary>
    /// Kaynağın kategorisi mevzuat mı? <see cref="GovAI.Application.Sources.SourceService"/>
    /// içindeki karşılığıyla aynı listedir; ikisi birlikte değişmelidir.
    /// </summary>
    private static bool IsRegulationSource(SourceCategory category) => category is
        SourceCategory.Regulation or SourceCategory.Tax or SourceCategory.SocialSecurity or
        SourceCategory.LabourLaw or SourceCategory.CommercialLaw;
}
