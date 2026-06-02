using System.Text.RegularExpressions;
using FinanceTracker.Api.Features.Rules;
using FinanceTracker.Api.Features.Shared;
using PipelineRunner.Services;
using PipelineRunner.Utils;

namespace FinanceTracker.Api.Services;

public sealed class ImportRuleService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public ImportRuleService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<IReadOnlyList<ImportRuleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var rules = await LoadRulesAsync(includeInactive: true, cancellationToken);
        return rules.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ImportRuleSetDto>> ListRuleSetsAsync(CancellationToken cancellationToken)
    {
        var sets = await _db.QuerySelect<ImportRuleSet>()
            .From<ImportRuleSet>()
            .SelectAllFrom<ImportRuleSet>()
            .Where(set => set.UserId == _currentUser.UserId)
            .ToListAsync();
        return sets.OrderBy(set => set.Name).Select(ToDto).ToList();
    }

    public async Task<ImportRuleSetDto> CreateRuleSetAsync(UpsertImportRuleSetRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Rule set name is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var set = new ImportRuleSet
        {
            UserId = _currentUser.UserId,
            Name = request.Name.Trim(),
            Institution = EmptyToNull(request.Institution),
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _db.SaveAsync(set, auditUserId: _currentUser.UserId.ToString());
        return ToDto(set);
    }

    public async Task<ImportRuleDto> CreateAsync(UpsertImportRuleRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var now = DateTimeOffset.UtcNow;
        var rule = new ImportRule
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            RuleSetId = request.RuleSetId,
            Name = request.Name.Trim(),
            Pattern = EmptyToNull(request.Pattern),
            SourceField = EmptyToNull(request.SourceField),
            TargetField = EmptyToNull(request.TargetField),
            ValueTransform = string.IsNullOrWhiteSpace(request.ValueTransform) ? "copy" : request.ValueTransform,
            MapsToType = EmptyToNull(request.MapsToType),
            MapsToCategory = EmptyToNull(request.MapsToCategory),
            MapsToClassification = EmptyToNull(request.MapsToClassification),
            MapsToDescription = EmptyToNull(request.MapsToDescription),
            Priority = request.Priority,
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _db.SaveAsync(rule, auditUserId: _currentUser.UserId.ToString());
        return ToDto(rule);
    }

    public async Task<ImportRuleDto?> UpdateAsync(Guid id, UpsertImportRuleRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var rule = await GetOwnedAsync(id, cancellationToken: cancellationToken);
        if (rule is null)
        {
            return null;
        }

        rule.Name = request.Name.Trim();
        rule.RuleSetId = request.RuleSetId;
        rule.Pattern = EmptyToNull(request.Pattern);
        rule.SourceField = EmptyToNull(request.SourceField);
        rule.TargetField = EmptyToNull(request.TargetField);
        rule.ValueTransform = string.IsNullOrWhiteSpace(request.ValueTransform) ? "copy" : request.ValueTransform;
        rule.MapsToType = EmptyToNull(request.MapsToType);
        rule.MapsToCategory = EmptyToNull(request.MapsToCategory);
        rule.MapsToClassification = EmptyToNull(request.MapsToClassification);
        rule.MapsToDescription = EmptyToNull(request.MapsToDescription);
        rule.Priority = request.Priority;
        rule.IsActive = request.IsActive;
        rule.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveAsync(rule, auditUserId: _currentUser.UserId.ToString());
        return ToDto(rule);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var rule = await GetOwnedAsync(id, cancellationToken: cancellationToken);
        if (rule is null)
        {
            return false;
        }

        await _db.DeleteAsync(rule, userId: _currentUser.UserId.ToString());
        return true;
    }

    public async Task ReorderAsync(ReorderRulesRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = _db.BeginMultiTransaction();
        transaction.Open();

        try
        {
            var priority = 10;
            foreach (var id in request.RuleIds)
            {
                var rule = await GetOwnedAsync(id, transaction, cancellationToken);
                if (rule is null)
                {
                    throw new ArgumentException("One or more import rules were not found.");
                }

                rule.Priority = priority;
                rule.UpdatedAt = DateTimeOffset.UtcNow;
                await transaction.Save(rule);
                priority += 10;
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<TestImportRuleResult> TestAsync(TestImportRuleRequest request, CancellationToken cancellationToken)
    {
        var result = ApplyRules(request.RawDescription, await LoadRulesAsync(includeInactive: false, cancellationToken));
        return new TestImportRuleResult(
            request.RawDescription,
            result.CleanedDescription,
            result.Type,
            result.Category,
            result.Classification,
            result.MatchedRules.Select(ToDto).ToList());
    }

    internal async Task<IReadOnlyList<ImportRule>> LoadRulesAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var rules = await _db.QuerySelect<ImportRule>()
            .From<ImportRule>()
            .SelectAllFrom<ImportRule>()
            .Where(rule => rule.UserId == _currentUser.UserId)
            .ToListAsync();
        return rules
            .Where(rule => includeInactive || rule.IsActive)
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.Name)
            .ToList();
    }

    internal async Task<IReadOnlyList<ImportRule>> LoadMappingRulesAsync(Guid? ruleSetId, CancellationToken cancellationToken)
    {
        if (ruleSetId is null)
        {
            return [];
        }

        var set = await _db.GetByIdAsync<ImportRuleSet>(ruleSetId.Value, depth: 0);
        if (set?.UserId != _currentUser.UserId || !set.IsActive)
        {
            throw new ArgumentException("Import rule set was not found.");
        }

        var rules = await _db.QuerySelect<ImportRule>()
            .From<ImportRule>()
            .SelectAllFrom<ImportRule>()
            .Where(rule => rule.UserId == _currentUser.UserId && rule.RuleSetId == ruleSetId.Value)
            .ToListAsync();
        return rules
            .Where(rule => rule.IsActive && !string.IsNullOrWhiteSpace(rule.SourceField) && !string.IsNullOrWhiteSpace(rule.TargetField))
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.Name)
            .ToList();
    }

    internal static ImportRuleApplication ApplyRules(string rawDescription, IEnumerable<ImportRule> rules)
    {
        var cleaned = rawDescription.Trim();
        string? type = null;
        string? category = null;
        var classification = "unknown";
        var matched = new List<ImportRule>();

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern) || !Regex.IsMatch(cleaned, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                continue;
            }

            matched.Add(rule);
            if (!string.IsNullOrWhiteSpace(rule.MapsToDescription))
            {
                cleaned = Regex.Replace(cleaned, rule.Pattern, rule.MapsToDescription, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            }

            type ??= rule.MapsToType;
            category ??= rule.MapsToCategory;
            if (!string.IsNullOrWhiteSpace(rule.MapsToClassification) && classification == "unknown")
            {
                classification = rule.MapsToClassification;
            }
        }

        return new ImportRuleApplication(cleaned, type, category, classification, matched);
    }

    private async Task<ImportRule?> GetOwnedAsync(
        Guid id,
        MultiTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var rule = transaction is null
            ? await _db.GetByIdAsync<ImportRule>(id, depth: 0)
            : await transaction.GetByIdAsync<ImportRule>(id);
        return rule?.UserId == _currentUser.UserId ? rule : null;
    }

    private static void Validate(UpsertImportRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Rule name is required.");
        }

        if (!string.IsNullOrWhiteSpace(request.Pattern))
        {
            _ = new Regex(request.Pattern);
        }

        if (!string.IsNullOrWhiteSpace(request.TargetField)
            && !new[] { "date", "description", "merchant", "amount", "type", "category", "classification" }.Contains(request.TargetField))
        {
            throw new ArgumentException("Invalid target field.");
        }

        if (!string.IsNullOrWhiteSpace(request.ValueTransform)
            && !new[] { "copy", "amount_positive", "amount_negative" }.Contains(request.ValueTransform))
        {
            throw new ArgumentException("Invalid value transform.");
        }

        if (!string.IsNullOrWhiteSpace(request.MapsToType) && !FinanceValues.TransactionTypes.Contains(request.MapsToType))
        {
            throw new ArgumentException("Invalid mapped transaction type.");
        }

        if (!string.IsNullOrWhiteSpace(request.MapsToClassification) && !FinanceValues.Classifications.Contains(request.MapsToClassification))
        {
            throw new ArgumentException("Invalid mapped classification.");
        }
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ImportRuleDto ToDto(ImportRule rule)
    {
        return new ImportRuleDto(
            rule.Id,
            rule.RuleSetId,
            rule.Name,
            rule.Pattern,
            rule.SourceField,
            rule.TargetField,
            rule.ValueTransform,
            rule.MapsToType,
            rule.MapsToCategory,
            rule.MapsToClassification,
            rule.MapsToDescription,
            rule.Priority,
            rule.IsActive,
            rule.CreatedAt,
            rule.UpdatedAt);
    }

    private static ImportRuleSetDto ToDto(ImportRuleSet set)
    {
        return new ImportRuleSetDto(
            set.Id,
            set.Name,
            set.Institution,
            set.IsActive,
            set.CreatedAt,
            set.UpdatedAt);
    }
}

public sealed record ImportRuleApplication(
    string CleanedDescription,
    string? Type,
    string? Category,
    string Classification,
    IReadOnlyList<ImportRule> MatchedRules);
