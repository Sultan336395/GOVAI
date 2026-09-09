using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Application.Sources;
using GovAI.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovAI.Application.Tests;

/// <summary>
/// Bekleyen katalog onarımlarının davranış sözleşmesi (Faz 3 — kritik eksik 4).
///
/// İki grup kayıt onarılıyor: KOSGEB'in iki çöp kaydı (liste sayfası ve yürürlükten
/// kaldırılmış destekler) ve SGK'nın beş taslağı (sağlık/SUT/ilaç, gayrimenkul ve iki
/// menü başlığı).
///
/// Üç garanti sabitleniyor:
/// 1. Hiçbir kayıt silinmez; çöp kayıt karantinaya alınır, kanıtı korunur.
/// 2. İşlem idempotenttir: ikinci koşu ilave değişiklik üretmez.
/// 3. Bağlı değerlendirmeler silinmez, yeniden değerlendirmeye düşer.
/// </summary>
public class CatalogRepairServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static (CatalogRepairService Service, FakeCatalogRepairRepository Repo) Build(
        params CatalogRepairCandidate[] kayitlar) => BuildFull(kayitlar).Kisa;

    private static (
        (CatalogRepairService Service, FakeCatalogRepairRepository Repo) Kisa,
        FakeMaintenanceRunRepository Runs,
        FakeEventPublisher Events) BuildFull(params CatalogRepairCandidate[] kayitlar)
    {
        var repo = new FakeCatalogRepairRepository(kayitlar);
        var runs = new FakeMaintenanceRunRepository();
        var events = new FakeEventPublisher();

        var service = new CatalogRepairService(
            repo, runs, new FakeUnitOfWork(), new FixedClock(Now),
            new FakeCurrentUser(), events, NullLogger<CatalogRepairService>.Instance);

        return ((service, repo), runs, events);
    }

    /// <summary>
    /// Planı alır ve <b>onun özetiyle</b> uygular. Gerçek akış budur: onay, görülen
    /// planın parmak izidir. Testlerin sabit bir dizeyle geçmesi, kapının kapalı
    /// olduğunu değil kapının olmadığını doğrulardı.
    /// </summary>
    private static async Task<CatalogRepairReport> UygulaAsync(CatalogRepairService service)
    {
        var plan = await service.PlanAsync();

        return await service.ApplyAsync(plan.PlanHash);
    }

    private static CatalogRepairCandidate KosgebListe(bool karantinada = false) => new(
        Guid.CreateVersion7(), CatalogRepairTarget.Opportunity,
        "KOSGEB Destekler", "https://www.kosgeb.gov.tr/site/tr/genel/destekler",
        karantinada, DocumentTitle: null, AssessmentCount: 7);

    private static CatalogRepairCandidate KosgebYururlukten(bool karantinada = false) => new(
        Guid.CreateVersion7(), CatalogRepairTarget.Opportunity,
        "Yürürlükten Kaldırılan Destekler", "https://www.kosgeb.gov.tr/site/tr/genel/destekler/yururlukten",
        karantinada, DocumentTitle: null, AssessmentCount: 4);

    private static CatalogRepairCandidate SgkSut(bool karantinada = false) => new(
        Guid.CreateVersion7(), CatalogRepairTarget.RegulatoryChange,
        "29/08/2026 SUT Değişiklik Tebliği İşlenmiş Güncel 2013 SUT",
        "https://www.sgk.gov.tr/duyuru/detay/1", karantinada, DocumentTitle: null, AssessmentCount: 0);

    private static CatalogRepairCandidate SgkIlac() => new(
        Guid.CreateVersion7(), CatalogRepairTarget.RegulatoryChange,
        "Bedeli Ödenecek İlaçlar Listesinde Yapılan Düzenlemeler Hakkında Duyuru",
        "https://www.sgk.gov.tr/duyuru/detay/2", false, null, 0);

    private static CatalogRepairCandidate SgkGayrimenkul() => new(
        Guid.CreateVersion7(), CatalogRepairTarget.RegulatoryChange,
        "Gayrimenkul Satış İlanı İNŞAAT VE EMLAK DAİRE BAŞKANLIĞI",
        "https://www.sgk.gov.tr/duyuru/detay/3", false, null, 0);

    private static CatalogRepairCandidate SgkMenuBasligi(string? belgeBasligi) => new(
        Guid.CreateVersion7(), CatalogRepairTarget.RegulatoryChange,
        "ÇALIŞAN VE İŞVEREN", "https://www.sgk.gov.tr/duyuru/detay/4",
        false, belgeBasligi, 0);

    [Fact(DisplayName = "OR1. KOSGEB liste sayfası karantinaya alınır, silinmez")]
    public async Task Kosgeb_liste_karantinaya_alinir()
    {
        var (service, repo) = Build(KosgebListe());

        var plan = await service.PlanAsync();
        var eslesme = Assert.Single(plan.Matches);

        Assert.Equal("KOSGEB-LISTE", eslesme.StepCode);
        Assert.Equal(CatalogRepairAction.Quarantine, eslesme.Action);
        Assert.True(eslesme.WillChange);

        await UygulaAsync(service);

        var uygulanan = Assert.Single(repo.Quarantined);
        Assert.Equal(QuarantineReason.InvalidSourcePage, uygulanan.Reason);
        Assert.Equal(CatalogRepairPlan.KosgebNote, uygulanan.Note);
        Assert.Empty(repo.Deleted);
    }

    [Fact(DisplayName = "OR2. Karantina notu kaydın neden çıkarıldığını söyler")]
    public async Task Karantina_notu_aciklayici()
    {
        var (service, repo) = Build(KosgebYururlukten());

        await UygulaAsync(service);

        Assert.Contains("aktif fırsat değildir", repo.Quarantined.Single().Note);
    }

    [Fact(DisplayName = "OR3. İkinci koşu ilave değişiklik üretmez (idempotent)")]
    public async Task Ikinci_kosu_degisiklik_uretmez()
    {
        var (service, repo) = Build(KosgebListe(), KosgebYururlukten());

        var ilk = await UygulaAsync(service);
        Assert.Equal(2, ilk.Changed);

        // İkinci koşuda kayıtlar artık karantinada.
        repo.MarkAllQuarantined();

        var ikinci = await UygulaAsync(service);

        Assert.Equal(0, ikinci.Changed);
        Assert.Equal(2, ikinci.AlreadyDone);
        Assert.Equal(2, repo.Quarantined.Count);
    }

    [Fact(DisplayName = "OR4. Zaten karantinadaki kayıt planda 'değişmeyecek' görünür")]
    public async Task Karantinadaki_kayit_atlanir()
    {
        var (service, _) = Build(KosgebListe(karantinada: true));

        var plan = await service.PlanAsync();
        var eslesme = Assert.Single(plan.Matches);

        Assert.False(eslesme.WillChange);
        Assert.Contains("zaten karantinada", eslesme.SkipReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, plan.WillChangeCount);
    }

    [Fact(DisplayName = "OR5. Kuru çalıştırma hiçbir kayda dokunmaz")]
    public async Task Kuru_calistirma_yazmaz()
    {
        var (service, repo) = Build(KosgebListe(), SgkSut(), SgkGayrimenkul());

        await service.PlanAsync();

        Assert.Empty(repo.Quarantined);
        Assert.Empty(repo.Retitled);
    }

    [Fact(DisplayName = "OR6. Bağlı değerlendirmeler silinmez, yeniden değerlendirmeye düşer")]
    public async Task Degerlendirmeler_silinmez()
    {
        var (service, repo) = Build(KosgebListe());

        var plan = await service.PlanAsync();
        Assert.Equal(7, plan.Matches.Single().AffectedAssessmentCount);

        var rapor = await UygulaAsync(service);

        Assert.Equal(7, rapor.Outcomes.Single().AffectedAssessmentCount);
        Assert.Empty(repo.Deleted);
    }

    [Fact(DisplayName = "OR7. Sağlık/SUT kaydı işveren mevzuatı olarak yayımlanmaz")]
    public async Task Saglik_kaydi_karantinaya_alinir()
    {
        var (service, repo) = Build(SgkSut());

        await UygulaAsync(service);

        var uygulanan = Assert.Single(repo.Quarantined);
        Assert.Equal(QuarantineReason.NeedsManualReview, uygulanan.Reason);
        Assert.Contains("işveren mevzuatı değildir", uygulanan.Note);
    }

    [Fact(DisplayName = "OR8. İlaç ve gayrimenkul kayıtları da karantinaya alınır")]
    public async Task Ilac_ve_gayrimenkul_karantinaya_alinir()
    {
        var (service, repo) = Build(SgkIlac(), SgkGayrimenkul());

        var rapor = await UygulaAsync(service);

        Assert.Equal(2, rapor.Changed);
        Assert.Equal(2, repo.Quarantined.Count);
    }

    [Fact(DisplayName = "OR9. Menü başlığı belge sürümündeki gerçek başlıkla düzeltilir")]
    public async Task Menu_basligi_duzeltilir()
    {
        var gercek = "2026/Nisan Ayı Muhtasar ve Prim Hizmet Beyannamelerinin Verilme Süresinin Uzatılması";
        var (service, repo) = Build(SgkMenuBasligi(gercek));

        var plan = await service.PlanAsync();
        Assert.Equal(gercek, plan.Matches.Single().ProposedTitle);

        await UygulaAsync(service);

        Assert.Equal(gercek, repo.Retitled.Single().Title);
    }

    [Fact(DisplayName = "OR10. Belge sürümünde başlık yoksa uydurma başlık yazılmaz")]
    public async Task Baslik_uydurulmaz()
    {
        var (service, repo) = Build(SgkMenuBasligi(belgeBasligi: null));

        var plan = await service.PlanAsync();

        Assert.False(plan.Matches.Single().WillChange);
        Assert.Contains("uydurma başlık yazılmaz", plan.Matches.Single().SkipReason);

        await UygulaAsync(service);
        Assert.Empty(repo.Retitled);
    }

    [Fact(DisplayName = "OR11. Başlık zaten doğruysa dokunulmaz")]
    public async Task Dogru_baslik_degistirilmez()
    {
        var (service, repo) = Build(new CatalogRepairCandidate(
            Guid.CreateVersion7(), CatalogRepairTarget.RegulatoryChange,
            "ÇALIŞAN VE İŞVEREN", "https://www.sgk.gov.tr/duyuru/detay/9",
            false, "ÇALIŞAN VE İŞVEREN", 0));

        await UygulaAsync(service);

        Assert.Empty(repo.Retitled);
    }

    [Fact(DisplayName = "OR12. Türkçe büyük harf farkı eşleşmeyi bozmaz")]
    public async Task Turkce_katlama_eslesmeyi_bozmaz()
    {
        // "YÜRÜRLÜKTEN KALDIRILAN" ile "Yürürlükten Kaldırılan" aynı kayıttır;
        // ToUpperInvariant bunları eşleştirmez (noktasız ı).
        var (service, _) = Build(new CatalogRepairCandidate(
            Guid.CreateVersion7(), CatalogRepairTarget.Opportunity,
            "YÜRÜRLÜKTEN KALDIRILAN DESTEKLER",
            "https://www.KOSGEB.gov.tr/site/tr/genel/destekler", false, null, 0));

        var plan = await service.PlanAsync();

        Assert.Contains(plan.Matches, m => m.StepCode == "KOSGEB-YURURLUKTEN-KALDIRILAN");
    }

    [Fact(DisplayName = "OR13. İlgisiz kayıt hiçbir adımla eşleşmez")]
    public async Task Ilgisiz_kayit_eslesmez()
    {
        var (service, repo) = Build(new CatalogRepairCandidate(
            Guid.CreateVersion7(), CatalogRepairTarget.Opportunity,
            "KOBİ Dijital Dönüşüm Destek Programı",
            "https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/1234", false, null, 3));

        var plan = await service.PlanAsync();

        Assert.Empty(plan.Matches);
        await UygulaAsync(service);
        Assert.Empty(repo.Quarantined);
    }

    [Fact(DisplayName = "OR14. Plan adımlarının tamamı gerekçelidir")]
    public void Adimlar_gerekceli()
    {
        Assert.All(CatalogRepairPlan.Steps, adim =>
        {
            Assert.False(string.IsNullOrWhiteSpace(adim.Code));
            Assert.False(string.IsNullOrWhiteSpace(adim.Rationale));

            if (adim.Action == CatalogRepairAction.Quarantine)
            {
                Assert.NotEqual(QuarantineReason.None, adim.Reason);
                Assert.False(string.IsNullOrWhiteSpace(adim.Note));
            }
        });
    }

    // ── Onay kapısı ve geri alma (Faz 3) ──────────────────────────────────

    [Fact(DisplayName = "KO15. Onay özeti olmadan hiçbir kayda dokunulmaz")]
    public async Task Onaysiz_uygulanmaz()
    {
        var (service, repo) = Build(KosgebListe());

        await Assert.ThrowsAsync<ValidationException>(() => service.ApplyAsync(string.Empty));

        Assert.Empty(repo.Quarantined);
    }

    [Fact(DisplayName = "KO16. Görülmemiş plan özeti reddedilir")]
    public async Task Uydurma_ozet_reddedilir()
    {
        var (service, repo) = Build(KosgebListe());

        await Assert.ThrowsAsync<ValidationException>(() => service.ApplyAsync(new string('a', 64)));

        Assert.Empty(repo.Quarantined);
    }

    [Fact(DisplayName = "KO17. Plan gösterildikten sonra veri değişirse uygulama reddedilir")]
    public async Task Bayat_plan_reddedilir()
    {
        var (service, repo) = Build(KosgebListe(), KosgebYururlukten());

        var plan = await service.PlanAsync();

        // Aradan başka bir işlem geçti: kayıtlar zaten karantinaya alınmış.
        repo.MarkAllQuarantined();

        await Assert.ThrowsAsync<ValidationException>(() => service.ApplyAsync(plan.PlanHash));

        Assert.Empty(repo.Quarantined);
    }

    [Fact(DisplayName = "KO18. Uygulanan onarım geri alınabilir")]
    public async Task Onarim_geri_alinabilir()
    {
        var ((service, repo), runs, _) = BuildFull(KosgebListe(), KosgebYururlukten());

        var rapor = await UygulaAsync(service);

        Assert.Equal(2, rapor.Changed);
        Assert.NotNull(rapor.RunId);

        var geri = await service.UndoAsync(rapor.RunId!.Value);

        Assert.Equal(2, geri.Changed);
        Assert.Equal(2, repo.Released.Count);
        Assert.NotNull(runs.Runs.Single().UndoneAt);

        // Kayıt SİLİNMEDİ; çalıştırma kaydı da duruyor.
        Assert.Empty(repo.Deleted);
        Assert.Single(runs.Runs);
    }

    [Fact(DisplayName = "KO22. Geri alınan fırsat için yeniden puanlama tetiklenir")]
    public async Task Geri_alinan_firsat_puanlanir()
    {
        var ((service, _), _, events) = BuildFull(KosgebListe());

        var rapor = await UygulaAsync(service);
        await service.UndoAsync(rapor.RunId!.Value);

        // Kayıt katalogda görünür ama puanlanmazsa firmaların eşleşme listesine
        // gece turuna kadar düşmez; danışman kaydı gün boyu göremez.
        var (kuyruk, mesaj) = Assert.Single(events.Published);

        Assert.Equal(QueueNames.ScoringRequested, kuyruk);

        var neden = mesaj.GetType().GetProperty("Reason")?.GetValue(mesaj) as string;
        Assert.Equal("CatalogRepairUndone", neden);
    }

    [Fact(DisplayName = "KO23. Mevzuat kaydının geri alınması puanlama tetiklemez")]
    public async Task Mevzuat_geri_alinirken_puanlama_yok()
    {
        // Mevzuata başvurulmaz ve skorlanmaz; boşuna toplu tur başlatmak maliyet üretir.
        var ((service, _), _, events) = BuildFull(SgkSut());

        var rapor = await UygulaAsync(service);
        await service.UndoAsync(rapor.RunId!.Value);

        Assert.Empty(events.Published);
    }

    [Fact(DisplayName = "KO19. Aynı çalıştırma iki kez geri alınamaz")]
    public async Task Ikinci_geri_alma_reddedilir()
    {
        var (service, _) = Build(KosgebListe());

        var rapor = await UygulaAsync(service);
        await service.UndoAsync(rapor.RunId!.Value);

        await Assert.ThrowsAsync<ValidationException>(() => service.UndoAsync(rapor.RunId!.Value));
    }

    [Fact(DisplayName = "KO20. Başlık düzeltmesinin eski hâli saklanır ve geri yazılır")]
    public async Task Baslik_geri_yazilir()
    {
        var (service, repo) = Build(SgkMenuBasligi("2026 Yılı Prim Teşviki Duyurusu"));

        var rapor = await UygulaAsync(service);
        var uygulanan = rapor.Outcomes.Single(o => o.Result == "Uygulandı.");

        // Eski başlık kayıtta artık yok; geri alma ancak saklandıysa mümkün.
        Assert.False(string.IsNullOrWhiteSpace(uygulanan.PreviousTitle));

        await service.UndoAsync(rapor.RunId!.Value);

        Assert.Equal(uygulanan.PreviousTitle, repo.Retitled[^1].Title);
    }

    [Fact(DisplayName = "KO21. Hiçbir şey değişmediyse çalıştırma kaydı açılmaz")]
    public async Task Degisiklik_yoksa_kayit_acilmaz()
    {
        var ((service, _), runs, _) = BuildFull(KosgebListe(karantinada: true));

        var rapor = await UygulaAsync(service);

        Assert.Equal(0, rapor.Changed);
        Assert.Null(rapor.RunId);
        Assert.Empty(runs.Runs);
    }
}

