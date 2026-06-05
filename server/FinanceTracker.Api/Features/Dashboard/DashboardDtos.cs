using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Api.Features.Organization;

namespace FinanceTracker.Api.Features.Dashboard;

public sealed record DashboardSummaryDto(
    DateOnly From,
    DateOnly To,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetCashFlow,
    decimal TotalLiquidBalance,
    IReadOnlyList<TransactionListItemDto> RecentTransactions,
    int PendingReminderCount,
    IReadOnlyList<ReminderDto> PendingReminders);
