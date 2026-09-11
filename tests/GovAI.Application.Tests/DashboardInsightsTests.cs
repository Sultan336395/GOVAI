using GovAI.Application.Dashboard;
using GovAI.Domain.Assessments;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Integrations;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Reporting;
using GovAI.Domain.Scoring;
using GovAI.Domain.Tenders;

namespace GovAI.Application.Tests;

/// <summary>
/// Dashboard'un dört ek bölümü.
///
/// <para>
/// Bölümler ekranın <b>karar aldığı</b> yerdir. Üç şeyi birden tutmalıdır:
/// deterministik olmak, uydurmamak (veri yoksa sebebini yazmak) ve kullanıcıyı bir
/// yere götürmek (her aksiyonun hedefi vardır).
/// </para>
/// </summary>
public class DashboardInsightsTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 14, 8, 0, 0, TimeSpan.FromHours(3));

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SourceId = Guid.NewGuid();

    // ── Kurulum yardımcıları ────────────────────────────────────────────────

    private static Company Firma(bool doluProfil = true)
    {
        var company = new Company(TenantId, "Test A.Ş.", "1234567890", LegalType.LimitedCompany);

        if (!doluProfil)
        {
            return company;
        }

        company.UpdateIdentity("Test A.Ş.", LegalType.LimitedCompany, new DateOnly(2015, 1, 1));
        company.UpdateWorkforce(new Workforce(
            employeeCount: 40,
            womenEmployeeCount: 18,
            youngEmployeeCount: 9,
            rAndDEmployeeCount: 6,
            disabledEmployeeCount: 1,
            youngEmployeeMaxAge: 29));

        company.UpdateFinancials(new Financials(
            annualRevenue: 24_000_000m,
            balanceSize: 18_000_000m,
            equity: 7_000_000m,
            exportRevenue: 4_000_000m,
            currency: "TRY",
            fiscalYear: 2025));

        return company;
    }

    private static Opportunity Cagri(string baslik, DateTimeOffset? sonBasvuru) =>
        Cagri(baslik, sonBasvuru, SupportCategory.Grant);

    private static Opportunity Cagri(string baslik, DateTimeOffset? sonBasvuru, SupportCategory kategori)
    {
        var cagri = new Opportunity(
            SourceId, SourceType.KosgebOrSimilar, kategori, baslik, "KOSGEB", AsOf.AddDays(-20));

        if (sonBasvuru is not null)
        {
            cagri.SetSchedule(AsOf.AddDays(-20), sonBasvuru);
        }

        return cagri;
    }

    private static EligibilityAssessment Degerlendirme(
        Guid companyId,
        Guid opportunityId,
        EligibilityVerdict karar = EligibilityVerdict.Eligible,
        int eksikBelge = 0,
        int veriBoslugu = 0)
    {
        // Değerlendirme kaydı motorun çıktısından kurulur; sayaçlar oradan türer.
        var sonuc = new EligibilityOutcome
        {
            CompanyId = companyId,
            OpportunityId = opportunityId,
            EvaluatedAt = AsOf.AddDays(-1),
            Verdict = karar,
            SectorFit = SectorFit.Matched,
            RuleEvaluations = Enumerable.Range(0, veriBoslugu)
                .Select(i => new RuleEvaluation
                {
                    RuleId = Guid.NewGuid(),
                    Field = $"Workforce.Alan{i}",
                    Dimension = RuleDimension.Employment,
                    Outcome = RuleOutcome.Unknown,
                    Severity = RuleSeverity.Minor,
                    Requirement = $"Bilinmeyen alan {i}",
                    ActualValue = "bilinmiyor",
                    ExpectedValue = "-",
                    Strength = 0m,
                })
                .ToList(),
            DocumentChecklist = Enumerable.Range(0, eksikBelge)
                .Select(i => new DocumentCheckResult
                {
                    Code = $"BELGE{i}",
                    Name = $"Belge {i}",
                    IsMandatory = true,
                    Status = DocumentStatus.Missing,
                })
                .ToList(),
            Score = new ScoreBreakdown
            {
                Dimensions = [],
                Weights = ScoreWeights.Default,
                FinalScore = 72m,
                HasBlockingFailure = false,
                Confidence = 0.8m,
            },
        };

        return new EligibilityAssessment(TenantId, sonuc, 1, "{}");
    }

    private static TenderPursuit Takip(Guid companyId, Guid opportunityId, string kisi = "u@f.test") =>
        new(TenantId, companyId, opportunityId, AsOf.AddDays(-5), kisi);

    private static DashboardInsightsDto Kur(
        Company? firma = null,
        IReadOnlyList<EligibilityAssessment>? degerlendirmeler = null,
        IReadOnlyList<TenderPursuit>? takipler = null,
        IReadOnlyList<WeeklyReport>? raporlar = null,
        IReadOnlyList<ReportInquiry>? sorular = null,
        ErpConnection? erp = null,
        IReadOnlyDictionary<Guid, Opportunity>? cagrilar = null)
    {
        firma ??= Firma();

        return DashboardInsightsBuilder.Build(
            firma,
            degerlendirmeler ?? [],
            takipler ?? [],
            raporlar ?? [],
            sorular ?? [],
            erp,
            cagrilar ?? new Dictionary<Guid, Opportunity>(),
            AsOf);
    }

    // ── Aksiyon odaklı üst bölüm ────────────────────────────────────────────

    [Fact(DisplayName = "DB1. Yaklaşan son başvuru ACİL aksiyon üretir ve hedefi vardır")]
    public void Yaklasan_son_basvuru_acil()
    {
        var firma = Firma();
        var cagri = Cagri("KOSGEB Ar-Ge", AsOf.AddDays(6));
        var degerlendirme = Degerlendirme(firma.Id, cagri.Id);

        var sonuc = Kur(
            firma,
            [degerlendirme],
            cagrilar: new Dictionary<Guid, Opportunity> { [cagri.Id] = cagri });

        var aksiyon = sonuc.Actions.First();

        Assert.Equal(DashboardActionPriority.Urgent, aksiyon.Priority);
        Assert.Contains("KOSGEB Ar-Ge", aksiyon.Title);
        Assert.Equal(6, aksiyon.DaysRemaining);

        // Götürmeyen bir uyarı kullanıcıyı "bir şey yapmalıyım ama nereden" bırakır.
        Assert.Equal($"/matches/{degerlendirme.Id}", aksiyon.Target);
    }

    [Fact(DisplayName = "DB2. Süresi geçmiş çağrı aksiyon üretmez")]
    public void Gecmis_cagri_aksiyon_uretmez()
    {
        // Başvurulamayacak bir çağrı için iş vermek, kullanıcıyı boşa çalıştırırdı.
        var firma = Firma();
        var cagri = Cagri("Kapanmış Çağrı", AsOf.AddDays(-2));

        var sonuc = Kur(
            firma,
            [Degerlendirme(firma.Id, cagri.Id)],
            cagrilar: new Dictionary<Guid, Opportunity> { [cagri.Id] = cagri });

        Assert.DoesNotContain(sonuc.Actions, a => a.Title.Contains("Kapanmış Çağrı"));
    }

    [Fact(DisplayName = "DB3. Uygun OLMAYAN çağrı için başvuru aksiyonu çıkmaz")]
    public void Uygun_olmayan_cagri_aksiyon_uretmez()
    {
        var firma = Firma();
        var cagri = Cagri("Uymayan Çağrı", AsOf.AddDays(5));

        var sonuc = Kur(
            firma,
            [Degerlendirme(firma.Id, cagri.Id, EligibilityVerdict.NotEligible)],
            cagrilar: new Dictionary<Guid, Opportunity> { [cagri.Id] = cagri });

        Assert.DoesNotContain(
            sonuc.Actions, a => a.Title.StartsWith("Başvuruyu hazırla", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "DB4. Eksik zorunlu belge tarih beklemeden YÜKSEK öncelikli çıkar")]
    public void Eksik_belge_aksiyonu()
    {
        // Belge temini zaman alır; son başvuru yaklaşana kadar beklemek geç kalmaktır.
        var firma = Firma();
        var cagri = Cagri("Belgeli Çağrı", null);

        var sonuc = Kur(
            firma,
            [Degerlendirme(firma.Id, cagri.Id, eksikBelge: 3)],
            cagrilar: new Dictionary<Guid, Opportunity> { [cagri.Id] = cagri });

        var aksiyon = Assert.Single(
            sonuc.Actions, a => a.Title.StartsWith("Eksik belgeleri", StringComparison.Ordinal));

        Assert.Equal(DashboardActionPriority.High, aksiyon.Priority);
        Assert.Contains("3", aksiyon.Reason);
    }

    [Fact(DisplayName = "DB5. Süresi geçtiği hâlde kapatılmamış takip uyarı üretir")]
    public void Kapatilmamis_takip_uyarir()
    {
        var firma = Firma();
        var cagri = Cagri("Geçmiş İhale", AsOf.AddDays(-3), SupportCategory.Tender);

        var sonuc = Kur(
            firma,
            takipler: [Takip(firma.Id, cagri.Id)],
            cagrilar: new Dictionary<Guid, Opportunity> { [cagri.Id] = cagri });

        var aksiyon = Assert.Single(
            sonuc.Actions, a => a.Title.StartsWith("Takibi sonuçlandır", StringComparison.Ordinal));

        Assert.Equal("/tenders", aksiyon.Target);
    }

    [Fact(DisplayName = "DB6. Kapanmış takip için uyarı çıkmaz")]
    public void Kapanmis_takip_uyarmaz()
    {
        var firma = Firma();
        var cagri = Cagri("Sonuçlanmış İhale", AsOf.AddDays(-3), SupportCategory.Tender);
        var takip = Takip(firma.Id, cagri.Id);

        takip.ChangeStatus(
            TenderPursuitStatus.Sonuclandi, TenderOutcome.Kaybedildi, null, AsOf.AddDays(-1), "u@f.test");

        var sonuc = Kur(
            firma,
            takipler: [takip],
            cagrilar: new Dictionary<Guid, Opportunity> { [cagri.Id] = cagri });

        Assert.DoesNotContain(
            sonuc.Actions, a => a.Title.StartsWith("Takibi sonuçlandır", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "DB7. Aksiyonlar önceliğe göre sıralanır")]
    public void Aksiyonlar_onceliğe_gore_sirali()
    {
        var firma = Firma(doluProfil: false);
        var acil = Cagri("Acil Çağrı", AsOf.AddDays(3));
        var belgeli = Cagri("Belgeli Çağrı", null);

        var sonuc = Kur(
            firma,
            [
                Degerlendirme(firma.Id, belgeli.Id, eksikBelge: 2),
                Degerlendirme(firma.Id, acil.Id),
            ],
            cagrilar: new Dictionary<Guid, Opportunity>
            {
                [acil.Id] = acil,
                [belgeli.Id] = belgeli,
            });

        var oncelikler = sonuc.Actions.Select(a => a.Priority).ToList();

        Assert.Equal(oncelikler.OrderBy(p => p).ToList(), oncelikler);
        Assert.Equal(DashboardActionPriority.Urgent, oncelikler.First());
    }

    [Fact(DisplayName = "DB8. Aksiyon listesi sınırlıdır")]
    public void Aksiyon_listesi_sinirli()
    {
        // Uzun liste önceliklendirmeyi yok eder; ekranın işi seçmektir.
        var firma = Firma();
        var cagrilar = new Dictionary<Guid, Opportunity>();
        var degerlendirmeler = new List<EligibilityAssessment>();

        for (var i = 0; i < 20; i++)
        {
            var cagri = Cagri($"Çağrı {i}", AsOf.AddDays(2 + i % 10));
            cagrilar[cagri.Id] = cagri;
            degerlendirmeler.Add(Degerlendirme(firma.Id, cagri.Id, eksikBelge: 1));
        }

        var sonuc = Kur(firma, degerlendirmeler, cagrilar: cagrilar);

        Assert.True(sonuc.Actions.Count <= DashboardInsightsBuilder.MaximumActions);
    }

    // ── Profil tamamlanma göstergesi ────────────────────────────────────────

    [Fact(DisplayName = "DB9. Boş profilde doluluk düşüktür ve eksik alanlar ADIYLA sayılır")]
    public void Bos_profil_eksikleri_listeler()
    {
        var sonuc = Kur(Firma(doluProfil: false));

        Assert.True(sonuc.Profile.Percentage < 60);
        Assert.NotEmpty(sonuc.Profile.MissingLabels);

        // Kullanıcıya sistemin iç terimleri gösterilmez.
        Assert.DoesNotContain(sonuc.Profile.MissingLabels, l => l.Contains('.'));
        Assert.DoesNotContain(sonuc.Profile.MissingLabels, l => l.Contains("Workforce"));
    }

    [Fact(DisplayName = "DB10. Doldurulan alan doluluğu YÜKSELTİR")]
    public void Dolu_profil_daha_yuksek()
    {
        var bos = Kur(Firma(doluProfil: false)).Profile;
        var dolu = Kur(Firma()).Profile;

        Assert.True(dolu.Percentage > bos.Percentage);
        Assert.True(dolu.KnownCount > bos.KnownCount);
        Assert.Equal(bos.TotalCount, dolu.TotalCount);
    }

    [Fact(DisplayName = "DB11. Sertifika eksikliği profil eksiği SAYILMAZ")]
    public void Sertifika_eksigi_sayilmaz()
    {
        // Boş sertifika kümesi "belgemiz yok" demektir; bu geçerli bir cevaptır (ADR-0003).
        var sonuc = Kur(Firma(doluProfil: false));

        Assert.DoesNotContain(
            sonuc.Profile.MissingLabels, l => l.Contains("ertifika", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "DB12. Veri boşluğu değerlendirmelerden TOPLANIR")]
    public void Veri_boslugu_toplanir()
    {
        var firma = Firma();
        var a = Cagri("A", null);
        var b = Cagri("B", null);

        var sonuc = Kur(firma, [
            Degerlendirme(firma.Id, a.Id, veriBoslugu: 4),
            Degerlendirme(firma.Id, b.Id, veriBoslugu: 3),
        ]);

        Assert.Equal(7, sonuc.Profile.DataGapCount);
    }

    // ── Fırsat hunisi ve eğilim ─────────────────────────────────────────────

    [Fact(DisplayName = "DB13. Huni değerlendirme ve takip sayılarını ayrı tutar")]
    public void Huni_sayilari()
    {
        var firma = Firma();
        var a = Cagri("A", null);
        var b = Cagri("B", null);
        var c = Cagri("C", null, SupportCategory.Tender);

        var takip = Takip(firma.Id, c.Id);
        takip.ChangeStatus(
            TenderPursuitStatus.Sonuclandi, TenderOutcome.Kazanildi, null, AsOf.AddDays(-1), "u@f.test");

        var sonuc = Kur(
            firma,
            [
                Degerlendirme(firma.Id, a.Id),
                Degerlendirme(firma.Id, b.Id, EligibilityVerdict.ConditionallyEligible),
            ],
            takipler: [takip]);

        Assert.Equal(2, sonuc.Funnel.Evaluated);
        Assert.Equal(1, sonuc.Funnel.Eligible);
        Assert.Equal(1, sonuc.Funnel.ConditionallyEligible);
        Assert.Equal(1, sonuc.Funnel.Tracked);
        Assert.Equal(1, sonuc.Funnel.Submitted);
        Assert.Equal(1, sonuc.Funnel.Won);
    }

    [Fact(DisplayName = "DB14. Eğilim ESKİDEN YENİYE sıralanır")]
    public void Egilim_sirali()
    {
        var firma = Firma();

        var raporlar = new[]
        {
            Rapor(firma, new DateOnly(2026, 8, 31)),
            Rapor(firma, new DateOnly(2026, 8, 17)),
            Rapor(firma, new DateOnly(2026, 8, 24)),
        };

        var sonuc = Kur(firma, raporlar: raporlar);

        Assert.Equal(
            [new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 31)],
            sonuc.Trend.Select(t => t.PeriodStart).ToList());
    }

    private static WeeklyReport Rapor(Company firma, DateOnly hafta) =>
        new(
            TenantId, firma.Id, firma.LegalName, hafta, hafta.AddDays(6),
            AsOf, ReportTrigger.Scheduled, "{}",
            new WeeklyReportCounters(
                OpportunityCount: 5,
                TenderCount: 2,
                RegulatoryChangeCount: 1,
                RiskCount: 3,
                ActionCount: 4,
                UrgentDeadlineCount: 1,
                DeadlineCount: 2,
                PastGapCount: 0));

    // ── Son hareketler akışı ────────────────────────────────────────────────

    [Fact(DisplayName = "DB15. Hareketler YENİDEN ESKİYE sıralanır")]
    public void Hareketler_sirali()
    {
        var firma = Firma();
        var cagri = Cagri("İhale", null, SupportCategory.Tender);
        var takip = Takip(firma.Id, cagri.Id);

        takip.ChangeStatus(
            TenderPursuitStatus.Hazirlaniyor, null, null, AsOf.AddDays(-2), "u@f.test");

        var sonuc = Kur(
            firma,
            takipler: [takip],
            raporlar: [Rapor(firma, new DateOnly(2026, 8, 31))],
            cagrilar: new Dictionary<Guid, Opportunity> { [cagri.Id] = cagri });

        var zamanlar = sonuc.Activity.Select(a => a.At).ToList();

        Assert.Equal(zamanlar.OrderByDescending(z => z).ToList(), zamanlar);
    }

    [Fact(DisplayName = "DB16. Skorlama TEK satır olur, her değerlendirme için ayrı değil")]
    public void Skorlama_tek_satir()
    {
        // Yüz değerlendirme, akışı yüz satırla doldurup asıl hareketleri bastırırdı.
        var firma = Firma();
        var degerlendirmeler = Enumerable.Range(0, 30)
            .Select(_ => Degerlendirme(firma.Id, Guid.NewGuid()))
            .ToList();

        var sonuc = Kur(firma, degerlendirmeler);

        var skorlama = Assert.Single(sonuc.Activity, a => a.Kind == "Scoring");

        Assert.Contains("30", skorlama.Detail);
    }

    [Fact(DisplayName = "DB17. BAŞARISIZ ERP çekimi hareket sayılmaz")]
    public void Basarisiz_erp_hareket_degil()
    {
        var firma = Firma();
        var baglanti = new ErpConnection(
            TenantId, firma.Id, ErpVendor.Logo, "https://erp.ornek.local", ErpAuthMode.ApiKeyHeader,
            "sifreli", isOnPremise: true);

        baglanti.RecordFailure(AsOf.AddDays(-1), "bağlanılamadı");

        var sonuc = Kur(firma, erp: baglanti);

        Assert.DoesNotContain(sonuc.Activity, a => a.Kind == "ErpPull");
    }

    [Fact(DisplayName = "DB18. Hareket listesi sınırlıdır")]
    public void Hareket_listesi_sinirli()
    {
        var firma = Firma();
        var raporlar = Enumerable.Range(0, 20)
            .Select(i => Rapor(firma, new DateOnly(2026, 1, 5).AddDays(7 * i)))
            .ToList();

        var sonuc = Kur(firma, raporlar: raporlar);

        Assert.True(sonuc.Activity.Count <= DashboardInsightsBuilder.ActivityLimit);
    }

    // ── Boşluğun sebebi yazılır ─────────────────────────────────────────────

    [Fact(DisplayName = "DB19. Değerlendirme yoksa SEBEBİ yazılır")]
    public void Degerlendirme_yoksa_sebep_yazilir()
    {
        // Boş bir bölüm, sessizce geçilirse "bu konu incelenmedi" izlenimi verir.
        var sonuc = Kur();

        Assert.Contains(sonuc.Notes, n => n.Contains("değerlendirme", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "DB20. Rapor geçmişi yoksa eğilimin sebebi yazılır")]
    public void Egilim_yoksa_sebep_yazilir()
    {
        var sonuc = Kur();

        Assert.Empty(sonuc.Trend);
        Assert.Contains(sonuc.Notes, n => n.Contains("Eğilim", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "DB21. Aynı girdi aynı çıktıyı verir")]
    public void Deterministiktir()
    {
        var firma = Firma();
        var cagri = Cagri("Çağrı", AsOf.AddDays(4));
        var degerlendirmeler = new[] { Degerlendirme(firma.Id, cagri.Id) };
        var harita = new Dictionary<Guid, Opportunity> { [cagri.Id] = cagri };

        var bir = Kur(firma, degerlendirmeler, cagrilar: harita);
        var iki = Kur(firma, degerlendirmeler, cagrilar: harita);

        Assert.Equal(
            bir.Actions.Select(a => (a.Priority, a.Title)),
            iki.Actions.Select(a => (a.Priority, a.Title)));

        Assert.Equal(bir.Profile.Percentage, iki.Profile.Percentage);
        Assert.Equal(bir.Funnel, iki.Funnel);
    }
}
