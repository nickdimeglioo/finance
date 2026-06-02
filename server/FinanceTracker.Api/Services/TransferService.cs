using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class TransferService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;
    private readonly TransactionService _transactionService;

    public TransferService(ICurrentUserContext currentUser, IOrmMapperService db, TransactionService transactionService)
    {
        _currentUser = currentUser;
        _db = db;
        _transactionService = transactionService;
    }

    public async Task<TransactionDetailDto> CreateAsync(CreateTransferRequest request, CancellationToken cancellationToken)
    {
        if (request.FromAccountId == request.ToAccountId)
        {
            throw new ArgumentException("Transfer accounts must be different.");
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentException("Transfer amount must be greater than zero.");
        }

        if (!FinanceValues.Classifications.Contains(request.Classification))
        {
            throw new ArgumentException("Invalid classification.");
        }

        await using var transaction = _db.BeginMultiTransaction();
        transaction.Open();

        try
        {
            await _transactionService.EnsureAccountOwnedAsync(request.FromAccountId, transaction, cancellationToken);
            await _transactionService.EnsureAccountOwnedAsync(request.ToAccountId, transaction, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var outId = Guid.NewGuid();
            var inId = Guid.NewGuid();
            var currency = request.Currency.ToUpperInvariant();
            var description = request.Description.Trim();

            var outgoing = new FinancialTransaction
            {
                Id = outId,
                UserId = _currentUser.UserId,
                AccountId = request.FromAccountId,
                Date = request.Date,
                PostedAt = request.PostedAt,
                Description = description,
                Type = "transfer",
                Classification = request.Classification,
                Category = request.Category,
                Amount = request.Amount,
                Currency = currency,
                Direction = "outflow",
                Status = "posted",
                Source = "manual",
                IsVoid = false,
                IsSplit = false,
                TransferPartnerId = inId,
                Metadata = "{}",
                CreatedAt = now,
                UpdatedAt = now
            };

            var incoming = new FinancialTransaction
            {
                Id = inId,
                UserId = _currentUser.UserId,
                AccountId = request.ToAccountId,
                Date = request.Date,
                PostedAt = request.PostedAt,
                Description = description,
                Type = "transfer",
                Classification = request.Classification,
                Category = request.Category,
                Amount = request.Amount,
                Currency = currency,
                Direction = "inflow",
                Status = "posted",
                Source = "manual",
                IsVoid = false,
                IsSplit = false,
                TransferPartnerId = outId,
                Metadata = "{}",
                CreatedAt = now,
                UpdatedAt = now
            };

            await transaction.Save(outgoing);
            await transaction.Save(incoming);
            var result = await _transactionService.GetRequiredAsync(outId, transaction, cancellationToken);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
