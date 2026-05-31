namespace FinanceTracker.Api.Configuration;

public sealed class FinanceAuthOptions
{
    public const string SectionName = "FinanceAuth";

    public Guid DevelopmentUserId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public string DevelopmentEmail { get; set; } = "dev@finance.local";
}

