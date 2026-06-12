using FinanceTracker.Api.Features.Accounts;
using FinanceTracker.Api.Features.Planning;
using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class BudgetGoalService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public BudgetGoalService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<IReadOnlyList<BudgetGoalDto>> ListAsync(string? kind, CancellationToken cancellationToken)
    {
        var items = await LoadOwnedAsync();
        var filtered = items
            .Where(item => string.IsNullOrWhiteSpace(kind) || item.Kind == kind)
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.EndsOn)
            .ThenBy(item => item.Name)
            .ToList();

        var result = new List<BudgetGoalDto>();
        foreach (var item in filtered)
        {
            result.Add(await ToDtoAsync(item, cancellationToken));
        }

        return result;
    }

    public async Task<BudgetGoalDto> CreateAsync(UpsertBudgetGoalRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request);
        var now = DateTimeOffset.UtcNow;
        var entity = new BudgetGoal
        {
            UserId = _currentUser.UserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        Apply(entity, request);
        await _db.SaveAsync(entity, auditUserId: _currentUser.UserId.ToString());
        return await ToDtoAsync(entity, cancellationToken);
    }

    public async Task<BudgetGoalDto?> UpdateAsync(Guid id, UpsertBudgetGoalRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request);
        var entity = await GetOwnedAsync(id);
        if (entity is null) return null;
        Apply(entity, request);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(entity, auditUserId: _currentUser.UserId.ToString());
        return await ToDtoAsync(entity, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await GetOwnedAsync(id);
        return entity is not null && await _db.DeleteAsync(entity, userId: _currentUser.UserId.ToString());
    }

    private async Task<BudgetGoalDto> ToDtoAsync(BudgetGoal item, CancellationToken cancellationToken)
    {
        var current = await CalculateCurrentAmountAsync(item, cancellationToken);
        var remaining = item.Kind == "budget"
            ? item.TargetAmount - current
            : Math.Max(0, item.TargetAmount - current);
        var percent = item.TargetAmount <= 0 ? 0 : decimal.Round(current / item.TargetAmount * 100m, 2);
        var status = Status(item.Kind, current, item.TargetAmount);

        return new BudgetGoalDto(
            item.Id,
            item.Name,
            item.Kind,
            item.AccountId,
            item.Category,
            item.Classification,
            ReadTags(item.TagNames),
            item.StartsOn,
            item.EndsOn,
            item.TargetAmount,
            item.Currency,
            item.IncludeSplits,
            item.IsActive,
            current,
            remaining,
            percent,
            status,
            item.CreatedAt,
            item.UpdatedAt);
    }

    private async Task<decimal> CalculateCurrentAmountAsync(BudgetGoal item, CancellationToken cancellationToken)
    {
        var tagNames = ReadTags(item.TagNames);
        var transactions = await _db.QuerySelect<FinancialTransaction>()
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(transaction => transaction.UserId == _currentUser.UserId)
            .ToListAsync();

        var scoped = transactions
            .Where(transaction => transaction.Status is "posted" or "reconciled")
            .Where(transaction => !transaction.IsVoid)
            .Where(transaction => transaction.Date >= item.StartsOn && transaction.Date <= item.EndsOn)
            .Where(transaction => transaction.Currency == item.Currency)
            .Where(transaction => item.AccountId is null || transaction.AccountId == item.AccountId.Value)
            .Where(transaction => TagsMatch(ReadTags(transaction.Tags), tagNames))
            .ToList();

        if (item.IncludeSplits)
        {
            var splitTotal = await CalculateSplitAwareAmountAsync(item, scoped, cancellationToken);
            if (splitTotal.HasValue)
            {
                return splitTotal.Value;
            }
        }

        return scoped
            .Where(transaction => MatchesParentFilters(transaction, item))
            .Sum(transaction => SignedAmountForKind(item.Kind, transaction.Type, transaction.Amount));
    }

    private async Task<decimal?> CalculateSplitAwareAmountAsync(BudgetGoal item, IReadOnlyList<FinancialTransaction> scoped, CancellationToken cancellationToken)
    {
        if (scoped.Count == 0 || string.IsNullOrWhiteSpace(item.Category) && string.IsNullOrWhiteSpace(item.Classification))
        {
            return null;
        }

        var transactionIds = scoped.Select(transaction => transaction.Id).ToHashSet();
        var splits = await _db.QuerySelect<TransactionSplit>()
            .From<TransactionSplit>()
            .SelectAllFrom<TransactionSplit>()
            .ToListAsync();
        var splitRows = splits.Where(split => transactionIds.Contains(split.TransactionId)).ToList();
        if (splitRows.Count == 0)
        {
            return null;
        }

        var parentById = scoped.ToDictionary(transaction => transaction.Id);
        var splitParentIds = splitRows.Select(split => split.TransactionId).ToHashSet();
        var splitAmount = splitRows
            .Where(split => MatchesSplitFilters(split, item))
            .Where(split => parentById.TryGetValue(split.TransactionId, out var parent) && parent.Type is "expense" or "income")
            .Sum(split => SignedAmountForKind(item.Kind, parentById[split.TransactionId].Type, split.Amount));
        var unsplitAmount = scoped
            .Where(transaction => !splitParentIds.Contains(transaction.Id))
            .Where(transaction => MatchesParentFilters(transaction, item))
            .Sum(transaction => SignedAmountForKind(item.Kind, transaction.Type, transaction.Amount));

        return splitAmount + unsplitAmount;
    }

    private async Task ValidateAsync(UpsertBudgetGoalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Budget or goal name is required.");
        if (request.Kind is not ("budget" or "goal")) throw new ArgumentException("Kind must be budget or goal.");
        if (request.TargetAmount <= 0) throw new ArgumentException("Target amount must be greater than zero.");
        if (request.EndsOn < request.StartsOn) throw new ArgumentException("End date must be on or after the start date.");
        if (string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Trim().Length != 3) throw new ArgumentException("Currency must be a 3-letter ISO code.");
        if (!string.IsNullOrWhiteSpace(request.Classification) && !FinanceValues.Classifications.Contains(request.Classification)) throw new ArgumentException("Invalid classification.");
        if (request.AccountId is Guid accountId)
        {
            var account = await _db.GetByIdAsync<Account>(accountId, depth: 0);
            if (account?.UserId != _currentUser.UserId) throw new ArgumentException("Account was not found.");
        }
    }

    private static void Apply(BudgetGoal entity, UpsertBudgetGoalRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.Kind = request.Kind;
        entity.AccountId = request.AccountId;
        entity.Category = EmptyToNull(request.Category);
        entity.Classification = EmptyToNull(request.Classification);
        entity.TagNames = RulesetJson.Write(NormalizeTags(request.TagNames ?? []));
        entity.StartsOn = request.StartsOn;
        entity.EndsOn = request.EndsOn;
        entity.TargetAmount = request.TargetAmount;
        entity.Currency = request.Currency.Trim().ToUpperInvariant();
        entity.IncludeSplits = request.IncludeSplits;
        entity.IsActive = request.IsActive;
    }

    private async Task<List<BudgetGoal>> LoadOwnedAsync()
        => await _db.QuerySelect<BudgetGoal>().From<BudgetGoal>().SelectAllFrom<BudgetGoal>().Where(item => item.UserId == _currentUser.UserId).ToListAsync();

    private async Task<BudgetGoal?> GetOwnedAsync(Guid id)
    {
        var item = await _db.GetByIdAsync<BudgetGoal>(id, depth: 0);
        return item?.UserId == _currentUser.UserId ? item : null;
    }

    private static bool MatchesParentFilters(FinancialTransaction transaction, BudgetGoal item)
    {
        if (!string.IsNullOrWhiteSpace(item.Category) && !string.Equals(transaction.Category, item.Category, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(item.Classification) && transaction.Classification != item.Classification) return false;
        return transaction.Type is "expense" or "income";
    }

    private static bool MatchesSplitFilters(TransactionSplit split, BudgetGoal item)
    {
        if (!string.IsNullOrWhiteSpace(item.Category) && !string.Equals(split.Category, item.Category, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(item.Classification) && split.Classification != item.Classification) return false;
        return true;
    }

    private static decimal SignedAmountForKind(string kind, string transactionType, decimal amount)
    {
        if (kind == "budget")
        {
            return transactionType == "expense" ? amount : 0m;
        }

        return transactionType switch
        {
            "income" => amount,
            "expense" => -amount,
            _ => 0m
        };
    }

    private static string Status(string kind, decimal current, decimal target)
    {
        if (kind == "budget")
        {
            if (current > target) return "over_limit";
            return current >= target * 0.90m ? "near_limit" : "on_track";
        }

        if (current >= target) return "met";
        return current > 0 ? "in_progress" : "no_progress";
    }

    private static bool TagsMatch(IReadOnlyList<string> transactionTags, IReadOnlyList<string> requiredTags)
        => requiredTags.Count == 0 || requiredTags.All(required => transactionTags.Any(tag => string.Equals(tag, required, StringComparison.OrdinalIgnoreCase)));

    private static IReadOnlyList<string> ReadTags(string tags)
        => RulesetJson.Read<IReadOnlyList<string>>(tags, []);

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string> tagNames)
        => tagNames.Select(EmptyToNull).OfType<string>().Select(tag => tag.ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(tag => tag).ToList();

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
