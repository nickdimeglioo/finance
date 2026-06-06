using FinanceTracker.Api.Mapping;
using FinanceTracker.Api.Features.Shared;

namespace FinanceTracker.Api.Features.Transactions;

public sealed class TransactionListItemDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? PostedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Merchant { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Subcategory { get; set; }
    [MapFrom("TagsJson")]
    public IReadOnlyList<string> Tags { get; set; } = [];
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Direction { get; set; } = "neutral";
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool IsVoid { get; set; }
    public bool IsSplit { get; set; }
    public Guid? TransferPartnerId { get; set; }
    public bool HasTransferLinkSuggestion { get; set; }
}

public sealed class TransactionDetailDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? PostedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Merchant { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public string? Category { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Direction { get; set; } = "neutral";
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? ImportHash { get; set; }
    public string? UniqueId { get; set; }
    public Guid? RulesetId { get; set; }
    public int? RulesetVersion { get; set; }
    public string? MatchedClassificationRuleId { get; set; }
    public string? Subcategory { get; set; }
    [MapFrom("TagsJson")]
    public IReadOnlyList<string> Tags { get; set; } = [];
    public bool IsVoid { get; set; }
    public bool IsSplit { get; set; }
    public Guid? TransferPartnerId { get; set; }
    public bool HasTransferLinkSuggestion { get; set; }
    public Guid? RecurringRuleId { get; set; }
    public IReadOnlyList<TransactionSplitDto> Splits { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TransactionSplitDto
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Classification { get; set; } = "unknown";
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

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

public sealed record CreditCardPaymentDrilldownDto(
    Guid PaymentTransactionId,
    Guid? SourceTransactionId,
    Guid AccountId,
    string AccountName,
    DateOnly PaymentDate,
    decimal PaymentAmount,
    decimal BalanceBeforePayment,
    decimal BalanceAfterPayment,
    decimal AppliedAmount,
    decimal UnappliedAmount,
    decimal PaymentAppliedPercent,
    decimal BalancePaidPercent,
    decimal CurrentUnpaidAmount,
    IReadOnlyList<CreditCardPaymentCoverageRowDto> CoveredRows,
    IReadOnlyList<CreditCardUnpaidChargeDto> UnpaidRows);

public sealed record CreditCardPaymentCoverageRowDto(
    Guid TransactionId,
    DateOnly Date,
    string Description,
    string? Merchant,
    string? Category,
    decimal OriginalAmount,
    decimal OutstandingBeforePayment,
    decimal CoveredAmount,
    decimal RemainingAmount,
    decimal CoveredPercent,
    string Currency);

public sealed record CreditCardUnpaidChargeDto(
    Guid TransactionId,
    DateOnly Date,
    string Description,
    string? Merchant,
    string? Category,
    decimal OriginalAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    decimal PaidPercent,
    string Currency);
