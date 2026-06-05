namespace FinanceTracker.Api.Features.Organization;

[TableName("tags")]
public sealed class Tag
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[TableName("transaction_tags")]
public sealed class TransactionTag
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public Guid TagId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

[TableName("recurring_rules")]
public sealed class RecurringRule
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? AccountId { get; set; }
    public string Type { get; set; } = "expense";
    public string Classification { get; set; } = "unknown";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Category { get; set; }
    public string? MerchantKeyword { get; set; }
    public string Frequency { get; set; } = "monthly";
    public DateOnly NextExpected { get; set; }
    public DateOnly? LastMatchedDate { get; set; }
    public decimal AmountTolerance { get; set; } = 0.20m;
    [DBType("jsonb")]
    public string Tags { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[TableName("notes")]
public sealed class FinanceNote
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public decimal? AmountHint { get; set; }
    public string? MerchantHint { get; set; }
    public DateOnly? DateHint { get; set; }
    public Guid? MatchedTransactionId { get; set; }
    public string Status { get; set; } = "unmatched";
    public DateOnly? RemindOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[TableName("reminders")]
public sealed class Reminder
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Type { get; set; } = "custom";
    public Guid? SourceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateOnly DueOn { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
