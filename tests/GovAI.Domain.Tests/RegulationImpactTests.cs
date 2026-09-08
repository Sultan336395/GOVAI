using GovAI.Domain.Analysis;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Regulatory;

namespace GovAI.Domain.Tests;

/// <summary>
/// Şirket–mevzuat etki analizinin davranış sözleşmesi (Faz 3 — Aşama 1).
///
/// Burada üretilen şey <b>hukuki görüş değildir</b>: belgede açıkça yazan ifadelere
/// dayanan bir ön incelemedir. Bu yüzden en sık sonuç "kapsaması muhtemel"dir ve her
/// çıktı doğrulanması gereken eksikleri listeler.
///
/// Kritik davranış: "bilmiyorum" kapsam dışı sayılmaz. Firma haberi olmadığı bir
/// yükümlülüğe düşerse zarar sistemin sessizliğinden doğar; bu yüzden kapsam dışı
/// demek için AÇIK bir gerekçe aranır.
/// </summary>
public class RegulationImpactTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static Company Firma(int calisan = 42, bool ihracat = false)
    {
        var company = new Company(Guid.CreateVersion7(), "Örnek Üretim A.Ş.", "1112223334", LegalType.JointStockCompany);
        company.UpdateIdentity("Örnek Üretim A.Ş.", LegalType.JointStockCompany, new DateOnly(2015, 1, 1));
        company.UpdateWorkforce(new Workforce(calisan, 14, 10, 7, 2, youngEmployeeMaxAge: 29));
        company.UpdateSectors("İmalat", null, null);
        company.UpdateFlags(ihracat, technologyFlag: false, previousSuccessfulApplications: 0);
        company.ReplaceNaceCodes([new CompanyNaceCode("2562", isPrimary: true)]);
        company.ReplaceLocations([new CompanyLocation("Mersin", "Yenişehir", "TR62", isHeadquarters: true)]);

        return company;
    }

    private static RegulatoryChange Mevzuat(
        RegulationDomain alan = RegulationDomain.SocialSecurity,
        string jurisdiction = "TR",
        DateTimeOffset? yururluk = null,
        RegulatoryChangeStatus? durum = null)
    {
        var change = new RegulatoryChange(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            jurisdiction, "Sosyal Güvenlik Kurumu", alan, RegulatoryChangeType.Communique,
            "Muhtasar ve Prim Hizmet Beyannamesi Süresinin Uzatılması",
            "https://www.sgk.gov.tr/duyuru/detay/1", new string('a', 64), Now.AddDays(-10));

        change.Describe(null, Now.AddDays(-10), yururluk ?? Now.AddDays(-5), null);

        if (durum == RegulatoryChangeStatus.Verified)
        {
            change.MarkVerified(Now.AddDays(-1));
        }

        return change;
    }

    private static RegulationEvidence Kanit(string metin, string? bolum = null) => new()
    {
        EvidenceChunkId = Guid.CreateVersion7(),
        DocumentVersionId = Guid.CreateVersion7(),
        Excerpt = metin,
        Locator = bolum
    };

    private static CriterionResult Kriter(CompanyRegulationImpact etki, string kod) =>
        etki.Criteria.Single(c => c.Code == kod);

    [Fact(DisplayName = "M1. Açık yükümlülük içeren yürürlükteki düzenleme firmayı kapsar")]
    public void Acik_yukumluluk_kapsar()
    {
        var kanitlar = new List<RegulationEvidence>
        {
            Kanit("İşverenler, aylık prim ve hizmet belgelerini süresi içinde vermek zorundadır."),
            Kanit("Sigorta primi ödemelerinde uzatılan süre uygulanır.")
        };

        var etki = RegulationImpactEvaluator.Evaluate(Firma(), Mevzuat(), kanitlar, Now);

        Assert.Equal(RegulationImpact.Applicable, etki.Impact);
    }

    [Fact(DisplayName = "M2. Yürürlüğe girmemiş düzenleme kesin kapsıyor sayılmaz")]
    public void Yururluge_girmemis_duzenleme_kesin_degildir()
    {
        var kanitlar = new List<RegulationEvidence>
        {
            Kanit("İşverenler yeni bildirimi yapmak zorundadır.")
        };

        var etki = RegulationImpactEvaluator.Evaluate(
            Firma(), Mevzuat(yururluk: Now.AddDays(30)), kanitlar, Now);

        Assert.NotEqual(RegulationImpact.Applicable, etki.Impact);
        Assert.Equal(RegulationImpact.PotentiallyApplicable, etki.Impact);
        Assert.Equal(CriterionOutcome.NotMet, Kriter(etki, CriterionCatalog.RegulationEffectiveDate).Outcome);
    }

    [Fact(DisplayName = "M3. Yürürlük tarihi yazmayan belgede tarih tahmin edilmez")]
    public void Yururluk_tarihi_tahmin_edilmez()
    {
        var change = new RegulatoryChange(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "TR", "SGK", RegulationDomain.SocialSecurity, RegulatoryChangeType.Communique,
            "Tarihsiz Tebliğ", "https://www.sgk.gov.tr/duyuru/detay/2", new string('b', 64), Now.AddDays(-10));

        change.Describe(null, Now.AddDays(-10), null, null);

        var etki = RegulationImpactEvaluator.Evaluate(
            Firma(), change, [Kanit("İşverenler bildirmek zorundadır.")], Now);

        var kriter = Kriter(etki, CriterionCatalog.RegulationEffectiveDate);

        Assert.Equal(CriterionOutcome.Unknown, kriter.Outcome);
        Assert.Contains("yayın tarihi yürürlük tarihi sayılmaz", kriter.MissingOrConflictExplanation);
    }

    [Fact(DisplayName = "M4. Çalışan sayısı girilmemişse işverenlik durumu Unknown kalır")]
    public void Calisan_sayisi_yoksa_isverenlik_bilinmez()
    {
        var profilsiz = new Company(Guid.CreateVersion7(), "Yeni Ltd. Şti.", "9998887776", LegalType.LimitedCompany);

        var etki = RegulationImpactEvaluator.Evaluate(
            profilsiz, Mevzuat(), [Kanit("İşverenler bildirmek zorundadır.")], Now);

        var kriter = Kriter(etki, CriterionCatalog.RegulationEmployerStatus);

        Assert.Equal(CriterionOutcome.Unknown, kriter.Outcome);
        // Bilinmeyen kapsam dışı sayılmaz.
        Assert.NotEqual(RegulationImpact.NotApplicable, etki.Impact);
    }

    [Fact(DisplayName = "M5. AB düzenlemesi ihracat yapmayan firmada kapsam dışıdır")]
    public void Ab_duzenlemesi_ihracatsiz_firmada_kapsam_disi()
    {
        var etki = RegulationImpactEvaluator.Evaluate(
            Firma(ihracat: false),
            Mevzuat(alan: RegulationDomain.CommercialLaw, jurisdiction: "EU"),
            [Kanit("İşletmeler raporlamakla yükümlüdür.")],
            Now);

        Assert.Equal(CriterionOutcome.NotMet, Kriter(etki, CriterionCatalog.RegulationJurisdiction).Outcome);
        Assert.Equal(RegulationImpact.NotApplicable, etki.Impact);
    }

    [Fact(DisplayName = "M6. AB düzenlemesi ihracatçı firmada kesin kapsam dışı sayılmaz")]
    public void Ab_duzenlemesi_ihracatcida_belirsizdir()
    {
        var etki = RegulationImpactEvaluator.Evaluate(
            Firma(ihracat: true),
            Mevzuat(alan: RegulationDomain.CommercialLaw, jurisdiction: "EU"),
            [Kanit("İşletmeler raporlamakla yükümlüdür.")],
            Now);

        Assert.Equal(CriterionOutcome.Unknown, Kriter(etki, CriterionCatalog.RegulationJurisdiction).Outcome);
        Assert.NotEqual(RegulationImpact.NotApplicable, etki.Impact);
    }

    [Fact(DisplayName = "M7. Belgede yükümlülük yazmıyorsa yükümlülük üretilmez")]
    public void Yazmayan_yukumluluk_uretilmez()
    {
        var etki = RegulationImpactEvaluator.Evaluate(
            Firma(), Mevzuat(), [Kanit("Kurumumuzun sosyal güvenlik haftası etkinlikleri duyurulur.")], Now);

        var kriter = Kriter(etki, CriterionCatalog.RegulationObligations);

        Assert.Equal(CriterionOutcome.Unknown, kriter.Outcome);
        Assert.Contains("üretilmez", kriter.MissingOrConflictExplanation);
    }

    [Fact(DisplayName = "M8. KOBİ ve büyük ölçek ifadeleri birlikte geçerse çelişki bildirilir")]
    public void Kobi_ve_buyuk_olcek_celiskisi()
    {
        var kanitlar = new List<RegulationEvidence>
        {
            Kanit("Küçük ve orta ölçekli işletmeler için istisna uygulanır."),
            Kanit("Bağımsız denetime tabi işletmeler ayrıca bildirim yapar.")
        };

        var etki = RegulationImpactEvaluator.Evaluate(Firma(), Mevzuat(), kanitlar, Now);
        var kriter = Kriter(etki, CriterionCatalog.RegulationCompanySize);

        Assert.Equal(CriterionOutcome.ConflictingEvidence, kriter.Outcome);
        Assert.Equal(2, kriter.Evidence.Count);
    }

    [Fact(DisplayName = "M9. Ölçek ayrımı yapmayan belge NotApplicable üretir")]
    public void Olcek_ayrimi_yoksa_notapplicable()
    {
        var etki = RegulationImpactEvaluator.Evaluate(
            Firma(), Mevzuat(), [Kanit("İşverenler bildirmek zorundadır.")], Now);

        Assert.Equal(CriterionOutcome.NotApplicable, Kriter(etki, CriterionCatalog.RegulationCompanySize).Outcome);
    }

    [Fact(DisplayName = "M10. Her bulgu kendi kanıt parçasının kimliğine bağlıdır")]
    public void Bulgular_kanit_kimligine_baglidir()
    {
        var kanit = Kanit("İşverenler aylık bildirim yapmak zorundadır.");

        var etki = RegulationImpactEvaluator.Evaluate(Firma(), Mevzuat(), [kanit], Now);
        var yukumluluk = Kriter(etki, CriterionCatalog.RegulationObligations);

        Assert.Single(yukumluluk.Evidence);
        Assert.Equal(kanit.EvidenceChunkId, yukumluluk.Evidence[0].EvidenceChunkId);
        Assert.Equal(kanit.DocumentVersionId, yukumluluk.Evidence[0].DocumentVersionId);
    }

    [Fact(DisplayName = "M11. Türkçe büyük harf farkı eşleşmeyi bozmaz")]
    public void Turkce_katlama_eslesmeyi_bozmaz()
    {
        // "İŞVEREN" ile "isveren" ToUpperInvariant ile eşleşmez; katlama şart.
        var etki = RegulationImpactEvaluator.Evaluate(
            Firma(), Mevzuat(), [Kanit("İŞVERENLER BİLDİRİM YAPMAK ZORUNDADIR.")], Now);

        Assert.Equal(CriterionOutcome.Met, Kriter(etki, CriterionCatalog.RegulationEmployerStatus).Outcome);
        Assert.Equal(CriterionOutcome.Met, Kriter(etki, CriterionCatalog.RegulationObligations).Outcome);
    }

    [Fact(DisplayName = "M12. Sonuç her zaman hukuki uyarıyla birlikte gelir")]
    public void Hukuki_uyari_her_zaman_vardir()
    {
        Assert.Contains("hukuki görüş değildir", CompanyRegulationImpact.LegalDisclaimer);
        Assert.Contains("doğrulanmalıdır", CompanyRegulationImpact.LegalDisclaimer);
    }

    [Fact(DisplayName = "M13. Doğrulanması gereken eksikler kullanıcıya listelenir")]
    public void Acik_sorular_listelenir()
    {
        var profilsiz = new Company(Guid.CreateVersion7(), "Yeni Ltd. Şti.", "9998887776", LegalType.LimitedCompany);

        var etki = RegulationImpactEvaluator.Evaluate(
            profilsiz, Mevzuat(), [Kanit("İşverenler bildirmek zorundadır.")], Now);

        Assert.NotEmpty(etki.OpenQuestions);
        Assert.All(etki.OpenQuestions, s => Assert.False(string.IsNullOrWhiteSpace(s)));
    }

    [Fact(DisplayName = "M14. Doğrulanmış kayıt, doğrulanmamıştan yüksek güven alır")]
    public void Dogrulanmis_kayit_daha_guvenlidir()
    {
        var kanitlar = new List<RegulationEvidence> { Kanit("İşverenler bildirmek zorundadır.") };

        var taslak = RegulationImpactEvaluator.Evaluate(Firma(), Mevzuat(), kanitlar, Now);
        var dogrulanmis = RegulationImpactEvaluator.Evaluate(
            Firma(), Mevzuat(durum: RegulatoryChangeStatus.Verified), kanitlar, Now);

        Assert.True(dogrulanmis.Confidence.Value > taslak.Confidence.Value);
    }

    [Fact(DisplayName = "M15. Mevzuat etkisinde mali veri güvene katılmaz")]
    public void Mali_veri_guvene_katilmaz()
    {
        var etki = RegulationImpactEvaluator.Evaluate(
            Firma(), Mevzuat(), [Kanit("İşverenler bildirmek zorundadır.")], Now);

        var mali = etki.Confidence.Factors.Single(f => f.Code == ConfidenceFactors.FinancialFreshness);

        Assert.True(mali.NotMeasured);
    }

    [Fact(DisplayName = "M16. Mevzuat kriterlerinde zorunlu kriter yoktur")]
    public void Mevzuatta_zorunlu_kriter_yoktur()
    {
        var etki = RegulationImpactEvaluator.Evaluate(
            Firma(), Mevzuat(), [Kanit("İşverenler bildirmek zorundadır.")], Now);

        Assert.All(etki.Criteria, k => Assert.False(k.IsMandatory));
    }

    [Fact(DisplayName = "M17. Aynı girdi aynı sonucu üretir (deterministik)")]
    public void Ayni_girdi_ayni_sonuc()
    {
        var firma = Firma();
        var mevzuat = Mevzuat();
        var kanitlar = new List<RegulationEvidence> { Kanit("İşverenler bildirmek zorundadır.") };

        var ilk = RegulationImpactEvaluator.Evaluate(firma, mevzuat, kanitlar, Now);
        var ikinci = RegulationImpactEvaluator.Evaluate(firma, mevzuat, kanitlar, Now);

        Assert.Equal(ilk.Impact, ikinci.Impact);
        Assert.Equal(ilk.Confidence.Value, ikinci.Confidence.Value);
    }
}
