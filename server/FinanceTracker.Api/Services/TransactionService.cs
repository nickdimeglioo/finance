using System.Text;
using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Data.Contracts;

namespace FinanceTracker.Api.Services;

public sealed class TransactionService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IFinanceDataSession _db;

    public TransactionService(ICurrentUserContext currentUser, IFinanceDataSession db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<PagedResult<TransactionListItemDto>> ListAsync(TransactionFiltersRequest filters, CancellationToken cancellationToken)
    {
        var page = Math.Max(filters.Page, 1);
        var pageSize = Math.Clamp(filters.PageSize, 1, 250);
        var where = BuildWhere(filters);
        var parameters = BuildParameters(filters, page, pageSize);

        var total = await _db.QuerySingleAsync<long>($"SELECT COUNT(*) FROM transactions t {where}", parameters, cancellationToken: cancellationToken);
        var items = await _db.QueryAsync<TransactionListItemDto>(
            TransactionSql.SelectList + where + " ORDER BY t.date DESC, t.created_at DESC OFFSET @Offset LIMIT @PageSize",
            parameters,
            cancellationToken: cancellationToken);

        return new PagedResult<TransactionListItemDto>(items, page, pageSize, total);
    }

    public async Task<TransactionDetailDto?> GetAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var detail = await _db.QuerySingleOrDefaultAsync<TransactionDetailDto>(
            TransactionSql.SelectDetail + " WHERE t.user_id = @UserId AND t.id = @TransactionId",
            new { UserId = _currentUser.UserId, TransactionId = transactionId },
            cancellationToken: cancellationToken);

        if (detail is null)
        {
            return null;
        }

        detail.Splits = await GetSplitsAsync(transactionId, cancellationToken);
        return detail;
    }

    public Task<TransactionDetailDto> CreateAsync(CreateTransactionRequest request, CancellationToken cancellationToken)
    {
        ValidateTransaction(request.Type, request.Classification, request.Amount, request.Currency, request.Splits);
        if (request.Type == "transfer")
        {
            throw new ArgumentException("Use the transfer endpoint to create transfer transactions.");
        }

        return _db.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            await EnsureAccountOwnedAsync(request.AccountId, connection, transaction, cancellationToken);
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

            await _db.SaveAsync(entity, _currentUser.UserId.ToString(), connection, transaction, cancellationToken);
            await ReplaceSplitsAsync(entity.Id, request.Splits, connection, transaction, cancellationToken);
            return await GetRequiredAsync(entity.Id, connection, transaction, cancellationToken);
        }, cancellationToken);
    }

    public Task<TransactionDetailDto?> UpdateAsync(Guid transactionId, UpdateTransactionRequest request, CancellationToken cancellationToken)
    {
        if (!FinanceValues.TransactionStatuses.Contains(request.Status))
        {
            throw new ArgumentException("Invalid transaction status.");
        }

        return _db.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            var entity = await GetEntityForUpdateAsync(transactionId, connection, transaction, cancellationToken);
            if (entity is null)
            {
                return null;
            }

            if (entity.Type == "transfer" || entity.TransferPartnerId is not null)
            {
                throw new ArgumentException("Transfers cannot be converted through the generic transaction update endpoint.");
            }

            ValidateTransaction(entity.Type, request.Classification, request.Amount, request.Currency, request.Splits);
            await EnsureAccountOwnedAsync(entity.AccountId, connection, transaction, cancellationToken);
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

            await _db.SaveAsync(entity, _currentUser.UserId.ToString(), connection, transaction, cancellationToken);
            await ReplaceSplitsAsync(entity.Id, request.Splits, connection, transaction, cancellationToken);
            return await GetRequiredAsync(entity.Id, connection, transaction, cancellationToken);
        }, cancellationToken);
    }

    public async Task<bool> VoidAsync(Guid transactionId, bool includeTransferPartner, CancellationToken cancellationToken)
    {
        return await _db.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            var entity = await GetEntityForUpdateAsync(transactionId, connection, transaction, cancellationToken);
            if (entity is null)
            {
                return false;
            }

            MarkVoided(entity);
            await _db.SaveAsync(entity, _currentUser.UserId.ToString(), connection, transaction, cancellationToken);

            if (includeTransferPartner && entity.TransferPartnerId is Guid partnerId)
            {
                var partner = await GetEntityForUpdateAsync(partnerId, connection, transaction, cancellationToken);
                if (partner is not null)
                {
                    MarkVoided(partner);
                    await _db.SaveAsync(partner, _currentUser.UserId.ToString(), connection, transaction, cancellationToken);
                }
            }

            return true;
        }, cancellationToken);
    }

    public async Task<TransactionDetailDto?> UpdateStatusAsync(Guid transactionId, string status, CancellationToken cancellationToken)
    {
        if (!FinanceValues.TransactionStatuses.Contains(status))
        {
            throw new ArgumentException("Invalid transaction status.");
        }

        var entity = await _db.QuerySingleOrDefaultAsync<FinancialTransaction>(
            """
            SELECT *
            FROM transactions
            WHERE user_id = @UserId AND id = @TransactionId
            """,
            new { UserId = _currentUser.UserId, TransactionId = transactionId },
            cancellationToken: cancellationToken);

        if (entity is null)
        {
            return null;
        }

        entity.Status = status;
        entity.IsVoid = status == "voided";
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(entity, _currentUser.UserId.ToString(), cancellationToken: cancellationToken);

        return await GetAsync(transactionId, cancellationToken);
    }

    internal async Task EnsureAccountOwnedAsync(Guid accountId, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var exists = await _db.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM accounts WHERE user_id = @UserId AND id = @AccountId",
            new { UserId = _currentUser.UserId, AccountId = accountId },
            connection,
            transaction,
            cancellationToken);

        if (exists is null)
        {
            throw new ArgumentException("Account was not found.");
        }
    }

    internal async Task<TransactionDetailDto> GetRequiredAsync(Guid transactionId, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var detail = await _db.QuerySingleOrDefaultAsync<TransactionDetailDto>(
            TransactionSql.SelectDetail + " WHERE t.user_id = @UserId AND t.id = @TransactionId",
            new { UserId = _currentUser.UserId, TransactionId = transactionId },
            connection,
            transaction,
            cancellationToken);

        if (detail is null)
        {
            throw new InvalidOperationException("Transaction was not found after save.");
        }

        var splits = await _db.QueryAsync<TransactionSplitDto>(
            TransactionSql.SelectSplits,
            new { TransactionId = transactionId },
            connection,
            transaction,
            cancellationToken);

        detail.Splits = splits;
        return detail;
    }

    private async Task<FinancialTransaction?> GetEntityForUpdateAsync(Guid transactionId, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return await _db.QuerySingleOrDefaultAsync<FinancialTransaction>(
            "SELECT * FROM transactions WHERE user_id = @UserId AND id = @TransactionId",
            new { UserId = _currentUser.UserId, TransactionId = transactionId },
            connection,
            transaction,
            cancellationToken);
    }

    private async Task<IReadOnlyList<TransactionSplitDto>> GetSplitsAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        return await _db.QueryAsync<TransactionSplitDto>(
            TransactionSql.SelectSplits,
            new { TransactionId = transactionId },
            cancellationToken: cancellationToken);
    }

    private async Task ReplaceSplitsAsync(Guid transactionId, IReadOnlyList<CreateTransactionSplitRequest>? splits, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, CancellationToken cancellationToken)
    {
        await _db.ExecuteAsync("DELETE FROM transaction_splits WHERE transaction_id = @TransactionId", new { TransactionId = transactionId }, connection, transaction, cancellationToken);

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
            await _db.SaveAsync(entity, _currentUser.UserId.ToString(), connection, transaction, cancellationToken);
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

    private object BuildParameters(TransactionFiltersRequest filters, int page, int pageSize)
    {
        return new
        {
            UserId = _currentUser.UserId,
            filters.AccountId,
            filters.Type,
            filters.Classification,
            filters.Category,
            filters.Status,
            From = filters.From,//?.ToDateTime(TimeOnly.MinValue),
            To = filters.To,//?.ToDateTime(TimeOnly.MinValue),
            filters.AmountMin,
            filters.AmountMax,
            Search = string.IsNullOrWhiteSpace(filters.Search) ? null : $"%{filters.Search.Trim()}%",
            filters.IncludeVoided,
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        };
    }

    private static string BuildWhere(TransactionFiltersRequest filters)
    {
        var builder = new StringBuilder(
            """
            WHERE t.user_id = @UserId
              AND (@IncludeVoided OR t.is_void = false)
              AND (@AccountId IS NULL OR t.account_id = @AccountId)
              AND (@Type IS NULL OR t.type = @Type)
              AND (@Classification IS NULL OR t.classification = @Classification)
              AND (@Category IS NULL OR t.category = @Category)
              AND (@Status IS NULL OR t.status = @Status)
              AND (@From::date IS NULL OR t.date >= @From::date)
              AND (@To::date IS NULL OR t.date <= @To::date)
              AND (@AmountMin IS NULL OR t.amount >= @AmountMin)
              AND (@AmountMax IS NULL OR t.amount <= @AmountMax)
              AND (@Search IS NULL OR t.description ILIKE @Search OR t.merchant ILIKE @Search) 
            """);

        return builder.ToString();
    }
}

internal static class TransactionSql
{
    public const string SelectList = """
        SELECT
            t.id,
            t.account_id,
            t.date,
            t.posted_at,
            t.description,
            t.merchant,
            t.type,
            t.classification,
            t.category,
            t.amount,
            t.currency,
            t.direction,
            t.status,
            t.source,
            t.is_void,
            t.is_split,
            t.transfer_partner_id
        FROM transactions t 
        """;

    public const string SelectDetail = """
        SELECT
            t.id,
            t.account_id,
            t.date,
            t.posted_at,
            t.description,
            t.merchant,
            t.type,
            t.classification,
            t.category,
            t.amount,
            t.currency,
            t.direction,
            t.status,
            t.source,
            t.import_hash,
            t.is_void,
            t.is_split,
            t.transfer_partner_id,
            t.recurring_rule_id,
            t.created_at,
            t.updated_at
        FROM transactions t 
        """;

    public const string SelectSplits = """
        SELECT id, category, classification, amount, notes
        FROM transaction_splits
        WHERE transaction_id = @TransactionId
        ORDER BY created_at, id 
        """;
}
