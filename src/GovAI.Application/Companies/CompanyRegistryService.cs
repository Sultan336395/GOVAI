using System.Text.Json;
using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Analysis;
using GovAI.Application.Common;
using GovAI.Application.Reference;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Companies;

/// <summary>
/// Manuel şirket ekleme, listeleme, grup ve ana–bağlı şirket yönetimi (Faz 1).
///
/// Şirket oluşturan kullanıcı otomatik olarak o şirketin <see cref="CompanyRole.CompanyOwner"/>
/// üyesi olur; aksi hâlde kendi eklediği şirkete erişemezdi.
/// </summary>
public sealed class CompanyRegistryService(
    ICompanyRepository companies,
    IUserCompanyRepository memberships,
    ICompanyGroupRepository groups,
    ICompanyVerificationRequestRepository verificationRequests,
    ICrossTenantCompanyLookup crossTenantLookup,
    ITenantRepository tenants,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    CompanyAccessGuard access,
    IDateTimeProvider clock,
    IEventPublisher events,
    AnalysisInvalidationService analysisInvalidation,
    ILogger<CompanyRegistryService> logger)
{
    // ══════════════════════ Şirketlerim ══════════════════════

    /// <summary>
    /// Kullanıcının <b>aktif üyeliği bulunan</b> şirketler. Kiracıdaki diğer şirketler
    /// listelenmez: üyelik yoksa erişim de yoktur.
    /// </summary>
    public async Task<IReadOnlyList<MyCompanyDto>> ListMyCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();
        var userId = RequireUser();

        var myMemberships = (await memberships.ListForUserAsync(userId, cancellationToken))
            .Where(m => m.GrantsAccess)
            .ToList();

        if (myMemberships.Count == 0)
        {
            return [];
        }

        var all = await companies.ListAsync(tenantId, cancellationToken);
        var byId = all.ToDictionary(c => c.Id);
        var groupNames = (await groups.ListAsync(tenantId, cancellationToken)).ToDictionary(g => g.Id, g => g.Name);

        var result = new List<MyCompanyDto>();

        foreach (var membership in myMemberships)
        {
            if (!byId.TryGetValue(membership.CompanyId, out var company))
            {
                continue;
            }

            byId.TryGetValue(company.ParentCompanyId ?? Guid.Empty, out var parent);

            result.Add(new MyCompanyDto(
                company.Id,
                company.LegalName,
                company.Registry.ShortName,
                company.TaxNumber,
                company.LegalType,
                company.Size,
                company.PrimaryNaceCode,
                company.MainSector,
                company.Contact.City,
                company.Workforce.EmployeeCount,
                company.Workforce.WomenEmployeeCount,
                company.Workforce.YoungEmployeeCount,
                company.Workforce.YoungEmployeeMaxAge,
                company.Workforce.RAndDEmployeeCount,
                company.Workforce.DisabledEmployeeCount,
                company.Financials.AnnualRevenue,
                company.ProfileCompletionPercentage,
                company.IsActive,
                company.GroupId,
                company.GroupId is not null && groupNames.TryGetValue(company.GroupId.Value, out var gn) ? gn : null,
                company.ParentCompanyId,
                parent?.LegalName,
                company.RelationshipType,
                company.IsHeadCompany,
                membership.CompanyRole,
                membership.IsDefault));
        }

        return result
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.LegalName)
            .ToList();
    }

    // ══════════════════════ Şirket ekleme ══════════════════════

    public async Task<CreateCompanyResult> CreateAsync(CreateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();
        var userId = RequireUser();

        // Yetki EN BAŞTA doğrulanır: reddedilen istek hiçbir kayıt bırakmamalıdır.
        // Aşağıdaki çapraz çalışma alanı yolu doğrulama talebi YAZAR; kontrol sonraya
        // kalsaydı yetkisiz bir kullanıcı da o kaydı oluşturabilirdi.
        await EnsureCanCreateCompanyAsync(tenantId, userId, cancellationToken);

        var taxNumber = NormalizeTaxNumber(request.TaxNumber, request.Country);
        ValidateRequiredFields(request);

        // 1) Aynı çalışma alanında zaten var mı?
        var existing = await companies.GetByTaxNumberAsync(tenantId, taxNumber, cancellationToken);
        if (existing is not null)
        {
            var membership = await memberships.GetAsync(userId, existing.Id, cancellationToken);

            return new CreateCompanyResult(
                CreateCompanyOutcome.AlreadyInWorkspace,
                "Bu şirket çalışma alanınızda kayıtlı.",
                existing.Id,
                CanNavigateToExisting: membership is not null && membership.GrantsAccess,
                VerificationRequestId: null);
        }

        // 2) Başka bir çalışma alanında mı?
        //    Karşı tarafın adı, kimliği veya kiracısı ASLA açıklanmaz; yalnızca
        //    doğrulama talebi açılır ve genel bir mesaj döner.
        if (await crossTenantLookup.ExistsInAnotherTenantAsync(tenantId, taxNumber, cancellationToken))
        {
            var pending = await verificationRequests.GetPendingAsync(tenantId, taxNumber, cancellationToken);

            if (pending is null)
            {
                pending = new CompanyVerificationRequest(
                    tenantId, taxNumber, request.LegalName.Trim(), userId, clock.UtcNow);

                await verificationRequests.AddAsync(pending, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            logger.LogInformation(
                "Çapraz çalışma alanı şirket talebi açıldı. TenantId={TenantId} TaxNumber={TaxNumber}",
                tenantId, taxNumber);

            return new CreateCompanyResult(
                CreateCompanyOutcome.VerificationRequired,
                "Bu şirket için doğrulama veya bağlantı talebi gereklidir. Talebiniz kaydedildi.",
                CompanyId: null,
                CanNavigateToExisting: false,
                VerificationRequestId: pending.Id);
        }

        await EnsureCompanyQuotaAsync(tenantId, cancellationToken);

        // 3) Oluştur
        var company = new Company(tenantId, request.LegalName.Trim(), taxNumber, request.LegalType);
        ApplyRequest(company, request);

        if (request.GroupId is not null || request.ParentCompanyId is not null || request.IsHeadCompany)
        {
            await ApplyHierarchyAsync(
                company,
                new UpdateCompanyHierarchyRequest
                {
                    GroupId = request.GroupId,
                    ParentCompanyId = request.ParentCompanyId,
                    RelationshipType = request.RelationshipType,
                    IsHeadCompany = request.IsHeadCompany
                },
                tenantId,
                cancellationToken);
        }

        await companies.AddAsync(company, cancellationToken);

        // Şirketi ekleyen kişi sahibidir; aksi hâlde kendi eklediği şirkete erişemezdi.
        var isFirstCompany = (await memberships.ListForUserAsync(userId, cancellationToken)).Count == 0;
        await memberships.AddAsync(
            new UserCompany(tenantId, userId, company.Id, CompanyRole.CompanyOwner, isDefault: isFirstCompany),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Şirket eklendi. CompanyId={CompanyId} TenantId={TenantId}", company.Id, tenantId);

        return new CreateCompanyResult(
            CreateCompanyOutcome.Created,
            "Şirket eklendi.",
            company.Id,
            CanNavigateToExisting: true,
            VerificationRequestId: null);
    }

    // ══════════════════════ Şirket düzenleme ══════════════════════

    public async Task<MyCompanyDto> UpdateAsync(
        Guid companyId,
        CreateCompanyRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();
        var company = await access.LoadAccessibleAsync(companyId, CompanyPermission.ManageProfile, cancellationToken);

        var taxNumber = NormalizeTaxNumber(request.TaxNumber, request.Country);
        ValidateRequiredFields(request);

        if (!string.Equals(taxNumber, company.TaxNumber, StringComparison.Ordinal))
        {
            throw new ValidationException(
                nameof(request.TaxNumber),
                "Vergi numarası değiştirilemez. Farklı bir tüzel kişilik için yeni şirket ekleyin.");
        }

        ApplyRequest(company, request);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Profil değişti; eski skorlar artık bu firmayı anlatmıyor.
        //
        // Bu tetikleme ERP eşitleme yolunda vardı ama panel yolunda YOKTU. Sonucu sahada
        // görüldü: kullanıcı sektörünü düzeltti, "fırsatlarım" listesi eski sektörle
        // hesaplanmış hâlde kaldı ve düzeltmenin işe yaramadığını sandı. Sektör artık
        // sıralamanın birincil ölçütü olduğu için bu sessiz eskime kabul edilemez.
        await QueueRescoringAsync(companyId, cancellationToken);

        // DeepTech analizleri de eskidi. Kayıt SİLİNMEZ; yalnızca "güncel" işareti
        // kalkar, böylece ekran eski profile göre hesaplanmış bir sonucu geçerli gibi
        // göstermez.
        await analysisInvalidation.InvalidateForCompanyAsync(companyId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var membership = await memberships.GetAsync(RequireUser(), companyId, cancellationToken);
        return ToMyCompany(company, membership!, null, null);
    }

    /// <summary>
    /// Yeniden skorlamayı kuyruğa bırakır. Skor burada hesaplanmaz: hesaplama uzun
    /// sürebilir ve kullanıcının kaydet düğmesini bekletmemelidir.
    /// </summary>
    private Task QueueRescoringAsync(Guid companyId, CancellationToken cancellationToken) =>
        events.PublishAsync(
            QueueNames.ScoringRequested,
            new { CompanyId = companyId, RequestedAt = clock.UtcNow, Reason = "CompanyProfileChanged" },
            cancellationToken);

    // ══════════════════════ Grup yönetimi ══════════════════════

    public async Task<IReadOnlyList<CompanyGroupDto>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();

        var items = await groups.ListAsync(tenantId, cancellationToken);
        var all = await companies.ListAsync(tenantId, cancellationToken);

        return items
            .Select(g => new CompanyGroupDto(
                g.Id, g.Name, g.Description, g.IsActive, all.Count(c => c.GroupId == g.Id)))
            .ToList();
    }

    public async Task<CompanyGroupDto> CreateGroupAsync(
        UpsertCompanyGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();

        if (await groups.NameExistsAsync(tenantId, request.Name, null, cancellationToken))
        {
            throw new ValidationException(nameof(request.Name), "Bu adda bir şirket grubu zaten var.");
        }

        var group = new CompanyGroup(tenantId, request.Name, request.Description);
        await groups.AddAsync(group, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CompanyGroupDto(group.Id, group.Name, group.Description, group.IsActive, 0);
    }

    public async Task<CompanyGroupDto> UpdateGroupAsync(
        Guid groupId,
        UpsertCompanyGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();

        var group = await groups.GetAsync(groupId, cancellationToken)
                    ?? throw new NotFoundException("Şirket grubu", groupId);

        if (group.TenantId != tenantId)
        {
            throw new NotFoundException("Şirket grubu", groupId);
        }

        if (await groups.NameExistsAsync(tenantId, request.Name, groupId, cancellationToken))
        {
            throw new ValidationException(nameof(request.Name), "Bu adda bir şirket grubu zaten var.");
        }

        group.Rename(request.Name, request.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var all = await companies.ListAsync(tenantId, cancellationToken);
        return new CompanyGroupDto(group.Id, group.Name, group.Description, group.IsActive, all.Count(c => c.GroupId == group.Id));
    }

    // ══════════════════════ Hiyerarşi ══════════════════════

    public async Task<MyCompanyDto> UpdateHierarchyAsync(
        Guid companyId,
        UpdateCompanyHierarchyRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();
        var company = await access.LoadAccessibleAsync(companyId, CompanyPermission.ManageProfile, cancellationToken);

        await ApplyHierarchyAsync(company, request, tenantId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var membership = await memberships.GetAsync(RequireUser(), companyId, cancellationToken);
        return ToMyCompany(company, membership!, null, null);
    }

    /// <summary>
    /// Grup ve ana şirket bağını doğrular ve uygular.
    ///
    /// Kiracı eşitliği ve döngü kontrolü burada yapılır; tek kayda bakarak
    /// doğrulanamayacak kurallar varlık sınıfında değil, burada olmalıdır.
    /// </summary>
    private async Task ApplyHierarchyAsync(
        Company company,
        UpdateCompanyHierarchyRequest request,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (request.GroupId is not null)
        {
            var group = await groups.GetAsync(request.GroupId.Value, cancellationToken)
                        ?? throw new NotFoundException("Şirket grubu", request.GroupId.Value);

            if (group.TenantId != tenantId)
            {
                throw new NotFoundException("Şirket grubu", request.GroupId.Value);
            }
        }

        if (request.ParentCompanyId is not null)
        {
            var parent = await companies.GetWithDetailsAsync(request.ParentCompanyId.Value, cancellationToken)
                         ?? throw new NotFoundException("Ana şirket", request.ParentCompanyId.Value);

            // Ana şirket başka kiracıdaysa "yok" sayılır: varlığı bile doğrulanmaz.
            if (parent.TenantId != tenantId)
            {
                throw new NotFoundException("Ana şirket", request.ParentCompanyId.Value);
            }

            await EnsureNoCycleAsync(company.Id, parent.Id, tenantId, cancellationToken);
        }

        company.SetGroupAndParent(
            request.GroupId, request.ParentCompanyId, request.RelationshipType, request.IsHeadCompany);
    }

    /// <summary>
    /// Döngü kontrolü: adayın ata zincirinde bu şirket geçiyorsa bağ kurulamaz.
    /// Zincir sonlu olduğu için ziyaret kümesiyle sonsuz döngü de engellenir.
    /// </summary>
    private async Task EnsureNoCycleAsync(
        Guid companyId,
        Guid candidateParentId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (candidateParentId == companyId)
        {
            throw new ValidationException("ParentCompanyId", "Bir şirket kendisinin ana şirketi olamaz.");
        }

        var all = (await companies.ListAsync(tenantId, cancellationToken)).ToDictionary(c => c.Id);
        var visited = new HashSet<Guid>();
        var cursor = candidateParentId;

        while (all.TryGetValue(cursor, out var node) && node.ParentCompanyId is not null)
        {
            if (!visited.Add(cursor))
            {
                break;
            }

            if (node.ParentCompanyId == companyId)
            {
                throw new ValidationException(
                    "ParentCompanyId",
                    "Döngüsel şirket hiyerarşisi oluşturulamaz: seçtiğiniz ana şirket bu şirkete bağlı.");
            }

            cursor = node.ParentCompanyId.Value;
        }
    }

    // ══════════════════════ Doğrulama talepleri ══════════════════════

    public async Task<IReadOnlyList<VerificationRequestDto>> ListVerificationRequestsAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = access.RequireTenant();
        var items = await verificationRequests.ListForTenantAsync(tenantId, cancellationToken);

        return items
            .Select(r => new VerificationRequestDto(
                r.Id, r.TaxNumber, r.RequestedLegalName, r.Status, r.RequestedAt, r.ResolvedAt, r.ResolutionNote))
            .ToList();
    }

    // ══════════════════════ Yardımcılar ══════════════════════

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new ForbiddenException("İstek bir kullanıcıya bağlı değil.");

    /// <summary>
    /// Vergi numarası yalnızca rakamlardan oluşur. Türkiye şirketlerinde 10 hane zorunludur;
    /// diğer ülkelerde uzunluk kuralı ülkeye göre değiştiği için yalnızca rakam kontrolü yapılır.
    /// </summary>
    private static string NormalizeTaxNumber(string raw, string country)
    {
        var trimmed = (raw ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ValidationException("TaxNumber", "Vergi numarası zorunludur.");
        }

        if (!trimmed.All(char.IsAsciiDigit))
        {
            throw new ValidationException("TaxNumber", "Vergi numarası yalnızca rakamlardan oluşmalıdır.");
        }

        var isTurkey = country.Trim() is "TR" or "TUR"
            || country.Trim().Equals("Türkiye", StringComparison.OrdinalIgnoreCase)
            || country.Trim().Equals("Turkiye", StringComparison.OrdinalIgnoreCase)
            || country.Trim().Equals("Turkey", StringComparison.OrdinalIgnoreCase);

        if (isTurkey && trimmed.Length != 10)
        {
            throw new ValidationException("TaxNumber", "Türkiye şirketlerinde vergi numarası 10 hane olmalıdır.");
        }

        return trimmed;
    }

    private static void ValidateRequiredFields(CreateCompanyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LegalName))
        {
            throw new ValidationException(nameof(request.LegalName), "Ticari unvan zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(request.Country))
        {
            throw new ValidationException(nameof(request.Country), "Ülke zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(request.MainSector))
        {
            throw new ValidationException(nameof(request.MainSector), "Ana sektör zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(request.PrimaryNaceCode))
        {
            throw new ValidationException(nameof(request.PrimaryNaceCode), "Ana NACE kodu zorunludur.");
        }

        // Serbest metin dönemi kapandı: iki alan da katalogdan seçilir. Aksi hâlde aynı
        // sektörün onlarca yazımı ("İnşaat", "inşaat taahhüt", "İNŞAAT") birikir, hiçbiri
        // eşleşmez ve firma kendi sektöründeki çağrılarda "uyumsuz" görünür.
        if (ActivityCatalog.NormalizeSector(request.MainSector) is null)
        {
            throw new ValidationException(
                nameof(request.MainSector),
                "Ana sektör listeden seçilmelidir; elle yazılan değer kabul edilmez.");
        }

        if (ActivityCatalog.NormalizeNace(request.PrimaryNaceCode) is null)
        {
            throw new ValidationException(
                nameof(request.PrimaryNaceCode),
                "Ana NACE kodu listeden seçilmelidir; elle yazılan değer kabul edilmez.");
        }

        foreach (var code in request.SecondaryNaceCodes.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (ActivityCatalog.NormalizeNace(code) is null)
            {
                throw new ValidationException(
                    nameof(request.SecondaryNaceCodes),
                    $"Diğer NACE kodu listeden seçilmelidir; tanınmayan kod: {code.Trim()}");
            }
        }

        foreach (var sub in request.SubSectors.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            if (ActivityCatalog.NormalizeSector(sub) is null)
            {
                throw new ValidationException(
                    nameof(request.SubSectors),
                    $"Alt sektör listeden seçilmelidir; tanınmayan değer: {sub.Trim()}");
            }
        }

        ValidateSectorConsistency(request);
    }

    /// <summary>
    /// Sektör ile NACE kodunun birbirini tutması. İki alanın tek tek geçerli olması
    /// YETMEZ.
    ///
    /// <para>
    /// Sahada görüldü: sektörü "İnşaat ve taahhüt" seçilmiş bir firmaya beton ürünleri
    /// imalatı kodu (23.61) atandı. İkisi de katalogdaydı ama farklı alanları anlatıyordu.
    /// Motor NACE koduna baktığı için firma kendi sektöründeki ihalelerde "sektör uyumsuz"
    /// göründü — üstelik profilinde sektörü doğru yazıyordu.
    /// </para>
    ///
    /// <para>
    /// Ana NACE kodu <b>ana sektöre</b> ait olmalıdır. Diğer kodlar ana sektöre ya da
    /// beyan edilen <b>alt sektörlerden</b> birine ait olabilir: bir firma birden fazla
    /// alanda faaliyet gösterebilir, ama bunu beyan etmek zorundadır.
    /// </para>
    /// </summary>
    private static void ValidateSectorConsistency(CreateCompanyRequest request)
    {
        var anaSektor = ActivityCatalog.NormalizeSector(request.MainSector)!;

        if (!ActivityCatalog.NaceBelongsToSectors(request.PrimaryNaceCode, [anaSektor]))
        {
            var gercekSektor = ActivityCatalog.SectorOfNace(request.PrimaryNaceCode);

            throw new ValidationException(
                nameof(request.PrimaryNaceCode),
                $"Ana NACE kodu '{ActivityCatalog.NormalizeNace(request.PrimaryNaceCode)}' "
                + $"'{gercekSektor}' sektörüne aittir, seçtiğiniz '{anaSektor}' sektörüne değil. "
                + "Ya sektörü ya da kodu düzeltin; ikisi aynı faaliyeti göstermelidir.");
        }

        var izinliSektorler = new List<string> { anaSektor };
        izinliSektorler.AddRange(request.SubSectors.Select(ActivityCatalog.NormalizeSector).OfType<string>());

        foreach (var code in request.SecondaryNaceCodes.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (!ActivityCatalog.NaceBelongsToSectors(code, izinliSektorler))
            {
                var gercekSektor = ActivityCatalog.SectorOfNace(code);

                throw new ValidationException(
                    nameof(request.SecondaryNaceCodes),
                    $"'{ActivityCatalog.NormalizeNace(code)}' kodu '{gercekSektor}' sektörüne aittir. "
                    + "Bu alanda da faaliyet gösteriyorsanız önce onu alt sektör olarak ekleyin.");
            }
        }
    }

    private static void ApplyRequest(Company company, CreateCompanyRequest request)
    {
        company.UpdateIdentity(request.LegalName.Trim(), request.LegalType, request.FoundedOn);

        company.UpdateRegistry(new CompanyRegistry(
            request.ShortName, request.TaxOffice, request.MersisNumber, request.TradeRegistryNumber));

        company.UpdateContact(new CompanyContact(
            request.Website, request.Phone, request.CorporateEmail,
            request.Country, request.City, request.Address));

        // Sektör katalogdaki kanonik yazımla kaydedilir; doğrulama bunu zaten garanti etti.
        // Böylece serbest metin döneminden kalan "inşaat" kaydı ilk kaydetmede
        // "İnşaat ve taahhüt" olur. NACE kodundaki nokta yalnızca gösterim içindir;
        // alan modeli kodu zaten noktasız saklar.
        var subSectors = request.SubSectors
            .Select(ActivityCatalog.NormalizeSector)
            .OfType<string>()
            .ToList();

        company.UpdateSectors(
            ActivityCatalog.NormalizeSector(request.MainSector),
            subSectors.Count > 0 ? JsonSerializer.Serialize(subSectors) : null,
            request.TargetCountries.Count > 0 ? JsonSerializer.Serialize(request.TargetCountries) : null);

        // Genç ve engelli çalışan sayıları Faz 3'te girilebilir oldu. Önceden sabit 0
        // gidiyordu; bu yüzden "en az 5 genç çalışan" arayan teşvikler hiçbir firmada
        // sağlanamıyordu. Alanlar isteğe bağlıdır — boş bırakılırsa 0 kalır ve motor
        // yaş tanımı beyan edilmediği için sonucu "doğrulanamadı" sayar.
        company.UpdateWorkforce(new Workforce(
            request.EmployeeCount,
            request.WomenEmployeeCount,
            request.YoungEmployeeCount,
            request.RAndDEmployeeCount,
            request.DisabledEmployeeCount,
            request.YoungEmployeeMaxAge));

        company.UpdateFinancials(new Financials(
            request.AnnualRevenue, request.BalanceSize, equity: 0, exportRevenue: 0, "TRY", null));

        company.UpdateFlags(request.ExportFlag, request.IsInTechnopark, previousSuccessfulApplications: 0);

        var naceCodes = new List<CompanyNaceCode>
        {
            new(ActivityCatalog.NormalizeNace(request.PrimaryNaceCode)!, isPrimary: true, null)
        };

        naceCodes.AddRange(request.SecondaryNaceCodes
            .Select(ActivityCatalog.NormalizeNace)
            .OfType<string>()
            .Where(c => c != naceCodes[0].Code)
            .Distinct(StringComparer.Ordinal)
            .Select(c => new CompanyNaceCode(c, isPrimary: false, null)));

        company.ReplaceNaceCodes(naceCodes);

        if (!string.IsNullOrWhiteSpace(request.City))
        {
            company.ReplaceLocations([
                new CompanyLocation(request.City!.Trim(), null, null, isHeadquarters: true, request.IsInTechnopark)
            ]);
        }

        company.SetProfileCompletion(CalculateCompletion(company, request));
    }

    /// <summary>Kullanıcıya "neyi tamamlamalıyım" sinyali veren basit doluluk ölçüsü.</summary>
    private static int CalculateCompletion(Company company, CreateCompanyRequest request)
    {
        var filled = 0;
        var total = 12;

        if (!string.IsNullOrWhiteSpace(request.LegalName)) filled++;
        if (!string.IsNullOrWhiteSpace(request.TaxNumber)) filled++;
        if (!string.IsNullOrWhiteSpace(request.Country)) filled++;
        if (!string.IsNullOrWhiteSpace(request.MainSector)) filled++;
        if (!string.IsNullOrWhiteSpace(request.PrimaryNaceCode)) filled++;
        if (!string.IsNullOrWhiteSpace(request.TaxOffice)) filled++;
        if (!string.IsNullOrWhiteSpace(request.MersisNumber)) filled++;
        if (!string.IsNullOrWhiteSpace(request.City)) filled++;
        if (!string.IsNullOrWhiteSpace(request.CorporateEmail)) filled++;
        if (request.EmployeeCount > 0) filled++;
        if (request.AnnualRevenue > 0) filled++;
        if (request.FoundedOn is not null) filled++;

        return (int)Math.Round(filled * 100.0 / total);
    }

    private static MyCompanyDto ToMyCompany(
        Company company,
        UserCompany membership,
        string? groupName,
        string? parentName) =>
        new(
            company.Id,
            company.LegalName,
            company.Registry.ShortName,
            company.TaxNumber,
            company.LegalType,
            company.Size,
            company.PrimaryNaceCode,
            company.MainSector,
            company.Contact.City,
            company.Workforce.EmployeeCount,
            company.Workforce.WomenEmployeeCount,
            company.Workforce.YoungEmployeeCount,
            company.Workforce.YoungEmployeeMaxAge,
            company.Workforce.RAndDEmployeeCount,
            company.Workforce.DisabledEmployeeCount,
            company.Financials.AnnualRevenue,
            company.ProfileCompletionPercentage,
            company.IsActive,
            company.GroupId,
            groupName,
            company.ParentCompanyId,
            parentName,
            company.RelationshipType,
            company.IsHeadCompany,
            membership.CompanyRole,
            membership.IsDefault);

    /// <summary>
    /// Kiracıya yeni şirket kaydetme yetkisi.
    ///
    /// Bu, aktif şirketteki sıradan yönetim yetkisinden <b>ayrı bir kiracı işlemidir</b>:
    /// bir şirketin yöneticisi olmak, kiracıya yeni tüzel kişilik eklemeye yetmez.
    ///
    /// İzin verilenler:
    /// <list type="bullet">
    ///   <item>Kiracı yöneticisi (<see cref="UserRole.SuperAdmin"/>) — her zaman.</item>
    ///   <item>Aynı kiracıda en az bir <b>etkin</b> CompanyOwner üyeliği olan kullanıcı.</item>
    /// </list>
    ///
    /// Karar <b>veritabanındaki üyelikten</b> verilir; jetondaki eski şirket claim'ine
    /// bakılmaz. Üyelik sorgusu kiracı filtresine tabidir, bu yüzden kullanıcının başka
    /// kiracıdaki sahipliği hesaba katılmaz; okunabilirlik için ayrıca elle de denetlenir.
    ///
    /// Kiracıda hiç şirket yoksa ilk şirketi yalnızca kiracı yöneticisi açabilir:
    /// aksi hâlde kural kendi kendini besleyemezdi, çünkü sahiplik ancak var olan bir
    /// şirket üzerinden doğar.
    /// </summary>
    private async Task EnsureCanCreateCompanyAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (currentUser.Role == UserRole.SuperAdmin)
        {
            return;
        }

        var tenantCompanyCount = await companies.CountAsync(tenantId, cancellationToken);
        if (tenantCompanyCount == 0)
        {
            throw new ForbiddenException(
                "Çalışma alanındaki ilk şirketi yalnızca çalışma alanı yöneticisi ekleyebilir.");
        }

        var ownsAnyCompany = (await memberships.ListForUserAsync(userId, cancellationToken))
            .Any(m => m.TenantId == tenantId
                      && m.GrantsAccess
                      && m.CompanyRole == CompanyRole.CompanyOwner);

        if (!ownsAnyCompany)
        {
            throw new ForbiddenException(
                "Yeni şirket eklemek için çalışma alanı yöneticisi olmanız ya da " +
                "en az bir şirkette şirket sahibi olmanız gerekir.");
        }
    }

    private async Task EnsureCompanyQuotaAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetAsync(tenantId, cancellationToken)
                     ?? throw new NotFoundException("Kiracı", tenantId);

        var count = await companies.CountAsync(tenantId, cancellationToken);
        if (count >= tenant.MaxCompanies)
        {
            throw new ValidationException(
                "Plan",
                $"'{tenant.Plan}' paketi en fazla {tenant.MaxCompanies} firma içerir. Paket yükseltmesi gerekiyor.");
        }
    }
}
