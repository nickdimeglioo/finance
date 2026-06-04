namespace FinanceTracker.Api.Features.Imports;

[TableName("import_preview_rows")]
public sealed class ImportPreviewRow
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ImportBatchId { get; set; }
    public int RowNumber { get; set; }
    [DBType("jsonb")]
    public string RawData { get; set; } = "{}";
    public string? RawDescription { get; set; }
    public string? CleanedDescription { get; set; }
    public DateOnly? Date { get; set; }
    public decimal? Amount { get; set; }
    public string? Type { get; set; }
    public string? Category { get; set; }
    public string Classification { get; set; } = "unknown";
    public string? ImportHash { get; set; }
    public bool IsDuplicate { get; set; }
    public bool Accepted { get; set; } = true;
    [DBType("jsonb")]
    public string Errors { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
