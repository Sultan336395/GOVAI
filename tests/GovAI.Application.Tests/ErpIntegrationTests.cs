using GovAI.Application.Integrations;
using GovAI.Infrastructure.Integrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovAI.Application.Tests;

/// <summary>
/// ERP entegrasyonu sözleşmesi (Faz 3, IKPROF ilk uyarlayıcı).
///
/// Bu testler entegrasyonun <b>hukuki ve güvenlik sınırlarını</b> korur. Teknik bir
/// hata düzeltilir; sözleşmeden sızan bir kişisel veri geri alınamaz.
/// </summary>
public sealed class ErpIntegrationTests
{
    private static readonly Guid KiraciA = Guid.CreateVersion7();
    private static readonly Guid KiraciB = Guid.CreateVersion7();
    private static readonly Guid SirketA = Guid.CreateVersion7();
    private static readonly Guid SirketB = Guid.CreateVersion7();

    private static MockErpIntegrationAdapter Uyarlayici()
    {
        var saat = new FixedClock(new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero));
        var adapter = new MockErpIntegrationAdapter(saat, NullLogger<MockErpIntegrationAdapter>.Instance);

        adapter.Link("IKPROF-A", KiraciA, SirketA, "1112223334");
        adapter.Link("IKPROF-B", KiraciB, SirketB, "5556667778");

