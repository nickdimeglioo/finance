using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;
using PipelineRunner.Utils;

namespace FinanceTracker.Api.Services;

public sealed class DeduplicationService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public DeduplicationService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<HashSet<string>> LoadExistingUniqueIdsAsync(Guid accountId, MultiTransaction? transaction, CancellationToken cancellationToken)
    {
        var query = transaction is null
            ? _db.QuerySelect<FinancialTransaction>()
            : transaction.QuerySelect<FinancialTransaction>();

        var transactions = await query
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(item => item.UserId == _currentUser.UserId && item.AccountId == accountId)
            .ToListAsync();

        return transactions
            .Select(item => item.UniqueId ?? item.ImportHash)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
    }
}
