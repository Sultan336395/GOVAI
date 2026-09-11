using GovAI.Application.Reporting;
using GovAI.Domain.Common;
using GovAI.Domain.Reporting;

namespace GovAI.Application.Tests;

/// <summary>
/// Rapora özel soru üretimi ve cevaplar.
///
/// <para>
/// Bu özelliğin iki sözü var ve testler ikisini de sabitliyor:
/// </para>
///
/// <para>
/// 1. <b>Soru rapordan çıkar.</b> Raporda karşılığı olmayan soru gösterilmez; aksi hâlde
///    kullanıcı hakkını "bu konuda veri yok" cevabına harcar.
/// </para>
///
/// <para>
/// 2. <b>Cevap uydurmaz.</b> Her cevap raporun kendi verisine dayanır ve deterministiktir;
///    aynı rapor her zaman aynı cevabı verir.
/// </para>
/// </summary>
public class ReportQuestionTests
{
    private static ReportOpportunityItem Cagri(
        string baslik,
        EligibilityVerdict karar = EligibilityVerdict.ConditionallyEligible,
        decimal skor = 70m,
        SectorFit sektor = SectorFit.Matched,
        string[]? eksikler = null) =>
        new(
            Guid.CreateVersion7(),
            baslik,
            "KOSGEB",
            SupportCategory.Grant,
            "Hibe",
            skor,
            karar,
            karar == EligibilityVerdict.Eligible ? "Uygun" : "Şartlı uygun",
            sektor,
            sektor == SectorFit.Matched ? "Sektör uyumlu" : "Sektör uyumu doğrulanamadı",
            DateTimeOffset.UtcNow.AddDays(20),
            20,
            null,
            eksikler ?? []);

    private static ReportRiskItem Risk(
        ReportRiskKind cins,
        string konu,
        int etkilenen = 1,
        string? aksiyon = "Yapılacak iş.") =>
        new(cins, cins.ToString(), konu, $"{konu} açıklaması", aksiyon, etkilenen);

