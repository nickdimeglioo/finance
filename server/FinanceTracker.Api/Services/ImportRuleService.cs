using System.Text.RegularExpressions;
using FinanceTracker.Api.Features.Rules;
using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Data.Contracts;

namespace FinanceTracker.Api.Services;

public sealed class ImportRuleService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IFinanceDataSession _db;

    public ImportRuleService(ICurrentUserContext currentUser, IFinanceDataSession db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<IReadOnlyList<ImportRuleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var rules = await LoadRulesAsync(includeInactive: true, cancellationToken);
        return rules.Select(ToDto).ToList();
    }

    public async Task<ImportRuleDto> CreateAsync(UpsertImportRuleRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var now = DateTimeOffset.UtcNow;
        var rule = new ImportRule
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            Name = request.Name.Trim(),
            Pattern = request.Pattern.Trim(),
            MapsToType = EmptyToNull(request.MapsToType),
            MapsToCategory = EmptyToNull(request.MapsToCategory),
            MapsToClassification = EmptyToNull(request.MapsToClassification),
            MapsToDescription = EmptyToNull(request.MapsToDescription),
            Priority = request.Priority,
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _db.SaveAsync(rule, _currentUser.UserId.ToString(), cancellationToken: cancellationToken);
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
        rule.Pattern = request.Pattern.Trim();
        rule.MapsToType = EmptyToNull(request.MapsToType);
        rule.MapsToCategory = EmptyToNull(request.MapsToCategory);
        rule.MapsToClassification = EmptyToNull(request.MapsToClassification);
        rule.MapsToDescription = EmptyToNull(request.MapsToDescription);
        rule.Priority = request.Priority;
        rule.IsActive = request.IsActive;
        rule.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveAsync(rule, _currentUser.UserId.ToString(), cancellationToken: cancellationToken);
        return ToDto(rule);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var rule = await GetOwnedAsync(id, cancellationToken: cancellationToken);
        if (rule is null)
        {
            return false;
        }

        await _db.ExecuteAsync("DELETE FROM import_rules WHERE id = @Id AND user_id = @UserId", new { Id = id, UserId = _currentUser.UserId }, cancellationToken: cancellationToken);
        return true;
    }

    public async Task ReorderAsync(ReorderRulesRequest request, CancellationToken cancellationToken)
    {
        await _db.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            var priority = 10;
            foreach (var id in request.RuleIds)
            {
                var rule = await GetOwnedAsync(id, connection, transaction, cancellationToken);
                if (rule is null)
                {
                    throw new ArgumentException("One or more import rules were not found.");
                }

                rule.Priority = priority;
                rule.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveAsync(rule, _currentUser.UserId.ToString(), connection, transaction, cancellationToken);
                priority += 10;
            }
        }, cancellationToken);
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
        var rules = await _db.WhereAsync<ImportRule>(rule => rule.UserId == _currentUser.UserId, cancellationToken: cancellationToken);
        return rules
            .Where(rule => includeInactive || rule.IsActive)
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
            if (!Regex.IsMatch(cleaned, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
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
        System.Data.IDbConnection? connection = null,
        System.Data.IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var rule = await _db.GetByIdAsync<ImportRule>(id, connection, transaction, cancellationToken);
        return rule?.UserId == _currentUser.UserId ? rule : null;
    }

    private static void Validate(UpsertImportRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Pattern))
        {
            throw new ArgumentException("Rule name and pattern are required.");
        }

        _ = new Regex(request.Pattern);

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
            rule.Name,
            rule.Pattern,
            rule.MapsToType,
            rule.MapsToCategory,
            rule.MapsToClassification,
            rule.MapsToDescription,
            rule.Priority,
            rule.IsActive,
            rule.CreatedAt,
            rule.UpdatedAt);
    }
}

public sealed record ImportRuleApplication(
    string CleanedDescription,
    string? Type,
    string? Category,
    string Classification,
    IReadOnlyList<ImportRule> MatchedRules);
