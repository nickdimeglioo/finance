using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinanceTracker.Api.Features.Rules;

namespace FinanceTracker.Api.Services;

internal sealed class RulesetEvaluationContext
{
    public RulesetEvaluationContext(
        IReadOnlyDictionary<string, string> rawRow,
        IReadOnlyDictionary<string, object?> values)
    {
        RawRow = rawRow;
        Values = values;
    }

    public IReadOnlyDictionary<string, string> RawRow { get; }
    public IReadOnlyDictionary<string, object?> Values { get; }
}

internal static class RulesetConditionEvaluator
{
    internal static bool Matches(ClassificationConditionDto? condition, RulesetEvaluationContext context)
    {
        if (condition is null)
        {
            return true;
        }

        if (string.Equals(condition.Op, "AND", StringComparison.OrdinalIgnoreCase))
        {
            return (condition.Conditions ?? []).All(child => Matches(child, context));
        }

        if (string.Equals(condition.Op, "OR", StringComparison.OrdinalIgnoreCase))
        {
            return (condition.Conditions ?? []).Any(child => Matches(child, context));
        }

        var fieldValue = ReadField(condition.Field, context);
        var expected = ReadJsonValue(condition.Value);
        return condition.Op switch
        {
            "contains" => Contains(fieldValue, expected),
            "startsWith" => Text(fieldValue).StartsWith(Text(expected), StringComparison.OrdinalIgnoreCase),
            "endsWith" => Text(fieldValue).EndsWith(Text(expected), StringComparison.OrdinalIgnoreCase),
            "equals" => string.Equals(Text(fieldValue), Text(expected), StringComparison.OrdinalIgnoreCase),
            "regex" => Regex.IsMatch(Text(fieldValue), Text(expected), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
            "in" => IsIn(fieldValue, condition.Value),
            "notContains" => !Contains(fieldValue, expected),
            "isEmpty" => string.IsNullOrWhiteSpace(Text(fieldValue)),
            "isNotEmpty" => !string.IsNullOrWhiteSpace(Text(fieldValue)),
            "gt" => Number(fieldValue) > Number(expected),
            "lt" => Number(fieldValue) < Number(expected),
            "gte" => Number(fieldValue) >= Number(expected),
            "lte" => Number(fieldValue) <= Number(expected),
            _ => false
        };
    }

    internal static object? ReadField(string? field, RulesetEvaluationContext context)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return null;
        }

        var normalized = field.Trim();
        if (normalized.StartsWith("row.", StringComparison.OrdinalIgnoreCase))
        {
            return ReadRaw(normalized[4..], context);
        }

        if (normalized.StartsWith("mapped.", StringComparison.OrdinalIgnoreCase))
        {
            return ReadMapped(normalized[7..], context);
        }

        if (normalized.StartsWith("row[", StringComparison.OrdinalIgnoreCase) && normalized.EndsWith(']'))
        {
            var key = normalized[4..^1].Trim().Trim('"', '\'');
            return ReadRaw(key, context);
        }

        return ReadMapped(normalized, context) ?? ReadRaw(normalized, context);
    }

    internal static object? ReadJsonValue(JsonElement? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.TryGetDecimal(out var numeric) ? numeric : null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => value.Value.EnumerateArray().Select(item => ReadJsonValue(item)).ToList(),
            _ => value.Value.ToString()
        };
    }

    private static object? ReadMapped(string key, RulesetEvaluationContext context)
    {
        return context.Values.TryGetValue(key, out var value) ? value : null;
    }

    private static string? ReadRaw(string key, RulesetEvaluationContext context)
    {
        return context.RawRow.TryGetValue(key, out var value)
            ? value
            : context.RawRow.TryGetValue(CsvParserService.NormalizeColumnReference(key), out var normalized)
                ? normalized
                : null;
    }

    private static bool IsIn(object? fieldValue, JsonElement? value)
    {
        if (value is null || value.Value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var text = Text(fieldValue);
        return value.Value.EnumerateArray().Any(item => string.Equals(text, Text(ReadJsonValue(item)), StringComparison.OrdinalIgnoreCase));
    }

    private static bool Contains(object? fieldValue, object? expected)
    {
        return Text(fieldValue).Contains(Text(expected), StringComparison.OrdinalIgnoreCase);
    }

    private static string Text(object? value)
    {
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static decimal Number(object? value)
    {
        if (value is decimal d)
        {
            return d;
        }

        return decimal.TryParse(Text(value), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }
}
