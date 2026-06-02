namespace FinanceTracker.Api.Features.Imports;

[TableName("import_batches")]
public sealed class ImportBatch
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public string? Institution { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/csv";
    public string S3ObjectKey { get; set; } = string.Empty;
    public string Status { get; set; } = "uploaded";
    public int RowCount { get; set; }
    public int AcceptedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int ErrorCount { get; set; }
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
