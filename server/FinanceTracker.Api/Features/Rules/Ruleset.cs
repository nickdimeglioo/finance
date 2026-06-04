namespace FinanceTracker.Api.Features.Rules;

[TableName("rulesets")]
public sealed class Ruleset
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    [DBType("jsonb")]
    public string SourceConfig { get; set; } = "{}";
    [DBType("jsonb")]
    public string Rules { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
