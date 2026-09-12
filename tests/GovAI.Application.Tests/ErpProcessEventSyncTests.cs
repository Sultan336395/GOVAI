using GovAI.Application.Integrations;
using GovAI.Domain.Common;
using GovAI.Domain.Integrations;

namespace GovAI.Application.Tests;

/// <summary>
/// ERP süreç olay günlüğünün uzlaştırılması.
///
/// <para>
/// Bu günlük nedensel ikizin (Regulatory Causal Twin) ham maddesidir ve değeri
/// <b>bütünlüğünden</b> gelir: mükerrer satır bir faaliyeti olduğundan sık gösterir,
/// kayıp satır bir adımı hiç olmamış sayar. İkisi de ölçümü sessizce bozar, çünkü
/// hiçbir hata mesajı üretmezler.
/// </para>
/// </summary>
public class ErpProcessEventSyncTests
{
    private static readonly Guid Kiraci = Guid.CreateVersion7();
    private static readonly Guid Firma = Guid.CreateVersion7();
    private static readonly DateTimeOffset Zaman = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static ProcessEventCandidate Aday(
        string? vaka = "SIP-1",
        string? faaliyet = "Teklif onaylandı",
        DateTimeOffset? zaman = null,
        string? kaynak = null,
        string? birim = null,
        string? erpKimlik = null) =>
        new()
        {
            CaseId = vaka,
            Activity = faaliyet,
            OccurredAt = zaman ?? Zaman,
            Resource = kaynak,
            Department = birim,
            ExternalId = erpKimlik,
        };

    private static ProcessEventSyncResult Uzlastir(
        IReadOnlyList<ProcessEventCandidate>? gelen, params string[] mevcutAnahtarlar) =>
        ErpProcessEventSync.Reconcile(
            Kiraci, Firma, gelen, new HashSet<string>(mevcutAnahtarlar, StringComparer.Ordinal));

    // ── Bölüm yokluğu ≠ boş liste ───────────────────────────────────────────

    [Fact(DisplayName = "SO1. Bölüm yanıtta YOKSA hiçbir şey yapılmaz")]
    public void Bolum_yoksa_islem_yok()
    {
        // Bölüm yokluğunu "olay olmadı" saymak, ERP arızasını gerçek veri gibi
        // göstermek olurdu (§2.2.8'deki alıcı kuralıyla aynı gerekçe).
        var sonuc = Uzlastir(null);

        Assert.True(sonuc.SectionAbsent);
        Assert.Empty(sonuc.ToInsert);
    }

    [Fact(DisplayName = "SO2. BOŞ liste, bölümün yokluğundan farklıdır")]
    public void Bos_liste_farklidir()
    {
        // Boş liste "bu dönemde olay olmadı" demektir ve geçerli bir cevaptır.
        var sonuc = Uzlastir([]);

        Assert.False(sonuc.SectionAbsent);
        Assert.Empty(sonuc.ToInsert);
    }

    // ── Mükerrer engeli ─────────────────────────────────────────────────────

    [Fact(DisplayName = "SO3. Daha önce kaydedilmiş olay TEKRAR EKLENMEZ")]
    public void Mevcut_olay_eklenmez()
    {
        // Her tur günlüğü yeniden yazsaydı bir faaliyet her turda bir kez daha
        // olmuş görünür ve süreç sıklıkları tamamen yanlış çıkardı.
        var sonuc = Uzlastir([Aday(erpKimlik: "E-100")], "id:E-100");

        Assert.Empty(sonuc.ToInsert);
        Assert.Equal(1, sonuc.Duplicates);
    }

    [Fact(DisplayName = "SO4. AYNI TURDA iki kez geçen olay bir kez eklenir")]
    public void Ayni_turda_mukerrer()
    {
        // Veritabanı kontrolü tek başına yetmez: aynı yanıtta tekrar eden olay
        // henüz kayıtlı değildir ve iki satır olurdu.
        var sonuc = Uzlastir([Aday(erpKimlik: "E-200"), Aday(erpKimlik: "E-200")]);

        Assert.Single(sonuc.ToInsert);
        Assert.Equal(1, sonuc.Duplicates);
    }

    [Fact(DisplayName = "SO5. ERP kimliği yoksa vaka+faaliyet+zaman ile tekilleştirilir")]
    public void Kimliksiz_uclu_ile_tekillesir()
    {
        var sonuc = Uzlastir([Aday(), Aday()]);

        Assert.Single(sonuc.ToInsert);
    }

    [Fact(DisplayName = "SO6. Aynı vakada FARKLI faaliyet ayrı olaydır")]
    public void Farkli_faaliyet_ayri()
    {
        // Bir süreç tam olarak budur: aynı vakanın ardışık adımları.
        var sonuc = Uzlastir(
        [
            Aday(faaliyet: "Teklif oluşturuldu"),
            Aday(faaliyet: "Teklif onaylandı", zaman: Zaman.AddHours(2)),
        ]);

        Assert.Equal(2, sonuc.ToInsert.Count);
    }

    [Fact(DisplayName = "SO7. Aynı faaliyet FARKLI zamanda ayrı olaydır")]
    public void Farkli_zaman_ayri()
    {
        var sonuc = Uzlastir([Aday(), Aday(zaman: Zaman.AddMinutes(30))]);

        Assert.Equal(2, sonuc.ToInsert.Count);
    }

    // ── Eksik alan ──────────────────────────────────────────────────────────

