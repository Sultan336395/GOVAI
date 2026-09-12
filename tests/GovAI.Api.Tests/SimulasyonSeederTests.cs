using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Identity;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Sources;
using GovAI.Persistence;
using GovAI.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovAI.Api.Tests;

/// <summary>
/// Tanıtım simülasyonu kurucusu.
///
/// <para>
/// Bu sınıf depodaki <b>tek bilerek yıkıcı</b> koddur: canlı firma verisini siler.
/// Bu yüzden "ne siliyor" kadar <b>ne silmediği</b> de sınanır. Fırsat kataloğu ya da
/// kaynak belgeleri yanlışlıkla silinirse haftalarca toplanmış veri gider ve yedekten
/// dönmek gerekir — testlerin yakalaması gereken hata tam olarak budur.
/// </para>
///
/// <para>
/// Testler <b>kendi yalıtılmış veritabanını</b> kurar; paylaşılan API fikstürü
/// kullanılmaz. Kullanılsaydı ilk çalışan test diğer bütün testlerin firmasını silerdi.
/// </para>
/// </summary>
public sealed class SimulasyonSeederTests
{
    private static GovAiDbContext YeniVeritabani(string ad) =>
        new(new DbContextOptionsBuilder<GovAiDbContext>()
            .UseInMemoryDatabase(ad)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static SimulasyonSeeder Kurucu(GovAiDbContext db) =>
        new(db, NullLogger<SimulasyonSeeder>.Instance);

    /// <summary>
    /// Silinmesi gereken ve silinmemesi gereken verileri bir arada kurar.
    /// Gerçek ortamın küçük ama temsilî bir kopyasıdır.
    /// </summary>
    private static async Task<(Guid TenantId, Guid EskiFirmaId, Guid FirsatId, Guid KullaniciId)>
        BaslangicAsync(GovAiDbContext db)
    {
        var tenant = new Tenant("Test Kiracı", "test-kiraci");
        await db.Tenants.AddAsync(tenant);

        var kullanici = new AppUser(tenant.Id, "yonetici@test.local", "Yönetici", UserRole.SuperAdmin);
        await db.Users.AddAsync(kullanici);

        var eskiFirma = new Company(tenant.Id, "Silinecek A.Ş.", "9999999999", LegalType.JointStockCompany);
        eskiFirma.UpdateWorkforce(new Workforce(50, 10, 5, 3, 1, 29));
        await db.Companies.AddAsync(eskiFirma);

        await db.UserCompanies.AddAsync(
            new UserCompany(tenant.Id, kullanici.Id, eskiFirma.Id, CompanyRole.CompanyOwner, isDefault: true));

        // KORUNMASI gerekenler: kaynak ve fırsat kataloğu firma verisi değildir.
        var kaynak = new Source("Test Kaynağı", SourceType.OfficialGazette, "https://ornek.gov.tr", "0 7 * * *");
        await db.Sources.AddAsync(kaynak);

        var firsat = new Opportunity(
            kaynak.Id, SourceType.OfficialGazette, SupportCategory.Tender,
            "Test İhalesi", "Test Kurumu", DateTimeOffset.UtcNow.AddDays(-3));
        await db.Opportunities.AddAsync(firsat);

        await db.SaveChangesAsync();

        return (tenant.Id, eskiFirma.Id, firsat.Id, kullanici.Id);
    }

    // ── Koruma ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "SS1. Kiracı yoksa hiçbir şey yapılmaz")]
    public async Task Kiraci_yoksa_durur()
    {
        using var db = YeniVeritabani(nameof(Kiraci_yoksa_durur));

        var sonuc = await Kurucu(db).KurAsync();

        Assert.Equal(SimulasyonSonucu.KiraciYok, sonuc);
    }

    [Fact(DisplayName = "SS2. İkinci çalıştırma HİÇBİR ŞEY SİLMEZ")]
    public async Task Ikinci_calistirma_silmez()
    {
        // Sahadaki asıl risk bu: bayrak açık unutulur, konteyner yeniden başlar ve
        // simülasyon her açılışta baştan kurulur. O zaman kullanıcının panoda
        // biriktirdiği ihale takipleri ve raporlar sessizce kaybolurdu.
        using var db = YeniVeritabani(nameof(Ikinci_calistirma_silmez));
        await BaslangicAsync(db);

        Assert.Equal(SimulasyonSonucu.Kuruldu, await Kurucu(db).KurAsync());

        var ilkFirmaSayisi = await db.Companies.IgnoreQueryFilters().CountAsync();
        var ilkKimlikler = await db.Companies.IgnoreQueryFilters().Select(c => c.Id).OrderBy(x => x).ToListAsync();

        Assert.Equal(SimulasyonSonucu.ZatenKurulu, await Kurucu(db).KurAsync());

        var sonKimlikler = await db.Companies.IgnoreQueryFilters().Select(c => c.Id).OrderBy(x => x).ToListAsync();

        Assert.Equal(ilkFirmaSayisi, sonKimlikler.Count);
        Assert.Equal(ilkKimlikler, sonKimlikler);
    }

    // ── Silme kapsamı ───────────────────────────────────────────────────────

    [Fact(DisplayName = "SS3. Eski firma ve üyelikleri silinir")]
    public async Task Eski_firma_silinir()
    {
        using var db = YeniVeritabani(nameof(Eski_firma_silinir));
        var (_, eskiFirmaId, _, _) = await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        Assert.False(await db.Companies.IgnoreQueryFilters().AnyAsync(c => c.Id == eskiFirmaId));
        Assert.False(await db.UserCompanies.IgnoreQueryFilters().AnyAsync(u => u.CompanyId == eskiFirmaId));
    }

    [Fact(DisplayName = "SS4. Fırsat kataloğu, kaynaklar ve kullanıcılar KORUNUR")]
    public async Task Katalog_korunur()
    {
        // Bunlar firma verisi değildir. Silinirlerse haftalarca toplanan resmî belge
        // ve çağrı verisi gider; simülasyon kurmak için ödenecek bedel bu olamaz.
        using var db = YeniVeritabani(nameof(Katalog_korunur));
        var (_, _, firsatId, kullaniciId) = await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        Assert.True(await db.Opportunities.IgnoreQueryFilters().AnyAsync(o => o.Id == firsatId));
        Assert.True(await db.Sources.IgnoreQueryFilters().AnyAsync());
        Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == kullaniciId));
        Assert.True(await db.Tenants.AnyAsync());
    }

    // ── Grup yapısı ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "SS5. Tek grup ve beş firma kurulur")]
    public async Task Grup_kurulur()
    {
        using var db = YeniVeritabani(nameof(Grup_kurulur));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        var grup = await db.CompanyGroups.IgnoreQueryFilters().SingleAsync();
        var firmalar = await db.Companies.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(SimulasyonSeeder.GrupAdi, grup.Name);
        Assert.Equal(5, firmalar.Count);
        Assert.All(firmalar, f => Assert.Equal(grup.Id, f.GroupId));
    }

    [Fact(DisplayName = "SS6. Tam olarak BİR ana şirket vardır ve o kimseye bağlı değildir")]
    public async Task Tek_ana_sirket()
    {
        using var db = YeniVeritabani(nameof(Tek_ana_sirket));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        var firmalar = await db.Companies.IgnoreQueryFilters().ToListAsync();
        var ana = Assert.Single(firmalar.Where(f => f.IsHeadCompany));

        Assert.Null(ana.ParentCompanyId);
        Assert.Equal(CompanyRelationshipType.HeadCompany, ana.RelationshipType);
        Assert.All(firmalar.Where(f => !f.IsHeadCompany), f => Assert.Equal(ana.Id, f.ParentCompanyId));
    }

    [Fact(DisplayName = "SS7. Bağlı ortaklık ve iştirak ayrı ayrı temsil edilir")]
    public async Task Iliski_turleri_cesitli()
    {
        // Hepsi Subsidiary olsaydı iştirak (azınlık payı) ayrımı ekranda hiç görünmezdi.
        using var db = YeniVeritabani(nameof(Iliski_turleri_cesitli));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        var turler = await db.Companies.IgnoreQueryFilters()
            .Select(f => f.RelationshipType).ToListAsync();

        Assert.Contains(CompanyRelationshipType.HeadCompany, turler);
        Assert.Contains(CompanyRelationshipType.Subsidiary, turler);
        Assert.Contains(CompanyRelationshipType.Affiliate, turler);
    }

    // ── Beyan durumları ─────────────────────────────────────────────────────

    [Fact(DisplayName = "SS8. Simülasyon üç beyan durumunu da içerir")]
    public async Task Uc_beyan_durumu()
    {
        // Ürünün üçüncü iddiası "eksik veri firmayı elemez"tir. Bu ayrımın ekranda
        // görülebilmesi için simülasyonda her üç durumdan en az bir firma olmalıdır;
        // hepsi tam beyanlı olsaydı ayrım tanıtımda hiç gösterilemezdi.
        using var db = YeniVeritabani(nameof(Uc_beyan_durumu));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        // Sahipli (owned) tip sahibinden ayrı projelenemez; firmalar çekilip
        // bellekte eşlenir.
        var firmalar = await db.Companies.IgnoreQueryFilters().ToListAsync();
        var personeller = firmalar.Select(f => f.Workforce).ToList();

        Assert.Contains(personeller, p => p.RAndDEmployeeCount > 0);
        Assert.Contains(personeller, p => p.RAndDEmployeeCount == 0);
        Assert.Contains(personeller, p => p.WomenEmployeeCount is null && p.EmployeeCount > 0);
    }

    [Fact(DisplayName = "SS9. Beyan edilmemiş kırılım motorda BİLİNMİYOR sayılır")]
    public async Task Beyansiz_kirilim_bilinmiyor()
    {
        // Veriyi doğru kurmak yetmez; motorun onu doğru okuduğu da görülmeli.
        using var db = YeniVeritabani(nameof(Beyansiz_kirilim_bilinmiyor));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        var beyansiz = await db.Companies.IgnoreQueryFilters()
            .FirstAsync(f => f.Workforce.WomenEmployeeCount == null && f.Workforce.EmployeeCount > 0);

        var deger = GovAI.Domain.Eligibility.CompanyFieldResolver.Resolve(
            beyansiz, "Workforce.WomenEmployeeRate", DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.False(deger.IsKnown);
    }

    // ── Yan veriler ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "SS10. Bildirim alıcısı OLMAYAN bir firma bilerek bırakılır")]
    public async Task Alicisiz_firma_vardir()
    {
        // Alıcısı olmayan firmada bildirimin kaybolmadığı (RecipientMissing) canlı
        // olarak görülebilsin diye. Her firmaya alıcı tanımlansaydı bu yol tanıtımda
        // hiç çalışmazdı.
        using var db = YeniVeritabani(nameof(Alicisiz_firma_vardir));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        var firmaIdleri = await db.Companies.IgnoreQueryFilters().Select(f => f.Id).ToListAsync();
        var aliciliFirmalar = await db.NotificationRecipients.IgnoreQueryFilters()
            .Select(a => a.CompanyId).Distinct().ToListAsync();

        Assert.NotEmpty(aliciliFirmalar);
        Assert.Contains(firmaIdleri, id => !aliciliFirmalar.Contains(id));
    }

    [Fact(DisplayName = "SS11. Birden çok alıcısı olan firma vardır")]
    public async Task Coklu_alici()
    {
        using var db = YeniVeritabani(nameof(Coklu_alici));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        var gruplu = await db.NotificationRecipients.IgnoreQueryFilters()
            .GroupBy(a => a.CompanyId)
            .Select(g => g.Count())
            .ToListAsync();

        Assert.Contains(gruplu, sayi => sayi > 1);
    }

    [Fact(DisplayName = "SS12. İhale takibi geçmişiyle birlikte kurulur")]
    public async Task Ihale_takibi_gecmisli()
    {
        using var db = YeniVeritabani(nameof(Ihale_takibi_gecmisli));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        var takipler = await db.TenderPursuits.IgnoreQueryFilters().ToListAsync();

        // Başlangıçta katalogda tek ihale var; en az o kadarı açılmalı.
        Assert.NotEmpty(takipler);
        Assert.All(takipler, t => Assert.NotEqual(Guid.Empty, t.OpportunityId));
    }

    [Fact(DisplayName = "SS13. Sonuçlanan takipte sonuç MUTLAKA yazılıdır")]
    public async Task Sonuclanan_takipte_sonuc_var()
    {
        using var db = YeniVeritabani(nameof(Sonuclanan_takipte_sonuc_var));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        var sonuclananlar = await db.TenderPursuits.IgnoreQueryFilters()
            .Where(t => t.Status == Domain.Tenders.TenderPursuitStatus.Sonuclandi)
            .ToListAsync();

        Assert.All(sonuclananlar, t => Assert.NotNull(t.Outcome));
    }

    // ── Profil zenginliği ───────────────────────────────────────────────────

    [Fact(DisplayName = "SS14. Firmalar farklı ölçek ve tüzel tiplerde olur")]
    public async Task Cesitlilik_vardir()
    {
        // Beşi de aynı ölçekte olsaydı KOBİ şartı arayan kuralların ayrımı görülmezdi.
        using var db = YeniVeritabani(nameof(Cesitlilik_vardir));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        var firmalar = await db.Companies.IgnoreQueryFilters().ToListAsync();

        Assert.True(firmalar.Select(f => f.Size).Distinct().Count() > 1, "Ölçekler aynı çıktı.");
        Assert.True(firmalar.Select(f => f.LegalType).Distinct().Count() > 1, "Tüzel tipler aynı çıktı.");
    }

    [Fact(DisplayName = "SS15. Çok yıllı mali veri ve teknopark firması bulunur")]
    public async Task Mali_gecmis_ve_teknopark()
    {
        using var db = YeniVeritabani(nameof(Mali_gecmis_ve_teknopark));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        var firmalar = await db.Companies
            .IgnoreQueryFilters()
            .Include(f => f.AnnualFinancials)
            .Include(f => f.Locations)
            .ToListAsync();

        Assert.Contains(firmalar, f => f.AnnualFinancials.Count >= 3);
        Assert.Contains(firmalar, f => f.Locations.Any(l => l.IsInTechnopark));
    }

    [Fact(DisplayName = "SS16. Sertifikasız bir firma bilerek bırakılır")]
    public async Task Sertifikasiz_firma_vardir()
    {
        // "Belgeniz eksik" aksiyonunun ve belge hazırlık boyutunun düşük puanının
        // ekranda görülebilmesi için.
        using var db = YeniVeritabani(nameof(Sertifikasiz_firma_vardir));
        await BaslangicAsync(db);

        await Kurucu(db).KurAsync();

        var firmalar = await db.Companies
            .IgnoreQueryFilters()
            .Include(f => f.Certificates)
            .ToListAsync();

        Assert.Contains(firmalar, f => f.Certificates.Count == 0);
        Assert.Contains(firmalar, f => f.Certificates.Count > 1);
    }
}
