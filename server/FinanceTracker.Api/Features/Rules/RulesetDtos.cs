using System.Text.Json;

namespace FinanceTracker.Api.Features.Rules;

public sealed record RulesetDto(
    Guid Id,
    string Name,
    string? Description,
    int Version,
    RulesetSourceConfigDto SourceConfig,
    RulesetRulesDocumentDto Rules,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertRulesetRequest(
    string Name,
    string? Description,
    RulesetSourceConfigDto? SourceConfig,
    RulesetRulesDocumentDto? Rules,
    bool IsActive);

public sealed record RulesetSourceConfigDto(
    string? Delimiter,
    string? Encoding,
    IReadOnlyList<string>? ExpectedColumns);

public sealed record RulesetRulesDocumentDto(
    string? OnError,
    RulesetFallbackDto? Fallback,
    IReadOnlyList<RulesetRuleDefinitionDto>? Rules);

public sealed record RulesetRuleDefinitionDto(
    string Id,
    string? Name,
    int Priority,
    string Kind,
    bool IsActive,
    string? Target,
    ClassificationConditionDto? Match,
    IReadOnlyList<RulesetRuleStepDto>? Flow,
    RulesetRuleOutputDto? Output);

public sealed record RulesetRuleStepDto(
    ClassificationConditionDto? When,
    string? Source,
    JsonElement? Value,
    MappingTransformDto? Transform,
    string? Expr);

public sealed record MappingTransformDto(
    string Type,
    string? Format,
    string? Value);

public sealed record RulesetFallbackDto(
    string? Merchant,
    string? Category,
    string? Subcategory,
    string? Classification,
    IReadOnlyList<string>? Tags);

public sealed record ClassificationConditionDto(
    string Op,
    string? Field,
    JsonElement? Value,
    IReadOnlyList<ClassificationConditionDto>? Conditions);

public sealed record RulesetRuleOutputDto(
    string? Merchant,
    string? Category,
    string? Subcategory,
    string? Classification,
    IReadOnlyList<string>? Tags);

public sealed record ImportRulesetJsonRequest(RulesetDto Ruleset);
