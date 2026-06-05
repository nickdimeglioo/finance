using FinanceTracker.Api.Features.Transactions;

namespace FinanceTracker.Api.Features.Reconciliation;

public sealed record BalanceCheckpointDto(
    Guid Id,
    Guid AccountId,
    DateOnly Date,
    decimal Balance,
    string? Notes,
    decimal ExpectedBalance,
    decimal Discrepancy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateBalanceCheckpointRequest(
    DateOnly Date,
    decimal Balance,
    string? Notes);

public sealed record ReconcileAccountDto(
    Guid AccountId,
    DateOnly From,
    DateOnly To,
    decimal OpeningClearedBalance,
    decimal ClosingExpectedBalance,
    IReadOnlyList<TransactionListItemDto> UnreconciledTransactions);

