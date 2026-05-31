namespace FinanceTracker.Api.Features.Accounts;

[TableName("accounts")]
public sealed class Account
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public string? Institution { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string Type { get; set; } = "checking";
    public string Currency { get; set; } = "USD";
    public decimal OpeningBalance { get; set; }
    public decimal? CreditLimit { get; set; }
    public decimal? InterestRate { get; set; }
    public string Status { get; set; } = "active";
    public bool IncludeInDashboard { get; set; } = true;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

