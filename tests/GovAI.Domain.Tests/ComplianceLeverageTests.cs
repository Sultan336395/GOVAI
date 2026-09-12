using GovAI.Domain.Common;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Leverage;
using GovAI.Domain.Scoring;

namespace GovAI.Domain.Tests;

/// <summary>
/// Uyumu fırsata çeviren motor.
///
/// <para>
/// Motorun değeri hesapta değil <b>dürüstlüğünde</b>: "şunu yaparsan altı milyonluk
/// hibe açılır" cümlesi yanlışsa ürün bir kez güven kaybeder ve geri kazanamaz. Bu
/// yüzden testlerin ağırlığı abartmanın engellendiğini sabitlemekte.
/// </para>
/// </summary>
public class ComplianceLeverageTests
{
    private static readonly Guid Firma = Guid.CreateVersion7();

    private static RuleEvaluation Kural(
        string alan,
        RuleOutcome sonuc,
        RuleSeverity onem = RuleSeverity.Major,
        string? koşul = null) =>
        new()
        {
            RuleId = Guid.CreateVersion7(),
            Field = alan,
            Dimension = RuleDimension.Employment,
            Severity = onem,
            Outcome = sonuc,
            Requirement = koşul ?? $"{alan} koşulu",
            ActualValue = "-",
            ExpectedValue = "-",
            Strength = 1m,
        };

    private static DocumentCheckResult Belge(string kod, DocumentStatus durum, bool zorunlu = true) =>
        new()
        {
            Code = kod,
            Name = $"{kod} belgesi",
            IsMandatory = zorunlu,
            Status = durum,
        };

    private static EligibilityOutcome Degerlendirme(
        Guid cagriId,
        IEnumerable<RuleEvaluation>? kurallar = null,
        IEnumerable<DocumentCheckResult>? belgeler = null) =>
        new()
        {
            CompanyId = Firma,
            OpportunityId = cagriId,
            EvaluatedAt = DateTimeOffset.UtcNow,
            Verdict = EligibilityVerdict.ConditionallyEligible,
            Score = new ScoreBreakdown
            {
                Dimensions = [],
                Weights = ScoreWeights.For(SupportCategory.Grant),
                FinalScore = 0m,
                HasBlockingFailure = false,
                Confidence = 1m,
            },
            RuleEvaluations = [.. kurallar ?? []],
            DocumentChecklist = [.. belgeler ?? []],
            SectorFit = SectorFit.Matched,
        };

    private static Dictionary<Guid, ComplianceLeverage.OpportunityBrief> Katalog(
        params (Guid Id, string Baslik, decimal? Tutar)[] cagrilar) =>
        cagrilar.ToDictionary(
            c => c.Id,
            c => new ComplianceLeverage.OpportunityBrief { Id = c.Id, Title = c.Baslik, MaxAmount = c.Tutar });

    // ── Abartma engeli ──────────────────────────────────────────────────────

    [Fact(DisplayName = "UF1. TEK eksik varsa çağrı 'açılır' sayılır")]
    public void Tek_eksik_acar()
    {
        var cagri = Guid.CreateVersion7();

        var eksikler = ComplianceLeverage.Analyze(
            [Degerlendirme(cagri, [Kural("Workforce.RAndDEmployeeCount", RuleOutcome.NotSatisfied)])],
            Katalog((cagri, "Ar-Ge Hibesi", 6_000_000m)));

        var eksik = Assert.Single(eksikler);
        Assert.Equal(1, eksik.UnlockCount);
        Assert.Equal(6_000_000m, eksik.UnlockedAmount);
    }

    [Fact(DisplayName = "UF2. ÜÇ eksikten biri kapanınca çağrı AÇILMIŞ sayılmaz")]
    public void Coklu_eksikte_acilmaz()
    {
        // Ürünün en kolay yalan söyleyeceği yer burası: birini kapatıp "hibe açıldı"
        // demek. Kalan iki engel duruyorken bu cümle yanlıştır.
        var cagri = Guid.CreateVersion7();

        var eksikler = ComplianceLeverage.Analyze(
            [Degerlendirme(cagri,
            [
                Kural("A", RuleOutcome.NotSatisfied),
                Kural("B", RuleOutcome.NotSatisfied),
                Kural("C", RuleOutcome.NotSatisfied),
            ])],
            Katalog((cagri, "Çok Koşullu Çağrı", 10_000_000m)));

        Assert.Equal(3, eksikler.Count);
        Assert.All(eksikler, e => Assert.Equal(0, e.UnlockCount));
        Assert.All(eksikler, e => Assert.Null(e.UnlockedAmount));
    }

