using FinanceTracker.Api.Features.Dashboard;
using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Data.Contracts;

namespace FinanceTracker.Api.Services;

public sealed class DashboardService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IFinanceDataSession _db;

    public DashboardService(ICurrentUserContext currentUser, IFinanceDataSession db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rangeFrom = from ?? new DateOnly(today.Year, today.Month, 1);
        var rangeTo = to ?? rangeFrom.AddMonths(1).AddDays(-1);

        var totals = await _db.QuerySingleAsync<DashboardTotals>(
            """
            SELECT
                COALESCE(SUM(CASE WHEN type = 'income' AND is_void = false AND date BETWEEN @From AND @To THEN amount ELSE 0 END), 0) AS total_income,
                COALESCE(SUM(CASE WHEN type = 'expense' AND is_void = false AND date BETWEEN @From AND @To THEN amount ELSE 0 END), 0) AS total_expenses
            FROM transactions
            WHERE user_id = @UserId
              AND status IN ('posted', 'reconciled')
            """,
            new
            {
                UserId = _currentUser.UserId,
                From = rangeFrom.ToDateTime(TimeOnly.MinValue),
                To = rangeTo.ToDateTime(TimeOnly.MinValue)
            },
            cancellationToken: cancellationToken);

        var liquidBalance = await _db.QuerySingleAsync<decimal>(
            $"""
            SELECT {AccountSql.BalanceExpression}
            FROM accounts a
            LEFT JOIN transactions t ON t.account_id = a.id AND t.user_id = a.user_id
            WHERE a.user_id = @UserId
              AND a.include_in_dashboard = true
              AND a.status = 'active'
              AND a.type IN ('checking', 'savings', 'cash')
            """,
            new { UserId = _currentUser.UserId },
            cancellationToken: cancellationToken);

        var recent = await _db.QueryAsync<TransactionListItemDto>(
            TransactionSql.SelectList +
            """
            WHERE t.user_id = @UserId
              AND t.is_void = false
            ORDER BY t.date DESC, t.created_at DESC
            LIMIT 10
            """,
            new { UserId = _currentUser.UserId },
            cancellationToken: cancellationToken);

        return new DashboardSummaryDto(
            rangeFrom,
            rangeTo,
            totals.TotalIncome,
            totals.TotalExpenses,
            totals.TotalIncome - totals.TotalExpenses,
            liquidBalance,
            recent,
            0);
    }

    private sealed class DashboardTotals
    {
        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
    }
}
