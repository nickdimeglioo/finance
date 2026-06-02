using System.Globalization;
using FinanceTracker.Api.Features.Rules;
using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;
using PipelineRunner.Utils;

namespace FinanceTracker.Api.Services;

public sealed class ClassificationRuleService
{
    private static readonly string[] RuleTypes = ["keyword_contains", "merchant_exact", "category_is", "amount_gte", "amount_lte", "amount_range"];
    private static readonly string[] FieldTargets = ["description", "merchant", "category", "amount"];

    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public ClassificationRuleService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<IReadOnlyList<ClassificationRuleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var rules = await LoadRulesAsync(includeInactive: true, cancellationToken);
        return rules.Select(ToDto).ToList();
    }

    public async Task<ClassificationRuleDto> CreateAsync(UpsertClassificationRuleRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var now = DateTimeOffset.UtcNow;
        var rule = new ClassificationRule
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            Name = request.Name.Trim(),
            RuleType = request.RuleType,
            FieldTarget = request.FieldTarget,
            Value = request.Value.Trim(),
            Classification = request.Classification,
            AlsoSetCategory = EmptyToNull(request.AlsoSetCategory),
            Priority = request.Priority,
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _db.SaveAsync(rule, auditUserId: _currentUser.UserId.ToString());
        return ToDto(rule);
    }

    public async Task<ClassificationRuleDto?> UpdateAsync(Guid id, UpsertClassificationRuleRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var rule = await GetOwnedAsync(id, cancellationToken: cancellationToken);
        if (rule is null)
        {
            return null;
        }

        rule.Name = request.Name.Trim();
        rule.RuleType = request.RuleType;
        rule.FieldTarget = request.FieldTarget;
        rule.Value = request.Value.Trim();
        rule.Classification = request.Classification;
        rule.AlsoSetCategory = EmptyToNull(request.AlsoSetCategory);
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
                    throw new ArgumentException("One or more classification rules were not found.");
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

    public async Task<TestClassificationRuleResult> TestAsync(TestClassificationRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await ApplyAsync(request.Description, request.Merchant, request.Category, request.Amount, cancellationToken);
        return new TestClassificationRuleResult(result.Classification, result.Category, result.Rule is null ? null : ToDto(result.Rule));
    }

    public async Task<ClassificationApplication> ApplyAsync(
        string description,
        string? merchant,
        string? category,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var rules = await LoadRulesAsync(includeInactive: false, cancellationToken);
        return Apply(description, merchant, category, amount, rules);
    }

    public async Task ApplyToTransactionAsync(FinancialTransaction transaction, CancellationToken cancellationToken)
    {
        var result = await ApplyAsync(transaction.Description, transaction.Merchant, transaction.Category, transaction.Amount, cancellationToken);
        if (result.Rule is null)
        {
            return;
        }

        transaction.Classification = result.Classification;
        if (!string.IsNullOrWhiteSpace(result.Category))
        {
            transaction.Category = result.Category;
        }
    }

    internal async Task<IReadOnlyList<ClassificationRule>> LoadRulesAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var rules = await _db.QuerySelect<ClassificationRule>()
            .From<ClassificationRule>()
            .SelectAllFrom<ClassificationRule>()
            .Where(rule => rule.UserId == _currentUser.UserId)
            .ToListAsync();
        return rules
            .Where(rule => includeInactive || rule.IsActive)
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.Name)
            .ToList();
    }

    internal static ClassificationApplication Apply(
        string description,
        string? merchant,
        string? category,
        decimal amount,
        IEnumerable<ClassificationRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!Matches(rule, description, merchant, category, amount))
            {
                continue;
            }

            return new ClassificationApplication(rule.Classification, rule.AlsoSetCategory ?? category, rule);
        }

        return new ClassificationApplication("unknown", category, null);
    }

    private async Task<ClassificationRule?> GetOwnedAsync(
        Guid id,
        MultiTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var rule = transaction is null
            ? await _db.GetByIdAsync<ClassificationRule>(id, depth: 0)
            : await transaction.GetByIdAsync<ClassificationRule>(id);
        return rule?.UserId == _currentUser.UserId ? rule : null;
    }

    private static bool Matches(ClassificationRule rule, string description, string? merchant, string? category, decimal amount)
    {
        return rule.RuleType switch
        {
            "keyword_contains" => ReadTarget(rule.FieldTarget, description, merchant, category, amount)
                .Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
            "merchant_exact" => string.Equals(merchant, rule.Value, StringComparison.OrdinalIgnoreCase),
            "category_is" => string.Equals(category, rule.Value, StringComparison.OrdinalIgnoreCase),
            "amount_gte" => amount >= ParseDecimal(rule.Value),
            "amount_lte" => amount <= ParseDecimal(rule.Value),
            "amount_range" => MatchesRange(amount, rule.Value),
            _ => false
        };
    }

    private static string ReadTarget(string target, string description, string? merchant, string? category, decimal amount)
    {
        return target switch
        {
            "merchant" => merchant ?? string.Empty,
            "category" => category ?? string.Empty,
            "amount" => amount.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            _ => description
        };
    }

    private static bool MatchesRange(decimal amount, string value)
    {
        var parts = value.Split(new[] { '-', ',', ':' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && amount >= ParseDecimal(parts[0]) && amount <= ParseDecimal(parts[1]);
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.Parse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Validate(UpsertClassificationRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Value))
        {
            throw new ArgumentException("Rule name and value are required.");
        }

        if (!RuleTypes.Contains(request.RuleType))
        {
            throw new ArgumentException("Invalid rule type.");
        }

        if (!FieldTargets.Contains(request.FieldTarget))
        {
            throw new ArgumentException("Invalid field target.");
        }

        if (!FinanceValues.Classifications.Contains(request.Classification))
        {
            throw new ArgumentException("Invalid classification.");
        }

        if (request.RuleType is "amount_gte" or "amount_lte")
        {
            _ = ParseDecimal(request.Value);
        }

        if (request.RuleType == "amount_range")
        {
            var parts = request.Value.Split(new[] { '-', ',', ':' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out _) || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                throw new ArgumentException("Amount range must include two numeric values.");
            }
        }
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ClassificationRuleDto ToDto(ClassificationRule rule)
    {
        return new ClassificationRuleDto(
            rule.Id,
            rule.Name,
            rule.RuleType,
            rule.FieldTarget,
            rule.Value,
            rule.Classification,
            rule.AlsoSetCategory,
            rule.Priority,
            rule.IsActive,
            rule.CreatedAt,
            rule.UpdatedAt);
    }
}

public sealed record ClassificationApplication(
    string Classification,
    string? Category,
    ClassificationRule? Rule);
