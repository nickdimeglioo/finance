namespace FinanceTracker.Api.Features.Guidance;

[TableName("user_finance_profiles")]
public sealed class UserFinanceProfile
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public decimal? AnnualIncome { get; set; }
    public string IncomeType { get; set; } = "other";
    public int Dependents { get; set; }
    [DBType("jsonb")]
    public string FinancialGoals { get; set; } = "[]";
    [DBType("jsonb")]
    public string CategoryMappings { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

