namespace FinanceTracker.Api.Features.Accounts;

public sealed class AccountListItemDto
{
    public Guid Id { get; set; }
    public string? Institution { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public decimal CurrentBalance { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IncludeInDashboard { get; set; }
}

public sealed class AccountDetailDto
{
    public Guid Id { get; set; }
    public string? Institution { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal? CreditLimit { get; set; }
    public decimal? InterestRate { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IncludeInDashboard { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

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
