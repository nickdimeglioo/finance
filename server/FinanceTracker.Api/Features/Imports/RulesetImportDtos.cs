using FinanceTracker.Api.Features.Rules;

namespace FinanceTracker.Api.Features.Imports;

public sealed record RulesetImportRequest(
    Guid AccountId,
    Guid RulesetId,
    string? DeduplicationStrategy);

public sealed record RulesetImportResult(
    ImportJobDto Job,
    IReadOnlyList<RulesetImportPreviewRowDto> PreviewRows);

public sealed record ImportJobDto(
    Guid Id,
    Guid AccountId,
    Guid RulesetId,
    int RulesetVersion,
    string FileName,
    string Status,
    int TotalRows,
    int SuccessRows,
    int SkippedRows,
    int ErrorRows,
    IReadOnlyList<ImportJobErrorDto> Errors,
    bool IsDryRun,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record ImportJobErrorDto(
    int Row,
    string? Column,
    string Message);

public sealed record RulesetImportPreviewRowDto(
    int RowNumber,
    IReadOnlyDictionary<string, string> RawRow,
    DateOnly? Date,
    decimal? Amount,
    string? Type,
    string? Description,
    string? Merchant,
    string? Category,
    string? Subcategory,
    string? Classification,
    IReadOnlyList<string> Tags,
    string? UniqueId,
    bool IsDuplicate,
    bool Accepted,
    IReadOnlyList<string> MatchedRuleIds,
    IReadOnlyList<ImportJobErrorDto> Errors);

public sealed record ParsedCsvRow(
    int RowNumber,
    IReadOnlyDictionary<string, string> Values);

public sealed record ParsedCsvResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<ParsedCsvRow> Rows,
    IReadOnlyList<ImportJobErrorDto> Errors);

public sealed record MappedRulesetTransaction(
    int RowNumber,
    IReadOnlyDictionary<string, string> RawRow,
    DateOnly? Date,
    decimal? Amount,
    string? Type,
    string? Description,
    string? Merchant,
    string? Category,
    string? Subcategory,
    string? Classification,
    IReadOnlyList<string> Tags,
    string? UniqueId,
    IReadOnlyList<ImportJobErrorDto> Errors);

public sealed record ClassifiedRulesetTransaction(
    MappedRulesetTransaction Mapped,
    string? Merchant,
    string? Category,
    string? Subcategory,
    string Classification,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> MatchedRuleIds);