    [Fact(DisplayName = "UF3. Kalan engel sayısı doğru bildirilir")]
    public void Kalan_engel_sayisi()
    {
        // "Bunu kapatırsan iki engel kalır" cümlesi, kullanıcının işi ölçmesini sağlar.
        var cagri = Guid.CreateVersion7();

        var eksikler = ComplianceLeverage.Analyze(
            [Degerlendirme(cagri, [Kural("A", RuleOutcome.NotSatisfied), Kural("B", RuleOutcome.NotSatisfied)])],
            Katalog((cagri, "Çağrı", null)));

        Assert.All(eksikler, e => Assert.Equal(1, e.Opportunities[0].RemainingGapCount));
    }

    [Fact(DisplayName = "UF4. Tutarı BİLİNMEYEN çağrı toplama katılmaz ve bu söylenir")]
    public void Bilinmeyen_tutar_toplanmaz()
    {
        // Tutarı bilinmeyeni sıfır sayıp toplasaydık sayı doğru görünür ama eksik
        // olurdu; hiç göstermeseydik bilgi kaybederdik. Doğrusu: topla ve eksik olduğunu söyle.
        var tutarli = Guid.CreateVersion7();
        var tutarsiz = Guid.CreateVersion7();

        var eksikler = ComplianceLeverage.Analyze(
            [
                Degerlendirme(tutarli, [Kural("A", RuleOutcome.NotSatisfied)]),
                Degerlendirme(tutarsiz, [Kural("A", RuleOutcome.NotSatisfied)]),
            ],
            Katalog((tutarli, "Tutarlı Çağrı", 5_000_000m), (tutarsiz, "Tutarsız Çağrı", null)));

        var eksik = Assert.Single(eksikler);

        Assert.Equal(2, eksik.UnlockCount);
        Assert.Equal(5_000_000m, eksik.UnlockedAmount);
        Assert.True(eksik.AmountIsPartial, "Tutarı bilinmeyen çağrı olduğu bildirilmedi.");
    }

    [Fact(DisplayName = "UF5. Hiçbirinin tutarı bilinmiyorsa toplam YOKTUR, sıfır değil")]
    public void Hepsi_bilinmiyorsa_toplam_yok()
    {
        var cagri = Guid.CreateVersion7();

        var eksik = Assert.Single(ComplianceLeverage.Analyze(
            [Degerlendirme(cagri, [Kural("A", RuleOutcome.NotSatisfied)])],
            Katalog((cagri, "Çağrı", null))));

        Assert.Null(eksik.UnlockedAmount);
        Assert.True(eksik.AmountIsPartial);
    }

    // ── Beyan eksiği koşulludur ─────────────────────────────────────────────

    [Fact(DisplayName = "UF6. Veri boşluğu BEYAN eksiği sayılır ve getirisi KOŞULLUDUR")]
    public void Beyan_eksigi_kosullu()
    {
        // Alan doldurulduğunda cevap "sağlamıyor" da çıkabilir. "Doldur, hibe açılacak"
        // demek kullanıcıyı yanıltırdı.
        var cagri = Guid.CreateVersion7();

        var eksik = Assert.Single(ComplianceLeverage.Analyze(
            [Degerlendirme(cagri, [Kural("Workforce.WomenEmployeeRate", RuleOutcome.Unknown)])],
            Katalog((cagri, "Kadın İstihdamı Desteği", 3_000_000m))));

        Assert.Equal(GapKind.Declaration, eksik.Kind);
        Assert.True(eksik.IsConditional);
    }

    [Fact(DisplayName = "UF7. Sağlanmayan koşul YETKİNLİK eksiğidir, koşullu değildir")]
    public void Saglanmayan_kosul_yetkinlik()
    {
        // Bu ayrım iş planı içindir: beyan girmek dakikalar, personel almak aylar sürer.
        var cagri = Guid.CreateVersion7();

        var eksik = Assert.Single(ComplianceLeverage.Analyze(
            [Degerlendirme(cagri, [Kural("Workforce.EmployeeCount", RuleOutcome.NotSatisfied)])],
            Katalog((cagri, "Çağrı", null))));

        Assert.Equal(GapKind.Capability, eksik.Kind);
        Assert.False(eksik.IsConditional);
    }

