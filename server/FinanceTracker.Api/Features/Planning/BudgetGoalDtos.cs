namespace FinanceTracker.Api.Features.Planning;

public sealed record BudgetGoalDto(
    Guid Id,
    string Name,
    string Kind,
    Guid? AccountId,
    string? Category,
    string? Classification,
    IReadOnlyList<string> TagNames,
    DateOnly StartsOn,
    DateOnly EndsOn,
    decimal TargetAmount,
    string Currency,
    bool IncludeSplits,
    bool IsActive,
    decimal CurrentAmount,
    decimal RemainingAmount,
    decimal PercentComplete,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertBudgetGoalRequest(
    string Name,
    string Kind,
    Guid? AccountId,
    string? Category,
    string? Classification,
    IReadOnlyList<string>? TagNames,
    DateOnly StartsOn,
    DateOnly EndsOn,
    decimal TargetAmount,
    string Currency,
    bool IncludeSplits,
    bool IsActive);
