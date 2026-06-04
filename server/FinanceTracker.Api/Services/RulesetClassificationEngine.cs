using FinanceTracker.Api.Features.Imports;
using FinanceTracker.Api.Features.Rules;

namespace FinanceTracker.Api.Services;

public sealed class RulesetClassificationEngine
{
    public ClassifiedRulesetTransaction Classify(MappedRulesetTransaction mapped, Ruleset ruleset)
    {
        var document = RulesetJson.Read(
            ruleset.Rules,
            new RulesetRulesDocumentDto("skip", new RulesetFallbackDto(null, "Uncategorized", null, "unknown", []), []));

        var merchant = mapped.Merchant;
        var category = mapped.Category;
        var subcategory = mapped.Subcategory;
        var classification = mapped.Classification;
        var tags = new List<string>(mapped.Tags);
        var matchedRuleIds = new List<string>();
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
                matchedRuleIds.Add(rule.Id);

                values["merchant"] = merchant;
                values["category"] = category;
                values["subcategory"] = subcategory;
                values["classification"] = classification;
                values["tags"] = tags;
            }
        }

        var fallback = document.Fallback;
        return new ClassifiedRulesetTransaction(
            mapped,
            EmptyToNull(fallback?.Merchant) ?? merchant,
            EmptyToNull(fallback?.Category) ?? category,
            EmptyToNull(fallback?.Subcategory) ?? subcategory,
            EmptyToNull(fallback?.Classification) ?? classification ?? "unknown",
            MergeTags(tags, fallback?.Tags),
            matchedRuleIds);
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

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
