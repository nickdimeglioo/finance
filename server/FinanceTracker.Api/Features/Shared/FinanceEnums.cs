namespace FinanceTracker.Api.Features.Shared;

public static class FinanceValues
{
    public static readonly string[] AccountTypes =
    [
        "checking",
        "savings",
        "credit_card",
        "loan",
        "investment",
        "cash",
        "other"
    ];

    public static readonly string[] AccountStatuses =
    [
        "active",
        "archived",
        "closed"
    ];

    public static readonly string[] TransactionTypes =
    [
        "income",
        "expense",
        "transfer",
        "adjustment",
        "opening_balance"
    ];

    public static readonly string[] Classifications =
    [
        "business",
        "personal",
        "mixed",
        "ignored",
        "unknown"
    ];

    public static readonly string[] TransactionStatuses =
    [
        "pending",
        "posted",
        "reconciled",
        "voided"
    ];

    public static readonly string[] TransactionSources =
    [
        "manual",
        "import",
        "system"
    ];
}

