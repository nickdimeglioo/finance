namespace FinanceTracker.Api.Features.Rules;

[TableName("import_rule_sets")]
public sealed class ImportRuleSet
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
