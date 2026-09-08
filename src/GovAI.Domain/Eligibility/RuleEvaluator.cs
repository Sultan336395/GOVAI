using System.Globalization;
using GovAI.Domain.Common;
using GovAI.Domain.Companies;
using GovAI.Domain.Opportunities;

namespace GovAI.Domain.Eligibility;

/// <summary>
/// Uygunluk Kural Motoru (Modül 5). Tek bir kuralı firma verisiyle karşılaştırır.
/// Deterministiktir: aynı girdi her zaman aynı sonucu verir, AI burada devrede değildir.
/// AI yalnızca metinden kuralı çıkarırken ve sonucu yönetici diline çevirirken kullanılır.
/// </summary>
public static class RuleEvaluator
{
    public static RuleEvaluation Evaluate(OpportunityRule rule, Company company, DateOnly asOf)
    {
        var actual = CompanyFieldResolver.Resolve(company, rule.Field, asOf);
        var (outcome, strength) = Compare(rule, actual);

        return new RuleEvaluation
        {
            RuleId = rule.Id,
            Field = rule.Field,
            Dimension = rule.Dimension,
            Severity = rule.Severity,
            Outcome = outcome,
            Operator = rule.Operator,
            Requirement = rule.HumanReadable,
            ActualValue = actual.Display(),
            ExpectedValue = DescribeExpectation(rule),
            Strength = strength,
            SourceExcerpt = rule.SourceExcerpt,
            SuggestedAction = outcome == RuleOutcome.NotSatisfied || outcome == RuleOutcome.Unknown
                ? SuggestAction(rule, actual, outcome)
                : null
        };
    }

    private static (RuleOutcome Outcome, decimal Strength) Compare(OpportunityRule rule, FieldValue actual)
    {
        if (!actual.IsKnown)
        {
            return (RuleOutcome.Unknown, 0m);
        }

        return rule.Operator switch
        {
            RuleOperator.IsTrue => Binary(actual.Boolean == true),
            RuleOperator.IsFalse => Binary(actual.Boolean == false),

            RuleOperator.Equals => CompareText(actual, rule.Value, expectEqual: true),
            RuleOperator.NotEquals => CompareText(actual, rule.Value, expectEqual: false),

            RuleOperator.GreaterThan => Numeric(actual, rule.Value, (a, b) => a > b),
            RuleOperator.GreaterThanOrEqual => Numeric(actual, rule.Value, (a, b) => a >= b),
            RuleOperator.LessThan => Numeric(actual, rule.Value, (a, b) => a < b),
            RuleOperator.LessThanOrEqual => Numeric(actual, rule.Value, (a, b) => a <= b),

            RuleOperator.In => InList(actual, rule.ValueList(), expectPresent: true),
            RuleOperator.NotIn => InList(actual, rule.ValueList(), expectPresent: false),

            RuleOperator.ContainsAll => ContainsAll(actual, rule.ValueList()),
            RuleOperator.ContainsAny => ContainsAny(actual, rule.ValueList()),

            RuleOperator.NaceMatch => NaceMatch(actual, rule.ValueList()),

            _ => (RuleOutcome.Unknown, 0m)
        };
    }

    private static (RuleOutcome, decimal) Binary(bool satisfied) =>
        satisfied ? (RuleOutcome.Satisfied, 1m) : (RuleOutcome.NotSatisfied, 0m);

    private static (RuleOutcome, decimal) Numeric(FieldValue actual, string expected, Func<decimal, decimal, bool> predicate)
    {
        if (actual.Kind != FieldValueKind.Number || !TryParseDecimal(expected, out var threshold))
        {
            return (RuleOutcome.Unknown, 0m);
        }

        return Binary(predicate(actual.Number!.Value, threshold));
    }

    private static (RuleOutcome, decimal) CompareText(FieldValue actual, string expected, bool expectEqual)
    {
        if (actual.Kind == FieldValueKind.Number && TryParseDecimal(expected, out var number))
        {
            return Binary((actual.Number!.Value == number) == expectEqual);
        }

        if (actual.Kind == FieldValueKind.Boolean && bool.TryParse(expected, out var flag))
        {
            return Binary((actual.Boolean!.Value == flag) == expectEqual);
        }

        if (actual.Kind != FieldValueKind.Text)
        {
            return (RuleOutcome.Unknown, 0m);
        }

        var equal = string.Equals(actual.Text, expected.Trim(), StringComparison.OrdinalIgnoreCase);
        return Binary(equal == expectEqual);
    }

