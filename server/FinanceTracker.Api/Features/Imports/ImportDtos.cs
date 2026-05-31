namespace FinanceTracker.Api.Features.Imports;

public sealed record ImportBatchDto(
    Guid Id,
    Guid AccountId,
    string? Institution,
    string OriginalFileName,
    string ContentType,
    string S3ObjectKey,
    string Status,
    int RowCount,
    int AcceptedCount,
    int DuplicateCount,
    int ErrorCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UploadImportResponse(
    Guid Id,
    Guid AccountId,
    string OriginalFileName,
    string ContentType,
    string S3ObjectKey,
    string Status);

public sealed record ParsedImportDto(
    Guid BatchId,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string>> SampleRows,
    IReadOnlyList<ImportTemplateDto> Templates);

public sealed record ImportTemplateDto(
    Guid Id,
    string? Institution,
    string Name,
    IReadOnlyDictionary<string, string> ColumnMap,
    string? DateFormat,
    string? AmountFormat);

public sealed record ImportColumnMap(
    string? Date,
    string? Description,
    string? Merchant,
    string? Amount,
    string? Debit,
    string? Credit,
    string? Type,
    string? Category,
    string? Classification);

public sealed record PreviewImportRequest(
    ImportColumnMap ColumnMap,
    string? DateFormat,
    string? AmountFormat,
    bool SaveTemplate,
    string? TemplateName);

public sealed record ImportPreviewRowDto(
    Guid Id,
    int RowNumber,
    IReadOnlyDictionary<string, string> RawData,
    string? RawDescription,
    string? CleanedDescription,
    DateOnly? Date,
    decimal? Amount,
    string? Type,
    string? Category,
    string Classification,
    string? ImportHash,
    bool IsDuplicate,
    bool Accepted,
    IReadOnlyList<string> Errors);

public sealed record UpdateImportPreviewRowRequest(
    string? CleanedDescription,
    DateOnly? Date,
    decimal? Amount,
    string? Type,
    string? Category,
    string? Classification,
    bool? Accepted);

public sealed record ImportCommitResult(
    int Imported,
    int SkippedDuplicates,
    int Rejected,
    int Errors);
