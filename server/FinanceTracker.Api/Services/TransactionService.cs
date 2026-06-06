using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Api.Mapping;
using PipelineRunner.Services;
using PipelineRunner.Utils;

namespace FinanceTracker.Api.Services;

public sealed class TransactionService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;
    private readonly ClassificationRuleService _classificationRules;
    private readonly TagService _tags;
    private readonly TransferLinkService _transferLinks;

    public TransactionService(ICurrentUserContext currentUser, IOrmMapperService db, ClassificationRuleService classificationRules, TagService tags, TransferLinkService transferLinks)
    {
        _currentUser = currentUser;
        _db = db;
        _classificationRules = classificationRules;
        _tags = tags;
        _transferLinks = transferLinks;
    }

    public async Task<PagedResult<TransactionListItemDto>> ListAsync(TransactionFiltersRequest filters, CancellationToken cancellationToken)
    {
        var page = Math.Max(filters.Page, 1);
        var pageSize = Math.Clamp(filters.PageSize, 1, 250);
        var filtered = await LoadFilteredTransactionsAsync(filters, cancellationToken: cancellationToken);
        if (filters.TagId is Guid tagId)
        {
            var transactionIds = await _tags.LoadTransactionIdsForTagAsync(tagId);
            filtered = filtered.Where(transaction => transactionIds.Contains(transaction.Id)).ToList();
        }
        var total = filtered.Count;
        var pageEntities = filtered
            .OrderByDescending(transaction => transaction.Date)
            .ThenByDescending(transaction => transaction.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize).ToList();
        var items = pageEntities.MapToList<FinancialTransaction, TransactionListItemDto>();
        foreach (var (item, entity) in items.Zip(pageEntities))
        {
            item.Tags = ReadTags(entity.Tags);
            item.HasTransferLinkSuggestion = await _transferLinks.HasOpenSuggestionAsync(entity.Id, cancellationToken);
        }

        return new PagedResult<TransactionListItemDto>(items, page, pageSize, total);
    }

    public async Task<TransactionDetailDto?> GetAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var entity = await GetOwnedEntityAsync(transactionId, cancellationToken: cancellationToken);
        if (entity is null)
        {
            return null;
        }

        return await ToDetailDtoAsync(entity, cancellationToken: cancellationToken);
    }

    public async Task<TransactionDetailDto> CreateAsync(CreateTransactionRequest request, CancellationToken cancellationToken)
    {
        ValidateTransaction(request.Type, request.Classification, request.Amount, request.Currency, request.Splits);
        if (request.Type == "transfer")
        {
            throw new ArgumentException("Use the transfer endpoint to create transfer transactions.");
        }

        await using var transaction = _db.BeginMultiTransaction();
        transaction.Open();

        try
        {
            await EnsureAccountOwnedAsync(request.AccountId, transaction, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var entity = new FinancialTransaction
            {
                Id = Guid.NewGuid(),
                UserId = _currentUser.UserId,
                AccountId = request.AccountId,
                Date = request.Date,
                PostedAt = request.PostedAt,
                Description = request.Description.Trim(),
                Merchant = request.Merchant,
                Type = request.Type,
                Classification = request.Classification,
                Category = request.Category,
                Amount = request.Amount,
                Currency = request.Currency.ToUpperInvariant(),
                Direction = DirectionForType(request.Type),
                Status = "posted",
                Source = "manual",
                IsVoid = false,
                IsSplit = request.Splits?.Count > 0,
                Metadata = "{}",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _classificationRules.ApplyToTransactionAsync(entity, cancellationToken);
            await transaction.Save(entity);
            await ReplaceSplitsAsync(entity.Id, request.Splits, transaction, cancellationToken);
            var result = await GetRequiredAsync(entity.Id, transaction, cancellationToken);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<TransactionDetailDto?> UpdateAsync(Guid transactionId, UpdateTransactionRequest request, CancellationToken cancellationToken)
    {
        if (!FinanceValues.TransactionStatuses.Contains(request.Status))
        {
            throw new ArgumentException("Invalid transaction status.");
        }

        await using var transaction = _db.BeginMultiTransaction();
        transaction.Open();

        try
        {
            var entity = await GetEntityForUpdateAsync(transactionId, transaction, cancellationToken);
            if (entity is null)
            {
                return null;
            }

            if (entity.Type == "transfer" || entity.TransferPartnerId is not null)
            {
                throw new ArgumentException("Transfers cannot be converted through the generic transaction update endpoint.");
            }

            ValidateTransaction(entity.Type, request.Classification, request.Amount, request.Currency, request.Splits);
            await EnsureAccountOwnedAsync(entity.AccountId, transaction, cancellationToken);
            entity.Date = request.Date;
            entity.PostedAt = request.PostedAt;
            entity.Description = request.Description.Trim();
            entity.Merchant = request.Merchant;
            entity.Classification = request.Classification;
            entity.Category = request.Category;
            entity.Amount = request.Amount;
            entity.Currency = request.Currency.ToUpperInvariant();
            entity.Direction = DirectionForType(entity.Type);
            entity.Status = request.Status;
            entity.IsVoid = request.Status == "voided";
            entity.IsSplit = request.Splits?.Count > 0;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await _classificationRules.ApplyToTransactionAsync(entity, cancellationToken);
            await transaction.Save(entity);
            await ReplaceSplitsAsync(entity.Id, request.Splits, transaction, cancellationToken);
            var result = await GetRequiredAsync(entity.Id, transaction, cancellationToken);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> VoidAsync(Guid transactionId, bool includeTransferPartner, CancellationToken cancellationToken)
    {
        await using var transaction = _db.BeginMultiTransaction();
        transaction.Open();

        try
        {
            var entity = await GetEntityForUpdateAsync(transactionId, transaction, cancellationToken);
            if (entity is null)
            {
                return false;
            }

            MarkVoided(entity);
            await transaction.Save(entity);

            if (includeTransferPartner && entity.TransferPartnerId is Guid partnerId)
            {
                var partner = await GetEntityForUpdateAsync(partnerId, transaction, cancellationToken);
                if (partner is not null)
                {
                    MarkVoided(partner);
                    await transaction.Save(partner);
                }
            }

            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<TransactionDetailDto?> UpdateStatusAsync(Guid transactionId, string status, CancellationToken cancellationToken)
    {
        if (!FinanceValues.TransactionStatuses.Contains(status))
        {
            throw new ArgumentException("Invalid transaction status.");
        }

        var entity = await GetOwnedEntityAsync(transactionId, cancellationToken: cancellationToken);

        if (entity is null)
        {
            return null;
        }

        entity.Status = status;
        entity.IsVoid = status == "voided";
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(entity, auditUserId: _currentUser.UserId.ToString());

        return await GetAsync(transactionId, cancellationToken);
    }

    internal async Task EnsureAccountOwnedAsync(Guid accountId, MultiTransaction transaction, CancellationToken cancellationToken)
    {
        var account = await transaction.GetByIdAsync<FinanceTracker.Api.Features.Accounts.Account>(accountId);

        if (account?.UserId != _currentUser.UserId)
        {
            throw new ArgumentException("Account was not found.");
        }
    }

    internal async Task<TransactionDetailDto> GetRequiredAsync(Guid transactionId, MultiTransaction transaction, CancellationToken cancellationToken)
    {
        var entity = await GetOwnedEntityAsync(transactionId, transaction, cancellationToken);
        if (entity is null)
        {
            throw new InvalidOperationException("Transaction was not found after save.");
        }

        return await ToDetailDtoAsync(entity, transaction, cancellationToken);
    }

    private async Task<FinancialTransaction?> GetEntityForUpdateAsync(Guid transactionId, MultiTransaction transaction, CancellationToken cancellationToken)
    {
        return await GetOwnedEntityAsync(transactionId, transaction, cancellationToken);
    }

    private async Task<FinancialTransaction?> GetOwnedEntityAsync(
        Guid transactionId,
        MultiTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var entity = transaction is null
            ? await _db.GetByIdAsync<FinancialTransaction>(transactionId, depth: 0)
            : await transaction.GetByIdAsync<FinancialTransaction>(transactionId);
        return entity?.UserId == _currentUser.UserId ? entity : null;
    }

    private async Task<IReadOnlyList<TransactionSplitDto>> GetSplitsAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var splits = await _db.QuerySelect<TransactionSplit>()
            .From<TransactionSplit>()
            .SelectAllFrom<TransactionSplit>()
            .Where(split => split.TransactionId == transactionId)
            .ToListAsync();
        return splits
            .OrderBy(split => split.CreatedAt)
            .ThenBy(split => split.Id)
            .MapToList<TransactionSplit, TransactionSplitDto>();
    }

    private async Task ReplaceSplitsAsync(Guid transactionId, IReadOnlyList<CreateTransactionSplitRequest>? splits, MultiTransaction transaction, CancellationToken cancellationToken)
    {
        var existingSplits = await transaction.QuerySelect<TransactionSplit>()
            .From<TransactionSplit>()
            .SelectAllFrom<TransactionSplit>()
            .Where(split => split.TransactionId == transactionId)
            .ToListAsync();
        foreach (var existingSplit in existingSplits)
        {
            await transaction.Delete(existingSplit);
        }

        if (splits is null || splits.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var split in splits)
        {
            var entity = new TransactionSplit
            {
                Id = Guid.NewGuid(),
                TransactionId = transactionId,
                Category = split.Category,
                Classification = split.Classification,
                Amount = split.Amount,
                Notes = split.Notes,
                Metadata = "{}",
                CreatedAt = now
            };
            await transaction.Save(entity);
        }
    }

    private static void MarkVoided(FinancialTransaction entity)
    {
        entity.IsVoid = true;
        entity.Status = "voided";
        entity.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateTransaction(string type, string classification, decimal amount, string currency, IReadOnlyList<CreateTransactionSplitRequest>? splits)
    {
        if (!FinanceValues.TransactionTypes.Contains(type))
        {
            throw new ArgumentException("Invalid transaction type.");
        }

        if (!FinanceValues.Classifications.Contains(classification))
        {
            throw new ArgumentException("Invalid classification.");
        }

        if (amount <= 0)
        {
            throw new ArgumentException("Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO code.");
        }

        if (splits is { Count: > 0 })
        {
            var splitTotal = splits.Sum(split => split.Amount);
            if (splitTotal != amount)
            {
                throw new ArgumentException("Split amounts must equal the parent transaction amount.");
            }

            if (splits.Any(split => split.Amount <= 0 || !FinanceValues.Classifications.Contains(split.Classification) || string.IsNullOrWhiteSpace(split.Category)))
            {
                throw new ArgumentException("Each split must have a positive amount, valid classification, and category.");
            }
        }
    }

    private static string DirectionForType(string type)
    {
        return type switch
        {
            "income" or "opening_balance" => "inflow",
            "expense" => "outflow",
            _ => "neutral"
        };
    }

    private async Task<List<FinancialTransaction>> LoadFilteredTransactionsAsync(
        TransactionFiltersRequest filters,
        MultiTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var query = (transaction is null
                ? _db.QuerySelect<FinancialTransaction>()
                : transaction.QuerySelect<FinancialTransaction>())
            .From<FinancialTransaction>("t")
            .SelectAllFrom<FinancialTransaction>("t")
            .Where<FinancialTransaction>(row => row.UserId == _currentUser.UserId, "t");

        if (!filters.IncludeVoided)
        {
            query.Where<FinancialTransaction>(row => row.IsVoid == false, "t");
        }

        if (filters.AccountId is Guid accountId)
        {
            query.Where<FinancialTransaction>(row => row.AccountId == accountId, "t");
        }

        if (!string.IsNullOrWhiteSpace(filters.Type))
        {
            var type = filters.Type;
            query.Where<FinancialTransaction>(row => row.Type == type, "t");
        }

        if (!string.IsNullOrWhiteSpace(filters.Classification))
        {
            var classification = filters.Classification;
            query.Where<FinancialTransaction>(row => row.Classification == classification, "t");
        }

        if (!string.IsNullOrWhiteSpace(filters.Category))
        {
            var category = filters.Category;
            query.Where<FinancialTransaction>(row => row.Category == category, "t");
        }

        if (!string.IsNullOrWhiteSpace(filters.Status))
        {
            var status = filters.Status;
            query.Where<FinancialTransaction>(row => row.Status == status, "t");
        }

        if (filters.From is DateOnly from)
        {
            query.Where<FinancialTransaction>(row => row.Date >= from, "t");
        }

        if (filters.To is DateOnly to)
        {
            query.Where<FinancialTransaction>(row => row.Date <= to, "t");
        }

        if (filters.AmountMin is decimal amountMin)
        {
            query.Where<FinancialTransaction>(row => row.Amount >= amountMin, "t");
        }

        if (filters.AmountMax is decimal amountMax)
        {
            query.Where<FinancialTransaction>(row => row.Amount <= amountMax, "t");
        }

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim();
            query.Where<FinancialTransaction>(row => row.Description.Contains(search) || row.Merchant!.Contains(search), "t");
        }

        return await query.ToListAsync();
    }

    private async Task<TransactionDetailDto> ToDetailDtoAsync(
        FinancialTransaction entity,
        MultiTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var detail = entity.MapTo<FinancialTransaction, TransactionDetailDto>();
        detail.Tags = ReadTags(entity.Tags);
        detail.HasTransferLinkSuggestion = await _transferLinks.HasOpenSuggestionAsync(entity.Id, cancellationToken);
        var query = transaction is null
            ? _db.QuerySelect<TransactionSplit>()
            : transaction.QuerySelect<TransactionSplit>();
        var splits = await query
            .From<TransactionSplit>()
            .SelectAllFrom<TransactionSplit>()
            .Where(split => split.TransactionId == entity.Id)
            .ToListAsync();
        detail.Splits = splits
            .OrderBy(split => split.CreatedAt)
            .ThenBy(split => split.Id)
            .MapToList<TransactionSplit, TransactionSplitDto>();
        return detail;
    }

    private static IReadOnlyList<string> ReadTags(string tags)
    {
        return RulesetJson.Read<IReadOnlyList<string>>(tags, []);
    }
}
