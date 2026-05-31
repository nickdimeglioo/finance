using FinanceTracker.Api.Features.Shared;

namespace FinanceTracker.Api.Features.Transactions;

public sealed record TransactionListItemDto(
    Guid Id,
    Guid AccountId,
    DateOnly Date,
    DateOnly? PostedAt,
    string Description,
    string? Merchant,
    string Type,
    string Classification,
    string? Category,
    decimal Amount,
    string Currency,
    string Direction,
    string Status,
    string Source,
    bool IsVoid,
    bool IsSplit,
    Guid? TransferPartnerId);

public sealed class TransactionDetailDto
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public DateOnly Date { get; init; }
    public DateOnly? PostedAt { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? Merchant { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Classification { get; init; } = string.Empty;
    public string? Category { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
    public string Direction { get; init; } = "neutral";
    public string Status { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? ImportHash { get; init; }
    public bool IsVoid { get; init; }
    public bool IsSplit { get; init; }
    public Guid? TransferPartnerId { get; init; }
    public Guid? RecurringRuleId { get; init; }
    public IReadOnlyList<TransactionSplitDto> Splits { get; set; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record TransactionSplitDto(
    Guid Id,
    string Category,
    string Classification,
    decimal Amount,
    string? Notes);

public sealed record CreateTransactionRequest(
    Guid AccountId,
    DateOnly Date,
    DateOnly? PostedAt,
    string Description,
    string? Merchant,
    string Type,
    string Classification,
    string? Category,
    decimal Amount,
    string Currency,
    IReadOnlyList<CreateTransactionSplitRequest>? Splits);

public sealed record CreateTransferRequest(
    Guid FromAccountId,
    Guid ToAccountId,
    DateOnly Date,
    DateOnly? PostedAt,
    string Description,
    decimal Amount,
    string Currency,
    string Classification,
    string? Category);

public sealed record CreateTransactionSplitRequest(
    string Category,
    string Classification,
    decimal Amount,
    string? Notes);

public sealed record UpdateTransactionRequest(
    DateOnly Date,
    DateOnly? PostedAt,
    string Description,
    string? Merchant,
    string Classification,
    string? Category,
    decimal Amount,
    string Currency,
    string Status,
    IReadOnlyList<CreateTransactionSplitRequest>? Splits);

public sealed class TransactionFiltersRequest
{
    public Guid? AccountId { get; init; }
    public string? Type { get; init; }
    public string? Classification { get; init; }
    public string? Category { get; init; }
    public string? Status { get; init; }
    public Guid? TagId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public decimal? AmountMin { get; init; }
    public decimal? AmountMax { get; init; }
    public string? Search { get; init; }
    public bool IncludeVoided { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed record TransactionPageDto(PagedResult<TransactionListItemDto> Result);
