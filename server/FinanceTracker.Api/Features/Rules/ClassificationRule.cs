namespace FinanceTracker.Api.Features.Rules;

[TableName("classification_rules")]
public sealed class ClassificationRule
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RuleType { get; set; } = "keyword_contains";
    public string FieldTarget { get; set; } = "description";
    public string Value { get; set; } = string.Empty;
    public string Classification { get; set; } = "unknown";
    public string? AlsoSetCategory { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
