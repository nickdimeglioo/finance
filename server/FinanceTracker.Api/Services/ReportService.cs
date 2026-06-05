using System.Text.Json;
using FinanceTracker.Api.Features.Accounts;
using FinanceTracker.Api.Features.Organization;
using FinanceTracker.Api.Features.Reports;
using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class ReportService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public ReportService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<IReadOnlyList<CashFlowPointDto>> GetCashFlowAsync(int months, CancellationToken cancellationToken)
    {
        months = Math.Clamp(months, 1, 36);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var transactions = await LoadTransactionsAsync(firstMonth, today, cancellationToken);
        var rows = transactions.Where(IsReportableCashFlow).ToList();
        var cumulative = 0m;
        var result = new List<CashFlowPointDto>();

        for (var i = 0; i < months; i++)
        {
            var month = firstMonth.AddMonths(i);
            var monthRows = rows.Where(transaction => transaction.Date.Year == month.Year && transaction.Date.Month == month.Month).ToList();
            var income = monthRows.Where(transaction => transaction.Type == "income").Sum(transaction => transaction.Amount);
            var expenses = monthRows.Where(transaction => transaction.Type == "expense").Sum(transaction => transaction.Amount);
            var net = income - expenses;
            cumulative += net;
            result.Add(new CashFlowPointDto($"{month:yyyy-MM}", income, expenses, net, cumulative));
        }

        return result;
    }

    public async Task<IReadOnlyList<BreakdownItemDto>> GetCategoryBreakdownAsync(ReportRangeRequest request, CancellationToken cancellationToken)
    {
        var (from, to) = NormalizeRange(request.From, request.To);
        var transactions = await LoadTransactionsAsync(from, to, cancellationToken);
        var splits = await LoadSplitsForTransactionsAsync(transactions, cancellationToken);
        var rows = ExpandRows(transactions, splits)
            .Where(row => row.Type == "expense" && row.Classification != "ignored")
            .Where(row => string.IsNullOrWhiteSpace(request.Classification) || row.Classification == request.Classification)
            .ToList();
        return BuildBreakdown(rows.GroupBy(row => string.IsNullOrWhiteSpace(row.Category) ? "Uncategorized" : row.Category!), row => row.Amount);
    }

    public async Task<BusinessPersonalSummaryDto> GetBusinessPersonalAsync(ReportRangeRequest request, CancellationToken cancellationToken)
    {
        var (from, to) = NormalizeRange(request.From, request.To);
        var transactions = await LoadTransactionsAsync(from, to, cancellationToken);
        var splits = await LoadSplitsForTransactionsAsync(transactions, cancellationToken);
        var rows = ExpandRows(transactions, splits).Where(row => row.Type == "expense").ToList();
        var items = BuildBreakdown(rows.GroupBy(row => row.Classification), row => row.Amount);
        return new BusinessPersonalSummaryDto(
            rows.Where(row => row.Classification == "business").Sum(row => row.Amount),
            rows.Where(row => row.Classification == "personal").Sum(row => row.Amount),
            rows.Where(row => row.Classification == "mixed").Sum(row => row.Amount),
            rows.Where(row => row.Classification == "ignored").Sum(row => row.Amount),
            rows.Where(row => row.Classification == "unknown").Sum(row => row.Amount),
            items);
    }

    public async Task<IReadOnlyList<BreakdownItemDto>> GetTagBreakdownAsync(ReportRangeRequest request, CancellationToken cancellationToken)
    {
        var (from, to) = NormalizeRange(request.From, request.To);
        var rows = (await LoadTransactionsAsync(from, to, cancellationToken))
            .Where(transaction => transaction.Type == "expense" && transaction.Classification != "ignored")
            .SelectMany(transaction => ReadTags(transaction.Tags).DefaultIfEmpty("Untagged").Select(tag => new ReportRow(tag, transaction.Amount)))
            .ToList();
        return BuildBreakdown(rows.GroupBy(row => row.Label), row => row.Amount);
    }

    public async Task<IReadOnlyList<BalanceHistoryPointDto>?> GetBalanceHistoryAsync(Guid accountId, int months, CancellationToken cancellationToken)
    {
        var account = await _db.GetByIdAsync<Account>(accountId, depth: 0);
        if (account?.UserId != _currentUser.UserId)
        {
            return null;
        }

        months = Math.Clamp(months, 1, 60);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var transactions = (await LoadTransactionsAsync(null, today, cancellationToken))
            .Where(transaction => transaction.AccountId == accountId && AccountService.CountsTowardBalance(transaction))
            .OrderBy(transaction => transaction.Date)
            .ToList();
        var result = new List<BalanceHistoryPointDto>();

        for (var i = 0; i < months; i++)
        {
            var monthEnd = firstMonth.AddMonths(i + 1).AddDays(-1);
            var balance = transactions.Where(transaction => transaction.Date <= monthEnd).Sum(AccountService.SignedBalanceAmount);
            result.Add(new BalanceHistoryPointDto(monthEnd, balance));
        }

        return result;
    }

    public async Task<IReadOnlyList<CashFlowPointDto>> GetSubscriptionTrendAsync(int months, CancellationToken cancellationToken)
    {
        months = Math.Clamp(months, 1, 36);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var rules = await _db.QuerySelect<RecurringRule>()
            .From<RecurringRule>()
            .SelectAllFrom<RecurringRule>()
            .Where(rule => rule.UserId == _currentUser.UserId && rule.IsActive == true)
            .ToListAsync();
        var monthlyExpense = rules.Where(rule => rule.Type == "expense").Sum(MonthlyRecurringAmount);
        var monthlyIncome = rules.Where(rule => rule.Type == "income").Sum(MonthlyRecurringAmount);

        return Enumerable.Range(0, months)
            .Select(index =>
            {
                var month = firstMonth.AddMonths(index);
                return new CashFlowPointDto($"{month:yyyy-MM}", monthlyIncome, monthlyExpense, monthlyIncome - monthlyExpense, monthlyIncome - monthlyExpense);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<NetWorthPointDto>> GetNetWorthAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var (rangeFrom, rangeTo) = NormalizeRange(from, to);
        var accounts = await _db.QuerySelect<Account>()
            .From<Account>()
            .SelectAllFrom<Account>()
            .Where(account => account.UserId == _currentUser.UserId)
            .ToListAsync();
        var accountIds = accounts.Select(account => account.Id).ToHashSet();
        var transactions = (await LoadTransactionsAsync(null, rangeTo, cancellationToken))
            .Where(transaction => accountIds.Contains(transaction.AccountId) && AccountService.CountsTowardBalance(transaction))
            .ToList();
        var months = ((rangeTo.Year - rangeFrom.Year) * 12) + rangeTo.Month - rangeFrom.Month + 1;
        var startMonth = new DateOnly(rangeFrom.Year, rangeFrom.Month, 1);

        return Enumerable.Range(0, Math.Clamp(months, 1, 60))
            .Select(index =>
            {
                var monthEnd = startMonth.AddMonths(index + 1).AddDays(-1);
                var netWorth = transactions.Where(transaction => transaction.Date <= monthEnd).Sum(AccountService.SignedBalanceAmount);
                return new NetWorthPointDto(monthEnd, netWorth);
            })
            .ToList();
    }

    internal async Task<IReadOnlyList<FinancialTransaction>> LoadTransactionsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var query = _db.QuerySelect<FinancialTransaction>()
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(transaction => transaction.UserId == _currentUser.UserId && transaction.IsVoid == false);

        if (from is DateOnly fromDate)
        {
            query.Where(transaction => transaction.Date >= fromDate);
        }

        if (to is DateOnly toDate)
        {
            query.Where(transaction => transaction.Date <= toDate);
        }

        return await query.ToListAsync();
    }

    private async Task<IReadOnlyList<TransactionSplit>> LoadSplitsForTransactionsAsync(IReadOnlyList<FinancialTransaction> transactions, CancellationToken cancellationToken)
    {
        var transactionIds = transactions.Where(transaction => transaction.IsSplit).Select(transaction => transaction.Id).ToHashSet();
        if (transactionIds.Count == 0)
        {
            return [];
        }

        var splits = await _db.QuerySelect<TransactionSplit>()
            .From<TransactionSplit>()
            .SelectAllFrom<TransactionSplit>()
            .ToListAsync();
        return splits.Where(split => transactionIds.Contains(split.TransactionId)).ToList();
    }

    private static IReadOnlyList<TransactionReportRow> ExpandRows(IReadOnlyList<FinancialTransaction> transactions, IReadOnlyList<TransactionSplit> splits)
    {
        var splitsByTransaction = splits.GroupBy(split => split.TransactionId).ToDictionary(group => group.Key, group => group.ToList());
        var rows = new List<TransactionReportRow>();
        foreach (var transaction in transactions.Where(IsReportableCashFlow))
        {
            if (transaction.IsSplit && splitsByTransaction.TryGetValue(transaction.Id, out var transactionSplits) && transactionSplits.Count > 0)
            {
                rows.AddRange(transactionSplits.Select(split => new TransactionReportRow(transaction.Type, split.Classification, split.Category, split.Amount)));
                continue;
            }

            rows.Add(new TransactionReportRow(transaction.Type, transaction.Classification, transaction.Category, transaction.Amount));
        }

        return rows;
    }

    private static IReadOnlyList<BreakdownItemDto> BuildBreakdown<TKey>(IEnumerable<IGrouping<TKey, TransactionReportRow>> groups, Func<TransactionReportRow, decimal> amountSelector)
        where TKey : notnull
    {
        var rows = groups
            .Select(group => new { Label = group.Key.ToString() ?? "Uncategorized", Amount = group.Sum(amountSelector) })
            .Where(row => row.Amount > 0)
            .OrderByDescending(row => row.Amount)
            .ToList();
        var total = rows.Sum(row => row.Amount);
        return rows.Select(row => new BreakdownItemDto(row.Label, row.Amount, total == 0 ? 0 : decimal.Round(row.Amount / total * 100m, 2))).ToList();
    }

    private static IReadOnlyList<BreakdownItemDto> BuildBreakdown(IEnumerable<IGrouping<string, ReportRow>> groups, Func<ReportRow, decimal> amountSelector)
    {
        var rows = groups
            .Select(group => new { Label = group.Key, Amount = group.Sum(amountSelector) })
            .Where(row => row.Amount > 0)
            .OrderByDescending(row => row.Amount)
            .ToList();
        var total = rows.Sum(row => row.Amount);
        return rows.Select(row => new BreakdownItemDto(row.Label, row.Amount, total == 0 ? 0 : decimal.Round(row.Amount / total * 100m, 2))).ToList();
    }

    private static (DateOnly From, DateOnly To) NormalizeRange(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var normalizedTo = to ?? today;
        var normalizedFrom = from ?? normalizedTo.AddMonths(-2).AddDays(1);
        return normalizedFrom <= normalizedTo ? (normalizedFrom, normalizedTo) : (normalizedTo, normalizedFrom);
    }

    private static bool IsReportableCashFlow(FinancialTransaction transaction)
        => !transaction.IsVoid && transaction.Type is "income" or "expense";

    private static IReadOnlyList<string> ReadTags(string tags)
        => JsonSerializer.Deserialize<IReadOnlyList<string>>(tags) ?? [];

    private static decimal MonthlyRecurringAmount(RecurringRule rule)
        => rule.Frequency switch
        {
            "daily" => rule.Amount * 30m,
            "weekly" => rule.Amount * 4.33m,
            "biweekly" => rule.Amount * 2.165m,
            "quarterly" => rule.Amount / 3m,
            "yearly" => rule.Amount / 12m,
            _ => rule.Amount
        };

    private sealed record TransactionReportRow(string Type, string Classification, string? Category, decimal Amount);
    private sealed record ReportRow(string Label, decimal Amount);
}
