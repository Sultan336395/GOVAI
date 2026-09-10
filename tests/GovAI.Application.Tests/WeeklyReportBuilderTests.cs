using GovAI.Application.Eligibility;
using GovAI.Application.Reporting;
using GovAI.Domain.Assessments;
using GovAI.Domain.Common;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Opportunities;
using GovAI.Domain.Reporting;
using GovAI.Domain.Scoring;

namespace GovAI.Application.Tests;

/// <summary>
/// Haftalık raporun kurulması.
///
/// <para>
/// Bu testler ürünün rapor sözleşmesidir. Rapor firmanın karar aldığı belgedir; üç şeyi
/// birden tutmak zorundadır: <b>deterministik</b> olmak (aynı girdi → aynı rapor),
/// <b>uydurmamak</b> (veri yoksa sebebini yazmak) ve <b>karantinadaki kaydı
/// göstermemek</b>.
/// </para>
/// </summary>
public class WeeklyReportBuilderTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 14, 7, 30, 0, TimeSpan.FromHours(3));

    private static readonly Guid SourceId = Guid.NewGuid();

    private static readonly Guid CompanyId = Guid.NewGuid();

    // ── Kurulum yardımcıları ────────────────────────────────────────────────

    private static Opportunity Cagri(
        SupportCategory kategori,
        string baslik,
        DateTimeOffset? sonBasvuru = null,
        string? naceKurali = null)
    {
        var cagri = new Opportunity(
            SourceId, SourceType.KosgebOrSimilar, kategori, baslik, "KOSGEB",
            AsOf.AddDays(-10));

        cagri.SetSchedule(AsOf.AddDays(-10), sonBasvuru);

        if (naceKurali is not null)
        {
            cagri.ReplaceRules(
                [
                    new OpportunityRule(
                        "Company.NaceCodes", RuleOperator.NaceMatch, naceKurali,
                        RuleDimension.Sector, RuleSeverity.Major,
                        "İlanın konusu bilişim alanındadır.", confidence: 0.7m),
                ],
                0.7m);
        }

        return cagri;
    }

    private static EligibilityOutcome Sonuc(
        Guid opportunityId,
        EligibilityVerdict karar,
        decimal skor,
        IReadOnlyList<RuleEvaluation>? kurallar = null,
        IReadOnlyList<DocumentCheckResult>? belgeler = null) =>
        new()
        {
            CompanyId = CompanyId,
            OpportunityId = opportunityId,
            EvaluatedAt = AsOf.AddDays(-1),
            Verdict = karar,
            SectorFit = SectorFit.Matched,
            RuleEvaluations = kurallar ?? [],
            DocumentChecklist = belgeler ?? [],
            Score = new ScoreBreakdown
            {
                Dimensions = [],
                Weights = ScoreWeights.Default,
                FinalScore = skor,
                HasBlockingFailure = false,
                Confidence = 0.9m,
            },
        };

    private static AssessedOpportunity Cift(
        Opportunity cagri,
        EligibilityVerdict karar = EligibilityVerdict.Eligible,
        decimal skor = 80m,
        IReadOnlyList<RuleEvaluation>? kurallar = null,
        IReadOnlyList<DocumentCheckResult>? belgeler = null)
    {
        var sonuc = Sonuc(cagri.Id, karar, skor, kurallar, belgeler);

        return new AssessedOpportunity(
            new EligibilityAssessment(Guid.NewGuid(), sonuc, 1, "{}"),
            cagri,
            AssessmentDetailSnapshot.From(sonuc));
    }

    private static WeeklyReportInput Girdi(params AssessedOpportunity[] cift) => new()
    {
        CompanyId = CompanyId,
        CompanyName = "Örnek Teknoloji A.Ş.",
        Week = ReportWeek.CompletedBefore(AsOf),
        AsOf = AsOf,
        Assessments = cift,
        RegulatoryChanges = [],
    };

    private static RuleEvaluation Kural(
        string alan,
        string metin,
        RuleOutcome sonuc,
        RuleSeverity siddet,
        string? aksiyon = null) =>
        new()
        {
            RuleId = Guid.NewGuid(),
            Field = alan,
            Dimension = RuleDimension.Employment,
            Severity = siddet,
            Outcome = sonuc,
            Requirement = metin,
            ActualValue = "7",
            ExpectedValue = ">= 10",
            Strength = 0m,
            SuggestedAction = aksiyon,
        };

    // ── Determinizm ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "HR1. Aynı girdi aynı raporu üretir")]
    public void Ayni_girdi_ayni_raporu_uretir()
    {
        // Determinizm ürünün en temel iddiasıdır: iki kez üretilen rapor farklı
        // çıkarsa firma hangisine göre karar verdiğini savunamaz.
        var girdi = Girdi(
            Cift(Cagri(SupportCategory.Grant, "B Hibesi", AsOf.AddDays(20)), skor: 70m),
            Cift(Cagri(SupportCategory.Grant, "A Hibesi", AsOf.AddDays(10)), skor: 70m));

        var bir = WeeklyReportBuilder.Build(girdi);
        var iki = WeeklyReportBuilder.Build(girdi);

        Assert.Equal(
            bir.Supports.Select(s => s.Title),
            iki.Supports.Select(s => s.Title));

        // Eşit skorda sıra başlığa göre sabitlenir; sözlük sırası rastgele olamaz.
        Assert.Equal(["A Hibesi", "B Hibesi"], bir.Supports.Select(s => s.Title));
    }

    // ── Bölüm ayrımı ────────────────────────────────────────────────────────

    [Fact(DisplayName = "HR2. İhale destek bölümüne, destek ihale bölümüne GİRMEZ")]
    public void Bolumler_karismaz()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Hibe Çağrısı")),
            Cift(Cagri(SupportCategory.Tender, "Yazılım Alımı", naceKurali: "62.01,62.02"))));

        Assert.Equal(["Hibe Çağrısı"], rapor.Supports.Select(s => s.Title));
        Assert.Equal(["Yazılım Alımı"], rapor.TechnologyTenders.Select(s => s.Title));
    }

    [Fact(DisplayName = "HR3. Teknoloji ihalesi başlığa değil KAYNAĞIN sınıflandırmasına bakar")]
    public void Teknoloji_ihalesi_kurala_bakar()
    {
        // Başlıkta "yazılım" geçen bir temizlik ihalesi listeye girmemeli; sektör kuralı
        // taşıyan bir ihale ise başlığında teknoloji kelimesi olmasa da girmeli.
        var yaniltici = Cagri(SupportCategory.Tender, "Yazılım Binası Temizlik Hizmeti", naceKurali: "81.21");
        var gercek = Cagri(SupportCategory.Tender, "Sunucu ve Lisans Alımı", naceKurali: "62.01,26.20");

        var rapor = WeeklyReportBuilder.Build(Girdi(Cift(yaniltici), Cift(gercek)));

        Assert.Equal(["Sunucu ve Lisans Alımı"], rapor.TechnologyTenders.Select(s => s.Title));
    }

    [Fact(DisplayName = "HR4. Sektör kuralı olmayan ihale teknoloji sayılmaz")]
    public void Kuralsiz_ihale_teknoloji_sayilmaz()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Tender, "Konusu Belirsiz İhale"))));

        Assert.Empty(rapor.TechnologyTenders);
    }

    // ── Karantina ve kapanmış çağrı ─────────────────────────────────────────

    [Fact(DisplayName = "HR5. Karantinadaki çağrı rapora GİRMEZ")]
    public void Karantinadaki_cagri_girmez()
    {
        // Katalogda gösterilmeyen bir kayıt için firmaya "başvurun" demek olurdu.
        var karantinali = Cagri(SupportCategory.Grant, "Karantinadaki Hibe");
        karantinali.Quarantine(QuarantineReason.InvalidSourcePage, "Liste sayfası.");

        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(karantinali),
            Cift(Cagri(SupportCategory.Grant, "Geçerli Hibe"))));

        Assert.Equal(["Geçerli Hibe"], rapor.Supports.Select(s => s.Title));
        Assert.Equal(1, rapor.Header.EvaluatedOpportunityCount);
    }

    [Fact(DisplayName = "HR6. Son başvurusu geçmiş çağrı rapora GİRMEZ")]
    public void Kapanmis_cagri_girmez()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Kapanmış Hibe", AsOf.AddDays(-1))),
            Cift(Cagri(SupportCategory.Grant, "Açık Hibe", AsOf.AddDays(5)))));

        Assert.Equal(["Açık Hibe"], rapor.Supports.Select(s => s.Title));
        Assert.DoesNotContain(rapor.Deadlines, d => d.Title == "Kapanmış Hibe");
    }

    [Fact(DisplayName = "HR7. Uygun olmayan çağrı öneri listesine girmez")]
    public void Uygun_olmayan_cagri_onerilmez()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Uygun Değil"), EligibilityVerdict.NotEligible, 0m),
            Cift(Cagri(SupportCategory.Grant, "Şartlı Uygun"), EligibilityVerdict.ConditionallyEligible, 55m)));

        Assert.Equal(["Şartlı Uygun"], rapor.Supports.Select(s => s.Title));
    }

    // ── Sıralama ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "HR8. Sektör uyumu skordan ÖNCE gelir")]
    public void Sektor_uyumu_birincil_olcuttur()
    {
        // CLAUDE.md §2.2.1: sektör uyumu sıralamanın birincil ölçütüdür. Yüksek skorlu
        // ama sektörü uyumsuz bir çağrıyı başa koymak, danışmana yanlış işi gösterir.
        var yuksekAmaUyumsuz = Cagri(SupportCategory.Grant, "Yüksek Skor Uyumsuz");
        var dusukAmaUyumlu = Cagri(SupportCategory.Grant, "Düşük Skor Uyumlu");

        var uyumsuzSonuc = Sonuc(yuksekAmaUyumsuz.Id, EligibilityVerdict.ConditionallyEligible, 95m)
            with { SectorFit = SectorFit.NotMatched };

        var rapor = WeeklyReportBuilder.Build(Girdi(
            new AssessedOpportunity(
                new EligibilityAssessment(Guid.NewGuid(), uyumsuzSonuc, 1, "{}"),
                yuksekAmaUyumsuz,
                AssessmentDetailSnapshot.From(uyumsuzSonuc)),
            Cift(dusukAmaUyumlu, skor: 40m)));

        Assert.Equal("Düşük Skor Uyumlu", rapor.Supports[0].Title);
    }

    // ── Riskler ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "HR9. Aynı eksik birden çok çağrıda TEK risk olur")]
    public void Ayni_eksik_tekillestirilir()
    {
        // Aynı eksik belge on çağrıyı bloke ediyorsa bu on ayrı risk değildir; listeyi
        // aynı satırın kopyalarıyla doldurmak asıl işi görünmez yapar.
        var engel = Kural("Workforce.EmployeeCount", "Asgari 10 çalışan", RuleOutcome.NotSatisfied,
            RuleSeverity.Blocking, "Çalışan sayısını 10'a çıkarın.");

        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Hibe 1"), kurallar: [engel]),
            Cift(Cagri(SupportCategory.Grant, "Hibe 2"), kurallar: [engel])));

        var risk = Assert.Single(rapor.Risks);

        Assert.Equal(ReportRiskKind.BlockingCondition, risk.Kind);
        Assert.Equal(2, risk.AffectedOpportunityCount);
    }

    [Fact(DisplayName = "HR10. Eksik zorunlu belge risk olarak görünür, isteğe bağlı belge görünmez")]
    public void Zorunlu_belge_eksigi_risktir()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(Cift(
            Cagri(SupportCategory.Grant, "Belgeli Hibe"),
            belgeler:
            [
                new DocumentCheckResult
                {
                    Code = "ISO9001", Name = "ISO 9001", IsMandatory = true,
                    Status = DocumentStatus.Missing, Action = "ISO 9001 belgesini temin edin.",
                },
                new DocumentCheckResult
                {
                    Code = "ISO14001", Name = "ISO 14001", IsMandatory = false,
                    Status = DocumentStatus.Missing,
                },
            ])));

        var risk = Assert.Single(rapor.Risks);

        Assert.Equal(ReportRiskKind.MissingDocument, risk.Kind);
        Assert.Equal("ISO 9001", risk.Subject);
        Assert.Equal("ISO 9001 belgesini temin edin.", risk.Action);
    }

    [Fact(DisplayName = "HR11. Eksik profil bilgisi ile engelleyici koşul AYRI cinstendir")]
    public void Eksik_veri_engelle_karistirilmaz()
    {
        // "Bilmiyorum" ile "hayır" ayrı şeylerdir (CLAUDE.md §2.2). Eksik profil bilgisi
        // başvuruyu engellemez, yalnızca kararı belirsiz bırakır.
        var rapor = WeeklyReportBuilder.Build(Girdi(Cift(
            Cagri(SupportCategory.Grant, "Hibe"),
            kurallar:
            [
                Kural("Financials.Revenue", "Asgari 1M ciro", RuleOutcome.Unknown,
                    RuleSeverity.Major, "Ciro bilgisini girin."),
                Kural("Workforce.EmployeeCount", "Asgari 10 çalışan", RuleOutcome.NotSatisfied,
                    RuleSeverity.Blocking, "Çalışan sayısını 10'a çıkarın."),
            ])));

        Assert.Contains(rapor.Risks, r => r.Kind == ReportRiskKind.DataGap);
        Assert.Contains(rapor.Risks, r => r.Kind == ReportRiskKind.BlockingCondition);
    }

    // ── Yapılacaklar ────────────────────────────────────────────────────────

    [Fact(DisplayName = "HR12. Yakın son başvuru ACİL, uzak olan değil")]
    public void Aciliyet_son_tarihe_baglidir()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Acil Hibe", AsOf.AddDays(5))),
            Cift(Cagri(SupportCategory.Grant, "Rahat Hibe", AsOf.AddDays(45)))));

        var acil = rapor.Todos.First();

        Assert.Equal(ReportTodoPriority.Urgent, acil.Priority);
        Assert.Contains("Acil Hibe", acil.Title);
        Assert.Contains("gün kaldı", acil.Reason);

        Assert.Contains(rapor.Todos, t => t.Priority == ReportTodoPriority.Normal
                                          && t.Title.Contains("Rahat Hibe", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "HR13. Gerekçesiz risk yapılacaklar listesine girmez")]
    public void Aksiyonu_olmayan_risk_is_uretmez()
    {
        // Ne yapılacağını söyleyemiyorsak yapılacaklar listesine satır eklemek,
        // kullanıcıya kapatamayacağı bir iş vermek olurdu.
        var rapor = WeeklyReportBuilder.Build(Girdi(Cift(
            Cagri(SupportCategory.Grant, "Hibe"),
            kurallar:
            [
                Kural("Workforce.EmployeeCount", "Asgari 10 çalışan",
                    RuleOutcome.NotSatisfied, RuleSeverity.Blocking),
            ])));

        Assert.Single(rapor.Risks);
        Assert.Empty(rapor.Todos);
    }

    [Fact(DisplayName = "HR14. Her yapılacak işin gerekçesi YAZILIDIR")]
    public void Her_isin_gerekcesi_vardir()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Hibe", AsOf.AddDays(7)),
                kurallar:
                [
                    Kural("Workforce.EmployeeCount", "Asgari 10 çalışan", RuleOutcome.NotSatisfied,
                        RuleSeverity.Blocking, "Çalışan sayısını 10'a çıkarın."),
                ])));

        Assert.NotEmpty(rapor.Todos);
        Assert.All(rapor.Todos, t => Assert.False(string.IsNullOrWhiteSpace(t.Reason)));
    }

    // ── Boşluk uydurulmaz ───────────────────────────────────────────────────

    [Fact(DisplayName = "HR15. Boş bölüm SESSİZCE geçilmez, sebebi yazılır")]
    public void Bos_bolumun_sebebi_yazilir()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Tek Hibe", AsOf.AddDays(10)))));

        Assert.Empty(rapor.TechnologyTenders);
        Assert.Empty(rapor.RegulatoryChanges);

        Assert.Contains(rapor.Notes, n => n.Contains("ihale bulunamadı", StringComparison.Ordinal));
        Assert.Contains(rapor.Notes, n => n.Contains("mevzuat değişikliği yayımlanmadı", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "HR16. Değerlendirmesi olmayan firmaya boş rapor değil AÇIKLAMA verilir")]
    public void Degerlendirmesiz_firma_aciklama_alir()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi());

        Assert.Empty(rapor.Supports);

        var not = Assert.Single(rapor.Notes);
        Assert.Contains("henüz değerlendirme yapılmamış", not, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "HR25. Kapanmış çağrı RİSK ÜRETMEZ")]
    public void Kapanmis_cagri_risk_uretmez()
    {
        // Son başvurusu geçmiş bir çağrı yüzünden "belgeyi temin edin" demek, artık
        // başvurulamayacak bir iş vermektir. Rapor göstermediği kaydın üzerinden iş
        // veremez.
        var belge = new DocumentCheckResult
        {
            Code = "SGK_BORCU_YOKTUR",
            Name = "SGK Borcu Yoktur Yazısı",
            IsMandatory = true,
            Status = DocumentStatus.Missing,
            Action = "SGK üzerinden temin edin.",
        };

        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Kapanmış Hibe", AsOf.AddDays(-1)), belgeler: [belge])));

        Assert.Empty(rapor.Supports);
        Assert.Empty(rapor.OtherOpportunities);
        Assert.Empty(rapor.Risks);
    }

    [Fact(DisplayName = "HR26. Açık çağrıdan gelen risk sayılır, kapanmış olan sayıya KATILMAZ")]
    public void Kapanmis_cagri_risk_sayisina_katilmaz()
    {
        var belge = new DocumentCheckResult
        {
            Code = "SGK_BORCU_YOKTUR",
            Name = "SGK Borcu Yoktur Yazısı",
            IsMandatory = true,
            Status = DocumentStatus.Missing,
            Action = "SGK üzerinden temin edin.",
        };

        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Açık Hibe", AsOf.AddDays(10)), belgeler: [belge]),
            Cift(Cagri(SupportCategory.Grant, "Kapanmış Hibe", AsOf.AddDays(-1)), belgeler: [belge])));

        var risk = Assert.Single(rapor.Risks);

        // Aynı belge iki çağrıda eksikti; yalnızca AÇIK olan sayılır.
        Assert.Equal(1, risk.AffectedOpportunityCount);
    }

    // ── Geçmiş dönem eksikleri ──────────────────────────────────────────────

    private static DocumentCheckResult SgkBelgesi() => new()
    {
        Code = "SGK_BORCU_YOKTUR",
        Name = "SGK Borcu Yoktur Yazısı",
        IsMandatory = true,
        Status = DocumentStatus.Missing,
        Action = "SGK üzerinden temin edin.",
    };

    [Fact(DisplayName = "HR27. Kapanmış çağrının eksiği GEÇMİŞ DÖNEM bölümünde görünür")]
    public void Kapanmis_cagri_eksigi_gecmiste_gorunur()
    {
        // Eksiği hiç göstermemek, firmanın kendi durumunu görmesini engellerdi:
        // aynı belgeyi isteyen yeni bir çağrı açıldığında yine karşısına çıkacak.
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Kapanmış Hibe", AsOf.AddDays(-1)),
                belgeler: [SgkBelgesi()])));

        var eksik = Assert.Single(rapor.PastPeriodGaps);

        Assert.Equal(ReportRiskKind.MissingDocument, eksik.Kind);
        Assert.Equal("SGK Borcu Yoktur Yazısı", eksik.Subject);
        Assert.Equal(1, eksik.AffectedOpportunityCount);
    }

    [Fact(DisplayName = "HR28. Geçmiş dönem eksiği YAPILACAK İŞ üretmez")]
    public void Gecmis_eksik_is_uretmez()
    {
        // Kapanmış çağrı için iş vermek, yapılamayacak bir iş vermektir. Kusurun
        // kendisi buydu: kapanmış hibeden gelen eksikler acil iş olarak listelenmişti.
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Kapanmış Hibe", AsOf.AddDays(-1)),
                belgeler: [SgkBelgesi()])));

        Assert.NotEmpty(rapor.PastPeriodGaps);
        Assert.Empty(rapor.Risks);
        Assert.Empty(rapor.Todos);
    }

    [Fact(DisplayName = "HR29. Aynı eksik iki bölümde AYNI ANDA görünmez")]
    public void Ayni_eksik_iki_bolumde_gorunmez()
    {
        // Aynı satırı hem güncel hem geçmiş listede göstermek, hangisinin üzerine iş
        // verildiğini belirsizleştirir. Açık çağrı varsa eksik GÜNCEL listededir.
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Açık Hibe", AsOf.AddDays(10)),
                belgeler: [SgkBelgesi()]),
            Cift(Cagri(SupportCategory.Grant, "Kapanmış Hibe", AsOf.AddDays(-1)),
                belgeler: [SgkBelgesi()])));

        var guncel = Assert.Single(rapor.Risks);

        Assert.Equal("SGK Borcu Yoktur Yazısı", guncel.Subject);
        Assert.Empty(rapor.PastPeriodGaps);
    }

    [Fact(DisplayName = "HR30. Geçmiş dönem eksiği varsa ne anlama geldiği YAZILIR")]
    public void Gecmis_eksigin_anlami_yazilir()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Kapanmış Hibe", AsOf.AddDays(-1)),
                belgeler: [SgkBelgesi()])));

        Assert.Contains(
            rapor.Notes,
            n => n.Contains("şimdi yapılacak bir iş yoktur", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "HR31. Açık çağrı yoksa geçmiş eksik güvenceyi BOZMAZ")]
    public void Gecmis_eksik_guvenceyi_bozmaz()
    {
        // "Engelleyen bir eksik görünmüyor" güncel durumu anlatır. Geçmiş dönem eksiği
        // bu cümleyi yanlış yapmaz; ikisi ayrı sorulara cevap verir.
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Açık Hibe", AsOf.AddDays(10))),
            Cift(Cagri(SupportCategory.Grant, "Kapanmış Hibe", AsOf.AddDays(-1)),
                belgeler: [SgkBelgesi()])));

        Assert.Empty(rapor.Risks);
        Assert.Single(rapor.PastPeriodGaps);
        Assert.Contains(rapor.Notes, n => n.Contains("eksik görünmüyor", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "HR32. Geçmiş eksik sayacı gövdeyle tutarlıdır")]
    public void Gecmis_eksik_sayaci_tutarlidir()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Kapanmış Hibe", AsOf.AddDays(-1)),
                belgeler: [SgkBelgesi()])));

        Assert.Equal(rapor.PastPeriodGaps.Count, WeeklyReportBuilder.Counters(rapor).PastGapCount);
    }

    // ── Kullanıcıya iç terim gösterilmez ────────────────────────────────────

    [Fact(DisplayName = "HR23. Eksik profil bilgisi iç alan adıyla DEĞİL okunabilir adla yazılır")]
    public void Eksik_bilgi_okunabilir_adla_yazilir()
    {
        // Raporda "Company.Nuts2Codes" yazıyordu. Sistemin iç terimleri kullanıcıya
        // sızmaz; kullanıcı profilinde böyle bir alan adı aramaz.
        var rapor = WeeklyReportBuilder.Build(Girdi(Cift(
            Cagri(SupportCategory.Grant, "Hibe"),
            kurallar:
            [
                Kural("Company.Nuts2Codes", "Bölge kuralı", RuleOutcome.Unknown,
                    RuleSeverity.Major, "Bölge kodlarını girin."),
            ])));

        var risk = Assert.Single(rapor.Risks);

        Assert.Equal(ReportRiskKind.DataGap, risk.Kind);
        Assert.DoesNotContain("Company.", risk.Subject, StringComparison.Ordinal);
        Assert.Equal("İstatistiki bölge kodları", risk.Subject);
    }

    [Fact(DisplayName = "HR24. Aynı alanın iki ayrı kuralı TEK risk olur")]
    public void Ayni_alan_tek_risk_olur()
    {
        // Eksiklik alanın kendisindedir, kuralın değil: ciro bilgisi girilmemişse bu
        // iki ayrı eksik değil, iki çağrıyı etkileyen tek eksiktir.
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Hibe 1"), kurallar:
                [Kural("Financials.AnnualRevenue", "Asgari 1M ciro", RuleOutcome.Unknown,
                    RuleSeverity.Major, "Ciro girin.")]),
            Cift(Cagri(SupportCategory.Grant, "Hibe 2"), kurallar:
                [Kural("Financials.AnnualRevenue", "Asgari 5M ciro", RuleOutcome.Unknown,
                    RuleSeverity.Major, "Ciro girin.")])));

        var risk = Assert.Single(rapor.Risks);

        Assert.Equal("Yıllık ciro", risk.Subject);
        Assert.Equal(2, risk.AffectedOpportunityCount);
    }

    // ── Rapor kendisiyle çelişmez ───────────────────────────────────────────

    [Fact(DisplayName = "HR18. Üzerine iş verilen her çağrı raporda GÖRÜNÜR")]
    public void Her_is_raporda_gorunur()
    {
        // Sahadaki kusur: taşınmaz, dikili ağaç ve sigorta ihaleleri ne destek bölümüne
        // (destek türü değil) ne teknoloji bölümüne (konusu teknoloji değil) giriyordu,
        // ama takvime ve yapılacaklara giriyordu. Rapor "uygun destek bulunamadı" derken
        // aynı anda "Acil: başvuruyu hazırla — DİKİLİ AĞAÇ SATILACAKTIR" diyordu.
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Tender, "DİKİLİ AĞAÇ SATILACAKTIR", AsOf.AddDays(8))),
            Cift(Cagri(SupportCategory.Tender, "TAŞINMAZ SATILACAKTIR", AsOf.AddDays(15))),
            Cift(Cagri(SupportCategory.Other, "SİGORTA ARACILIK HİZMETİ", AsOf.AddDays(27)))));

        var listelenen = rapor.Supports
            .Concat(rapor.TechnologyTenders)
            .Concat(rapor.OtherOpportunities)
            .Select(i => i.OpportunityId)
            .ToHashSet();

        Assert.NotEmpty(rapor.Todos);

        foreach (var is_ in rapor.Todos.Where(t => t.OpportunityId is not null))
        {
            Assert.Contains(is_.OpportunityId!.Value, listelenen);
        }

        foreach (var tarih in rapor.Deadlines)
        {
            Assert.Contains(tarih.OpportunityId, listelenen);
        }
    }

    [Fact(DisplayName = "HR19. Bir çağrı aynı anda iki bölümde listelenmez")]
    public void Cagri_tek_bolumde_listelenir()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Hibe")),
            Cift(Cagri(SupportCategory.Tender, "Sunucu Alımı", naceKurali: "62.01")),
            Cift(Cagri(SupportCategory.Tender, "Taşınmaz Satışı"))));

        var hepsi = rapor.Supports
            .Concat(rapor.TechnologyTenders)
            .Concat(rapor.OtherOpportunities)
            .Select(i => i.OpportunityId)
            .ToList();

        Assert.Equal(hepsi.Count, hepsi.Distinct().Count());
        Assert.Equal(3, hepsi.Count);
    }

    [Fact(DisplayName = "HR20. Takvim sektör uyumunu da yazar")]
    public void Takvim_sektor_uyumunu_yazar()
    {
        // Yalnızca skor gösteren bir takvim, sektörü doğrulanamamış yüksek puanlı bir
        // çağrıyı güvenli gibi gösterir (CLAUDE.md §2.2.1).
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Hibe", AsOf.AddDays(10)))));

        var satir = Assert.Single(rapor.Deadlines);

        Assert.Equal(SectorFit.Matched, satir.SectorFit);
        Assert.False(string.IsNullOrWhiteSpace(satir.SectorFitLabel));
    }

    // ── Okunamayan ayrıntı sessizce "sorun yok"a dönüşmez ───────────────────

    [Fact(DisplayName = "HR21. Ayrıntı okunamadıysa rapor YALAN GÜVENCE vermez")]
    public void Okunamayan_ayrinti_guvenceye_donusmez()
    {
        // Sahadaki en tehlikeli kusur buydu: 36 değerlendirmenin ayrıntısı okunamıyordu,
        // risk listesi boş kalıyordu ve rapor "engelleyen bir eksik görünmüyor" yazıyordu.
        var girdi = Girdi(Cift(Cagri(SupportCategory.Grant, "Hibe", AsOf.AddDays(10))))
            with { UnreadableDetailCount = 36 };

        var rapor = WeeklyReportBuilder.Build(girdi);

        Assert.Empty(rapor.Risks);
        Assert.DoesNotContain(rapor.Notes, n => n.Contains("eksik görünmüyor", StringComparison.Ordinal));
        Assert.Contains(rapor.Notes, n => n.Contains("okunamadı", StringComparison.Ordinal));
        Assert.Contains(rapor.Notes, n => n.Contains("36", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "HR22. Her şey okunabildiyse güvence VERİLİR")]
    public void Her_sey_okunduysa_guvence_verilir()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Hibe", AsOf.AddDays(10)))));

        Assert.Contains(rapor.Notes, n => n.Contains("eksik görünmüyor", StringComparison.Ordinal));
    }

    // ── Sayaçlar ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "HR17. Sayaçlar gövdeyle tutarlıdır")]
    public void Sayaclar_govdeyle_tutarlidir()
    {
        var rapor = WeeklyReportBuilder.Build(Girdi(
            Cift(Cagri(SupportCategory.Grant, "Acil Hibe", AsOf.AddDays(3))),
            Cift(Cagri(SupportCategory.Tender, "Yazılım Alımı", AsOf.AddDays(40), "62.01"))));

        var sayac = WeeklyReportBuilder.Counters(rapor);

        // Sayaç BÜTÜN çağrıları sayar. Eskiden yalnızca destekleri sayıyordu ve destek
        // bölümü boş olan rapor listede baştan sona sıfır görünüyordu — oysa içinde
        // üç acil iş vardı.
        Assert.Equal(
            rapor.Supports.Count + rapor.TechnologyTenders.Count + rapor.OtherOpportunities.Count,
            sayac.OpportunityCount);

        Assert.Equal(rapor.TechnologyTenders.Count, sayac.TenderCount);
        Assert.Equal(rapor.Todos.Count, sayac.ActionCount);
        Assert.Equal(rapor.Deadlines.Count, sayac.DeadlineCount);

        // 3 gün kalan acil, 40 gün kalan değil.
        Assert.Equal(1, sayac.UrgentDeadlineCount);
    }
}