    private static WeeklyReportContent Rapor(
        IReadOnlyList<ReportOpportunityItem>? destekler = null,
        IReadOnlyList<ReportOpportunityItem>? teknoloji = null,
        IReadOnlyList<ReportOpportunityItem>? diger = null,
        IReadOnlyList<ReportRiskItem>? riskler = null,
        IReadOnlyList<ReportRiskItem>? gecmis = null,
        IReadOnlyList<ReportDeadlineItem>? takvim = null,
        IReadOnlyList<ReportTodoItem>? isler = null,
        IReadOnlyList<ReportRegulatoryItem>? mevzuat = null) =>
        new()
        {
            Header = new WeeklyReportHeader(
                Guid.CreateVersion7(), "Örnek A.Ş.",
                new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 13),
                DateTimeOffset.UtcNow, 10, 3, 1),
            Supports = destekler ?? [],
            TechnologyTenders = teknoloji ?? [],
            OtherOpportunities = diger ?? [],
            RegulatoryChanges = mevzuat ?? [],
            Risks = riskler ?? [],
            PastPeriodGaps = gecmis ?? [],
            Deadlines = takvim ?? [],
            Todos = isler ?? [],
            Notes = [],
        };

    // ── Sorular rapordan çıkar ──────────────────────────────────────────────

    [Fact(DisplayName = "SR1. Boş raporda soru ÜRETİLMEZ")]
    public void Bos_raporda_soru_uretilmez()
    {
        // Karşılığı olmayan soru göstermek, kullanıcıya hakkını "veri yok" cevabına
        // harcatmaktır.
        Assert.Empty(ReportQuestionCatalog.Build(Rapor()));
    }

    [Fact(DisplayName = "SR2. Risk yoksa 'hangi eksiğimi kapatayım' sorulmaz")]
    public void Risksiz_raporda_eksik_sorusu_yok()
    {
        var sorular = ReportQuestionCatalog.Build(Rapor(destekler: [Cagri("Hibe")]));

        Assert.DoesNotContain(sorular, s => s.Kind == ReportQuestionKind.EnEtkiliEksik);
    }

    [Fact(DisplayName = "SR3. Çağrıya özel soru yalnızca ŞARTLI uygun çağrılar için çıkar")]
    public void Cagri_sorusu_yalnizca_sartlilar_icin()
    {
        // Tam uygun bir çağrıya "neden tam uygun değilim" diye sormak anlamsızdır.
        var rapor = Rapor(destekler:
        [
            Cagri("Tam Uygun Hibe", EligibilityVerdict.Eligible),
            Cagri("Şartlı Hibe", EligibilityVerdict.ConditionallyEligible),
        ]);

        var cagriSorulari = ReportQuestionCatalog.Build(rapor)
            .Where(s => s.Kind == ReportQuestionKind.CagriNedenSartli)
            .ToList();

        var soru = Assert.Single(cagriSorulari);
        Assert.Contains("Şartlı Hibe", soru.Text, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SR4. Aynı rapor her zaman AYNI soruları verir")]
    public void Sorular_deterministiktir()
    {
        var rapor = Rapor(
            destekler: [Cagri("A Hibesi"), Cagri("B Hibesi")],
            riskler: [Risk(ReportRiskKind.MissingDocument, "SGK Borcu Yoktur")]);

        var bir = ReportQuestionCatalog.Build(rapor).Select(s => s.Key);
        var iki = ReportQuestionCatalog.Build(rapor).Select(s => s.Key);

        Assert.Equal(bir, iki);
    }

    [Fact(DisplayName = "SR5. Devam soruları SORULMUŞ olanı tekrar önermez")]
    public void Devam_sorulari_tekrar_onermez()
    {
        // Kullanıcı aynı cevabı ikinci kez okumak için hak harcamamalı.
        var rapor = Rapor(
            destekler: [Cagri("Hibe")],
            riskler:
            [
                Risk(ReportRiskKind.MissingDocument, "ISO 9001"),
                Risk(ReportRiskKind.DataGap, "Yıllık ciro"),
            ]);

        var sonrakiler = ReportQuestionCatalog.FollowUps(
            ReportQuestionKind.EnEtkiliEksik,
            rapor,
            [ReportQuestionKind.EksikBelgeler.ToString()]);

        Assert.DoesNotContain(sonrakiler, s => s.Kind == ReportQuestionKind.EksikBelgeler);
        Assert.Contains(sonrakiler, s => s.Kind == ReportQuestionKind.EksikProfilEtkisi);
    }

    // ── Cevaplar rapordan çıkar ─────────────────────────────────────────────

    [Fact(DisplayName = "SR6. 'En etkili eksik' cevabı EN ÇOK çağrıyı etkileyeni söyler")]
    public void En_etkili_eksik_dogru_secilir()
    {
        var rapor = Rapor(riskler:
        [
            Risk(ReportRiskKind.MissingDocument, "Az Etkili Belge", etkilenen: 1),
            Risk(ReportRiskKind.MissingDocument, "Çok Etkili Belge", etkilenen: 7),
        ]);

        var soru = ReportQuestionCatalog.Build(rapor)
            .Single(s => s.Kind == ReportQuestionKind.EnEtkiliEksik);

        var cevap = ReportAnswerBuilder.Build(soru, rapor);

        Assert.Contains("Çok Etkili Belge", cevap, StringComparison.Ordinal);
        Assert.Contains("7 çağrıyı", cevap, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SR7. Aksiyonu olmayan eksik için ÖNERİ UYDURULMAZ")]
    public void Aksiyonsuz_eksikte_oneri_uydurulmaz()
    {
        var rapor = Rapor(riskler:
            [Risk(ReportRiskKind.BlockingCondition, "Asgari 10 çalışan", aksiyon: null)]);

        var soru = ReportQuestionCatalog.Build(rapor)
            .Single(s => s.Kind == ReportQuestionKind.EnEtkiliEksik);

        var cevap = ReportAnswerBuilder.Build(soru, rapor);

        Assert.Contains("danışman değerlendirmesi", cevap, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "SR8. Başvuru sırası önce KAPANMA TARİHİNE bakar")]
    public void Basvuru_sirasi_tarihe_bakar()
    {
        // Yüksek skorlu ama iki ay sonra kapanan bir çağrı, bu hafta kapanandan sonra gelir.
        var rapor = Rapor(takvim:
        [
            new(Guid.CreateVersion7(), "Uzak Ama Yüksek Skorlu", DateTimeOffset.UtcNow.AddDays(50),
                50, 95m, EligibilityVerdict.Eligible, "Uygun", SectorFit.Matched, "Sektör uyumlu"),
            new(Guid.CreateVersion7(), "Yakın Ama Düşük Skorlu", DateTimeOffset.UtcNow.AddDays(3),
                3, 40m, EligibilityVerdict.ConditionallyEligible, "Şartlı uygun",
                SectorFit.Matched, "Sektör uyumlu"),
        ]);

        var soru = ReportQuestionCatalog.Build(rapor)
            .Single(s => s.Kind == ReportQuestionKind.BasvuruOnceligi);

        var cevap = ReportAnswerBuilder.Build(soru, rapor);

        Assert.Contains("Yakın Ama Düşük Skorlu", cevap, StringComparison.Ordinal);
        Assert.StartsWith("Önce **Yakın Ama Düşük Skorlu**", cevap, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SR9. Eksik koşulu olmayan şartlı çağrıda sebep VERİ EKSİĞİ denir")]
    public void Sartli_ama_eksiksiz_cagrida_sebep_aciklanir()
    {
        // Kullanıcıyı olmayan bir koşulu aramaya itmemek gerekir: sebep eksik veridir.
        var cagri = Cagri("Şartlı Hibe", EligibilityVerdict.ConditionallyEligible, eksikler: []);

        var rapor = Rapor(
            destekler: [cagri],
            riskler: [Risk(ReportRiskKind.DataGap, "Yıllık ciro")]);

        var soru = ReportQuestionCatalog.Build(rapor)
            .Single(s => s.Kind == ReportQuestionKind.CagriNedenSartli);

        var cevap = ReportAnswerBuilder.Build(soru, rapor);

        Assert.Contains("eksik bilgi", cevap, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Yıllık ciro", cevap, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SR10. Sektör cevabı 'uymuyor' DEMEZ, doğrulanamadığını söyler")]
    public void Sektor_cevabi_elemez()
    {
        var rapor = Rapor(destekler:
            [Cagri("Hibe", sektor: SectorFit.Unverified)]);

        var soru = ReportQuestionCatalog.Build(rapor)
            .Single(s => s.Kind == ReportQuestionKind.SektorUyumu);

        var cevap = ReportAnswerBuilder.Build(soru, rapor);

        Assert.Contains("DEĞİLDİR", cevap, StringComparison.Ordinal);
        Assert.Contains("elemez", cevap, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SR11. Mevzuat cevabı etkinin HESAPLANMADIĞINI söyler")]
    public void Mevzuat_cevabi_sinirini_soyler()
    {
        // Bunu söylememek, kullanıcının "sistem baktı, sorun yok" sanmasına yol açar.
        var rapor = Rapor(mevzuat:
        [
            new(Guid.CreateVersion7(), "SUT Değişiklik Tebliği", "SGK",
                DateTimeOffset.UtcNow.AddDays(-2), null, null),
        ]);

        var soru = ReportQuestionCatalog.Build(rapor)
            .Single(s => s.Kind == ReportQuestionKind.MevzuatEtkisi);

        var cevap = ReportAnswerBuilder.Build(soru, rapor);

        Assert.Contains("hesaplamaz", cevap, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "SR12. Geçmiş dönem cevabı 'şimdi yapılacak iş yok' der")]
    public void Gecmis_donem_cevabi_is_vermez()
    {
        var rapor = Rapor(gecmis:
            [Risk(ReportRiskKind.MissingDocument, "SGK Borcu Yoktur")]);

        var soru = ReportQuestionCatalog.Build(rapor)
            .Single(s => s.Kind == ReportQuestionKind.GecmisDonemEksikleri);

        var cevap = ReportAnswerBuilder.Build(soru, rapor);

        Assert.Contains("şimdi yapılacak bir iş yoktur", cevap, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SR13. Cevap deterministiktir")]
    public void Cevap_deterministiktir()
    {
        var rapor = Rapor(riskler:
        [
            Risk(ReportRiskKind.MissingDocument, "ISO 9001", etkilenen: 3),
            Risk(ReportRiskKind.DataGap, "Yıllık ciro", etkilenen: 3),
        ]);

        var soru = ReportQuestionCatalog.Build(rapor)
            .Single(s => s.Kind == ReportQuestionKind.EnEtkiliEksik);

        Assert.Equal(
            ReportAnswerBuilder.Build(soru, rapor),
            ReportAnswerBuilder.Build(soru, rapor));
    }

    // ── Hak sayacı ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "SR14. Rapor başına beş hak vardır")]
    public void Rapor_basina_bes_hak()
    {
        Assert.Equal(5, ReportInquiryQuota.PerReport);
        Assert.Equal(5, ReportInquiryQuota.Remaining(0));
        Assert.Equal(2, ReportInquiryQuota.Remaining(3));
        Assert.Equal(0, ReportInquiryQuota.Remaining(5));

        // Sayaç eksiye düşmez: fazladan kayıt olsa bile "kalan" negatif gösterilmez.
        Assert.Equal(0, ReportInquiryQuota.Remaining(9));
        Assert.False(ReportInquiryQuota.HasRemaining(5));
    }
}
