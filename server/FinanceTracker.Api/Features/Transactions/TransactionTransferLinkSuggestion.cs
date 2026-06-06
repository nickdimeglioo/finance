namespace FinanceTracker.Api.Features.Transactions;

[TableName("transaction_transfer_link_suggestions")]
public sealed class TransactionTransferLinkSuggestion
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid? TargetAccountId { get; set; }
    public Guid? CandidateTransactionId { get; set; }
    public string LinkMode { get; set; } = "suggest";
    public int MatchWindowDays { get; set; } = 7;
    public int CandidateCount { get; set; }
    public string Status { get; set; } = "suggested";
    public string? Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