        return adapter;
    }

    private static CompanySnapshot Goruntu(
        string external = "IKPROF-A",
        string taxNumber = "1112223334",
        string? sektor = "Makine ve ekipman imalatı",
        string[]? nace = null,
        int? calisan = 42,
        EmploymentSnapshot? istihdam = null) => new()
        {
            ExternalCompanyId = external,
            TaxNumber = taxNumber,
            LegalName = "Örnek Üretim ve Teknoloji A.Ş.",
            MainSector = sektor,
            NaceCodes = nace ?? ["28.41"],
            EmployeeCount = calisan,
            Employment = istihdam,
            ObservedAt = new DateTimeOffset(2026, 9, 7, 6, 0, 0, TimeSpan.Zero),
        };

    // ═══════════ Kiracı ve şirket yalıtımı ═══════════

    [Fact(DisplayName = "E1. Başka kiracının şirketi ERP kimliğiyle okunamaz/yazılamaz")]
    public async Task Baska_kiracinin_sirketi_yazilamaz()
    {
        var adapter = Uyarlayici();

        // A kiracısının ERP kimliğiyle, B kiracısının vergi numarası gönderiliyor.
        var sonuc = await adapter.ApplyCompanySnapshotAsync(
            Guid.CreateVersion7().ToString(),
            Goruntu(external: "IKPROF-A", taxNumber: "5556667778"));

        Assert.Equal(SnapshotOutcome.Rejected, sonuc.Outcome);
        Assert.Null(sonuc.CompanyId);
    }

    [Fact(DisplayName = "E2. Eşlemesi olmayan ERP kimliği şirket OLUŞTURMAZ")]
    public async Task Eslemesiz_kimlik_sirket_olusturmaz()
    {
        // Sessizce şirket açmak, yanlış kiracıya veri yazmanın en kolay yoludur.
        var adapter = Uyarlayici();

        var sonuc = await adapter.ApplyCompanySnapshotAsync(
            Guid.CreateVersion7().ToString(), Goruntu(external: "IKPROF-BILINMEYEN"));

        Assert.Equal(SnapshotOutcome.LinkNotFound, sonuc.Outcome);
        Assert.Null(sonuc.CompanyId);
    }

    [Fact(DisplayName = "E3. Doğru eşleme kabul edilir ve doğru şirkete yazılır")]
    public async Task Dogru_esleme_kabul_edilir()
    {
        var adapter = Uyarlayici();

        var sonuc = await adapter.ApplyCompanySnapshotAsync(Guid.CreateVersion7().ToString(), Goruntu());

        Assert.Equal(SnapshotOutcome.Applied, sonuc.Outcome);
        Assert.Equal(SirketA, sonuc.CompanyId);
    }

    // ═══════════ Idempotency ═══════════

    [Fact(DisplayName = "E4. Aynı mesaj mükerrer kayıt oluşturmaz")]
    public async Task Ayni_mesaj_mukerrer_kayit_olusturmaz()
    {
        var adapter = Uyarlayici();
        var anahtar = Guid.CreateVersion7().ToString();

        var ilk = await adapter.ApplyCompanySnapshotAsync(anahtar, Goruntu());
        var ikinci = await adapter.ApplyCompanySnapshotAsync(anahtar, Goruntu());

        Assert.Equal(SnapshotOutcome.Applied, ilk.Outcome);
        Assert.Equal(SnapshotOutcome.Duplicate, ikinci.Outcome);
        Assert.Equal(ilk.CompanyId, ikinci.CompanyId);
    }

    [Fact(DisplayName = "E5. Idempotency anahtarı zorunludur")]
    public async Task Idempotency_anahtari_zorunludur()
    {
        var adapter = Uyarlayici();

        var sonuc = await adapter.ApplyCompanySnapshotAsync(string.Empty, Goruntu());

        Assert.Equal(SnapshotOutcome.Rejected, sonuc.Outcome);
    }

    [Fact(DisplayName = "E6. Reddedilen istek idempotency defterine yazılmaz")]
    public async Task Reddedilen_istek_defterine_yazilmaz()
    {
        // Gönderen hatasını düzeltip aynı anahtarla tekrar denerse çalışmalıdır.
        var adapter = Uyarlayici();
        var anahtar = Guid.CreateVersion7().ToString();

        var hatali = await adapter.ApplyCompanySnapshotAsync(anahtar, Goruntu(sektor: "uydurma sektör"));
        var duzeltilmis = await adapter.ApplyCompanySnapshotAsync(anahtar, Goruntu());

        Assert.Equal(SnapshotOutcome.Rejected, hatali.Outcome);
        Assert.Equal(SnapshotOutcome.Applied, duzeltilmis.Outcome);
    }

    // ═══════════ Veri minimizasyonu ═══════════

    [Fact(DisplayName = "E7. Kişisel çalışan verileri MVP sözleşmesinde BULUNMAZ")]
    public void Kisisel_veri_sozlesmede_yok()
    {
        // Alan varsa bir gün dolar: koruma "göndermiyoruz" sözü değil, alanın yokluğudur.
        var alanlar = typeof(CompanySnapshot).GetProperties().Select(p => p.Name)
            .Concat(typeof(EmploymentSnapshot).GetProperties().Select(p => p.Name))
            .Concat(typeof(DepartmentSnapshot).GetProperties().Select(p => p.Name))
            .Concat(typeof(FinancialSnapshot).GetProperties().Select(p => p.Name))
            .ToList();

        string[] yasak =
        [
            "FirstName", "LastName", "FullName", "EmployeeName",
            "NationalId", "Tckn", "IdentityNumber",
            "Salary", "Wage", "NetSalary", "GrossSalary",
            "Iban", "BankAccount", "HealthData", "UnionMembership",
        ];

        foreach (var ad in yasak)
        {
            Assert.DoesNotContain(ad, alanlar);
        }
    }

    [Theory(DisplayName = "E8. Sözleşme dışı alan gelirse istek tamamen reddedilir")]
    [InlineData("tcKimlikNo")]
    [InlineData("salary")]
    [InlineData("iban")]
    [InlineData("healthData")]
    [InlineData("employeeName")]
    public void Sozlesme_disi_alan_reddedilir(string alan)
    {
        var sonuc = CompanySnapshotValidator.CheckForbiddenFields(["taxNumber", alan]);

        Assert.False(sonuc.IsValid);
        Assert.Contains(alan, sonuc.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "E9. Sözleşmedeki alanlar reddedilmez")]
    public void Sozlesmedeki_alanlar_gecer()
    {
        var sonuc = CompanySnapshotValidator.CheckForbiddenFields(
            ["externalCompanyId", "taxNumber", "mainSector", "naceCodes", "employeeCount", "financials"]);

        Assert.True(sonuc.IsValid);
    }

    // ═══════════ Veri kalitesi ═══════════

    [Fact(DisplayName = "E10. Sektörle tutmayan NACE kodu ERP yolundan da geçemez")]
    public void Tutmayan_nace_erp_yolundan_gecemez()
    {
        // Entegrasyon, panelde zorunlu olan tutarlılık kuralının arka kapısı olamaz.
        var sonuc = CompanySnapshotValidator.Validate(
            Goruntu(sektor: "İnşaat ve taahhüt", nace: ["23.61"]));

        Assert.False(sonuc.IsValid);
        Assert.Contains("Yapı malzemeleri ve cam", sonuc.Reason!, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "E11. Katalog dışı sektör reddedilir")]
    public void Katalog_disi_sektor_reddedilir()
    {
        var sonuc = CompanySnapshotValidator.Validate(Goruntu(sektor: "makine"));

        Assert.False(sonuc.IsValid);
    }

    [Fact(DisplayName = "E12. Personel kırılımı toplamı aşamaz")]
    public void Personel_kirilimi_toplami_asamaz()
    {
        var sonuc = CompanySnapshotValidator.Validate(
            Goruntu(calisan: 10, istihdam: new EmploymentSnapshot(20, null, null, null)));

        Assert.False(sonuc.IsValid);
        Assert.Contains("aşamaz", sonuc.Reason!, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "E13. Geçerli görüntü doğrulamadan geçer")]
    public void Gecerli_goruntu_gecer()
    {
        var sonuc = CompanySnapshotValidator.Validate(
            Goruntu(istihdam: new EmploymentSnapshot(14, 9, 1, 7)));

        Assert.True(sonuc.IsValid, sonuc.Reason);
    }

    // ═══════════ Dürüstlük ═══════════

    [Fact(DisplayName = "E14. Sahte uyarlayıcı kendini canlı göstermez")]
    public void Sahte_uyarlayici_canli_gorunmez()
    {
        // "Bağlantı kuruldu" diye raporlanamamalı: sahteyi gerçek sanmak, entegrasyonun
        // çalıştığı varsayılarak canlıya alınmasına yol açar.
        var adapter = Uyarlayici();

        Assert.False(adapter.IsLive);
        Assert.Equal("IKPROF", adapter.PartnerName);
    }

    [Fact(DisplayName = "E15. Sahte uyarlayıcı uydurma sinyal üretmez")]
    public async Task Sahte_uyarlayici_sinyal_uretmez()
    {
        var adapter = Uyarlayici();

        Assert.Empty(await adapter.ListSignalsAsync(SirketA, null));
    }
}
