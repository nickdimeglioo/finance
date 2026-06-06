using FinanceTracker.Api.Features.Imports;
using FinanceTracker.Api.Features.Rules;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FinanceTracker.Api.Services;

public sealed class RulesetClassificationEngine
{
    public ClassifiedRulesetTransaction Classify(MappedRulesetTransaction mapped, Ruleset ruleset)
    {
        var document = RulesetJson.Read(
            ruleset.Rules,
            new RulesetRulesDocumentDto("skip", new RulesetFallbackDto(null, null, null, null, []), []));

        var merchant = mapped.Merchant;
        var category = mapped.Category;
        var subcategory = mapped.Subcategory;
        var classification = mapped.Classification;
        var tags = new List<string>(mapped.Tags);
        var matchedRuleIds = new List<string>();
        Guid? transferTargetAccountId = null;
        string? transferTargetAccountName = null;
        string? transferLinkMode = null;
        int transferMatchWindowDays = 7;
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["date"] = mapped.Date,
            ["amount"] = mapped.Amount,
            ["type"] = mapped.Type,
            ["description"] = mapped.Description,
            ["merchant"] = merchant,
            ["category"] = category,
            ["subcategory"] = subcategory,
            ["classification"] = classification,
            ["tags"] = tags
        };

        foreach (var rule in (document.Rules ?? [])
            .Where(rule => rule.IsActive && !string.Equals(rule.Kind, "field", StringComparison.OrdinalIgnoreCase))
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.Name)
            .ThenBy(rule => rule.Id))
        {
            var context = new RulesetEvaluationContext(mapped.RawRow, values);
            if (RulesetConditionEvaluator.Matches(rule.Match, context))
            {
                merchant = EmptyToNull(rule.Output?.Merchant) ?? merchant;
                category = EmptyToNull(rule.Output?.Category) ?? category;
                subcategory = EmptyToNull(rule.Output?.Subcategory) ?? subcategory;
                classification = EmptyToNull(rule.Output?.Classification) ?? classification;
                AddTags(tags, rule.Output?.Tags);
                transferTargetAccountId = rule.Output?.TransferTargetAccountId ?? transferTargetAccountId;
                transferTargetAccountName = EmptyToNull(rule.Output?.TransferTargetAccountName) ?? transferTargetAccountName;
                transferLinkMode = NormalizeTransferLinkMode(rule.Output?.TransferLinkMode) ?? transferLinkMode;
                transferMatchWindowDays = NormalizeWindow(rule.Output?.TransferMatchWindowDays) ?? transferMatchWindowDays;

                values["merchant"] = merchant;
                values["category"] = category;
                values["subcategory"] = subcategory;
                values["classification"] = classification;
                values["tags"] = tags;

                AddTags(tags, ExtractDynamicTags(rule.Output?.TagFrom, new RulesetEvaluationContext(mapped.RawRow, values)));
                values["tags"] = tags;
                matchedRuleIds.Add(rule.Id);
            }
        }

        var fallback = document.Fallback;
        return new ClassifiedRulesetTransaction(
            mapped,
            EmptyToNull(merchant) ?? EmptyToNull(fallback?.Merchant),
            EmptyToNull(category) ?? EmptyToNull(fallback?.Category),
            EmptyToNull(subcategory) ?? EmptyToNull(fallback?.Subcategory),
            EmptyToNull(classification) ?? EmptyToNull(fallback?.Classification) ?? "unknown",
            MergeTags(tags, fallback?.Tags),
            matchedRuleIds,
            transferTargetAccountId,
            transferTargetAccountName,
            transferLinkMode,
            transferMatchWindowDays);
    }

    private static IReadOnlyList<string> MergeTags(IReadOnlyList<string> current, IReadOnlyList<string>? fallback)
    {
        var tags = current.ToList();
        AddTags(tags, fallback);
        return tags;
    }

    private static void AddTags(List<string> tags, IReadOnlyList<string>? additions)
    {
        foreach (var tag in additions ?? [])
        {
            var normalized = EmptyToNull(tag?.ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(normalized) && !tags.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(normalized);
            }
        }
    }

    private static IReadOnlyList<string> ExtractDynamicTags(IReadOnlyList<RulesetDynamicTagDto>? extractors, RulesetEvaluationContext context)
    {
        var tags = new List<string>();
        foreach (var extractor in extractors ?? [])
        {
            if (string.IsNullOrWhiteSpace(extractor.Regex))
            {
                continue;
            }

            var source = Convert.ToString(
                RulesetConditionEvaluator.ReadField(extractor.Field ?? "description", context),
                CultureInfo.InvariantCulture) ?? string.Empty;

            foreach (Match match in Regex.Matches(source, extractor.Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                var rawValue = CapturedValue(match);
                var value = Slugify(rawValue);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var tag = string.IsNullOrWhiteSpace(extractor.Format)
                    ? $"{extractor.Prefix}{value}{extractor.Suffix}"
                    : extractor.Format
                        .Replace("{value}", value, StringComparison.OrdinalIgnoreCase)
                        .Replace("{raw}", rawValue.Trim(), StringComparison.OrdinalIgnoreCase);
                tags.Add(tag);
            }
        }

        return tags;
    }

    private static string CapturedValue(Match match)
    {
        if (match.Groups["tag"].Success)
        {
            return match.Groups["tag"].Value;
        }

        if (match.Groups["value"].Success)
        {
            return match.Groups["value"].Value;
        }

        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }

    private static string Slugify(string value)
    {
        var cleaned = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return cleaned.Trim('-');
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeTransferLinkMode(string? value)
    {
        var normalized = EmptyToNull(value)?.ToLowerInvariant();
        return normalized is "auto" or "suggest" ? normalized : null;
    }

    private static int? NormalizeWindow(int? value)
    {
        return value is >= 0 and <= 45 ? value.Value : null;
    }
}
