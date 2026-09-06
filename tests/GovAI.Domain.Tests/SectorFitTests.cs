using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Tests;

/// <summary>
/// Sektör uyumunun davranış sözleşmesi (Faz 2).
///
/// Sahada görülen hata: bir makine imalatçısına "Didi soğuk çay nakliye" ihalesi, bir
/// inşaat firmasına "Sivas YHT Garı buz çözme" işi 88 puanla "uygun" olarak önerildi.
/// Sebep motorun içindeydi — sektör kuralı olmayan bir çağrı sektör boyutundan TAM PUAN
/// alıyordu, yani "kural yok" sessizce "her sektör uygun" diye okunuyordu.
///
/// Bu testler üç şeyi birden sabitler:
/// 1. Kuralsız sektör tam puan almaz (ama sıfır da almaz — eksik veri firmayı elemez).
/// 2. "Bilmiyorum" ile "hayır" ayrı kararlardır.
/// 3. Sektör uyumsuzluğu tek başına firmayı ELEMEZ; yalnızca listenin sonuna atar.
/// </summary>
public class SectorFitTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Makine imalatı yapan firma: NACE 25.62 ve 62.01.</summary>
    private static Company MakineImalatcisi()
    {
        var company = new Company(Guid.CreateVersion7(), "Örnek Üretim A.Ş.", "1112223334", LegalType.JointStockCompany);
        company.UpdateIdentity("Örnek Üretim A.Ş.", LegalType.JointStockCompany, new DateOnly(2015, 1, 1));
        company.UpdateWorkforce(new Workforce(42, 14, 10, 7, 0));
        company.UpdateFinancials(new Financials(50_000_000m, 30_000_000m, 12_000_000m, 8_000_000m, "TRY", 2025));
        company.ReplaceNaceCodes([new CompanyNaceCode("2562", isPrimary: true), new CompanyNaceCode("6201", isPrimary: false)]);
        company.ReplaceLocations([new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: true)]);

        return company;
    }

    private static Opportunity Cagri(string baslik = "Test Çağrısı")
    {
        var opportunity = new Opportunity(
            Guid.CreateVersion7(), SourceType.OfficialGazette, SupportCategory.Tender,
            baslik, "Test İdaresi", Now.AddDays(-3));

        opportunity.SetSchedule(Now.AddDays(-3), Now.AddDays(20));

        return opportunity;
    }

    private static OpportunityRule SektorKurali(string naceListesi, RuleSeverity severity = RuleSeverity.Major) =>
        new("Company.NaceCodes", RuleOperator.NaceMatch, naceListesi,
            RuleDimension.Sector, severity, $"İlan konusu NACE {naceListesi} alanındadır.");

    [Fact(DisplayName = "SF1. Sektör kuralı olmayan çağrı, sektör boyutundan tam puan ALAMAZ")]
    public void Kuralsiz_cagri_sektorden_tam_puan_almaz()
    {
        // Regresyon: çay nakliye ihalesinin makine imalatçısında 100/100 sektör puanı alması.
        var outcome = EligibilityEngine.Evaluate(MakineImalatcisi(), Cagri(), Now);

        var sektor = outcome.Score.ScoreOf(RuleDimension.Sector);

        Assert.NotEqual(1.0m, sektor);
        Assert.Equal(0.5m, sektor);
    }

    [Fact(DisplayName = "SF2. Kuralsız çağrı sektör uyumu 'doğrulanamadı' sayılır")]
    public void Kuralsiz_cagri_dogrulanamadi_sayilir()
    {
        var outcome = EligibilityEngine.Evaluate(MakineImalatcisi(), Cagri(), Now);

        Assert.Equal(SectorFit.Unverified, outcome.SectorFit);
    }

    [Fact(DisplayName = "SF3. Sektörü tutan çağrı, sektörü doğrulanamayan çağrıdan yüksek skor alır")]
    public void Sektoru_tutan_cagri_daha_yuksek_skor_alir()
    {
        var firma = MakineImalatcisi();

        var uyumlu = Cagri("Makine Alımı");
        uyumlu.ReplaceRules([SektorKurali("28,25")], extractionConfidence: 0.7m);

        var belirsiz = Cagri("Yatırımcılara Duyuru");

        var uyumluSkor = EligibilityEngine.Evaluate(firma, uyumlu, Now).Score.FinalScore;
        var belirsizSkor = EligibilityEngine.Evaluate(firma, belirsiz, Now).Score.FinalScore;

        Assert.True(
            uyumluSkor > belirsizSkor,
            $"Sektörü tutan çağrı ({uyumluSkor}) doğrulanamayanın ({belirsizSkor}) üstünde olmalı.");
    }

    [Fact(DisplayName = "SF4. Sektörü tutan çağrı 'uyumlu' işaretlenir")]
    public void Sektoru_tutan_cagri_uyumlu_isaretlenir()
    {
        var opportunity = Cagri("Talaşlı İmalat Tezgâhı Alımı");
        opportunity.ReplaceRules([SektorKurali("28,25")], extractionConfidence: 0.7m);

        var outcome = EligibilityEngine.Evaluate(MakineImalatcisi(), opportunity, Now);

        Assert.Equal(SectorFit.Matched, outcome.SectorFit);
    }

    [Fact(DisplayName = "SF5. Sektörü tutmayan çağrı 'uyumsuz' işaretlenir")]
    public void Sektoru_tutmayan_cagri_uyumsuz_isaretlenir()
    {
        // Gerçek örnek: Çay İşletmeleri GM'nin nakliye ihalesi (NACE 49/52).
        var opportunity = Cagri("Didi Soğuk Çay Nakliye İşi");
        opportunity.ReplaceRules([SektorKurali("49.41,49.39,52.29")], extractionConfidence: 0.7m);

        var outcome = EligibilityEngine.Evaluate(MakineImalatcisi(), opportunity, Now);

        Assert.Equal(SectorFit.NotMatched, outcome.SectorFit);
    }

    [Fact(DisplayName = "SF6. Sektör uyumsuzluğu tek başına firmayı ELEMEZ")]
    public void Sektor_uyumsuzlugu_firmayi_elemez()
    {
        // İlanın konusundan çıkarılan sektör bir yeterlilik şartı değildir; firma hukuken
        // teklif verebilir. Kayıt listeden çıkarılmaz, yalnızca sona iner ve etiketlenir.
        var opportunity = Cagri("Didi Soğuk Çay Nakliye İşi");
        opportunity.ReplaceRules([SektorKurali("49.41,49.39,52.29")], extractionConfidence: 0.7m);

        var outcome = EligibilityEngine.Evaluate(MakineImalatcisi(), opportunity, Now);

        Assert.NotEqual(EligibilityVerdict.NotEligible, outcome.Verdict);
        Assert.False(outcome.Score.HasBlockingFailure);
        Assert.True(outcome.Score.FinalScore > 0m);
    }

    [Fact(DisplayName = "SF7. Uyumsuz çağrının skoru uyumlu çağrının altındadır")]
    public void Uyumsuz_cagri_uyumlunun_altinda_kalir()
    {
        var firma = MakineImalatcisi();

        var uyumlu = Cagri("Makine Alımı");
        uyumlu.ReplaceRules([SektorKurali("28,25")], extractionConfidence: 0.7m);

        var uyumsuz = Cagri("Didi Soğuk Çay Nakliye İşi");
        uyumsuz.ReplaceRules([SektorKurali("49.41,49.39,52.29")], extractionConfidence: 0.7m);

        Assert.True(
            EligibilityEngine.Evaluate(firma, uyumlu, Now).Score.FinalScore
            > EligibilityEngine.Evaluate(firma, uyumsuz, Now).Score.FinalScore);
    }

    [Fact(DisplayName = "SF8. Firmanın NACE kodu yoksa karar 'doğrulanamadı'dır, 'uyumsuz' değil")]
    public void Firma_nace_kodu_yoksa_dogrulanamadi_olur()
    {
        // CLAUDE.md §2.2: eksik veri firmayı elemez. Boş NACE kümesi "hiçbir sektörde
        // faaliyet göstermiyor" değil, "girilmemiş" demektir.
        var firma = new Company(Guid.CreateVersion7(), "Veri Eksik A.Ş.", "9999999999", LegalType.LimitedCompany);

        var opportunity = Cagri("Makine Alımı");
        opportunity.ReplaceRules([SektorKurali("28,25")], extractionConfidence: 0.7m);

        var outcome = EligibilityEngine.Evaluate(firma, opportunity, Now);

        Assert.Equal(SectorFit.Unverified, outcome.SectorFit);
        Assert.NotEqual(EligibilityVerdict.NotEligible, outcome.Verdict);
    }

    [Fact(DisplayName = "SF9. Karşılanmayan ana sektör koşulu, sağlanan bir başkasıyla telafi edilmez")]
    public void Karsilanmayan_ana_kosul_telafi_edilmez()
    {
        var opportunity = Cagri("Karma Konulu İlan");
        opportunity.ReplaceRules(
        [
            SektorKurali("6201"),                              // firma tutuyor
            SektorKurali("49.41", RuleSeverity.Major)          // firma tutmuyor
        ], extractionConfidence: 0.7m);

        var outcome = EligibilityEngine.Evaluate(MakineImalatcisi(), opportunity, Now);

        Assert.Equal(SectorFit.NotMatched, outcome.SectorFit);
    }

    [Fact(DisplayName = "SF10. Yalnızca avantaj kuralı varsa, sağlanmaması 'uyumsuz' sayılmaz")]
    public void Yalnizca_avantaj_kurali_uyumsuzluk_uretmez()
    {
        var opportunity = Cagri("Öncelikli Sektör Avantajı Olan Çağrı");
        opportunity.ReplaceRules([SektorKurali("49.41", RuleSeverity.Bonus)], extractionConfidence: 0.7m);

        var outcome = EligibilityEngine.Evaluate(MakineImalatcisi(), opportunity, Now);

        Assert.Equal(SectorFit.Unverified, outcome.SectorFit);
    }

    [Fact(DisplayName = "SF11. Sıralama değeri listedeki sırayı verir: uyumlu → doğrulanamadı → uyumsuz")]
    public void Siralama_degeri_liste_sirasini_verir()
    {
        // Repository sıralaması bu sayısal sıraya güvenir (OrderBy(SectorFit)). Değerler
        // değişirse liste sessizce ters döner; bu yüzden burada sabitlenir.
        Assert.True((int)SectorFit.Matched < (int)SectorFit.Unverified);
        Assert.True((int)SectorFit.Unverified < (int)SectorFit.NotMatched);
    }

    [Fact(DisplayName = "SF12. Aynı girdi her zaman aynı sektör kararını verir")]
    public void Karar_deterministiktir()
    {
        var firma = MakineImalatcisi();
        var opportunity = Cagri("Didi Soğuk Çay Nakliye İşi");
        opportunity.ReplaceRules([SektorKurali("49.41,49.39,52.29")], extractionConfidence: 0.7m);

        var ilk = EligibilityEngine.Evaluate(firma, opportunity, Now);
        var ikinci = EligibilityEngine.Evaluate(firma, opportunity, Now);

        Assert.Equal(ilk.SectorFit, ikinci.SectorFit);
        Assert.Equal(ilk.Score.FinalScore, ikinci.Score.FinalScore);
    }

    [Fact(DisplayName = "SF13. Sektör gerekçesi kullanıcıya gösterilebilir bir metindir")]
    public void Gerekce_kullaniciya_gosterilebilir()
    {
        var outcome = EligibilityEngine.Evaluate(MakineImalatcisi(), Cagri(), Now);

        var sektor = outcome.Score.Dimensions.Single(d => d.Dimension == RuleDimension.Sector);

        Assert.Contains("sektör", sektor.Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".", sektor.Rationale, StringComparison.Ordinal);
    }
}
