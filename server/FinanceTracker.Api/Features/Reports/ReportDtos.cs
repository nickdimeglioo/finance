namespace FinanceTracker.Api.Features.Reports;

public sealed record ReportRangeRequest(DateOnly? From, DateOnly? To, string? Classification);

public sealed record CashFlowPointDto(
    string Month,
    decimal Income,
    decimal Expenses,
    decimal NetCashFlow,
    decimal CumulativeCashFlow);

public sealed record BreakdownItemDto(
    string Label,
    decimal Amount,
    decimal Percentage);

public sealed record BusinessPersonalSummaryDto(
    decimal BusinessExpenses,
    decimal PersonalExpenses,
    decimal MixedExpenses,
    decimal IgnoredExpenses,
    decimal UnknownExpenses,
    IReadOnlyList<BreakdownItemDto> Items);

public sealed record BalanceHistoryPointDto(
    DateOnly Date,
    decimal Balance);

public sealed record NetWorthPointDto(
    DateOnly Date,
    decimal NetWorth);

public sealed record ExportRequest(
    string ExportType,
    DateOnly? From,
    DateOnly? To,
    Guid? AccountId,
    IReadOnlyList<Guid>? AccountIds,
    string? Classification);

public sealed record ExportFileDto(
    Guid Id,
    string ExportType,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string DownloadUrl);
