namespace FinanceTracker.Api.Features.Rules;

public sealed record ImportRuleDto(
    Guid Id,
    Guid? RuleSetId,
    string Name,
    string? Pattern,
    string? SourceField,
    string? TargetField,
    string ValueTransform,
    string? MapsToType,
    string? MapsToCategory,
    string? MapsToClassification,
    string? MapsToDescription,
    int Priority,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertImportRuleRequest(
    Guid? RuleSetId,
    string Name,
    string? Pattern,
    string? SourceField,
    string? TargetField,
    string? ValueTransform,
    string? MapsToType,
    string? MapsToCategory,
    string? MapsToClassification,
    string? MapsToDescription,
    int Priority,
    bool IsActive);

public sealed record TestImportRuleRequest(string RawDescription);

public sealed record ImportRuleSetDto(
    Guid Id,
    string Name,
    string? Institution,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertImportRuleSetRequest(
    string Name,
    string? Institution,
    bool IsActive);

public sealed record TestImportRuleResult(
    string RawDescription,
    string CleanedDescription,
    string? Type,
    string? Category,
    string Classification,
    IReadOnlyList<ImportRuleDto> MatchedRules);

public sealed record ClassificationRuleDto(
    Guid Id,
    string Name,
    string RuleType,
    string FieldTarget,
    string Value,
    string Classification,
    string? AlsoSetCategory,
    int Priority,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertClassificationRuleRequest(
    string Name,
    string RuleType,
    string FieldTarget,
    string Value,
    string Classification,
    string? AlsoSetCategory,
    int Priority,
    bool IsActive);

public sealed record TestClassificationRuleRequest(
    string Description,
    string? Merchant,
    string? Category,
    decimal Amount);

public sealed record TestClassificationRuleResult(
    string Classification,
    string? Category,
    ClassificationRuleDto? MatchedRule);

public sealed record ReorderRulesRequest(IReadOnlyList<Guid> RuleIds);