    [Fact(DisplayName = "UF8. Eksik zorunlu belge BELGE eksiğidir")]
    public void Belge_eksigi()
    {
        var cagri = Guid.CreateVersion7();

        var eksik = Assert.Single(ComplianceLeverage.Analyze(
            [Degerlendirme(cagri, belgeler: [Belge("ISO9001", DocumentStatus.Missing)])],
            Katalog((cagri, "Çağrı", null))));

        Assert.Equal(GapKind.Document, eksik.Kind);
    }

    [Fact(DisplayName = "UF9. SÜRESİ DOLMUŞ belge de eksiktir")]
    public void Suresi_dolmus_belge_eksik()
    {
        // Evidence Half-Life'ın bulduğu durum tam olarak budur: belge duruyor ama
        // artık geçerli değil. Eksik saymasaydık iki motor çelişirdi.
        var cagri = Guid.CreateVersion7();

        var eksikler = ComplianceLeverage.Analyze(
            [Degerlendirme(cagri, belgeler: [Belge("ISO14001", DocumentStatus.Expired)])],
            Katalog((cagri, "Çağrı", null)));

        Assert.Single(eksikler);
    }

    [Fact(DisplayName = "UF10. ELDE OLAN ve GEREKMEYEN belge eksik sayılmaz")]
    public void Olan_belge_eksik_degil()
    {
        var cagri = Guid.CreateVersion7();

        var eksikler = ComplianceLeverage.Analyze(
            [Degerlendirme(cagri, belgeler:
            [
                Belge("ISO9001", DocumentStatus.Provided),
                Belge("CE", DocumentStatus.NotRequired),
                Belge("OPSIYONEL", DocumentStatus.Missing, zorunlu: false),
            ])],
            Katalog((cagri, "Çağrı", null)));

        Assert.Empty(eksikler);
    }

    // ── Gruplama ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "UF11. AYNI eksik birden çok çağrıda TEK satırda toplanır")]
    public void Ayni_eksik_toplanir()
    {
        // Ürünün asıl vaadi bu: "bu tek belgeyi al, üç çağrıya birden gir."
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();

        var eksik = Assert.Single(ComplianceLeverage.Analyze(
            [
                Degerlendirme(a, belgeler: [Belge("ISO27001", DocumentStatus.Missing)]),
                Degerlendirme(b, belgeler: [Belge("ISO27001", DocumentStatus.Missing)]),
                Degerlendirme(c, belgeler: [Belge("ISO27001", DocumentStatus.Missing)]),
            ],
            Katalog((a, "A", 1_000_000m), (b, "B", 2_000_000m), (c, "C", 3_000_000m))));

        Assert.Equal(3, eksik.UnlockCount);
        Assert.Equal(6_000_000m, eksik.UnlockedAmount);
    }

    [Fact(DisplayName = "UF12. Aynı çağrıda aynı anahtar İKİ KEZ sayılmaz")]
    public void Ayni_anahtar_tekrarlanmaz()
    {
        // Sayılsaydı "kalan engel" şişer ve hiçbir eksik "tek başına açar" görünmezdi.
        var cagri = Guid.CreateVersion7();

        var eksik = Assert.Single(ComplianceLeverage.Analyze(
            [Degerlendirme(cagri,
            [
                Kural("Workforce.EmployeeCount", RuleOutcome.NotSatisfied),
                Kural("Workforce.EmployeeCount", RuleOutcome.NotSatisfied),
            ])],
            Katalog((cagri, "Çağrı", 1_000_000m))));

        Assert.Equal(1, eksik.UnlockCount);
    }

    // ── Sıralama ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "UF13. En çok çağrı açan eksik BAŞA gelir")]
    public void En_cok_acan_basta()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        var eksikler = ComplianceLeverage.Analyze(
            [
                Degerlendirme(a, belgeler: [Belge("COK", DocumentStatus.Missing)]),
                Degerlendirme(b, belgeler: [Belge("COK", DocumentStatus.Missing)]),
                Degerlendirme(Guid.CreateVersion7(), belgeler: [Belge("AZ", DocumentStatus.Missing)]),
            ],
            Katalog((a, "A", 100m), (b, "B", 100m)));

