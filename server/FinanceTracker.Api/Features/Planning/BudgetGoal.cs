namespace FinanceTracker.Api.Features.Planning;

[TableName("budget_goals")]
public sealed class BudgetGoal
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "budget";
    public Guid? AccountId { get; set; }
    public string? Category { get; set; }
    public string? Classification { get; set; }
    [DBType("jsonb")]
    public string TagNames { get; set; } = "[]";
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public decimal TargetAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public bool IncludeSplits { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
