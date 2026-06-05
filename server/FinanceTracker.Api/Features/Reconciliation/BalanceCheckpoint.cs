namespace FinanceTracker.Api.Features.Reconciliation;

[TableName("balance_checkpoints")]
public sealed class BalanceCheckpoint
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Balance { get; set; }
    public string? Notes { get; set; }
    public decimal ExpectedBalance { get; set; }
    public decimal Discrepancy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

