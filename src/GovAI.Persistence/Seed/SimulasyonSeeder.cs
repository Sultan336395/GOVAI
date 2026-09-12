using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Identity;
using GovAI.Domain.Notifications;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Tenders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovAI.Persistence.Seed;

/// <summary>
/// Tanıtım simülasyonu: mevcut firma verisini siler ve GOVAI'nin tüm yeteneklerini
/// tek ekranda gösteren bir <b>grup şirketi</b> kurar.
///
/// <para>
/// <b>NEDEN AYRI BİR SINIF:</b> <see cref="DatabaseSeeder"/> yalnızca boş veritabanında
/// çalışır ve hiçbir şeyi silmez — kurulum verisidir. Bu sınıf ise bilerek YIKICIDIR.
/// İkisini aynı yere koymak, "başlangıç verisi yükle" niyetiyle çalıştırılan bir
/// bayrağın canlı firma verisini silmesi demek olurdu.
/// </para>
///
/// <para>
/// <b>İKİ KAT KORUMA:</b> Çalışması için hem yapılandırma bayrağı açık olmalı hem de
/// grup henüz kurulmamış olmalı. Bayrak yanlışlıkla açık bırakılırsa ikinci açılışta
/// hiçbir şey silinmez; kurulum bir kez olur. Bayrağı açık unutmak veri kaybettirmez.
/// </para>
///
/// <para>
/// <b>NE SİLİNİR:</b> Yalnızca firmaya bağlı veriler — firmalar, gruplar, üyelikler,
/// değerlendirmeler, bildirimler, ihale takipleri, haftalık raporlar, senaryolar.
/// Fırsat kataloğu, kurallar, kaynaklar, belgeler, kullanıcı hesapları ve denetim
/// kaydı <b>korunur</b>: onlar firma verisi değildir ve yeniden toplanmaları günler alır.
/// </para>
/// </summary>
public sealed class SimulasyonSeeder(
    GovAiDbContext context,
    ILogger<SimulasyonSeeder> logger)
{
    /// <summary>Grubun adı; aynı zamanda "bu simülasyon zaten kuruldu" işaretidir.</summary>
    public const string GrupAdi = "Atlas Grup";

    public async Task<SimulasyonSonucu> KurAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await context.Tenants.FirstOrDefaultAsync(cancellationToken);

        if (tenant is null)
        {
            logger.LogWarning("Simülasyon kurulamadı: kiracı yok. Önce başlangıç verisi yüklenmeli.");
            return SimulasyonSonucu.KiraciYok;
        }

        var zatenVar = await context.CompanyGroups
            .IgnoreQueryFilters()
            .AnyAsync(g => g.Name == GrupAdi, cancellationToken);

        if (zatenVar)
        {
            logger.LogInformation("Simülasyon zaten kurulu ({Grup}); hiçbir şey silinmedi.", GrupAdi);
            return SimulasyonSonucu.ZatenKurulu;
        }

        var silinen = await FirmaVerisiniSilAsync(cancellationToken);

        logger.LogWarning(
            "Simülasyon için firma verisi silindi. Firma={Firma} Değerlendirme={Degerlendirme} Bildirim={Bildirim}",
            silinen.Firma, silinen.Degerlendirme, silinen.Bildirim);

        var kurulan = await GrubuKurAsync(tenant.Id, cancellationToken);

        logger.LogInformation(
            "Simülasyon kuruldu. Grup={Grup} Firma={Firma} Alıcı={Alici} İhaleTakibi={Takip}",
            GrupAdi, kurulan.Firma, kurulan.Alici, kurulan.Takip);

        return SimulasyonSonucu.Kuruldu;
    }

    // ── Silme ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Firmaya bağlı tüm veriyi siler.
    ///
    /// <para>
    /// Sorgu süzgeçleri <b>bilerek devre dışı</b>: yumuşak silinmiş (soft-deleted)
    /// kayıtlar da gitmelidir. Aksi halde eski firmanın satırları veritabanında kalır,
    /// vergi numarası tekilliği yeni kaydı reddeder ve "sildim" dediğim veri ekranda
    /// olmasa da diskte durur.
    /// </para>
    /// </summary>
    private async Task<SilmeOzeti> FirmaVerisiniSilAsync(CancellationToken cancellationToken)
    {
        // Varsayılan davranış pasifleştirmedir; burada GERÇEKTEN silinmesi gerekir.
        // Pasifleştirilselerdi eski demo firmaları satır olarak kalır, ham veritabanı
        // sorgularında görünmeye devam eder ve vergi numarası tekilliğini işgal ederdi.
        context.KaliciSilmeyiEtkinlestir();

        // Sıra FK bağımlılığına göre: çocuklar önce. Aynı işlemde silindikleri için
        // veritabanı ara durumu görmez.
        var takipOlaylari = await context.TenderPursuitEvents.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.TenderPursuitEvents.RemoveRange(takipOlaylari);

        var takipler = await context.TenderPursuits.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.TenderPursuits.RemoveRange(takipler);

        var sorular = await context.ReportInquiries.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.ReportInquiries.RemoveRange(sorular);

        var raporlar = await context.WeeklyReports.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.WeeklyReports.RemoveRange(raporlar);

        var senaryolar = await context.ScenarioSimulations.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.ScenarioSimulations.RemoveRange(senaryolar);

        var bildirimler = await context.Notifications.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.Notifications.RemoveRange(bildirimler);

        var alicilar = await context.NotificationRecipients.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.NotificationRecipients.RemoveRange(alicilar);

        // Değerlendirmeler boyutlarıyla birlikte; boyutlar sahipli (owned) olduğu için
        // ana kayıtla beraber gider.
        var degerlendirmeler = await context.Assessments.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.Assessments.RemoveRange(degerlendirmeler);

        var uzmanGorusleri = await context.ExpertVerdicts.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.ExpertVerdicts.RemoveRange(uzmanGorusleri);

        var davetler = await context.CompanyInvitations.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.CompanyInvitations.RemoveRange(davetler);

        var dogrulamalar = await context.CompanyVerificationRequests.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.CompanyVerificationRequests.RemoveRange(dogrulamalar);

        var uyelikler = await context.UserCompanies.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.UserCompanies.RemoveRange(uyelikler);

        // ERP bağlantıları firmaya bağlıdır; firma gidince kimlik de gitmeli.
        var erpKullanimlari = await context.ErpAssertionUses.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.ErpAssertionUses.RemoveRange(erpKullanimlari);

        var erpAnahtarlari = await context.ErpSigningKeys.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.ErpSigningKeys.RemoveRange(erpAnahtarlari);

        var erpKimlikleri = await context.ErpServiceIdentities.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.ErpServiceIdentities.RemoveRange(erpKimlikleri);

        var erpBaglantilari = await context.ErpConnections.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.ErpConnections.RemoveRange(erpBaglantilari);

        var firmalar = await context.Companies.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.Companies.RemoveRange(firmalar);

        var gruplar = await context.CompanyGroups.IgnoreQueryFilters().ToListAsync(cancellationToken);
        context.CompanyGroups.RemoveRange(gruplar);

        await context.SaveChangesAsync(cancellationToken);

        return new SilmeOzeti(firmalar.Count, degerlendirmeler.Count, bildirimler.Count);
    }

    // ── Kurulum ─────────────────────────────────────────────────────────────

    private async Task<KurulumOzeti> GrubuKurAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var bugun = DateOnly.FromDateTime(DateTime.UtcNow);
        var simdi = DateTimeOffset.UtcNow;

        var grup = new CompanyGroup(
            tenantId,
            GrupAdi,
            "Mersin merkezli, üretim–yazılım–lojistik–enerji alanlarında faaliyet gösteren örnek şirketler topluluğu.");

        await context.CompanyGroups.AddAsync(grup, cancellationToken);

        var holding = Holding(tenantId, bugun);
        var metal = Metal(tenantId, bugun);
        var yazilim = Yazilim(tenantId, bugun);
        var lojistik = Lojistik(tenantId, bugun);
        var enerji = Enerji(tenantId, bugun);

        holding.SetGroupAndParent(grup.Id, null, CompanyRelationshipType.HeadCompany, isHeadCompany: true);
        metal.SetGroupAndParent(grup.Id, holding.Id, CompanyRelationshipType.Subsidiary, isHeadCompany: false);
        yazilim.SetGroupAndParent(grup.Id, holding.Id, CompanyRelationshipType.Subsidiary, isHeadCompany: false);
        lojistik.SetGroupAndParent(grup.Id, holding.Id, CompanyRelationshipType.Subsidiary, isHeadCompany: false);
        enerji.SetGroupAndParent(grup.Id, holding.Id, CompanyRelationshipType.Affiliate, isHeadCompany: false);

        Company[] firmalar = [holding, metal, yazilim, lojistik, enerji];

        await context.Companies.AddRangeAsync(firmalar, cancellationToken);

        // Yönetici kullanıcılar gruba bağlanır; hangi hesabın var olduğu ortama göre
        // değişir, bu yüzden platform rolü olmayan kullanıcıların hepsi eklenir.
        var kullanicilar = await context.Users.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var uyelikler = new List<UserCompany>();

        foreach (var kullanici in kullanicilar)
        {
            foreach (var firma in firmalar)
            {
                uyelikler.Add(new UserCompany(
                    tenantId,
                    kullanici.Id,
                    firma.Id,
                    CompanyRole.CompanyOwner,
                    isDefault: firma.Id == holding.Id));
            }
        }

        await context.UserCompanies.AddRangeAsync(uyelikler, cancellationToken);

        var alicilar = Alicilar(tenantId, holding, metal, yazilim, lojistik);
        await context.NotificationRecipients.AddRangeAsync(alicilar, cancellationToken);

        var takipler = await IhaleTakipleriAsync(tenantId, metal, lojistik, simdi, cancellationToken);
        await context.TenderPursuits.AddRangeAsync(takipler, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        return new KurulumOzeti(firmalar.Length, alicilar.Count, takipler.Count);
    }

    // ── Firmalar ────────────────────────────────────────────────────────────
    //
    // Beş firma bilerek FARKLI durumlar gösterir; tek bir "ideal firma" motorun
    // ayrımlarını görünmez kılardı:
    //
    //   Holding   → Ar-Ge personeli SIFIR BEYAN EDİLMİŞ ("yok" diyor)
    //   Metal     → tüm kırılım beyan edilmiş, sertifikalı, ihracatçı
    //   Yazılım   → teknopark + yoğun Ar-Ge, engelli çalışan sıfır beyanı
    //   Lojistik  → kırılım HİÇ BEYAN EDİLMEMİŞ (bilinmiyor), sertifikasız
    //   Enerji    → neredeyse boş profil; tamamlanma göstergesi düşük çıkar
    //
    // "Sıfır beyanı" ile "beyan edilmedi" ayrımı ürünün üçüncü iddiasıdır
    // (eksik veri firmayı elemez); iki firma bu ayrımın iki ucunu temsil eder.

    private static Company Holding(Guid tenantId, DateOnly bugun)
    {
        var firma = new Company(tenantId, "Atlas Holding Anonim Şirketi", "1000000001", LegalType.JointStockCompany);
        firma.UpdateIdentity("Atlas Holding Anonim Şirketi", LegalType.JointStockCompany, new DateOnly(2004, 6, 18));

        firma.UpdateRegistry(new CompanyRegistry("Atlas Holding", "Mersin Kurumlar", "0100000000000001", "12345-6"));
        firma.UpdateContact(new CompanyContact(
            "https://atlasgrup.example.com", "+90 324 000 00 01", "iletisim@atlasgrup.example.com",
            "Türkiye", "Mersin", "Akdeniz Mah. Atatürk Cad. No:1 Yenişehir/Mersin"));

        firma.UpdateSectors("Holding ve yönetim danışmanlığı", null, null);

        // Ar-Ge personeli SIFIR olarak BEYAN EDİLMİŞTİR: holdingin kendi bünyesinde
        // Ar-Ge yapılmıyor, iştiraklerde yapılıyor. Bu bir cevaptır, eksik veri değil.
        firma.UpdateWorkforce(new Workforce(
            employeeCount: 35,
            womenEmployeeCount: 14,
            youngEmployeeCount: 6,
            rAndDEmployeeCount: 0,
            disabledEmployeeCount: 1,
            youngEmployeeMaxAge: 29));

        firma.UpdateFinancials(new Financials(
            annualRevenue: 128_000_000m, balanceSize: 420_000_000m,
            equity: 195_000_000m, exportRevenue: 0m, currency: "TRY", fiscalYear: 2025));

        firma.UpdateFlags(exportFlag: false, technologyFlag: false, previousSuccessfulApplications: 3);

        firma.ReplaceNaceCodes([new CompanyNaceCode("7010", isPrimary: true, "Holding şirketlerinin faaliyetleri")]);

        firma.ReplaceLocations(
        [
            new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: true, isInTechnopark: false)
        ]);

        // Biri yakında doluyor: son başvuru / eksik belge bildirimlerinin ekranda
        // görünmesi için bilerek yaklaşan bir tarih verilir.
        firma.ReplaceCertificates(
        [
            new CompanyCertificate("ISO9001", "ISO 9001 Kalite Yönetim Sistemi", bugun.AddYears(-2), bugun.AddYears(2)),
            new CompanyCertificate("ISO27001", "ISO 27001 Bilgi Güvenliği Yönetim Sistemi", bugun.AddYears(-3), bugun.AddDays(24))
        ]);

        MaliYillar(firma, 2023, [98_000_000m, 112_000_000m, 128_000_000m]);

        return firma;
    }

    private static Company Metal(Guid tenantId, DateOnly bugun)
    {
        var firma = new Company(tenantId, "Atlas Metal Sanayi Anonim Şirketi", "1000000002", LegalType.JointStockCompany);
        firma.UpdateIdentity("Atlas Metal Sanayi Anonim Şirketi", LegalType.JointStockCompany, new DateOnly(2009, 2, 9));

        firma.UpdateRegistry(new CompanyRegistry("Atlas Metal", "Tarsus", "0100000000000002", "22111-3"));
        firma.UpdateContact(new CompanyContact(
            "https://metal.atlasgrup.example.com", "+90 324 000 00 02", "info@metal.atlasgrup.example.com",
            "Türkiye", "Mersin", "Tarsus OSB 3. Cad. No:14 Tarsus/Mersin"));

        firma.UpdateSectors("Metal işleme ve makine imalatı", null, "[\"Almanya\",\"İtalya\",\"Irak\"]");

        // Tam beyan: motorun "sağlanıyor / sağlanmıyor" ayrımını net gösterir.
        firma.UpdateWorkforce(new Workforce(
            employeeCount: 128,
            womenEmployeeCount: 34,
            youngEmployeeCount: 41,
            rAndDEmployeeCount: 11,
            disabledEmployeeCount: 4,
            youngEmployeeMaxAge: 29));

        firma.UpdateFinancials(new Financials(
            annualRevenue: 264_000_000m, balanceSize: 198_000_000m,
            equity: 86_000_000m, exportRevenue: 96_000_000m, currency: "TRY", fiscalYear: 2025));

        firma.UpdateFlags(exportFlag: true, technologyFlag: true, previousSuccessfulApplications: 2);

        firma.ReplaceNaceCodes(
        [
            new CompanyNaceCode("2562", isPrimary: true, "Metallerin makinede işlenmesi"),
            new CompanyNaceCode("2825", isPrimary: false, "Soğutma ve havalandırma donanımı imalatı")
        ]);

        firma.ReplaceLocations(
        [
            new CompanyLocation("Mersin", "Tarsus", "TR62", isHeadquarters: true, isInTechnopark: false),
            new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: false, isInTechnopark: true),
            new CompanyLocation("Adana", "Seyhan", "TR62", isHeadquarters: false, isInTechnopark: false)
        ]);

        firma.ReplaceCertificates(
        [
            new CompanyCertificate("ISO9001", "ISO 9001 Kalite Yönetim Sistemi", bugun.AddYears(-3), bugun.AddYears(1)),
            new CompanyCertificate("ISO14001", "ISO 14001 Çevre Yönetim Sistemi", bugun.AddYears(-2), bugun.AddYears(3)),
            new CompanyCertificate("CE", "CE Uygunluk Beyanı", bugun.AddYears(-4), null)
        ]);

        firma.ReplaceInvestments(
        [
            new CompanyInvestment(
                "CNC tezgâh yenileme ve hat otomasyonu",
                SupportCategory.InvestmentIncentive,
                plannedBudget: 42_000_000m,
                bugun.AddMonths(2),
                bugun.AddMonths(16)),
            new CompanyInvestment(
                "Çatı GES kurulumu ile enerji verimliliği",
                SupportCategory.GreenTransformation,
                plannedBudget: 18_500_000m,
                bugun.AddMonths(1),
                bugun.AddMonths(9))
        ]);

        MaliYillar(firma, 2023, [176_000_000m, 213_000_000m, 264_000_000m]);

        return firma;
    }

    private static Company Yazilim(Guid tenantId, DateOnly bugun)
    {
        var firma = new Company(tenantId, "Atlas Yazılım ve Teknoloji Anonim Şirketi", "1000000003", LegalType.JointStockCompany);
        firma.UpdateIdentity("Atlas Yazılım ve Teknoloji Anonim Şirketi", LegalType.JointStockCompany, new DateOnly(2019, 11, 4));

        firma.UpdateRegistry(new CompanyRegistry("Atlas Yazılım", "Mersin Kurumlar", "0100000000000003", "33222-1"));
        firma.UpdateContact(new CompanyContact(
            "https://yazilim.atlasgrup.example.com", "+90 324 000 00 03", "info@yazilim.atlasgrup.example.com",
            "Türkiye", "Mersin", "Mersin Teknopark B Blok No:12 Yenişehir/Mersin"));

        firma.UpdateSectors("Kurumsal yazılım ve veri analitiği", null, "[\"Birleşik Krallık\",\"Katar\"]");

        // Engelli çalışan SIFIR BEYAN EDİLMİŞ: 24 kişilik işletmede yasal kota yok.
        firma.UpdateWorkforce(new Workforce(
            employeeCount: 24,
            womenEmployeeCount: 11,
            youngEmployeeCount: 18,
            rAndDEmployeeCount: 15,
            disabledEmployeeCount: 0,
            youngEmployeeMaxAge: 29));

        firma.UpdateFinancials(new Financials(
            annualRevenue: 21_500_000m, balanceSize: 14_200_000m,
            equity: 8_900_000m, exportRevenue: 6_400_000m, currency: "TRY", fiscalYear: 2025));

        firma.UpdateFlags(exportFlag: true, technologyFlag: true, previousSuccessfulApplications: 1);

        firma.ReplaceNaceCodes(
        [
            new CompanyNaceCode("6201", isPrimary: true, "Bilgisayar programlama faaliyetleri"),
            new CompanyNaceCode("6311", isPrimary: false, "Veri işleme ve barındırma faaliyetleri")
        ]);

        firma.ReplaceLocations(
        [
            new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: true, isInTechnopark: true)
        ]);

        firma.ReplaceCertificates(
        [
            new CompanyCertificate("ISO27001", "ISO 27001 Bilgi Güvenliği Yönetim Sistemi", bugun.AddYears(-1), bugun.AddYears(2))
        ]);

        firma.ReplaceInvestments(
        [
            new CompanyInvestment(
                "Yapay zekâ destekli üretim planlama ürünü geliştirme",
                SupportCategory.RndSupport,
                plannedBudget: 9_800_000m,
                bugun.AddMonths(1),
                bugun.AddMonths(18))
        ]);

        MaliYillar(firma, 2024, [13_100_000m, 21_500_000m]);

        return firma;
    }

    private static Company Lojistik(Guid tenantId, DateOnly bugun)
    {
        var firma = new Company(tenantId, "Atlas Lojistik Limited Şirketi", "1000000004", LegalType.LimitedCompany);
        firma.UpdateIdentity("Atlas Lojistik Limited Şirketi", LegalType.LimitedCompany, new DateOnly(2014, 7, 22));

        firma.UpdateRegistry(new CompanyRegistry("Atlas Lojistik", "Seyhan", "0100000000000004", "44333-2"));
        firma.UpdateContact(new CompanyContact(
            null, "+90 322 000 00 04", "info@lojistik.atlasgrup.example.com",
            "Türkiye", "Adana", "Yeşiloba Mah. 46. Sk. No:8 Seyhan/Adana"));

        firma.UpdateSectors("Karayolu yük taşımacılığı", null, null);

        // Kırılım HİÇ BEYAN EDİLMEMİŞ. Toplam çalışan biliniyor ama kimin kadın, genç,
        // Ar-Ge ya da engelli olduğu bilinmiyor — "yok" DEĞİL, "bilinmiyor". Bu firma,
        // kadın istihdamı arayan bir çağrıda ELENMEZ; karar "doğrulanamadı" kalır.
        firma.UpdateWorkforce(new Workforce(
            employeeCount: 46,
            womenEmployeeCount: null,
            youngEmployeeCount: null,
            rAndDEmployeeCount: null,
            disabledEmployeeCount: 2));

        firma.UpdateFinancials(new Financials(
            annualRevenue: 38_400_000m, balanceSize: 27_600_000m,
            equity: 9_100_000m, exportRevenue: 0m, currency: "TRY", fiscalYear: 2025));

        firma.UpdateFlags(exportFlag: false, technologyFlag: false, previousSuccessfulApplications: 0);

        firma.ReplaceNaceCodes([new CompanyNaceCode("4941", isPrimary: true, "Karayolu ile yük taşımacılığı")]);

        firma.ReplaceLocations(
        [
            new CompanyLocation("Adana", "Seyhan", "TR62", isHeadquarters: true, isInTechnopark: false)
        ]);

        // Sertifikası yok: eksik belge uyarılarının ve belge hazırlık boyutunun
        // düşük puanla nasıl göründüğünü gösterir.
        firma.ReplaceCertificates([]);

        MaliYillar(firma, 2025, [38_400_000m]);

        return firma;
    }

    private static Company Enerji(Guid tenantId, DateOnly bugun)
    {
        var firma = new Company(tenantId, "Atlas Enerji Anonim Şirketi", "1000000005", LegalType.JointStockCompany);
        firma.UpdateIdentity("Atlas Enerji Anonim Şirketi", LegalType.JointStockCompany, new DateOnly(2024, 1, 15));

        firma.UpdateContact(new CompanyContact(null, null, null, "Türkiye", "Karaman", null));

        // Profili bilerek eksik: profil tamamlanma göstergesinin ve "önce şu alanı
        // doldur" aksiyonlarının ekranda çalıştığı görülsün.
        firma.UpdateWorkforce(new Workforce(
            employeeCount: 8,
            womenEmployeeCount: null,
            youngEmployeeCount: null,
            rAndDEmployeeCount: null,
            disabledEmployeeCount: null));

        firma.UpdateFinancials(new Financials(
            annualRevenue: 2_100_000m, balanceSize: 0m,
            equity: 0m, exportRevenue: 0m, currency: "TRY", fiscalYear: 2025));

        firma.ReplaceNaceCodes([new CompanyNaceCode("3511", isPrimary: true, "Elektrik üretimi")]);

        firma.ReplaceLocations(
        [
            new CompanyLocation("Karaman", "Merkez", "TR71", isHeadquarters: true, isInTechnopark: false)
        ]);

        return firma;
    }

    /// <summary>
    /// Ardışık mali yılları yazar. Çok yıllı veri, ciro eğilimi ve büyüme oranı arayan
    /// kuralların değerlendirilebilmesi için gerekir; tek yıl bunları bilinmez bırakır.
    /// </summary>
    private static void MaliYillar(Company firma, int ilkYil, decimal[] cirolar)
    {
        for (var i = 0; i < cirolar.Length; i++)
        {
            var ciro = cirolar[i];

            firma.UpsertAnnualFinancials(
                fiscalYear: ilkYil + i,
                currency: "TRY",
                annualRevenue: ciro,
                annualIncome: ciro,
                annualExpense: Math.Round(ciro * 0.86m, 2),
                netProfitOrLoss: Math.Round(ciro * 0.14m, 2),
                balanceTotal: Math.Round(ciro * 0.78m, 2),
                dataSource: FinancialDataSource.SelfDeclared,
                // Son yıl henüz doğrulanmamıştır (mali müşavir onayı gelmedi), önceki
                // yıllar onaylıdır. Doğrulama durumunun ekranda ayrıştığı görülsün.
                verificationStatus: i == cirolar.Length - 1
                    ? FinancialVerificationStatus.Unverified
                    : FinancialVerificationStatus.Verified,
                updatedAt: DateTimeOffset.UtcNow);
        }
    }

    // ── Bildirim alıcıları ──────────────────────────────────────────────────

    /// <summary>
    /// Bildirim sorumluları. GOVAI hesabı olması gerekmez; e-posta yeterlidir.
    ///
    /// <para>
    /// Enerji şirketine bilerek alıcı TANIMLANMAZ: alıcısı olmayan firmada bildirimin
    /// kaybolmadığı, <c>RecipientMissing</c> durumuyla saklanıp ERP modülünde
    /// gösterildiği canlı olarak görülebilsin.
    /// </para>
    /// </summary>
    private static List<NotificationRecipient> Alicilar(
        Guid tenantId, Company holding, Company metal, Company yazilim, Company lojistik) =>
    [
        new(tenantId, holding.Id, "mali.isler@atlasgrup.example.com", "Selin Aydın", "Mali İşler Müdürü", RecipientSource.Manual),
        new(tenantId, holding.Id, "tesvik@atlasgrup.example.com", "Kerem Doğan", "Teşvik Uzmanı", RecipientSource.Manual),
        new(tenantId, metal.Id, "kalite@metal.atlasgrup.example.com", "Burak Şen", "Kalite Yöneticisi", RecipientSource.ErpPull, "ERP-1002"),
        new(tenantId, metal.Id, "muhasebe@metal.atlasgrup.example.com", "Elif Kaya", "Muhasebe Şefi", RecipientSource.ErpPull, "ERP-1003"),
        new(tenantId, yazilim.Id, "proje@yazilim.atlasgrup.example.com", "Deniz Yılmaz", "Proje Yöneticisi", RecipientSource.ErpPull, "ERP-1004"),
        new(tenantId, lojistik.Id, "idari@lojistik.atlasgrup.example.com", "Mert Arslan", "İdari İşler Sorumlusu", RecipientSource.Manual)
    ];

    // ── İhale takibi ────────────────────────────────────────────────────────

    /// <summary>
    /// İhale takip panosunu doldurur; her aşamadan en az bir kart olur ki pano boş
    /// görünmesin ve aşama geçişleri geçmişiyle birlikte incelenebilsin.
    ///
    /// <para>
    /// Kayıtlar gerçek ihale kayıtlarına bağlanır. Katalogda yeterli ihale yoksa
    /// bulunabilen kadarı kullanılır; uydurma fırsat üretilmez.
    /// </para>
    /// </summary>
    private async Task<List<TenderPursuit>> IhaleTakipleriAsync(
        Guid tenantId, Company metal, Company lojistik, DateTimeOffset simdi, CancellationToken cancellationToken)
    {
        var ihaleler = await context.Opportunities
            .Where(o => o.SupportCategory == SupportCategory.Tender && o.QuarantineReason == QuarantineReason.None)
            .OrderByDescending(o => o.PublishedAt)
            .Take(5)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var takipler = new List<TenderPursuit>();

        if (ihaleler.Count == 0)
        {
            logger.LogWarning("Katalogda uygun ihale bulunamadı; ihale takip panosu boş kuruldu.");
            return takipler;
        }

        const string kullanici = "simulasyon@govai.local";

        // 1. İnceleniyor — yeni düşmüş, henüz karar verilmemiş.
        takipler.Add(new TenderPursuit(
            tenantId, metal.Id, ihaleler[0], simdi.AddDays(-2), kullanici,
            "Teknik şartname inceleniyor; kapasite uygun görünüyor."));

        if (ihaleler.Count > 1)
        {
            // 2. Hazırlanıyor — dosya toplanıyor.
            var hazirlaniyor = new TenderPursuit(
                tenantId, metal.Id, ihaleler[1], simdi.AddDays(-9), kullanici,
                "Katılım kararı verildi.");
            hazirlaniyor.AssignOwner("Burak Şen");
            hazirlaniyor.ChangeStatus(
                TenderPursuitStatus.Hazirlaniyor, null,
                "İş deneyim belgesi ve bilanço hazırlanıyor.", simdi.AddDays(-6), kullanici);
            takipler.Add(hazirlaniyor);
        }

        if (ihaleler.Count > 2)
        {
            // 3. Teklif verildi — sonuç bekleniyor.
            var teklif = new TenderPursuit(
                tenantId, lojistik.Id, ihaleler[2], simdi.AddDays(-21), kullanici,
                "Araç filosu yeterli.");
            teklif.AssignOwner("Mert Arslan");
            teklif.ChangeStatus(TenderPursuitStatus.Hazirlaniyor, null, "Evrak tamamlandı.", simdi.AddDays(-16), kullanici);
            teklif.ChangeStatus(TenderPursuitStatus.TeklifVerildi, null, "Teklif EKAP üzerinden sunuldu.", simdi.AddDays(-11), kullanici);
            takipler.Add(teklif);
        }

        if (ihaleler.Count > 3)
        {
            // 4. Sonuçlandı — kazanıldı. Sonuç yazılmadan bu aşamaya geçilemez.
            var kazanildi = new TenderPursuit(
                tenantId, metal.Id, ihaleler[3], simdi.AddDays(-44), kullanici);
            kazanildi.AssignOwner("Burak Şen");
            kazanildi.ChangeStatus(TenderPursuitStatus.Hazirlaniyor, null, null, simdi.AddDays(-39), kullanici);
            kazanildi.ChangeStatus(TenderPursuitStatus.TeklifVerildi, null, null, simdi.AddDays(-33), kullanici);
            kazanildi.ChangeStatus(
                TenderPursuitStatus.Sonuclandi, TenderOutcome.Kazanildi,
                "Sözleşme imzalandı.", simdi.AddDays(-19), kullanici);
            takipler.Add(kazanildi);
        }

        if (ihaleler.Count > 4)
        {
            // 5. Vazgeçildi — takip edilmeyeceğine karar verildi; kayıt silinmez.
            var vazgecildi = new TenderPursuit(
                tenantId, lojistik.Id, ihaleler[4], simdi.AddDays(-30), kullanici);
            vazgecildi.ChangeStatus(
                TenderPursuitStatus.Vazgecildi, null,
                "Teminat tutarı ölçeğimizin üzerinde; bu tur atlandı.", simdi.AddDays(-27), kullanici);
            takipler.Add(vazgecildi);
        }

        return takipler;
    }

    private readonly record struct SilmeOzeti(int Firma, int Degerlendirme, int Bildirim);

    private readonly record struct KurulumOzeti(int Firma, int Alici, int Takip);
}

/// <summary>Simülasyon kurulumunun sonucu; çağıranın loglaması için.</summary>
public enum SimulasyonSonucu
{
    /// <summary>Kiracı yok; önce başlangıç verisi yüklenmeli.</summary>
    KiraciYok = 0,

    /// <summary>Grup zaten kurulu; hiçbir şey silinmedi.</summary>
    ZatenKurulu = 1,

    /// <summary>Eski firma verisi silindi, grup kuruldu.</summary>
    Kuruldu = 2
}