    [Theory(DisplayName = "SO8. Zorunlu üçlüsü eksik olan olay ALINMAZ ve SAYILIR")]
    [InlineData(null, "Faaliyet", true)]
    [InlineData("SIP-1", null, true)]
    [InlineData("SIP-1", "Faaliyet", false)]
    public void Eksik_zorunlu_alan(string? vaka, string? faaliyet, bool zamanVar)
    {
        // Sessizce atlansaydı günlük eksik olur ve kimse fark etmezdi; sayılması
        // eşlemenin yanlış olduğunu görünür kılar.
        //
        // Aday burada doğrudan kurulur: yardımcı metot verilmeyen zamanı varsayılana
        // çevirdiği için "zaman yok" durumu onun üzerinden kurgulanamaz.
        var aday = new ProcessEventCandidate
        {
            CaseId = vaka,
            Activity = faaliyet,
            OccurredAt = zamanVar ? Zaman : null,
        };

        var sonuc = Uzlastir([aday]);

        Assert.Empty(sonuc.ToInsert);
        Assert.Equal(1, sonuc.Invalid);
    }

    [Fact(DisplayName = "SO9. Geçersiz olay, geçerli olanları engellemez")]
    public void Gecersiz_digerlerini_engellemez()
    {
        // Tek bozuk satır yüzünden turu düşürmek, bir eşleme hatasında bütün
        // günlüğü kaybettirirdi.
        var sonuc = Uzlastir([Aday(vaka: null), Aday(erpKimlik: "E-1")]);

        Assert.Single(sonuc.ToInsert);
        Assert.Equal(1, sonuc.Invalid);
    }

    // ── Sınır ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "SO10. Tur başına azami olay sınırı uygulanır")]
    public void Tur_siniri()
    {
        // Sınırsız çekme gece turunu saatlerce sürdürür ve belleği şişirir.
        var cok = Enumerable.Range(0, ErpProcessEventSync.MaximumPerRun + 250)
            .Select(i => Aday(erpKimlik: $"E-{i}"))
            .ToList();

        var sonuc = Uzlastir(cok);

        Assert.Equal(ErpProcessEventSync.MaximumPerRun, sonuc.ToInsert.Count);
    }

    // ── Alan modeli ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "SO11. Olay firma ve kiracıya bağlanır")]
    public void Kiraci_ve_firma_baglanir()
    {
        var olay = Assert.Single(Uzlastir([Aday(erpKimlik: "E-9")]).ToInsert);

        Assert.Equal(Kiraci, olay.TenantId);
        Assert.Equal(Firma, olay.CompanyId);
    }

    [Fact(DisplayName = "SO12. Birim ve kaynak taşınır")]
    public void Birim_ve_kaynak_tasinir()
    {
        // Birim olmadan "mevzuat hangi departmanı vuracak" sorusu cevaplanamaz.
        var olay = Assert.Single(
            Uzlastir([Aday(kaynak: "PRS-4471", birim: "Satın Alma", erpKimlik: "E-10")]).ToInsert);

        Assert.Equal("PRS-4471", olay.Resource);
        Assert.Equal("Satın Alma", olay.Department);
    }

    [Fact(DisplayName = "SO13. Zorunlu alanı olmayan olay alan modelinde ÜRETİLEMEZ")]
    public void Alan_modeli_de_korur()
    {
        // İkinci savunma hattı: uzlaştırıcı atlansa bile geçersiz olay kaydedilemez.
        Assert.Throws<DomainException>(() =>
            new ErpProcessEvent(Kiraci, Firma, "   ", "Faaliyet", Zaman));

        Assert.Throws<DomainException>(() =>
            new ErpProcessEvent(Kiraci, Firma, "SIP-1", "  ", Zaman));
    }

    [Fact(DisplayName = "SO14. ERP kimliği olan ve olmayan olayın anahtarı FARKLI biçimdedir")]
    public void Anahtar_bicimi()
    {
        // İki biçim karışsaydı, kimliği olmayan bir olay kimliği olan başka bir
        // olayla çakışabilirdi.
        var kimlikli = new ErpProcessEvent(Kiraci, Firma, "SIP-1", "Onay", Zaman, externalId: "E-1");
        var kimliksiz = new ErpProcessEvent(Kiraci, Firma, "SIP-1", "Onay", Zaman);

        Assert.StartsWith("id:", kimlikli.DeduplicationKey, StringComparison.Ordinal);
        Assert.StartsWith("vaka:", kimliksiz.DeduplicationKey, StringComparison.Ordinal);
    }

    // ── Eşleme ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "SO15. Zorunlu alanları tanımlanmamış eşleme günlük çekemez")]
    public void Eksik_esleme_cekmez()
    {
        // Üç zorunlu alandan biri bile eksikse çekmeye kalkışmak, boş ya da bozuk
        // günlük üretirdi.
        var eksik = new ErpFieldMap { ProcessEvents = "olaylar", EventCaseIdField = "vakaNo" };
        var tam = eksik with { EventActivityField = "faaliyet", EventTimestampField = "zaman" };

        Assert.False(eksik.SupportsProcessEvents);
        Assert.True(tam.SupportsProcessEvents);
    }

    [Fact(DisplayName = "SO16. Bölüm tanımlı değilse eşleme günlük istemez")]
    public void Bolum_tanimsizsa_istemez()
    {
        // Çoğu ERP kurulumunda süreç günlüğü dışarı açılmaz; varsayılan olarak
        // istememek doğru davranıştır.
        Assert.False(new ErpFieldMap().SupportsProcessEvents);
    }
}
