using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Eligibility;

namespace GovAI.Domain.Tests;

/// <summary>
/// Personel kırılımında "girilmedi" ile "sıfır" ayrımı.
///
/// <para>
/// Bu testler sahadan gelen bir hatayı sabitler. Pilot firmanın profilinde personel
/// sayısı 50 yazıyordu ama kırılım hiç girilmemişti; alanlar <c>int</c> olduğu için
/// 0 olarak duruyordu ve motor bunları <b>gerçek sıfır</b> sayıyordu. Firma "kadın
/// çalışanı yok" ve "Ar-Ge personeli yok" diye kaydedilmiş, o şartları arayan
/// çağrılardan <b>elenmişti</b>. Ürünün üçüncü iddiası ("eksik veri firmayı elemez")
/// fiilen çiğneniyordu.
/// </para>
///
/// <para>
/// Artık her alan açık beyan ister. <c>null</c> "bilinmiyor", <c>0</c> firmanın
/// "yok" beyanıdır — ve ikisi <b>farklı sonuç doğurur</b>.
/// </para>
/// </summary>
public class PersonelKirilimiAcikBeyanTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 12);

    private static Company Firma(Workforce personel)
    {
        var company = new Company(
            Guid.CreateVersion7(), "Pilot A.Ş.", "1234567890", LegalType.JointStockCompany);

        company.UpdateWorkforce(personel);

        return company;
    }

    private static FieldValue Oku(Workforce personel, string alan) =>
        CompanyFieldResolver.Resolve(Firma(personel), alan, Bugun);

    // ── Girilmedi ≠ sıfır ───────────────────────────────────────────────────

    [Theory(DisplayName = "PK1. BEYAN EDİLMEMİŞ kırılım bilinmiyordur")]
    [InlineData("Workforce.WomenEmployeeCount")]
    [InlineData("Workforce.WomenEmployeeRate")]
    [InlineData("Workforce.RAndDEmployeeCount")]
    [InlineData("Workforce.RAndDEmployeeRate")]
    [InlineData("Workforce.DisabledEmployeeCount")]
    public void Beyan_edilmemis_bilinmiyor(string alan)
    {
        // Sahadaki hata tam buradaydı: personel sayısı dolu, kırılım boş.
        var personel = new Workforce(
            employeeCount: 50,
            womenEmployeeCount: null,
            youngEmployeeCount: null,
            rAndDEmployeeCount: null,
            disabledEmployeeCount: null);

        Assert.False(Oku(personel, alan).IsKnown, $"{alan} bilinmiyor olmalıydı.");
    }

    [Theory(DisplayName = "PK2. BEYAN EDİLMİŞ sıfır bilinen bir değerdir")]
    [InlineData("Workforce.WomenEmployeeCount")]
    [InlineData("Workforce.RAndDEmployeeCount")]
    [InlineData("Workforce.DisabledEmployeeCount")]
    public void Beyan_edilmis_sifir_bilinir(string alan)
    {
        // Firma "gerçekten yok" diyorsa bu bir cevaptır ve koşulu sağlamadığını
        // söyler; bilinmiyor sayılırsa firma sonsuza kadar "belirsiz" kalırdı.
        var personel = new Workforce(
            employeeCount: 50,
            womenEmployeeCount: 0,
            youngEmployeeCount: 0,
            rAndDEmployeeCount: 0,
            disabledEmployeeCount: 0,
            youngEmployeeMaxAge: 29);

        var deger = Oku(personel, alan);

        Assert.True(deger.IsKnown, $"{alan} bilinen olmalıydı.");
        Assert.Equal(0m, deger.Number);
    }

    [Fact(DisplayName = "PK3. Beyan edilmemiş ile beyan edilmiş sıfır AYNI SONUCU vermez")]
    public void Ikisi_ayni_degil()
    {
        // Düzeltmenin özü bu: eskiden ikisi de aynıydı.
        var beyansiz = new Workforce(50, null, null, null, null);
        var sifirBeyani = new Workforce(50, 0, 0, 0, 0);

        Assert.False(Oku(beyansiz, "Workforce.WomenEmployeeCount").IsKnown);
        Assert.True(Oku(sifirBeyani, "Workforce.WomenEmployeeCount").IsKnown);
    }

    // ── Oranlar ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "PK4. Beyan edilmemiş sayının ORANI da bilinmiyordur")]
    public void Beyan_edilmemis_oran_bilinmiyor()
    {
        // Oran 0 döndürülseydi, "kadın oranı en az %30" arayan çağrı firmayı
        // "oranı sıfır" diye elerdi — sayı hiç girilmemiş olmasına rağmen.
        var personel = new Workforce(50, null, null, null, null);

        Assert.Null(personel.WomenEmployeeRate);
        Assert.Null(personel.RAndDEmployeeRate);
        Assert.False(Oku(personel, "Workforce.WomenEmployeeRate").IsKnown);
    }

    [Fact(DisplayName = "PK5. Beyan edilmiş sayının oranı hesaplanır")]
    public void Beyan_edilmis_oran_hesaplanir()
    {
        var personel = new Workforce(50, 20, null, null, null);

        Assert.Equal(0.4m, personel.WomenEmployeeRate);
        Assert.Equal(0.4m, Oku(personel, "Workforce.WomenEmployeeRate").Number);
    }

    [Fact(DisplayName = "PK6. Beyan edilmiş SIFIRIN oranı sıfırdır, bilinmiyor değil")]
    public void Sifir_beyaninin_orani_sifir()
    {
        var personel = new Workforce(50, 0, null, null, null);

        Assert.Equal(0m, personel.WomenEmployeeRate);
    }

    // ── Mevcut kaba süzgeç korunur ──────────────────────────────────────────

    [Theory(DisplayName = "PK7. Toplam çalışan girilmemişse kırılım BEYAN EDİLSE DE bilinmez")]
    [InlineData("Workforce.WomenEmployeeCount")]
    [InlineData("Workforce.WomenEmployeeRate")]
    [InlineData("Workforce.DisabledEmployeeCount")]
    public void Toplam_yoksa_kirilim_okunmaz(string alan)
    {
        // CLAUDE.md §2.2'deki kaba süzgeç korunur: toplamı bilinmeyen bir firmada
        // kırılımın anlamı yoktur. Oran zaten hesaplanamaz.
        var personel = new Workforce(employeeCount: 0, 0, 0, 0, 0);

        Assert.False(Oku(personel, alan).IsKnown);
    }

    // ── Genç çalışan: iki kapı birden ───────────────────────────────────────

    [Fact(DisplayName = "PK8. Genç sayısı beyan edilse de YAŞ SINIRI yoksa bilinmez")]
    public void Genc_yas_siniri_olmadan_bilinmez()
    {
        // Mevcut koruma: "genç" tanımı bilinmeden sayı anlamsızdır.
        var personel = new Workforce(50, null, youngEmployeeCount: 12, null, null);

        Assert.False(Oku(personel, "Workforce.YoungEmployeeCount").IsKnown);
        Assert.False(Oku(personel, "Workforce.YoungEmployeeRate").IsKnown);
    }

    [Fact(DisplayName = "PK9. Yaş sınırı VAR ama sayı beyan edilmemişse yine bilinmez")]
    public void Yas_siniri_var_sayi_yoksa_bilinmez()
    {
        // İki kapı bağımsızdır; biri açık diğeri kapalı olabilir.
        var personel = new Workforce(50, null, youngEmployeeCount: null, null, null, youngEmployeeMaxAge: 29);

        Assert.False(Oku(personel, "Workforce.YoungEmployeeCount").IsKnown);
    }

    [Fact(DisplayName = "PK10. İkisi de varsa genç sayısı okunur")]
    public void Ikisi_de_varsa_okunur()
    {
        var personel = new Workforce(50, null, youngEmployeeCount: 12, null, null, youngEmployeeMaxAge: 29);

        var deger = Oku(personel, "Workforce.YoungEmployeeCount");

        Assert.True(deger.IsKnown);
        Assert.Equal(12m, deger.Number);
    }

    // ── Alan modeli ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "PK11. Boş personel nesnesinde kırılımların HEPSİ beyansızdır")]
    public void Bos_nesnede_hepsi_beyansiz()
    {
        var personel = Workforce.Empty;

        Assert.Equal(0, personel.EmployeeCount);
        Assert.Null(personel.WomenEmployeeCount);
        Assert.Null(personel.YoungEmployeeCount);
        Assert.Null(personel.RAndDEmployeeCount);
        Assert.Null(personel.DisabledEmployeeCount);
    }

    [Fact(DisplayName = "PK12. Beyan edilen sayı toplamı AŞAMAZ")]
    public void Toplami_asamaz()
    {
        Assert.Throws<DomainException>(() => new Workforce(10, womenEmployeeCount: 11, null, null, null));
        Assert.Throws<DomainException>(() => new Workforce(10, null, null, null, disabledEmployeeCount: 11));
    }

    [Fact(DisplayName = "PK13. Beyan edilmemiş alan toplam denetimini TETİKLEMEZ")]
    public void Beyansiz_alan_denetime_takilmaz()
    {
        // null karşılaştırması C#'ta false döner; kuralın yanlışlıkla "null > n"
        // üzerinden tetiklenmediği açıkça sınanır.
        var hata = Record.Exception(() => new Workforce(0, null, null, null, null));

        Assert.Null(hata);
    }

    [Fact(DisplayName = "PK14. Negatif beyan reddedilir")]
    public void Negatif_reddedilir()
    {
        Assert.Throws<DomainException>(() => new Workforce(10, womenEmployeeCount: -1, null, null, null));
    }
}
