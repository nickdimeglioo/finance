using FinanceTracker.Api.Features.Attachments;
using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Api.Infrastructure.Storage;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class ReceiptAttachmentService
{
    private const long MaxReceiptBytes = 25 * 1024 * 1024;

    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;
    private readonly IObjectStorageService _storage;

    public ReceiptAttachmentService(ICurrentUserContext currentUser, IOrmMapperService db, IObjectStorageService storage)
    {
        _currentUser = currentUser;
        _db = db;
        _storage = storage;
    }

    public async Task<IReadOnlyList<ReceiptAttachmentDto>> ListAsync(string? status, Guid? transactionId, CancellationToken cancellationToken)
    {
        if (transactionId is Guid id && !await TransactionOwnedAsync(id, cancellationToken))
        {
            return [];
        }

        var items = await LoadOwnedAsync();
        return items
            .Where(item => string.IsNullOrWhiteSpace(status) || item.Status == status)
            .Where(item => transactionId is null || item.TransactionId == transactionId)
            .OrderBy(item => item.Status)
            .ThenByDescending(item => item.CreatedAt)
            .Select(ToDto)
            .ToList();
    }

    public async Task<ReceiptAttachmentDto> UploadAsync(
        string title,
        string? notes,
        decimal? amountHint,
        string? merchantHint,
        DateOnly? dateHint,
        Guid? transactionId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        ValidateMetadata(title, amountHint);
        if (file.Length <= 0 || file.Length > MaxReceiptBytes)
        {
            throw new ArgumentException("Receipt files must be between 1 byte and 25 MB.");
        }

        if (transactionId is Guid id && !await TransactionOwnedAsync(id, cancellationToken))
        {
            throw new ArgumentException("Transaction was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new ReceiptAttachment
        {
            UserId = _currentUser.UserId,
            TransactionId = transactionId,
            Title = title.Trim(),
            Notes = EmptyToNull(notes),
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            AmountHint = amountHint,
            MerchantHint = EmptyToNull(merchantHint),
            DateHint = dateHint,
            Status = transactionId.HasValue ? "matched" : "unmatched",
            CreatedAt = now,
            UpdatedAt = now
        };
        entity.S3ObjectKey = $"attachments/receipts/{_currentUser.UserId:D}/{entity.Id:D}/{entity.StoredFileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _storage.PutAsync(entity.S3ObjectKey, stream, entity.ContentType, cancellationToken);
        }

        await _db.SaveAsync(entity, auditUserId: _currentUser.UserId.ToString());
        return ToDto(entity);
    }

    public async Task<ReceiptAttachmentDto?> UpdateAsync(Guid id, UpdateReceiptAttachmentRequest request, CancellationToken cancellationToken)
    {
        ValidateMetadata(request.Title, request.AmountHint);
        var entity = await GetOwnedAsync(id);
        if (entity is null) return null;
        if (request.TransactionId is Guid transactionId && !await TransactionOwnedAsync(transactionId, cancellationToken))
        {
            throw new ArgumentException("Transaction was not found.");
        }

        entity.Title = request.Title.Trim();
        entity.Notes = EmptyToNull(request.Notes);
        entity.AmountHint = request.AmountHint;
        entity.MerchantHint = EmptyToNull(request.MerchantHint);
        entity.DateHint = request.DateHint;
        entity.TransactionId = request.TransactionId;
        entity.Status = request.TransactionId.HasValue ? "matched" : "unmatched";
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(entity, auditUserId: _currentUser.UserId.ToString());
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await GetOwnedAsync(id);
        return entity is not null && await _db.DeleteAsync(entity, userId: _currentUser.UserId.ToString());
    }

    public async Task<IReadOnlyList<ReceiptMatchSuggestionDto>?> MatchAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await _db.GetByIdAsync<FinancialTransaction>(transactionId, depth: 0);
        if (transaction?.UserId != _currentUser.UserId) return null;
        return (await LoadOwnedAsync())
            .Where(receipt => receipt.Status == "unmatched")
            .Select(receipt => Score(receipt, transaction))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ToList();
    }

    public async Task<ReceiptAttachmentDto?> AcceptMatchAsync(Guid id, Guid transactionId, CancellationToken cancellationToken)
    {
        var receipt = await GetOwnedAsync(id);
        var transaction = await _db.GetByIdAsync<FinancialTransaction>(transactionId, depth: 0);
        if (receipt is null || transaction?.UserId != _currentUser.UserId) return null;
        receipt.TransactionId = transactionId;
        receipt.Status = "matched";
        receipt.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(receipt, auditUserId: _currentUser.UserId.ToString());
        return ToDto(receipt);
    }

    public async Task<ReceiptAttachmentDto?> DismissAsync(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await GetOwnedAsync(id);
        if (receipt is null) return null;
        receipt.Status = "dismissed";
        receipt.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(receipt, auditUserId: _currentUser.UserId.ToString());
        return ToDto(receipt);
    }

    public async Task<(ReceiptAttachmentDto Metadata, StoredObject File)?> DownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await GetOwnedAsync(id);
        if (receipt is null) return null;
        var stored = await _storage.GetAsync(receipt.S3ObjectKey, cancellationToken);
        return (ToDto(receipt), stored);
    }

    private static ReceiptMatchSuggestionDto Score(ReceiptAttachment receipt, FinancialTransaction transaction)
    {
        var score = 0;
        var reasons = new List<string>();
        var text = $"{transaction.Merchant} {transaction.Description}";
        if (!string.IsNullOrWhiteSpace(receipt.MerchantHint) && text.Contains(receipt.MerchantHint, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
            reasons.Add("merchant");
        }

        if (receipt.AmountHint.HasValue && receipt.AmountHint.Value > 0 && Math.Abs(transaction.Amount - receipt.AmountHint.Value) <= receipt.AmountHint.Value * 0.05m)
        {
            score += 30;
            reasons.Add("amount");
        }

        if (receipt.DateHint.HasValue && Math.Abs(transaction.Date.DayNumber - receipt.DateHint.Value.DayNumber) <= 7)
        {
            score += 20;
            reasons.Add("date");
        }

        return new ReceiptMatchSuggestionDto(ToDto(receipt), score, reasons);
    }

    private async Task<bool> TransactionOwnedAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await _db.GetByIdAsync<FinancialTransaction>(transactionId, depth: 0);
        return transaction?.UserId == _currentUser.UserId;
    }

    private async Task<List<ReceiptAttachment>> LoadOwnedAsync()
        => await _db.QuerySelect<ReceiptAttachment>().From<ReceiptAttachment>().SelectAllFrom<ReceiptAttachment>().Where(item => item.UserId == _currentUser.UserId).ToListAsync();

    private async Task<ReceiptAttachment?> GetOwnedAsync(Guid id)
    {
        var item = await _db.GetByIdAsync<ReceiptAttachment>(id, depth: 0);
        return item?.UserId == _currentUser.UserId ? item : null;
    }

    private static void ValidateMetadata(string title, decimal? amountHint)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Receipt title is required.");
        if (amountHint < 0) throw new ArgumentException("Amount hint cannot be negative.");
    }

    private static ReceiptAttachmentDto ToDto(ReceiptAttachment receipt)
        => new(
            receipt.Id,
            receipt.TransactionId,
            receipt.Title,
            receipt.Notes,
            receipt.OriginalFileName,
            receipt.StoredFileName,
            receipt.ContentType,
            receipt.SizeBytes,
            receipt.AmountHint,
            receipt.MerchantHint,
            receipt.DateHint,
            receipt.Status,
            receipt.CreatedAt,
            receipt.UpdatedAt);

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
