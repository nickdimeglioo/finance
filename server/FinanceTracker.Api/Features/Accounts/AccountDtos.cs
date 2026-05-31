namespace FinanceTracker.Api.Features.Accounts;

public sealed record AccountListItemDto(
    Guid Id,
    string? Institution,
    string Nickname,
    string Type,
    string Currency,
    decimal CurrentBalance,
    string Status,
    bool IncludeInDashboard);

public sealed record AccountDetailDto(
    Guid Id,
    string? Institution,
    string Nickname,
    string Type,
    string Currency,
    decimal OpeningBalance,
    decimal CurrentBalance,
    decimal? CreditLimit,
    decimal? InterestRate,
    string Status,
    bool IncludeInDashboard,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateAccountRequest(
    string? Institution,
    string Nickname,
    string Type,
    string Currency,
    decimal OpeningBalance,
    decimal? CreditLimit,
    decimal? InterestRate,
    string? Notes,
    bool IncludeInDashboard = true);

public sealed record UpdateAccountRequest(
    string? Institution,
    string Nickname,
    string Type,
    string Currency,
    decimal? CreditLimit,
    decimal? InterestRate,
    string Status,
    bool IncludeInDashboard,
    string? Notes);

