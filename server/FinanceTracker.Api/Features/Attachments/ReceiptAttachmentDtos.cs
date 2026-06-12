namespace FinanceTracker.Api.Features.Attachments;

public sealed record ReceiptAttachmentDto(
    Guid Id,
    Guid? TransactionId,
    string Title,
    string? Notes,
    string OriginalFileName,
    string StoredFileName,
    string ContentType,
    long SizeBytes,
    decimal? AmountHint,
    string? MerchantHint,
    DateOnly? DateHint,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpdateReceiptAttachmentRequest(
    string Title,
    string? Notes,
    decimal? AmountHint,
    string? MerchantHint,
    DateOnly? DateHint,
    Guid? TransactionId);

public sealed record ReceiptMatchRequest(Guid TransactionId);
public sealed record ReceiptMatchSuggestionDto(ReceiptAttachmentDto Receipt, int Score, IReadOnlyList<string> Reasons);
