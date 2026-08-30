using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Identity;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovAI.Persistence.Seed;

/// <summary>
/// Geliştirme ve demo ortamı için başlangıç verisi.
/// Üretimde çalıştırılmaz; <c>Seed:Enabled</c> ayarı ile kontrol edilir.
/// </summary>
public sealed class DatabaseSeeder(
    GovAiDbContext context,
    IPasswordHasher passwordHasher,
    IDateTimeProvider clock,
    ILogger<DatabaseSeeder> logger)
{
    /// <summary>
    /// Worker'ın kullandığı sınırlı kimliği oluşturur. Kimlik bilgileri <b>koda ve depoya
    /// yazılmaz</b>; dağıtım yapılandırmasından (ortam değişkeni) gelir. Verilmezse hesap
    /// oluşturulmaz ve worker çalışmaz — sessizce ayrıcalıklı bir hesaba düşmez.
    ///
    /// Bilinen sınır: hesap seed edilen kiracıya bağlıdır. Çok kiracılı üretimde her
    /// kiracı için ayrı ingest kimliği veya kiracıdan bağımsız servis kimliği gerekir.
    /// </summary>
    /// <summary>
    /// Resmî kaynak kataloğunu yükler (Faz 2).
    ///
    /// Fikir birliği: kaynaklar <b>veritabanında</b> yaşar. Bu metot yalnızca eksik
    /// olanları ekler; var olanların yapılandırmasına dokunmaz — platform yöneticisinin
    /// panelden yaptığı düzeltmeler her açılışta geri alınmamalıdır.
    ///
    /// Hepsi doğrulanmamış ve kapalı olarak eklenir; taranabilmesi için seçicisinin
    /// canlı olarak çalıştığının kanıtlanması gerekir.
    /// </summary>
    public async Task<int> SeedOfficialSourceCatalogAsync(CancellationToken cancellationToken = default)
    {
        var mevcutlar = await context.Sources.ToListAsync(cancellationToken);

        var eklenen = 0;
        var yukseltilen = 0;

        // Bir satır yalnızca BİR tanıma eşlenir. Aynı kurum birden çok kaynak
        // tanımlayabilir (Resmî Gazete hem mevzuat hem ihale ilanı yayımlar); alan adı
        // eşleşmesi tek başına bırakılırsa ikinci tanım birincinin satırını ele geçirir
        // ve yeni kaynak hiç açılmaz.
        var eslesenler = new HashSet<Guid>();

        foreach (var tanim in OfficialSourceCatalog.All)
        {
            // Önce ada, sonra resmî alan adına bakılır: Faz 0 seed'inden gelen kayıtlar
            // farklı adlarla ("KOSGEB Destek Çağrıları") aynı kurumu gösteriyor olabilir.
            var mevcut =
                mevcutlar.FirstOrDefault(s => s.Name == tanim.Name && eslesenler.Add(s.Id))
                ?? mevcutlar.FirstOrDefault(s =>
                       SameHost(s.BaseUrl, tanim.BaseUrl)
                       // Alan adı eşleşmesi, adı katalogda geçen bir satırı ele geçiremez.
                       && !OfficialSourceCatalog.All.Any(d => d.Name == s.Name)
                       && eslesenler.Add(s.Id));

            if (mevcut is null)
            {
                context.Sources.Add(OfficialSourceCatalog.ToSource(tanim));
                eklenen++;
                continue;
            }

            // Doğrulanmış kaynağa DOKUNULMAZ: platform yöneticisinin panelden yaptığı
            // düzeltmeler her açılışta geri alınmamalıdır.
            if (mevcut.ConfigurationVerified)
            {
                continue;
            }

            // Künyesi olmayan eski kayıt katalog tanımıyla yükseltilir; doğrulanmamış
            // olduğu için kaybedilecek bir operatör emeği yoktur.
            mevcut.Describe(
                tanim.Category,
                new Domain.Sources.SourceProfile(
                    tanim.Authority, tanim.Jurisdiction, tanim.OfficialDomain, tanim.Language));

            mevcut.PlanCrawl(new Domain.Sources.SourceCrawlPlan(
                tanim.StartUrl, tanim.ListSelector, tanim.ContentSelector, tanim.UrlPattern,
                tanim.MaxPages, tanim.AllowedDomains ?? tanim.OfficialDomain, tanim.DocumentTypes));

            // Arşiv şablonu da güncellenmeli: "bugün yayın yok" ayrımı buna dayanır.
            if (tanim.ArchiveUrlTemplate is { Length: > 0 } arsiv)
            {
                mevcut.Configure(
                    tanim.CronExpression,
                    "{\"archiveUrlTemplate\":\"" + arsiv + "\"}");
            }

            // Doğrulanana kadar taranmaz.
            mevcut.Disable();
            yukseltilen++;
        }

        if (eklenen == 0 && yukseltilen == 0)
        {
            logger.LogInformation("Resmî kaynak kataloğu güncel.");
            return 0;
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Resmî kaynak kataloğu: {Eklenen} eklendi, {Yukseltilen} yükseltildi (doğrulanmamış, kapalı).",
            eklenen, yukseltilen);

        return eklenen + yukseltilen;
    }

    /// <summary>İki adresin aynı kuruma ait olup olmadığı (alan adı karşılaştırması).</summary>
    private static bool SameHost(string left, string right)
    {
        if (!Uri.TryCreate(left, UriKind.Absolute, out var a) ||
            !Uri.TryCreate(right, UriKind.Absolute, out var b))
        {
            return false;
        }

        static string Root(string host) =>
            host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

        return string.Equals(Root(a.Host), Root(b.Host), StringComparison.OrdinalIgnoreCase);
    }

    public async Task SeedWorkerIdentityAsync(
        string workerEmail,
        string workerPassword,
        CancellationToken cancellationToken = default)
    {
        var tenant = await context.Tenants.FirstOrDefaultAsync(cancellationToken);
        if (tenant is null)
        {
            logger.LogWarning("Kiracı bulunamadığı için worker kimliği oluşturulamadı.");
            return;
        }

        var normalized = workerEmail.Trim().ToLowerInvariant();
        var existing = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation("Worker kimliği zaten mevcut: {Email}", normalized);
            return;
        }

        var worker = new AppUser(tenant.Id, normalized, "GOVAI Veri Toplama Servisi", UserRole.SystemIngest);
        worker.SetPasswordHash(passwordHasher.Hash(workerPassword));

        await context.Users.AddAsync(worker, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Worker kimliği oluşturuldu: {Email} (SystemIngest)", normalized);
    }

    public async Task SeedAsync(string adminEmail, string adminPassword, CancellationToken cancellationToken = default)
    {
        if (await context.Tenants.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Başlangıç verisi zaten mevcut, atlanıyor.");
            return;
        }

        var tenant = new Tenant("TalentHub Demo", "talenthub-demo");
        tenant.SetPlan("Professional", maxCompanies: 25);
        await context.Tenants.AddAsync(tenant, cancellationToken);

        var admin = new AppUser(tenant.Id, adminEmail, "Sistem Yöneticisi", UserRole.SuperAdmin);
        admin.SetPasswordHash(passwordHasher.Hash(adminPassword));
        await context.Users.AddAsync(admin, cancellationToken);

        // Kiracı ve kullanıcı ÖNCE yazılır. companies.tenant_id üzerindeki yabancı anahtar
        // Faz 1 ile eklendi; EF bu ilişkiyi modellemediği için tek SaveChanges'te firmayı
        // kiracıdan önce yazabiliyor ve kurulum yabancı anahtar ihlaliyle düşüyordu.
        await context.SaveChangesAsync(cancellationToken);

        var company = BuildDemoCompany(tenant.Id);
        await context.Companies.AddAsync(company, cancellationToken);

        var sources = BuildSources();
        await context.Sources.AddRangeAsync(sources, cancellationToken);

        var opportunities = BuildDemoOpportunities(sources[0].Id, sources[2].Id, clock.UtcNow);
        await context.Opportunities.AddRangeAsync(opportunities, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        // Şirket erişimi Faz 1'den beri yalnızca üyelikten okunur. Üyelik açılmazsa
        // yeni kurulan sistemde yönetici panelde hiçbir şirket göremez.
        await context.UserCompanies.AddAsync(
            new UserCompany(tenant.Id, admin.Id, company.Id, CompanyRole.CompanyOwner, isDefault: true),
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Başlangıç verisi yüklendi. Kiracı={Tenant} Firma={Company} Kaynak={SourceCount} Fırsat={OpportunityCount}",
            tenant.Name, company.LegalName, sources.Count, opportunities.Count);
    }

    private static Company BuildDemoCompany(Guid tenantId)
    {
        var company = new Company(tenantId, "Örnek Üretim ve Teknoloji A.Ş.", "1234567890", LegalType.JointStockCompany);
        company.UpdateIdentity("Örnek Üretim ve Teknoloji A.Ş.", LegalType.JointStockCompany, new DateOnly(2016, 3, 14));

        company.UpdateWorkforce(new Workforce(
            employeeCount: 42,
            womenEmployeeCount: 16,
            youngEmployeeCount: 11,
            rAndDEmployeeCount: 6,
            disabledEmployeeCount: 1));

        company.UpdateFinancials(new Financials(
            annualRevenue: 84_500_000m,
            balanceSize: 61_200_000m,
            equity: 23_400_000m,
            exportRevenue: 19_800_000m,
            currency: "TRY",
            fiscalYear: 2025));

        company.UpdateFlags(exportFlag: true, technologyFlag: true, previousSuccessfulApplications: 2);

        company.ReplaceNaceCodes(
        [
            new CompanyNaceCode("2562", isPrimary: true, "Metallerin makinede işlenmesi"),
            new CompanyNaceCode("6201", isPrimary: false, "Bilgisayar programlama faaliyetleri")
        ]);

        company.ReplaceLocations(
        [
            new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: true, isInTechnopark: true),
            new CompanyLocation("Adana", "Seyhan", "TR62", isHeadquarters: false)
        ]);

        company.ReplaceCertificates(
        [
            new CompanyCertificate("ISO9001", "ISO 9001 Kalite Yönetim Sistemi", new DateOnly(2023, 5, 1), new DateOnly(2027, 5, 1)),
            new CompanyCertificate("CE", "CE Uygunluk Beyanı", new DateOnly(2022, 9, 12), null)
        ]);

        company.ReplaceInvestments(
        [
            new CompanyInvestment(
                "Kapasite artırımı ve otomasyon yatırımı",
                SupportCategory.InvestmentIncentive,
                plannedBudget: 12_000_000m,
                new DateOnly(2026, 9, 1),
                new DateOnly(2027, 6, 30))
        ]);

        return company;
    }

    private static List<Source> BuildSources()
    {
        var resmiGazete = new Source("Resmî Gazete", SourceType.OfficialGazette, "https://www.resmigazete.gov.tr", "0 7 * * *");
        var sanayiBakanligi = new Source("Sanayi ve Teknoloji Bakanlığı Duyuruları", SourceType.Ministry, "https://www.sanayi.gov.tr", "0 8 * * *");
        var cukurovaKalkinma = new Source("Çukurova Kalkınma Ajansı", SourceType.DevelopmentAgency, "https://www.cka.org.tr", "0 9 * * 1-5");
        var kosgeb = new Source("KOSGEB Destek Çağrıları", SourceType.KosgebOrSimilar, "https://www.kosgeb.gov.tr", "0 9 * * *");
        var ekap = new Source("EKAP İhale İlanları", SourceType.TenderPortal, "https://ekap.kik.gov.tr", "0 */6 * * *");

        return [resmiGazete, sanayiBakanligi, cukurovaKalkinma, kosgeb, ekap];
    }

    /// <summary>
    /// Demo çağrılar. Kural setleri gerçek mevzuatın birebir kopyası değildir; motorun
    /// nasıl çalıştığını göstermek için basitleştirilmiştir.
    /// </summary>
    private static List<Opportunity> BuildDemoOpportunities(Guid gazetteSourceId, Guid agencySourceId, DateTimeOffset now)
    {
        var rndCall = new Opportunity(
            agencySourceId,
            SourceType.DevelopmentAgency,
            SupportCategory.RndSupport,
            "2026 Yılı Ar-Ge ve Dijitalleşme Mali Destek Programı",
            "Çukurova Kalkınma Ajansı",
            now.AddDays(-12));

        rndCall.Describe(
            "TR62 bölgesinde faaliyet gösteren, en az 10 çalışanı ve Ar-Ge personeli bulunan işletmelerin dijitalleşme ve Ar-Ge projelerine hibe desteği.",
            "https://www.cka.org.tr/duyurular/2026-arge-dijitallesme",
            null);

        rndCall.SetSchedule(now.AddDays(-12), now.AddDays(38));
        rndCall.SetBudget(new BudgetRange(500_000m, 6_000_000m, "TRY", 0.60m));

        rndCall.ReplaceRules(
        [
            new OpportunityRule("Company.Nuts2Codes", RuleOperator.ContainsAny, "TR62", RuleDimension.Region, RuleSeverity.Blocking,
                "Firma TR62 (Adana–Mersin) bölgesinde faaliyet göstermelidir.", "Programdan yalnızca TR62 Düzey 2 Bölgesi'nde kayıtlı işletmeler yararlanabilir."),
            new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "10", RuleDimension.Employment, RuleSeverity.Blocking,
                "Asgari 10 çalışan şartı.", "Başvuru sahibinin son bordroya göre en az 10 çalışanı bulunmalıdır."),
            new OpportunityRule("Workforce.RAndDEmployeeCount", RuleOperator.GreaterThanOrEqual, "3", RuleDimension.Employment, RuleSeverity.Major,
                "En az 3 Ar-Ge personeli.", "Proje ekibinde en az 3 Ar-Ge personeli görevlendirilmelidir."),
            new OpportunityRule("Company.NaceCodes", RuleOperator.NaceMatch, "25,26,27,62", RuleDimension.Sector, RuleSeverity.Blocking,
                "İmalat ve yazılım NACE kodları hedeflenmektedir.", "NACE 25, 26, 27 ve 62 ana grupları uygundur."),
            new OpportunityRule("Financials.AnnualRevenue", RuleOperator.LessThanOrEqual, "250000000", RuleDimension.Financial, RuleSeverity.Blocking,
                "Yıllık ciro 250 milyon TL'yi aşmamalıdır (KOBİ şartı).", "KOBİ tanımına uyan işletmeler başvurabilir."),
            new OpportunityRule("Financials.Equity", RuleOperator.GreaterThan, "0", RuleDimension.Financial, RuleSeverity.Major,
                "Özkaynak pozitif olmalıdır.", "Mali yeterlilik değerlendirmesinde negatif özkaynak elenme sebebidir."),
            new OpportunityRule("Company.Certificates", RuleOperator.ContainsAll, "ISO9001", RuleDimension.Documentation, RuleSeverity.Minor,
                "ISO 9001 belgesi puanlamada dikkate alınır.", "Kalite yönetim sistemi belgesi olan başvurular önceliklendirilir."),
            new OpportunityRule("Company.IsInTechnopark", RuleOperator.IsTrue, "true", RuleDimension.TechnicalQualification, RuleSeverity.Bonus,
                "Teknoparkta yerleşik olmak avantaj sağlar.", "Teknoloji Geliştirme Bölgesi'nde yerleşik başvurulara ilave puan verilir.")
        ], extractionConfidence: 0.86m);

        rndCall.ReplaceDocumentChecklist(
        [
            new DocumentRequirement("ISO9001", "ISO 9001 Kalite Belgesi", isMandatory: false, "TÜRKAK akrediteli kuruluş"),
            new DocumentRequirement("FAALIYET_BELGESI", "Ticaret Odası Faaliyet Belgesi", isMandatory: true, "Ticaret ve Sanayi Odası"),
            new DocumentRequirement("SGK_BORCU_YOKTUR", "SGK Borcu Yoktur Yazısı", isMandatory: true, "SGK"),
            new DocumentRequirement("VERGI_BORCU_YOKTUR", "Vergi Borcu Yoktur Yazısı", isMandatory: true, "Gelir İdaresi Başkanlığı"),
            new DocumentRequirement("IMZA_SIRKULERI", "İmza Sirküleri", isMandatory: true, "Noter")
        ]);

        var employmentIncentive = new Opportunity(
            gazetteSourceId,
            SourceType.OfficialGazette,
            SupportCategory.EmploymentIncentive,
            "Kadın ve Genç İstihdamı Prim Desteği",
            "Çalışma ve Sosyal Güvenlik Bakanlığı",
            now.AddDays(-40));

        employmentIncentive.Describe(
            "Kadın ve 29 yaş altı çalışan istihdam eden işletmelere sigorta primi işveren payı desteği.",
            "https://www.resmigazete.gov.tr/eskiler/2026/istihdam-prim-destegi",
            null);

        employmentIncentive.SetSchedule(now.AddDays(-40), now.AddDays(9));
        employmentIncentive.SetBudget(new BudgetRange(null, null, "TRY", null));

        employmentIncentive.ReplaceRules(
        [
            new OpportunityRule("Workforce.WomenEmployeeRate", RuleOperator.GreaterThanOrEqual, "0.30", RuleDimension.Employment, RuleSeverity.Blocking,
                "Kadın çalışan oranı en az %30 olmalıdır.", "Destekten yararlanmak için kadın çalışan oranı %30'un altında olmamalıdır."),
            new OpportunityRule("Workforce.YoungEmployeeCount", RuleOperator.GreaterThanOrEqual, "5", RuleDimension.Employment, RuleSeverity.Major,
                "En az 5 genç (29 yaş altı) çalışan.", "29 yaşını doldurmamış en az 5 sigortalı çalıştırılmalıdır."),
            new OpportunityRule("Workforce.EmployeeCount", RuleOperator.GreaterThanOrEqual, "5", RuleDimension.Employment, RuleSeverity.Blocking,
                "Asgari 5 çalışan şartı.", "İşyerinde en az 5 sigortalı bulunmalıdır."),
            new OpportunityRule("Company.LegalType", RuleOperator.NotEquals, "PublicEntity", RuleDimension.Sector, RuleSeverity.Blocking,
                "Kamu kurumları yararlanamaz.", "Kamu kurum ve kuruluşları destek kapsamı dışındadır.")
        ], extractionConfidence: 0.92m);

        employmentIncentive.ReplaceDocumentChecklist(
        [
            new DocumentRequirement("SGK_ISYERI_BILDIRGESI", "SGK İşyeri Bildirgesi", isMandatory: true, "SGK"),
            new DocumentRequirement("SGK_BORCU_YOKTUR", "SGK Borcu Yoktur Yazısı", isMandatory: true, "SGK")
        ]);

        employmentIncentive.MarkReviewed();

        var exportSupport = new Opportunity(
            gazetteSourceId,
            SourceType.Ministry,
            SupportCategory.ExportSupport,
            "Pazara Giriş Belgeleri Desteği",
            "Ticaret Bakanlığı",
            now.AddDays(-5));

        exportSupport.Describe(
            "İhracatçı firmaların uluslararası pazara giriş belgeleri ve test/analiz giderlerine destek.",
            "https://www.ticaret.gov.tr/destekler/pazara-giris-belgeleri",
            null);

        exportSupport.SetSchedule(now.AddDays(-5), now.AddDays(120));
        exportSupport.SetBudget(new BudgetRange(50_000m, 1_500_000m, "TRY", 0.50m));

        exportSupport.ReplaceRules(
        [
            new OpportunityRule("Company.ExportFlag", RuleOperator.IsTrue, "true", RuleDimension.Sector, RuleSeverity.Blocking,
                "Firma ihracat yapıyor olmalıdır.", "Destekten yalnızca ihracatçı firmalar yararlanabilir."),
            new OpportunityRule("Financials.ExportRatio", RuleOperator.GreaterThanOrEqual, "0.10", RuleDimension.Financial, RuleSeverity.Major,
                "İhracatın ciroya oranı en az %10 olmalıdır.", "Değerlendirmede ihracat yoğunluğu dikkate alınır."),
            new OpportunityRule("Company.Certificates", RuleOperator.ContainsAny, "CE,ISO14001", RuleDimension.Documentation, RuleSeverity.Minor,
                "CE veya ISO 14001 belgesi puanlamada dikkate alınır.", "Pazara giriş belgeleri kapsamında değerlendirilir.")
        ], extractionConfidence: 0.78m);

        exportSupport.ReplaceDocumentChecklist(
        [
            new DocumentRequirement("IHRACATCI_BIRLIGI_UYELIK", "İhracatçı Birlikleri Üyelik Belgesi", isMandatory: true, "TİM"),
            new DocumentRequirement("FAALIYET_BELGESI", "Ticaret Odası Faaliyet Belgesi", isMandatory: true, "Ticaret ve Sanayi Odası")
        ]);

        return [rndCall, employmentIncentive, exportSupport];
    }
}
