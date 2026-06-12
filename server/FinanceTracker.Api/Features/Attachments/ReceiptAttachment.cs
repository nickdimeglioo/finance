namespace FinanceTracker.Api.Features.Attachments;

[TableName("receipt_attachments")]
public sealed class ReceiptAttachment
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TransactionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string S3ObjectKey { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public decimal? AmountHint { get; set; }
    public string? MerchantHint { get; set; }
    public DateOnly? DateHint { get; set; }
    public string Status { get; set; } = "unmatched";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
