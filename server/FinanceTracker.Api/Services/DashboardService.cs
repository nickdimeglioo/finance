using FinanceTracker.Api.Features.Dashboard;
using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Api.Mapping;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class DashboardService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;
    private readonly ReminderService _reminders;

    public DashboardService(ICurrentUserContext currentUser, IOrmMapperService db, ReminderService reminders)
    {
        _currentUser = currentUser;
        _db = db;
        _reminders = reminders;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(DateOnly? from, DateOnly? to, IReadOnlyCollection<Guid>? accountIds, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rangeFrom = from ?? new DateOnly(today.Year, today.Month, 1);
        var rangeTo = to ?? rangeFrom.AddMonths(1).AddDays(-1);
        var selectedAccountIds = accountIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        var filterAccounts = selectedAccountIds.Length > 0;

        var transactions = await _db.QuerySelect<FinancialTransaction>()
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(transaction => transaction.UserId == _currentUser.UserId)
            .ToListAsync();
        var scopedTransactions = transactions
            .Where(transaction => !filterAccounts || selectedAccountIds.Contains(transaction.AccountId))
            .ToList();

        var reportingTransactions = scopedTransactions
            .Where(transaction => transaction.Status is "posted" or "reconciled")
            .Where(transaction => !transaction.IsVoid)
            .Where(transaction => transaction.Date >= rangeFrom && transaction.Date <= rangeTo)
            .ToList();

        var totalIncome = reportingTransactions
            .Where(transaction => transaction.Type == "income")
            .Sum(transaction => transaction.Amount);
        var totalExpenses = reportingTransactions
            .Where(transaction => transaction.Type == "expense")
            .Sum(transaction => transaction.Amount);

        var activeAccounts = await _db.QuerySelect<FinanceTracker.Api.Features.Accounts.Account>()
            .From<FinanceTracker.Api.Features.Accounts.Account>()
            .SelectAllFrom<FinanceTracker.Api.Features.Accounts.Account>()
            .Where(account => account.UserId == _currentUser.UserId && account.Status == "active")
            .ToListAsync();
        var activeAccountIds = activeAccounts
            .Where(account => !filterAccounts || selectedAccountIds.Contains(account.Id))
            .Select(account => account.Id)
            .ToHashSet();
        var liquidBalance = scopedTransactions
            .Where(transaction => activeAccountIds.Contains(transaction.AccountId))
            .Where(AccountService.CountsTowardBalance)
            .Sum(AccountService.SignedBalanceAmount);

        var recent = scopedTransactions
            .Where(transaction => transaction.IsVoid == false)
            .OrderByDescending(transaction => transaction.Date)
            .ThenByDescending(transaction => transaction.CreatedAt)
            .Take(10)
            .MapToList<FinancialTransaction, TransactionListItemDto>();

        var reminders = await _reminders.ListAsync(includeResolved: false, cancellationToken);
        return new DashboardSummaryDto(
            rangeFrom,
            rangeTo,
            totalIncome,
            totalExpenses,
            totalIncome - totalExpenses,
            liquidBalance,
            recent,
            reminders.Count,
            reminders);
    }
}
