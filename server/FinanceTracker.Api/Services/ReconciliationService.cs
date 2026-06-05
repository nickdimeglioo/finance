using FinanceTracker.Api.Features.Accounts;
using FinanceTracker.Api.Features.Reconciliation;
using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Api.Mapping;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class ReconciliationService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public ReconciliationService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<IReadOnlyList<BalanceCheckpointDto>?> ListCheckpointsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (!await AccountOwnedAsync(accountId, cancellationToken))
        {
            return null;
        }

        var checkpoints = await _db.QuerySelect<BalanceCheckpoint>()
            .From<BalanceCheckpoint>()
            .SelectAllFrom<BalanceCheckpoint>()
            .Where(checkpoint => checkpoint.UserId == _currentUser.UserId && checkpoint.AccountId == accountId)
            .ToListAsync();
        return checkpoints.OrderByDescending(checkpoint => checkpoint.Date).ThenByDescending(checkpoint => checkpoint.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<BalanceCheckpointDto?> CreateCheckpointAsync(Guid accountId, CreateBalanceCheckpointRequest request, CancellationToken cancellationToken)
    {
        if (!await AccountOwnedAsync(accountId, cancellationToken))
        {
            return null;
        }

        var expected = await CalculateExpectedBalanceAsync(accountId, request.Date, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var checkpoint = new BalanceCheckpoint
        {
            UserId = _currentUser.UserId,
            AccountId = accountId,
            Date = request.Date,
            Balance = request.Balance,
            Notes = request.Notes,
            ExpectedBalance = expected,
            Discrepancy = request.Balance - expected,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _db.SaveAsync(checkpoint, auditUserId: _currentUser.UserId.ToString());
        return ToDto(checkpoint);
    }

    public async Task<ReconcileAccountDto?> GetReconcileAsync(Guid accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (!await AccountOwnedAsync(accountId, cancellationToken))
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rangeTo = to ?? today;
        var rangeFrom = from ?? rangeTo.AddMonths(-1).AddDays(1);
        if (rangeFrom > rangeTo)
        {
            (rangeFrom, rangeTo) = (rangeTo, rangeFrom);
        }

        var transactions = await _db.QuerySelect<FinancialTransaction>()
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(transaction => transaction.UserId == _currentUser.UserId && transaction.AccountId == accountId && transaction.IsVoid == false && transaction.Date <= rangeTo)
            .ToListAsync();
        var openingCleared = transactions
            .Where(transaction => transaction.Date < rangeFrom && AccountService.CountsTowardBalance(transaction))
            .Sum(AccountService.SignedBalanceAmount);
        var closingExpected = transactions
            .Where(AccountService.CountsTowardBalance)
            .Sum(AccountService.SignedBalanceAmount);
        var unreconciled = transactions
            .Where(transaction => transaction.Date >= rangeFrom && transaction.Date <= rangeTo && transaction.Status is "posted" or "pending")
            .OrderBy(transaction => transaction.Date)
            .ThenBy(transaction => transaction.CreatedAt)
            .MapToList<FinancialTransaction, TransactionListItemDto>();

        foreach (var item in unreconciled)
        {
            item.Tags = [];
        }

        return new ReconcileAccountDto(accountId, rangeFrom, rangeTo, openingCleared, closingExpected, unreconciled);
    }

    private async Task<bool> AccountOwnedAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _db.GetByIdAsync<Account>(accountId, depth: 0);
        return account?.UserId == _currentUser.UserId;
    }

    private async Task<decimal> CalculateExpectedBalanceAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken)
    {
        var transactions = await _db.QuerySelect<FinancialTransaction>()
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(transaction => transaction.UserId == _currentUser.UserId && transaction.AccountId == accountId && transaction.IsVoid == false && transaction.Date <= date)
            .ToListAsync();
        return transactions.Where(AccountService.CountsTowardBalance).Sum(AccountService.SignedBalanceAmount);
    }

    private static BalanceCheckpointDto ToDto(BalanceCheckpoint checkpoint)
        => new(
            checkpoint.Id,
            checkpoint.AccountId,
            checkpoint.Date,
            checkpoint.Balance,
            checkpoint.Notes,
            checkpoint.ExpectedBalance,
            checkpoint.Discrepancy,
            checkpoint.CreatedAt,
            checkpoint.UpdatedAt);
}

