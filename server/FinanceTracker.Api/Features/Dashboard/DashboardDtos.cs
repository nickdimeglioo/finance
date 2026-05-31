using FinanceTracker.Api.Features.Transactions;

namespace FinanceTracker.Api.Features.Dashboard;

public sealed record DashboardSummaryDto(
    DateOnly From,
    DateOnly To,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetCashFlow,
    decimal TotalLiquidBalance,
    IReadOnlyList<TransactionListItemDto> RecentTransactions,
    int PendingReminderCount);

