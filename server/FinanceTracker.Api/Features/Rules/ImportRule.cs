namespace FinanceTracker.Api.Features.Rules;

[TableName("import_rules")]
public sealed class ImportRule
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public Guid? RuleSetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Pattern { get; set; }
    public string? SourceField { get; set; }
    public string? TargetField { get; set; }
    public string ValueTransform { get; set; } = "copy";
    public string? MapsToType { get; set; }
    public string? MapsToCategory { get; set; }
    public string? MapsToClassification { get; set; }
    public string? MapsToDescription { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
