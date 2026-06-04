namespace FinanceTracker.Api.Features.Imports;

[TableName("import_jobs")]
public sealed class ImportJob
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public Guid RulesetId { get; set; }
    public int RulesetVersion { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int SkippedRows { get; set; }
    public int ErrorRows { get; set; }
    [DBType("jsonb")]
    public string Errors { get; set; } = "[]";
    public bool IsDryRun { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
