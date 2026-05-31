using FinanceTracker.Api.Features.Accounts;
using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Data.Contracts;

namespace FinanceTracker.Api.Services;

public sealed class AccountService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IFinanceDataSession _db;

    public AccountService(ICurrentUserContext currentUser, IFinanceDataSession db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public Task<IReadOnlyList<AccountListItemDto>> ListAsync(CancellationToken cancellationToken)
    {
        return _db.QueryAsync<AccountListItemDto>(
            AccountSql.SelectAccountList + " ORDER BY a.status, a.nickname",
            new { UserId = _currentUser.UserId },
            cancellationToken: cancellationToken);
    }

    public Task<AccountDetailDto?> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return _db.QuerySingleOrDefaultAsync<AccountDetailDto>(
            AccountSql.SelectAccountDetail + " WHERE a.user_id = @UserId AND a.id = @AccountId GROUP BY a.id",
            new { UserId = _currentUser.UserId, AccountId = accountId },
            cancellationToken: cancellationToken);
    }

    public Task<AccountDetailDto> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        ValidateAccount(request.Nickname, request.Type, request.Currency, request.OpeningBalance, request.CreditLimit, request.InterestRate);

        return _db.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            var now = DateTimeOffset.UtcNow;
            var account = new Account
            {
                Id = Guid.NewGuid(),
                UserId = _currentUser.UserId,
                Institution = request.Institution,
                Nickname = request.Nickname.Trim(),
                Type = request.Type,
                Currency = request.Currency.ToUpperInvariant(),
                OpeningBalance = request.OpeningBalance,
                CreditLimit = request.CreditLimit,
                InterestRate = request.InterestRate,
                Status = "active",
                IncludeInDashboard = request.IncludeInDashboard,
                Notes = request.Notes,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _db.SaveAsync(account, _currentUser.UserId.ToString(), connection, transaction, cancellationToken);

            if (request.OpeningBalance != 0)
            {
                var opening = new FinancialTransaction
                {
                    Id = Guid.NewGuid(),
                    UserId = _currentUser.UserId,
                    AccountId = account.Id,
                    Date = DateOnly.FromDateTime(now.UtcDateTime),
                    Description = "Opening balance",
                    Type = "opening_balance",
                    Classification = "ignored",
                    Amount = Math.Abs(request.OpeningBalance),
                    Currency = account.Currency,
                    Direction = request.OpeningBalance >= 0 ? "inflow" : "outflow",
                    Status = "posted",
                    Source = "system",
                    IsVoid = false,
                    IsSplit = false,
                    Metadata = "{}",
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await _db.SaveAsync(opening, _currentUser.UserId.ToString(), connection, transaction, cancellationToken);
            }

            return await GetRequiredAsync(account.Id, connection, transaction, cancellationToken);
        }, cancellationToken);
    }

    public Task<AccountDetailDto?> UpdateAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        ValidateAccount(request.Nickname, request.Type, request.Currency, 0, request.CreditLimit, request.InterestRate);
        if (!FinanceValues.AccountStatuses.Contains(request.Status))
        {
            throw new ArgumentException("Invalid account status.");
        }

        return _db.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            var account = await _db.QuerySingleOrDefaultAsync<Account>(
                """
                SELECT *
                FROM accounts
                WHERE user_id = @UserId AND id = @AccountId
                """,
                new { UserId = _currentUser.UserId, AccountId = accountId },
                connection,
                transaction,
                cancellationToken);

            if (account is null)
            {
                return null;
            }

            account.Institution = request.Institution;
            account.Nickname = request.Nickname.Trim();
            account.Type = request.Type;
            account.Currency = request.Currency.ToUpperInvariant();
            account.CreditLimit = request.CreditLimit;
            account.InterestRate = request.InterestRate;
            account.Status = request.Status;
            account.IncludeInDashboard = request.IncludeInDashboard;
            account.Notes = request.Notes;
            account.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveAsync(account, _currentUser.UserId.ToString(), connection, transaction, cancellationToken);
            return await GetRequiredAsync(accountId, connection, transaction, cancellationToken);
        }, cancellationToken);
    }

    public async Task<bool> ArchiveAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _db.QuerySingleOrDefaultAsync<Account>(
            """
            SELECT *
            FROM accounts
            WHERE user_id = @UserId AND id = @AccountId
            """,
            new { UserId = _currentUser.UserId, AccountId = accountId },
            cancellationToken: cancellationToken);

        if (account is null)
        {
            return false;
        }

        account.Status = "archived";
        account.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveAsync(account, _currentUser.UserId.ToString(), cancellationToken: cancellationToken);
        return true;
    }

    private async Task<AccountDetailDto> GetRequiredAsync(Guid accountId, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var detail = await _db.QuerySingleOrDefaultAsync<AccountDetailDto>(
            AccountSql.SelectAccountDetail + " WHERE a.user_id = @UserId AND a.id = @AccountId GROUP BY a.id",
            new { UserId = _currentUser.UserId, AccountId = accountId },
            connection,
            transaction,
            cancellationToken);

        return detail ?? throw new InvalidOperationException("Account was not found after save.");
    }

    private static void ValidateAccount(string nickname, string type, string currency, decimal openingBalance, decimal? creditLimit, decimal? interestRate)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            throw new ArgumentException("Account nickname is required.");
        }

        if (!FinanceValues.AccountTypes.Contains(type))
        {
            throw new ArgumentException("Invalid account type.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO code.");
        }

        if (openingBalance < 0)
        {
            throw new ArgumentException("Opening balance must be greater than or equal to zero.");
        }

        if (creditLimit < 0 || interestRate < 0)
        {
            throw new ArgumentException("Credit limit and interest rate cannot be negative.");
        }
    }
}

internal static class AccountSql
{
    public const string BalanceExpression = """
        COALESCE(SUM(
            CASE
                WHEN t.is_void THEN 0
                WHEN t.status NOT IN ('posted', 'reconciled') THEN 0
                WHEN t.direction = 'inflow' THEN t.amount
                WHEN t.direction = 'outflow' THEN -t.amount
                WHEN t.type IN ('income', 'opening_balance') THEN t.amount
                WHEN t.type = 'expense' THEN -t.amount
                ELSE 0
            END
        ), 0)
        """;

    public const string SelectAccountList = $"""
        SELECT
            a.id,
            a.institution,
            a.nickname,
            a.type,
            a.currency,
            {BalanceExpression} AS current_balance,
            a.status,
            a.include_in_dashboard
        FROM accounts a
        LEFT JOIN transactions t ON t.account_id = a.id AND t.user_id = a.user_id
        WHERE a.user_id = @UserId
        GROUP BY a.id
        """;

    public const string SelectAccountDetail = $"""
        SELECT
            a.id,
            a.institution,
            a.nickname,
            a.type,
            a.currency,
            a.opening_balance,
            {BalanceExpression} AS current_balance,
            a.credit_limit,
            a.interest_rate,
            a.status,
            a.include_in_dashboard,
            a.notes,
            a.created_at,
            a.updated_at
        FROM accounts a
        LEFT JOIN transactions t ON t.account_id = a.id AND t.user_id = a.user_id
        """;
}
