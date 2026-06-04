using System.Globalization;
using FinanceTracker.Api.Features.Imports;
using FinanceTracker.Api.Features.Rules;
using FinanceTracker.Api.Features.Shared;

namespace FinanceTracker.Api.Services;

public sealed class MappingEngine
{
    private static readonly HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase)
    {
        "date",
        "amount",
        "type",
        "isDebit",
        "isCredit",
        "uniqueId",
        "description",
        "merchant",
        "category",
        "subcategory",
        "classification",
        "tags"
    };

    public IReadOnlyList<MappedRulesetTransaction> Map(ParsedCsvResult parsed, Ruleset ruleset)
    {
        var document = RulesetJson.Read(ruleset.Rules, new RulesetRulesDocumentDto("skip", null, []));
        var mapped = new List<MappedRulesetTransaction>();

        foreach (var row in parsed.Rows)
        {
            mapped.Add(MapRow(row, document));
        }

        return mapped;
    }

    private static MappedRulesetTransaction MapRow(ParsedCsvRow row, RulesetRulesDocumentDto document)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportJobErrorDto>();

        foreach (var rule in (document.Rules ?? [])
            .Where(rule => rule.IsActive && string.Equals(rule.Kind, "field", StringComparison.OrdinalIgnoreCase))
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.Name)
            .ThenBy(rule => rule.Id))
        {
            if (string.IsNullOrWhiteSpace(rule.Target) || !Targets.Contains(rule.Target))
            {
                errors.Add(new ImportJobErrorDto(row.RowNumber, rule.Target, "Mapping target is not supported."));
                continue;
            }

            try
            {
                var context = new RulesetEvaluationContext(row.Values, values);
                if (!RulesetConditionEvaluator.Matches(rule.Match, context))
                {
                    continue;
                }

                if (TryEvaluateRule(row.Values, values, rule, out var value))
                {
                    values[rule.Target] = value;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                errors.Add(new ImportJobErrorDto(row.RowNumber, rule.Target, ex.Message));
            }
        }

        var date = ReadDate(values, "date", errors, row.RowNumber);
        var amount = ReadDecimal(values, "amount", errors, row.RowNumber);
        var type = NormalizeType(ReadString(values, "type"));
        var classification = NormalizeClassification(ReadString(values, "classification"));
        var isDebit = ReadBool(values, "isDebit");
        var isCredit = ReadBool(values, "isCredit");

        if (amount.HasValue && string.IsNullOrWhiteSpace(type))
        {
            type = isDebit == true
                ? "expense"
                : isCredit == true
                    ? "income"
                    : amount.Value >= 0 ? "income" : "expense";
        }

        if (amount.HasValue)
        {
            amount = Math.Abs(amount.Value);
        }

        var description = ReadString(values, "description");
        var tags = ReadTags(values, "tags");
        var uniqueId = ReadString(values, "uniqueId");
        if (string.IsNullOrWhiteSpace(uniqueId) && date.HasValue && amount.HasValue && !string.IsNullOrWhiteSpace(description))
        {
            uniqueId = $"{date:yyyy-MM-dd}|{amount:0.0000}|{NormalizeDescription(description)}";
        }

        ValidateRequired(row.RowNumber, date, amount, type, description, classification, errors);

        return new MappedRulesetTransaction(
            row.RowNumber,
            row.Values,
            date,
            amount,
            type,
            NormalizeDescriptionForStorage(description),
            ReadString(values, "merchant"),
            ReadString(values, "category"),
            ReadString(values, "subcategory"),
            classification,
            tags,
            uniqueId,
            errors);
    }

    private static bool TryEvaluateRule(
        IReadOnlyDictionary<string, string> row,
        Dictionary<string, object?> values,
        RulesetRuleDefinitionDto rule,
        out object? value)
    {
        foreach (var step in rule.Flow ?? [])
        {
            var context = new RulesetEvaluationContext(row, values);
            if (!RulesetConditionEvaluator.Matches(step.When, context))
            {
                continue;
            }

            value = EvaluateStep(row, step);
            return true;
        }

        value = null;
        return false;
    }

    private static object? EvaluateStep(IReadOnlyDictionary<string, string> row, RulesetRuleStepDto step)
    {
        object? value = null;
        if (!string.IsNullOrWhiteSpace(step.Expr))
        {
            value = MappingExpressionEvaluator.Evaluate(step.Expr, row);
        }
        else if (!string.IsNullOrWhiteSpace(step.Source))
        {
            value = ReadValue(row, step.Source);
        }
        else if (step.Value is not null)
        {
            value = RulesetConditionEvaluator.ReadJsonValue(step.Value);
        }

        return ApplyTransform(value, step.Transform);
    }

    private static object? ApplyTransform(object? value, MappingTransformDto? transform)
    {
        if (transform is null)
        {
            return value;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return transform.Type switch
        {
            "parseDate" => ParseDate(text, transform.Format),
            "toDate" => ParseDate(text, transform.Format),
            "toDecimal" => ParseDecimal(text),
            "toString" => text,
            "trim" => text?.Trim(),
            "toUpper" => text?.ToUpperInvariant(),
            "toLower" => text?.ToLowerInvariant(),
            "toEnum" => string.IsNullOrWhiteSpace(transform.Value) ? text?.Trim().ToLowerInvariant() : transform.Value.Trim().ToLowerInvariant(),
            "toBoolean" => ParseBool(text),
            "splitTags" => SplitTags(text),
            _ => throw new ArgumentException($"Transform '{transform.Type}' is not supported.")
        };
    }

    private static string? ReadValue(IReadOnlyDictionary<string, string> row, string source)
    {
        return row.TryGetValue(source, out var value)
            ? value
            : row.TryGetValue(CsvParserService.NormalizeColumnReference(source), out var normalized)
                ? normalized
                : null;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> values, string key)
    {
        return values.TryGetValue(key, out var value) ? EmptyToNull(Convert.ToString(value, CultureInfo.InvariantCulture)) : null;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is bool b)
        {
            return b;
        }

        return ParseBool(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static IReadOnlyList<string> ReadTags(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        if (value is IEnumerable<string> tags)
        {
            return tags.Select(NormalizeTag).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        return SplitTags(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static DateOnly? ReadDate(IReadOnlyDictionary<string, object?> values, string key, List<ImportJobErrorDto> errors, int rowNumber)
    {
        if (!values.TryGetValue(key, out var value) || value is null || string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)))
        {
            return null;
        }

        if (value is DateOnly date)
        {
            return date;
        }

        try
        {
            return ParseDate(Convert.ToString(value, CultureInfo.InvariantCulture), null);
        }
        catch (FormatException ex)
        {
            errors.Add(new ImportJobErrorDto(rowNumber, key, ex.Message));
            return null;
        }
    }

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, object?> values, string key, List<ImportJobErrorDto> errors, int rowNumber)
    {
        if (!values.TryGetValue(key, out var value) || value is null || string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)))
        {
            return null;
        }

        try
        {
            return value is decimal amount ? amount : ParseDecimal(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        catch (FormatException ex)
        {
            errors.Add(new ImportJobErrorDto(rowNumber, key, ex.Message));
            return null;
        }
    }

    private static DateOnly ParseDate(string? value, string? format)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Date value is empty.");
        }

        var normalizedFormat = NormalizeDateFormat(format);
        if (!string.IsNullOrWhiteSpace(normalizedFormat)
            && DateOnly.TryParseExact(value.Trim(), normalizedFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        if (DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        throw new FormatException("Date could not be parsed.");
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Amount value is empty.");
        }

        var cleaned = value.Replace("$", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        if (cleaned.StartsWith('(') && cleaned.EndsWith(')'))
        {
            cleaned = $"-{cleaned[1..^1]}";
        }

        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return amount;
        }

        throw new FormatException("Amount could not be parsed.");
    }

    private static bool ParseBool(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Trim().ToLowerInvariant() is not ("false" or "0" or "no" or "n");
    }

    private static IReadOnlyList<string> SplitTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeTag)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeTag(string tag)
    {
        return EmptyToNull(tag.Trim().ToLowerInvariant());
    }

    private static void ValidateRequired(
        int rowNumber,
        DateOnly? date,
        decimal? amount,
        string? type,
        string? description,
        string? classification,
        List<ImportJobErrorDto> errors)
    {
        if (date is null)
        {
            errors.Add(new ImportJobErrorDto(rowNumber, "date", "Date is required."));
        }

        if (amount is null || amount <= 0)
        {
            errors.Add(new ImportJobErrorDto(rowNumber, "amount", "Amount must be greater than zero."));
        }

        if (string.IsNullOrWhiteSpace(type) || !FinanceValues.TransactionTypes.Contains(type))
        {
            errors.Add(new ImportJobErrorDto(rowNumber, "type", "Transaction type is invalid."));
        }

        if (!string.IsNullOrWhiteSpace(classification) && !FinanceValues.Classifications.Contains(classification))
        {
            errors.Add(new ImportJobErrorDto(rowNumber, "classification", "Classification is invalid."));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            errors.Add(new ImportJobErrorDto(rowNumber, "description", "Description is required."));
        }
    }

    private static string? NormalizeType(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "receivable" => "income",
            "expense" => "expense",
            "income" => "income",
            "transfer" => "transfer",
            "adjustment" => "adjustment",
            "opening_balance" => "opening_balance",
            _ => EmptyToNull(type?.Trim().ToLowerInvariant())
        };
    }

    private static string? NormalizeClassification(string? classification)
    {
        return EmptyToNull(classification?.Trim().ToLowerInvariant());
    }

    private static string? NormalizeDateFormat(string? format)
    {
        return format?
            .Replace("YYYY", "yyyy", StringComparison.Ordinal)
            .Replace("YY", "yy", StringComparison.Ordinal)
            .Replace("DD", "dd", StringComparison.Ordinal);
    }

    private static string NormalizeDescription(string description)
    {
        return string.Join(' ', description.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? NormalizeDescriptionForStorage(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var cleaned = description.Trim();
        var markers = new[] { "  ", "\t" };
        foreach (var marker in markers)
        {
            while (cleaned.Contains(marker, StringComparison.Ordinal))
            {
                cleaned = cleaned.Replace(marker, " ", StringComparison.Ordinal);
            }
        }

        return cleaned;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
