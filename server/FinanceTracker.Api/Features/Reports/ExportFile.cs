namespace FinanceTracker.Api.Features.Reports;

[TableName("export_files")]
public sealed class ExportFile
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string ExportType { get; set; } = "transactions";
    [DBType("jsonb")]
    public string Filters { get; set; } = "{}";
    public string S3ObjectKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/csv";
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

