namespace FinanceTracker.Api.Features.Transactions;

[TableName("transactions")]
public sealed class FinancialTransaction
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? PostedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Merchant { get; set; }
    public string Type { get; set; } = "expense";
    public string Classification { get; set; } = "unknown";
    public string? Category { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Direction { get; set; } = "neutral";
    public string Status { get; set; } = "posted";
    public string Source { get; set; } = "manual";
    public string? ImportHash { get; set; }
    public bool IsVoid { get; set; }
    public bool IsSplit { get; set; }
    public Guid? TransferPartnerId { get; set; }
    public Guid? RecurringRuleId { get; set; }
    [DBType("jsonb")]
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
