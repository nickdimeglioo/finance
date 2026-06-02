namespace FinanceTracker.Api.Features.Transactions;

[TableName("transaction_splits")]
public sealed class TransactionSplit
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TransactionId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Classification { get; set; } = "unknown";
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
