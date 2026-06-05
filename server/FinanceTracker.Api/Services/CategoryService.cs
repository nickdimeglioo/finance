using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed record CategoryDto(string Name, int TransactionCount, int SplitCount);
public sealed record RenameCategoryRequest(string From, string To);

public sealed class CategoryService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public CategoryService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var transactions = await LoadOwnedTransactionsAsync(cancellationToken);
        var transactionCounts = transactions
            .Where(transaction => !string.IsNullOrWhiteSpace(transaction.Category))
            .GroupBy(transaction => transaction.Category!)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var transactionIds = transactions.Select(transaction => transaction.Id).ToHashSet();
        var splits = await _db.QuerySelect<TransactionSplit>().From<TransactionSplit>().SelectAllFrom<TransactionSplit>().ToListAsync();
        var splitCounts = splits
            .Where(split => transactionIds.Contains(split.TransactionId) && !string.IsNullOrWhiteSpace(split.Category))
            .GroupBy(split => split.Category)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return transactionCounts.Keys.Concat(splitCounts.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .Select(name => new CategoryDto(name, transactionCounts.GetValueOrDefault(name), splitCounts.GetValueOrDefault(name)))
            .ToList();
    }

    public async Task<int> RenameAsync(RenameCategoryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.To))
        {
            throw new ArgumentException("Both source and target category are required.");
        }

        var from = request.From.Trim();
        var to = request.To.Trim();
        var changed = 0;
        var transactions = await LoadOwnedTransactionsAsync(cancellationToken);
        var ownedTransactionIds = transactions.Select(transaction => transaction.Id).ToHashSet();
        foreach (var transaction in transactions.Where(transaction => string.Equals(transaction.Category, from, StringComparison.OrdinalIgnoreCase)))
        {
            transaction.Category = to;
            transaction.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveAsync(transaction, auditUserId: _currentUser.UserId.ToString());
            changed++;
        }

        var splits = await _db.QuerySelect<TransactionSplit>().From<TransactionSplit>().SelectAllFrom<TransactionSplit>().ToListAsync();
        foreach (var split in splits.Where(split => ownedTransactionIds.Contains(split.TransactionId) && string.Equals(split.Category, from, StringComparison.OrdinalIgnoreCase)))
        {
            split.Category = to;
            await _db.SaveAsync(split, auditUserId: _currentUser.UserId.ToString());
            changed++;
        }

        return changed;
    }

    private async Task<IReadOnlyList<FinancialTransaction>> LoadOwnedTransactionsAsync(CancellationToken cancellationToken)
        => await _db.QuerySelect<FinancialTransaction>()
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(transaction => transaction.UserId == _currentUser.UserId)
            .ToListAsync();
}

