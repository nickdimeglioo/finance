namespace FinanceTracker.Api.Features.Guidance;

public sealed record UserFinanceProfileDto(
    Guid Id,
    DateOnly? DateOfBirth,
    decimal? AnnualIncome,
    string IncomeType,
    int Dependents,
    IReadOnlyList<string> FinancialGoals,
    IReadOnlyDictionary<string, string> CategoryMappings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpdateUserFinanceProfileRequest(
    DateOnly? DateOfBirth,
    decimal? AnnualIncome,
    string IncomeType,
    int Dependents,
    IReadOnlyList<string>? FinancialGoals,
    IReadOnlyDictionary<string, string>? CategoryMappings);

public sealed record GuidanceItemDto(
    string Id,
    string Title,
    string Status,
    string Message,
    IReadOnlyDictionary<string, decimal> SupportingMetrics);

public sealed record GuidanceSummaryDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<GuidanceItemDto> Guidance);