        Assert.Equal("belge:COK", eksikler[0].Key);
    }

    [Fact(DisplayName = "UF14. Eşit sayıda çağrıda YÜKSEK TUTARLI olan öne geçer")]
    public void Yuksek_tutar_one_gecer()
    {
        var ucuz = Guid.CreateVersion7();
        var pahali = Guid.CreateVersion7();

        var eksikler = ComplianceLeverage.Analyze(
            [
                Degerlendirme(ucuz, belgeler: [Belge("UCUZ", DocumentStatus.Missing)]),
                Degerlendirme(pahali, belgeler: [Belge("PAHALI", DocumentStatus.Missing)]),
            ],
            Katalog((ucuz, "Ucuz", 100_000m), (pahali, "Pahalı", 9_000_000m)));

        Assert.Equal("belge:PAHALI", eksikler[0].Key);
    }

    [Fact(DisplayName = "UF15. Çağrı listesinde önce AÇILANLAR gelir")]
    public void Acilanlar_once()
    {
        var acilan = Guid.CreateVersion7();
        var acilmayan = Guid.CreateVersion7();

        var eksik = ComplianceLeverage.Analyze(
            [
                Degerlendirme(acilan, [Kural("A", RuleOutcome.NotSatisfied)]),
                Degerlendirme(acilmayan, [Kural("A", RuleOutcome.NotSatisfied), Kural("B", RuleOutcome.NotSatisfied)]),
            ],
            Katalog((acilan, "Açılan", 1m), (acilmayan, "Açılmayan", 999m)))
            .First(e => e.Key == "alan:A");

        Assert.True(eksik.Opportunities[0].UnlockedByThisAlone);
    }

    // ── Sınır durumları ─────────────────────────────────────────────────────

    [Fact(DisplayName = "UF16. Eksiksiz değerlendirme hiç eksik üretmez")]
    public void Eksiksizde_eksik_yok()
    {
        var cagri = Guid.CreateVersion7();

        var eksikler = ComplianceLeverage.Analyze(
            [Degerlendirme(cagri, [Kural("A", RuleOutcome.Satisfied)])],
            Katalog((cagri, "Çağrı", 1m)));

        Assert.Empty(eksikler);
    }

    [Fact(DisplayName = "UF17. Künyesi olmayan çağrı ATLANIR, başlık uydurulmaz")]
    public void Kunyesiz_cagri_atlanir()
    {
        // Katalogda olmayan bir çağrı için "Bilinmeyen çağrı" satırı üretmek, ekranda
        // tıklanamayan ve doğrulanamayan bir kayıt göstermek olurdu.
        var eksikler = ComplianceLeverage.Analyze(
            [Degerlendirme(Guid.CreateVersion7(), [Kural("A", RuleOutcome.NotSatisfied)])],
            Katalog());

        Assert.Empty(eksikler);
    }

    [Fact(DisplayName = "UF18. Boş girdi çökmez")]
    public void Bos_girdi()
    {
        Assert.Empty(ComplianceLeverage.Analyze([], Katalog()));
    }

    [Fact(DisplayName = "UF19. Aynı girdi AYNI sonucu verir")]
    public void Deterministik()
    {
        // §2.1: motor deterministiktir.
        var cagri = Guid.CreateVersion7();
        var girdi = new[] { Degerlendirme(cagri, [Kural("A", RuleOutcome.NotSatisfied)]) };
        var katalog = Katalog((cagri, "Çağrı", 1_000m));

        Assert.Equal(
            ComplianceLeverage.Analyze(girdi, katalog).Select(e => e.Key),
            ComplianceLeverage.Analyze(girdi, katalog).Select(e => e.Key));
    }

    [Fact(DisplayName = "UF20. ELEYEN koşul da kapatılabilir eksik olarak listelenir")]
    public void Eleyen_kosul_da_listelenir()
    {
        // "Belgen yok" eleyicidir ama kapatılabilir. Eleyenleri gizleseydik firma en
        // önemli aksiyonu göremezdi; kapatılıp kapatılamayacağına kullanıcı karar verir.
        var cagri = Guid.CreateVersion7();

        var eksik = Assert.Single(ComplianceLeverage.Analyze(
            [Degerlendirme(cagri, [Kural("Company.Certificates", RuleOutcome.NotSatisfied, RuleSeverity.Blocking)])],
            Katalog((cagri, "Çağrı", 2_000_000m))));

        Assert.Equal(1, eksik.UnlockCount);
    }
}
