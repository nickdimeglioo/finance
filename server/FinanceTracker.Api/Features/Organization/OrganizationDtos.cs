namespace FinanceTracker.Api.Features.Organization;

public sealed record TagDto(Guid Id, string Name, string? Color, int TransactionCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record UpsertTagRequest(string Name, string? Color);
public sealed record ReplaceTransactionTagsRequest(IReadOnlyList<Guid> TagIds);

public sealed record RecurringRuleDto(
    Guid Id, string Name, Guid? AccountId, string Type, string Classification, decimal Amount, string Currency,
    string? Category, string? MerchantKeyword, string Frequency, DateOnly NextExpected, DateOnly? LastMatchedDate,
    decimal AmountTolerance, IReadOnlyList<string> Tags, bool IsActive, string Status, decimal MonthlyNormalizedCost,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record UpsertRecurringRuleRequest(
    string Name, Guid? AccountId, string Type, string Classification, decimal Amount, string Currency,
    string? Category, string? MerchantKeyword, string Frequency, DateOnly NextExpected, decimal AmountTolerance,
    IReadOnlyList<string>? Tags, bool IsActive);
public sealed record SubscriptionStatusDto(decimal MonthlyTotal, decimal BusinessMonthlyTotal, decimal PersonalMonthlyTotal, IReadOnlyList<RecurringRuleDto> Rules);
public sealed record RecurringRuleSuggestionDto(
    Guid AccountId, string Name, string Type, string Classification, decimal Amount, string Currency,
    string? Category, string MerchantKeyword, string Frequency, DateOnly NextExpected, int Occurrences);

public sealed record NoteDto(
    Guid Id, string Title, string? Body, decimal? AmountHint, string? MerchantHint, DateOnly? DateHint,
    Guid? MatchedTransactionId, string Status, DateOnly? RemindOn, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record UpsertNoteRequest(string Title, string? Body, decimal? AmountHint, string? MerchantHint, DateOnly? DateHint, DateOnly? RemindOn);
public sealed record NoteMatchRequest(Guid TransactionId);
public sealed record NoteMatchSuggestionDto(NoteDto Note, int Score, IReadOnlyList<string> Reasons);

public sealed record ReminderDto(Guid Id, string Type, Guid? SourceId, string Title, string? Message, DateOnly DueOn, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