    private static (RuleOutcome, decimal) InList(FieldValue actual, IReadOnlyList<string> values, bool expectPresent)
    {
        if (values.Count == 0)
        {
            return (RuleOutcome.NotApplicable, 1m);
        }

        var present = actual.Kind switch
        {
            FieldValueKind.Text => values.Any(v => string.Equals(v, actual.Text, StringComparison.OrdinalIgnoreCase)),
            FieldValueKind.Number => values.Any(v => TryParseDecimal(v, out var n) && n == actual.Number),
            FieldValueKind.Set => actual.Set!.Any(item => values.Any(v => string.Equals(v, item, StringComparison.OrdinalIgnoreCase))),
            _ => (bool?)null
        } ?? false;

        return Binary(present == expectPresent);
    }

    private static (RuleOutcome, decimal) ContainsAll(FieldValue actual, IReadOnlyList<string> values)
    {
        if (actual.Kind != FieldValueKind.Set)
        {
            return (RuleOutcome.Unknown, 0m);
        }

        if (values.Count == 0)
        {
            return (RuleOutcome.NotApplicable, 1m);
        }

        var owned = values.Count(v => actual.Set!.Contains(v.Trim().ToUpperInvariant()));
        var ratio = (decimal)owned / values.Count;

        // Kısmen sahip olunan sertifika setinde derece bilgisi korunur; karar yine de ikili verilir.
        return owned == values.Count
            ? (RuleOutcome.Satisfied, 1m)
            : (RuleOutcome.NotSatisfied, Math.Round(ratio, 4));
    }

    private static (RuleOutcome, decimal) ContainsAny(FieldValue actual, IReadOnlyList<string> values)
    {
        if (actual.Kind != FieldValueKind.Set)
        {
            return (RuleOutcome.Unknown, 0m);
        }

        if (values.Count == 0)
        {
            return (RuleOutcome.NotApplicable, 1m);
        }

        var any = values.Any(v => actual.Set!.Contains(v.Trim().ToUpperInvariant()));
        return Binary(any);
    }

    private static (RuleOutcome, decimal) NaceMatch(FieldValue actual, IReadOnlyList<string> requiredCodes)
    {
        if (actual.Kind != FieldValueKind.Set)
        {
            return (RuleOutcome.Unknown, 0m);
        }

        var strength = NaceCode.BestMatch(actual.Set!, requiredCodes);

        // 0.6 ve üzeri eşleşmeler uygun sayılır; altındakiler sektörel uyumsuzluk olarak raporlanır.
        return strength >= 0.6m
            ? (RuleOutcome.Satisfied, strength)
            : (RuleOutcome.NotSatisfied, strength);
    }

    private static bool TryParseDecimal(string raw, out decimal value) =>
        decimal.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static string DescribeExpectation(OpportunityRule rule) => rule.Operator switch
    {
        RuleOperator.GreaterThan => $"> {rule.Value}",
        RuleOperator.GreaterThanOrEqual => $">= {rule.Value}",
        RuleOperator.LessThan => $"< {rule.Value}",
        RuleOperator.LessThanOrEqual => $"<= {rule.Value}",
        RuleOperator.Equals => $"= {rule.Value}",
        RuleOperator.NotEquals => $"!= {rule.Value}",
        RuleOperator.In => $"şunlardan biri: {rule.Value}",
        RuleOperator.NotIn => $"şunlardan hiçbiri: {rule.Value}",
        RuleOperator.ContainsAll => $"tamamı gerekli: {rule.Value}",
        RuleOperator.ContainsAny => $"en az biri gerekli: {rule.Value}",
        RuleOperator.NaceMatch => $"NACE: {rule.Value}",
        RuleOperator.IsTrue => "evet",
        RuleOperator.IsFalse => "hayır",
        _ => rule.Value
    };

    private static string SuggestAction(OpportunityRule rule, FieldValue actual, RuleOutcome outcome)
    {
        if (outcome == RuleOutcome.Unknown)
        {
            var label = CompanyFieldResolver.SupportedFields.TryGetValue(rule.Field, out var description)
                ? description
                : rule.Field;
            return $"Firma profilinde eksik alanı doldurun: {label}.";
        }

        return rule.Operator switch
        {
            RuleOperator.GreaterThan or RuleOperator.GreaterThanOrEqual =>
                $"{rule.HumanReadable} — mevcut {actual.Display()}, hedef {rule.Value}. Aradaki farkı kapatın.",
            RuleOperator.LessThan or RuleOperator.LessThanOrEqual =>
                $"{rule.HumanReadable} — mevcut {actual.Display()}, üst sınır {rule.Value}. Bu çağrı için ölçek sınırı aşılıyor.",
            RuleOperator.ContainsAll or RuleOperator.ContainsAny =>
                $"Eksik belge/sertifika temin edin: {rule.Value}.",
            RuleOperator.NaceMatch =>
                $"Çağrı şu NACE kodlarını hedefliyor: {rule.Value}. Faaliyet kodunuz kapsam dışında.",
            _ => $"Koşul sağlanmıyor: {rule.HumanReadable}."
        };
    }
}
