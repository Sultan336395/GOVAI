using GovAI.Application.Evidence;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Evidence;

namespace GovAI.Application.Tests;

/// <summary>
/// Firma verisinin kanıta çevrilmesi.
///
/// <para>
/// Motor doğru çalışsa bile buradaki eşleme yanlışsa sonuç yanlış olur: hangi alanın
/// hangi tarihe dayandığı ve hangisinin tarihinin <b>bilinmediği</b> burada belirlenir.
/// Testlerin asıl işi, boşlukların tarih uydurularak doldurulmadığını sabitlemektir.
/// </para>
/// </summary>
public class CompanyEvidenceCollectorTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 12);

    private static Company Firma()
    {
        var company = new Company(
            Guid.CreateVersion7(), "Örnek A.Ş.", "1234567890", LegalType.JointStockCompany);

        company.UpdateWorkforce(new Workforce(40, 12, 8, 5, 1, 29));

        return company;
    }

    private static EvidenceReliability? Bul(Company firma, EvidenceKind tur) =>
        CompanyEvidenceCollector.Collect(firma, Bugun).FirstOrDefault(k => k.Kind == tur);

    // ── Tarih uydurulmaz ────────────────────────────────────────────────────

    [Fact(DisplayName = "KT1. Hiç güncellenmemiş profil beyanı TARİHSİZ kalır")]
    public void Guncellenmemis_profil_tarihsiz()
    {
        // Kayıt tarihine düşmek cazip ama yanlış: kuruluşta girilen personel sayısının
        // bugün hâlâ doğru olduğu varsayılamaz — ama "yanlış" da denemez.
        var sonuc = Bul(Firma(), EvidenceKind.WorkforceDeclaration);

        Assert.NotNull(sonuc);
        Assert.Equal(EvidenceStatus.Bilinmiyor, sonuc.Status);
    }

    [Fact(DisplayName = "KT2. ERP hiç eşitlenmemişse kanıt ÜRETİLMEZ")]
    public void Erp_yoksa_kanit_yok()
    {
        // Tarihsiz bir ERP kanıtı üretmek, entegrasyonu olmayan firmada "ERP verisi var
        // ama tazeliği bilinmiyor" demek olurdu. Olmayan kanıt hakkında konuşulmaz.
        Assert.Null(Bul(Firma(), EvidenceKind.ErpSnapshot));
    }

    [Fact(DisplayName = "KT3. ERP eşitlemesi varsa kanıt üretilir ve tarihi bilinir")]
    public void Erp_varsa_kanit_var()
    {
        var firma = Firma();
        firma.MarkSynced(new DateTimeOffset(2026, 9, 10, 2, 45, 0, TimeSpan.Zero));

        var sonuc = Bul(firma, EvidenceKind.ErpSnapshot);

        Assert.NotNull(sonuc);
        Assert.Equal(2, sonuc.AgeDays);
        Assert.Equal(EvidenceStatus.Guvenilir, sonuc.Status);
    }

    // ── Mali veri ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "KT4. Mali kanıt MALİ YILA dayanır, giriş gününe değil")]
    public void Mali_kanit_yila_dayanir()
    {
        // 2023 bilançosu bugün girilse bile 2023'ün verisidir. Giriş gününe bakmak
        // üç yıllık veriyi "bugün elde edilmiş" gibi gösterirdi.
        var firma = Firma();
        firma.UpsertAnnualFinancials(
            2023, "TRY", 1_000_000m, null, null, null, null,
            FinancialDataSource.SelfDeclared, FinancialVerificationStatus.Unverified,
            DateTimeOffset.UtcNow);

        var sonuc = Bul(firma, EvidenceKind.FinancialStatement);

        Assert.NotNull(sonuc);
        // 31.12.2023 ile 12.09.2026 arası ~2,7 yıl.
        Assert.True(sonuc.AgeDays > 900, $"Yaş {sonuc.AgeDays} gün çıktı; mali yıl sonuna göre hesaplanmamış.");
    }

    [Fact(DisplayName = "KT5. Onaylı mali veri daha güvenilir çıkar")]
    public void Onayli_mali_veri_daha_guvenilir()
    {
        var beyan = Firma();
        beyan.UpsertAnnualFinancials(
            2024, "TRY", 1_000_000m, null, null, null, null,
            FinancialDataSource.SelfDeclared, FinancialVerificationStatus.Unverified, DateTimeOffset.UtcNow);

        var onayli = Firma();
        onayli.UpsertAnnualFinancials(
            2024, "TRY", 1_000_000m, null, null, null, null,
            FinancialDataSource.AccountantStatement, FinancialVerificationStatus.Verified, DateTimeOffset.UtcNow);

        Assert.True(Bul(onayli, EvidenceKind.FinancialStatement)!.Score
            > Bul(beyan, EvidenceKind.FinancialStatement)!.Score);
    }

    [Fact(DisplayName = "KT6. BOŞ mali yıl kaydı kanıt sayılmaz")]
    public void Bos_mali_yil_sayilmaz()
    {
        // Hiçbir tutarı olmayan bir yıl kaydı "mali verimiz var" demek değildir;
        // sayılsaydı portföy dolu görünür, karar dayanağı ise boş olurdu.
        var firma = Firma();
        firma.UpsertAnnualFinancials(
            2025, "TRY", null, null, null, null, null,
            FinancialDataSource.Unspecified, FinancialVerificationStatus.Unverified, DateTimeOffset.UtcNow);

        Assert.Null(Bul(firma, EvidenceKind.FinancialStatement));
    }

    // ── Sertifikalar ────────────────────────────────────────────────────────

    [Fact(DisplayName = "KT7. Her sertifika ayrı kanıttır")]
    public void Sertifikalar_ayri_ayri()
    {
        var firma = Firma();
        firma.ReplaceCertificates(
        [
            new CompanyCertificate("ISO9001", "ISO 9001", Bugun.AddYears(-1), Bugun.AddYears(2)),
            new CompanyCertificate("ISO27001", "ISO 27001", Bugun.AddYears(-3), Bugun.AddDays(-2)),
        ]);

        var kanitlar = CompanyEvidenceCollector.Collect(firma, Bugun)
            .Where(k => k.Kind == EvidenceKind.Certificate)
            .ToList();

        Assert.Equal(2, kanitlar.Count);
        Assert.Contains(kanitlar, k => k.Status == EvidenceStatus.Guvenilir);
        Assert.Contains(kanitlar, k => k.Status == EvidenceStatus.Gecersiz);
    }

    [Fact(DisplayName = "KT8. Sertifika adı yoksa kodu gösterilir")]
    public void Ad_yoksa_kod()
    {
        // Etiket boş kalsaydı kullanıcı listede hangi belgenin bittiğini göremezdi.
        var firma = Firma();
        firma.ReplaceCertificates([new CompanyCertificate("CE", "CE", null, null)]);

        var sonuc = Bul(firma, EvidenceKind.Certificate);

        Assert.NotNull(sonuc);
        Assert.False(string.IsNullOrWhiteSpace(sonuc.Label));
    }

    // ── Portföy ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "KT9. Boş profilde bile en az bir kanıt vardır")]
    public void Bos_profilde_de_kanit_var()
    {
        // Personel beyanı her firmada vardır — tarihi bilinmese bile. Hiç kanıt
        // dönmeseydi ekran "kanıt yok" der, oysa doğru mesaj "tarihi girilmemiş"tir.
        Assert.NotEmpty(CompanyEvidenceCollector.Collect(Firma(), Bugun));
    }

    [Fact(DisplayName = "KT10. Portföy özeti gerçek firmada tutarlıdır")]
    public void Portfoy_tutarli()
    {
        var firma = Firma();
        firma.MarkSynced(DateTimeOffset.UtcNow);
        firma.ReplaceCertificates(
        [
            new CompanyCertificate("ISO9001", "ISO 9001", Bugun.AddYears(-1), Bugun.AddYears(2)),
            new CompanyCertificate("ISO14001", "ISO 14001", Bugun.AddYears(-4), Bugun.AddDays(-10)),
        ]);

        var ozet = EvidenceHalfLife.Summarize(CompanyEvidenceCollector.Collect(firma, Bugun));

        // 2 sertifika + 1 personel beyanı + 1 ERP = 4
        Assert.Equal(4, ozet.Total);
        Assert.Equal(1, ozet.Expired);
        Assert.Equal(1, ozet.Unknown);
        Assert.NotNull(ozet.AverageScore);
    }

    [Fact(DisplayName = "KT11. Aynı firma aynı gün için AYNI sonucu verir")]
    public void Deterministik()
    {
        var firma = Firma();
        firma.MarkSynced(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        var ilk = CompanyEvidenceCollector.Collect(firma, Bugun).Select(k => k.Score).ToList();
        var ikinci = CompanyEvidenceCollector.Collect(firma, Bugun).Select(k => k.Score).ToList();

        Assert.Equal(ilk, ikinci);
    }
}
