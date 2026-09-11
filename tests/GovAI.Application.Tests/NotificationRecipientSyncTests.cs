using GovAI.Application.Notifications;
using GovAI.Domain.Common;
using GovAI.Domain.Notifications;

namespace GovAI.Application.Tests;

/// <summary>
/// ERP'den gelen bildirim sorumlularının uzlaştırılması.
///
/// <para>
/// Bu kuralların bedeli tek yönlü değildir: fazla agresif uzlaştırma bir firmayı
/// sessizce bildirimsiz bırakır, fazla gevşek uzlaştırma ise işten ayrılmış kişiye
/// şirket verisi göndermeye devam eder. İkisi de sahada fark edilmesi geç olan
/// hatalardır.
/// </para>
/// </summary>
public class NotificationRecipientSyncTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static NotificationRecipient Mevcut(
        string eposta,
        RecipientSource kaynak = RecipientSource.ErpPull,
        bool etkin = true)
    {
        var kayit = new NotificationRecipient(
            TenantId, CompanyId, eposta, "Ad Soyad", "Görev", kaynak);

        if (!etkin)
        {
            kayit.Deactivate();
        }

        return kayit;
    }

    private static RecipientSyncResult Uzlastir(
        IReadOnlyList<NotificationRecipient> mevcut,
        IReadOnlyList<IncomingRecipient>? gelen) =>
        NotificationRecipientSync.Reconcile(TenantId, CompanyId, mevcut, gelen);

    private static IncomingRecipient Gelen(string eposta, string? ad = "Ad", string? gorev = null) =>
        new(eposta, ad, gorev, null);

    // ── Bölüm tanımlı değil / boş ───────────────────────────────────────────

    [Fact(DisplayName = "AU1. Bölüm ERP'de TANIMLI DEĞİLSE hiçbir şey değişmez")]
    public void Bolum_tanimsizsa_dokunulmaz()
    {
        // null = "bu ERP'de bu bölüm yok". Mevcut tanımları silmek, ERP'sinde bu modül
        // olmayan bir firmada elle girilmiş doğru listeyi yok etmek olurdu.
        var mevcut = new[] { Mevcut("a@firma.test") };

        var sonuc = Uzlastir(mevcut, null);

        Assert.False(sonuc.Changed);
        Assert.True(mevcut[0].IsActive);
    }

    [Fact(DisplayName = "AU2. BOŞ liste 'artık sorumlu yok' demektir; ERP kaynaklılar pasifleşir")]
    public void Bos_liste_pasiflestirir()
    {
        var mevcut = new[] { Mevcut("a@firma.test") };

        var sonuc = Uzlastir(mevcut, []);

        Assert.Equal(1, sonuc.Deactivated);
        Assert.False(mevcut[0].IsActive);
    }

    // ── Ekleme, güncelleme, pasifleştirme ───────────────────────────────────

    [Fact(DisplayName = "AU3. Yeni adres eklenir ve ERP kaynaklı işaretlenir")]
    public void Yeni_adres_eklenir()
    {
        var sonuc = Uzlastir([], [Gelen("yeni@firma.test", "Yeni Kişi", "Mali İşler")]);

        var kayit = Assert.Single(sonuc.Added);

        Assert.Equal("yeni@firma.test", kayit.Email);
        Assert.Equal("Yeni Kişi", kayit.FullName);
        Assert.Equal("Mali İşler", kayit.Role);
        Assert.Equal(RecipientSource.ErpPull, kayit.Source);
        Assert.True(kayit.IsActive);
        Assert.Equal(CompanyId, kayit.CompanyId);
        Assert.Equal(TenantId, kayit.TenantId);
    }

    [Fact(DisplayName = "AU4. Bir şirkette BİRDEN FAZLA alıcı tanımlanabilir")]
    public void Birden_fazla_alici()
    {
        var sonuc = Uzlastir([], [
            Gelen("bir@firma.test"),
            Gelen("iki@firma.test"),
            Gelen("uc@firma.test"),
        ]);

        Assert.Equal(3, sonuc.Added.Count);
    }

    [Fact(DisplayName = "AU5. ERP listesinden düşen alıcı pasifleşir ama SİLİNMEZ")]
    public void Dusen_alici_pasiflesir()
    {
        // Silinseydi "bu uyarı kime gitti" sorusunun cevabı da kaybolurdu.
        var kalan = Mevcut("kalan@firma.test");
        var dusen = Mevcut("dusen@firma.test");

        var sonuc = Uzlastir([kalan, dusen], [Gelen("kalan@firma.test")]);

        Assert.Equal(1, sonuc.Deactivated);
        Assert.True(kalan.IsActive);
        Assert.False(dusen.IsActive);
    }

    [Fact(DisplayName = "AU6. ERP'ye geri dönen alıcı yeniden ETKİNLEŞİR")]
    public void Geri_donen_alici_etkinlesir()
    {
        var kayit = Mevcut("geri@firma.test", etkin: false);

        var sonuc = Uzlastir([kayit], [Gelen("geri@firma.test")]);

        Assert.Equal(1, sonuc.Reactivated);
        Assert.True(kayit.IsActive);
        Assert.Empty(sonuc.Added);
    }

    [Fact(DisplayName = "AU7. Mevcut alıcının künyesi TAZELENİR, kopya açılmaz")]
    public void Kunye_tazelenir()
    {
        var kayit = Mevcut("a@firma.test");

        var sonuc = Uzlastir([kayit], [Gelen("a@firma.test", "Yeni Ad", "Yeni Görev")]);

        Assert.Empty(sonuc.Added);
        Assert.Equal("Yeni Ad", kayit.FullName);
        Assert.Equal("Yeni Görev", kayit.Role);
    }

    // ── Elle tanımlanmış alıcılar ───────────────────────────────────────────

    [Fact(DisplayName = "AU8. ELLE tanımlı alıcı, ERP listesinde yok diye pasifleşmez")]
    public void Elle_tanimli_alici_korunur()
    {
        // ERP'sinde bu modül olmayan firmalar için bilinçli girilmiştir; ERP'nin
        // sessizliği onları silmek için gerekçe değildir.
        var elle = Mevcut("elle@firma.test", RecipientSource.Manual);

        var sonuc = Uzlastir([elle], [Gelen("baska@firma.test")]);

        Assert.Equal(0, sonuc.Deactivated);
        Assert.True(elle.IsActive);
    }

    [Fact(DisplayName = "AU9. Elle tanımlı adres ERP'den de gelirse ERP'ye DEVREDİLİR")]
    public void Elle_tanimli_erpye_devredilir()
    {
        // Tek doğruluk kaynağı kalır: ERP'de silindiğinde burada da pasifleşir.
        var elle = Mevcut("ortak@firma.test", RecipientSource.Manual);

        Uzlastir([elle], [Gelen("ortak@firma.test")]);

        Assert.Equal(RecipientSource.ErpPull, elle.Source);
    }

    // ── Bozuk veri ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "AU10. Geçersiz adres ATLANIR, tur düşmez")]
    public void Gecersiz_adres_atlanir()
    {
        var sonuc = Uzlastir([], [
            Gelen("bozuk-adres"),
            Gelen("gecerli@firma.test"),
        ]);

        Assert.Single(sonuc.Added);
        Assert.Equal("gecerli@firma.test", sonuc.Added[0].Email);
        Assert.Contains("bozuk-adres", sonuc.Invalid);
    }

    [Fact(DisplayName = "AU11. Aynı adres iki kez gelirse TEK kayıt açılır")]
    public void Mukerrer_satir_tek_kayit()
    {
        // Kopya kayıt, o kişinin her bildirimi iki kez almasına yol açardı.
        var sonuc = Uzlastir([], [
            Gelen("ayni@firma.test"),
            Gelen("AYNI@firma.test"),
        ]);

        Assert.Single(sonuc.Added);
    }

    [Fact(DisplayName = "AU12. Büyük/küçük harf farkı AYNI kişidir")]
    public void Buyuk_kucuk_harf_ayni_kisi()
    {
        var kayit = Mevcut("ayse@firma.test");

        var sonuc = Uzlastir([kayit], [Gelen("Ayse@Firma.Test")]);

        Assert.Empty(sonuc.Added);
        Assert.Equal(0, sonuc.Deactivated);
        Assert.True(kayit.IsActive);
    }

    // ── Alan modeli ─────────────────────────────────────────────────────────

    [Theory(DisplayName = "AU13. Geçersiz adresler reddedilir")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("adres")]
    [InlineData("@firma.test")]
    [InlineData("ad@")]
    [InlineData("ad@firma")]
    [InlineData("ad@.test")]
    [InlineData("ad@firma.")]
    [InlineData("ad soyad@firma.test")]
    [InlineData("iki@adres@firma.test")]
    public void Gecersiz_adres_reddedilir(string adres)
    {
        Assert.False(NotificationRecipient.IsValidEmail(adres));

        Assert.Throws<DomainException>(() => new NotificationRecipient(
            TenantId, CompanyId, adres, null, null, RecipientSource.Manual));
    }

    [Fact(DisplayName = "AU14. Adres normalleştirilerek saklanır")]
    public void Adres_normallestirilir()
    {
        var kayit = new NotificationRecipient(
            TenantId, CompanyId, "  Ayse.Yilmaz@Firma.TEST  ", null, null, RecipientSource.Manual);

        Assert.Equal("ayse.yilmaz@firma.test", kayit.Email);
    }

    [Fact(DisplayName = "AU15. Alıcının GOVAI hesabı olmak zorunda değildir")]
    public void Hesap_gerekmez()
    {
        // Kayıtta kullanıcı kimliği alanı YOKTUR; olsaydı e-posta almak için herkese
        // GOVAI hesabı açmak gerekirdi.
        var kayit = new NotificationRecipient(
            TenantId, CompanyId, "dis@firma.test", "Dış Kişi", null, RecipientSource.ErpPull);

        Assert.DoesNotContain(
            typeof(NotificationRecipient).GetProperties(),
            p => p.Name.Contains("User", StringComparison.Ordinal));

        Assert.Equal("dis@firma.test", kayit.Email);
    }

    [Fact(DisplayName = "AU16. Firma kimliği zorunludur")]
    public void Firma_zorunludur()
    {
        Assert.Throws<DomainException>(() => new NotificationRecipient(
            TenantId, Guid.Empty, "a@firma.test", null, null, RecipientSource.Manual));
    }
}
