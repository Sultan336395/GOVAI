using System.Text.Json;
using GovAI.Application.Eligibility;
using GovAI.Domain.Common;
using GovAI.Domain.Eligibility;
using GovAI.Domain.Scoring;

namespace GovAI.Application.Tests;

/// <summary>
/// Değerlendirme ayrıntı gövdesinin sözleşmesi.
///
/// <para>
/// Sahada görülen arıza: gövde isimsiz bir nesneyle yazılıyor, haftalık rapor ise onu
/// <c>EligibilityOutcome</c> olarak okumaya çalışıyordu. İki şekil hiç tutmadı ve
/// okuma <b>her seferinde</b> başarısız oldu. Sonuç sessiz bir yanlıştı: risk listesi
/// boş üretildi ve rapor "başvuruyu engelleyen bir eksik görünmüyor" diye yazdı —
/// oysa her değerlendirmede dört eksik zorunlu belge vardı.
/// </para>
///
/// <para>
/// Bu testler iki şeyi birden sabitliyor: gövde yazıldığı gibi okunabilir olmalı ve
/// alan adları değişmemeli (kayıtlı geçmiş onlarla duruyor).
/// </para>
/// </summary>
public class AssessmentDetailSnapshotTests
{
    private static EligibilityOutcome Sonuc() => new()
    {
        CompanyId = Guid.NewGuid(),
        OpportunityId = Guid.NewGuid(),
        EvaluatedAt = DateTimeOffset.UnixEpoch,
        Verdict = EligibilityVerdict.ConditionallyEligible,
        SectorFit = SectorFit.Matched,
        RuleEvaluations =
        [
            new RuleEvaluation
            {
                RuleId = Guid.NewGuid(),
                Field = "Workforce.EmployeeCount",
                Dimension = RuleDimension.Employment,
                Severity = RuleSeverity.Blocking,
                Outcome = RuleOutcome.NotSatisfied,
                Requirement = "Asgari 10 çalışan",
                ActualValue = "7",
                ExpectedValue = ">= 10",
                Strength = 0m,
                SuggestedAction = "Çalışan sayısını 10'a çıkarın.",
            },
            new RuleEvaluation
            {
                RuleId = Guid.NewGuid(),
                Field = "Financials.Revenue",
                Dimension = RuleDimension.Financial,
                Severity = RuleSeverity.Major,
                Outcome = RuleOutcome.Unknown,
                Requirement = "Asgari 1M ciro",
                ActualValue = "bilinmiyor",
                ExpectedValue = ">= 1000000",
                Strength = 0m,
            },
        ],
        DocumentChecklist =
        [
            new DocumentCheckResult
            {
                Code = "VERGI_BORCU_YOKTUR",
                Name = "Vergi Borcu Yoktur Yazısı",
                IsMandatory = true,
                Status = DocumentStatus.Missing,
                Action = "Gelir İdaresi Başkanlığı'ndan temin edin.",
            },
            new DocumentCheckResult
            {
                Code = "ISO14001",
                Name = "ISO 14001",
                IsMandatory = false,
                Status = DocumentStatus.Missing,
            },
        ],
        Score = new ScoreBreakdown
        {
            Dimensions = [],
            Weights = ScoreWeights.Default,
            FinalScore = 55m,
            HasBlockingFailure = true,
            Confidence = 0.7m,
        },
    };

    [Fact(DisplayName = "AG1. Yazılan gövde OKUNABİLİR — arızanın kendisi buydu")]
    public void Yazilan_govde_okunabilir()
    {
        var json = JsonSerializer.Serialize(
            AssessmentDetailSnapshot.From(Sonuc()), AssessmentDetailSnapshot.JsonOptions);

        var okunan = JsonSerializer.Deserialize<AssessmentDetailSnapshot>(
            json, AssessmentDetailSnapshot.JsonOptions);

        Assert.NotNull(okunan);
        Assert.Equal(2, okunan.RuleEvaluations.Count);
        Assert.Equal(2, okunan.DocumentChecklist.Count);
    }

    [Fact(DisplayName = "AG2. Riskin üç cinsi gövdeden doğru çıkarılır")]
    public void Risk_cinsleri_dogru_cikarilir()
    {
        var json = JsonSerializer.Serialize(
            AssessmentDetailSnapshot.From(Sonuc()), AssessmentDetailSnapshot.JsonOptions);

        var okunan = JsonSerializer.Deserialize<AssessmentDetailSnapshot>(
            json, AssessmentDetailSnapshot.JsonOptions)!;

        Assert.Single(okunan.BlockingFailures);
        Assert.Single(okunan.DataGaps);

        // İsteğe bağlı belge eksikliği risk değildir; yalnızca zorunlu olan sayılır.
        Assert.Single(okunan.MissingMandatoryDocuments);
        Assert.Equal("Vergi Borcu Yoktur Yazısı", okunan.MissingMandatoryDocuments[0].Name);
    }

    [Fact(DisplayName = "AG3. Alan adları DEĞİŞMEZ — kayıtlı geçmiş onlarla duruyor")]
    public void Alan_adlari_degismez()
    {
        var json = JsonSerializer.Serialize(
            AssessmentDetailSnapshot.From(Sonuc()), AssessmentDetailSnapshot.JsonOptions);

        // Bu dört ad veritabanındaki mevcut satırlarda bu şekilde yazılıdır. Birini
        // yeniden adlandırmak geçmiş raporların dayandığı veriyi okunamaz yapar.
        foreach (var ad in new[] { "RuleEvaluations", "DocumentChecklist", "Dimensions", "Weights" })
        {
            Assert.Contains($"\"{ad}\"", json, StringComparison.Ordinal);
        }
    }

    [Fact(DisplayName = "AG4. Eskiden yazılmış gövde (sayı numaralandırma, PascalCase) okunur")]
    public void Eski_govde_okunur()
    {
        // Üretimdeki kayıtların birebir biçimi: numaralandırmalar sayı, adlar PascalCase.
        const string eski = """
        {
          "RuleEvaluations": [
            {
              "RuleId": "01a05d9e-8945-70c3-b226-1618a1e3718b",
              "Field": "Company.Certificates",
              "Dimension": 4,
              "Severity": 3,
              "Outcome": 2,
              "Operator": 9,
              "Requirement": "ISO 9001 belgesi gerekli.",
              "ActualValue": "CE",
              "ExpectedValue": "tamamı gerekli: ISO9001",
              "Strength": 0,
              "NeedsData": false,
              "IsBlockingFailure": false
            }
          ],
          "DocumentChecklist": [
            {
              "Code": "VERGI_BORCU_YOKTUR",
              "Name": "Vergi Borcu Yoktur Yazısı",
              "IsMandatory": true,
              "Status": 0,
              "Action": "Gelir İdaresi Başkanlığı'ndan temin edin."
            }
          ],
          "Dimensions": [],
          "Weights": {
            "SectorMatch": 0.25, "FinancialFit": 0.20, "EmployeeFit": 0.15,
            "DocumentReadiness": 0.15, "RegionalCompliance": 0.10,
            "TechnicalQualification": 0.10, "Timing": 0.05
          }
        }
        """;

        var okunan = JsonSerializer.Deserialize<AssessmentDetailSnapshot>(
            eski, AssessmentDetailSnapshot.JsonOptions);

        Assert.NotNull(okunan);
        Assert.Single(okunan.RuleEvaluations);
        Assert.Equal(RuleOutcome.NotSatisfied, okunan.RuleEvaluations[0].Outcome);
        Assert.Single(okunan.MissingMandatoryDocuments);
    }
}