/// <summary>Bellek içi onarım deposu; silme yolu KASTEN yoktur.</summary>
internal sealed class FakeCatalogRepairRepository(IReadOnlyList<CatalogRepairCandidate> kayitlar)
    : ICatalogRepairRepository
{
    private List<CatalogRepairCandidate> _kayitlar = [.. kayitlar];

    public List<(Guid Id, QuarantineReason Reason, string? Note)> Quarantined { get; } = [];

    public List<(Guid Id, string Title)> Retitled { get; } = [];

    /// <summary>Hiçbir zaman dolmamalı: onarım kayıt silmez.</summary>
    public List<Guid> Deleted { get; } = [];

    public void MarkAllQuarantined() =>
        _kayitlar = [.. _kayitlar.Select(k => k with { IsQuarantined = true })];

    public Task<IReadOnlyList<CatalogRepairCandidate>> ListRepairCandidatesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CatalogRepairCandidate>>(_kayitlar);

    public Task<int> QuarantineAsync(
        CatalogRepairTarget target,
        Guid recordId,
        QuarantineReason reason,
        string? note,
        CancellationToken cancellationToken = default)
    {
        Quarantined.Add((recordId, reason, note));

        var adet = _kayitlar.FirstOrDefault(k => k.Id == recordId)?.AssessmentCount ?? 0;
        _kayitlar = [.. _kayitlar.Select(k => k.Id == recordId ? k with { IsQuarantined = true } : k)];

        return Task.FromResult(adet);
    }

    /// <summary>Geri alma: karantinadan çıkarılan kayıtlar.</summary>
    public List<Guid> Released { get; } = [];

    public Task<int> ReleaseAsync(
        CatalogRepairTarget target,
        Guid recordId,
        CancellationToken cancellationToken = default)
    {
        var kayit = _kayitlar.FirstOrDefault(k => k.Id == recordId);

        // Karantinada değilse geri alma dokunmaz.
        if (kayit is null || !kayit.IsQuarantined)
        {
            return Task.FromResult(0);
        }

        Released.Add(recordId);
        _kayitlar = [.. _kayitlar.Select(k => k.Id == recordId ? k with { IsQuarantined = false } : k)];

        return Task.FromResult(1);
    }

    public Task<int> RetitleAsync(
        CatalogRepairTarget target,
        Guid recordId,
        string title,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        Retitled.Add((recordId, title));
        return Task.FromResult(1);
    }
}
