using FinanceTracker.Api.Features.Accounts;
using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Api.Mapping;
using PipelineRunner.Services;
using PipelineRunner.Utils;

namespace FinanceTracker.Api.Services;

public sealed class AccountService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public AccountService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<IReadOnlyList<AccountListItemDto>> ListAsync(CancellationToken cancellationToken)
    {
        var accounts = await _db.QuerySelect<Account>()
            .From<Account>()
            .SelectAllFrom<Account>()
            .Where(account => account.UserId == _currentUser.UserId)
            .ToListAsync();
        var balances = await LoadBalancesAsync(cancellationToken: cancellationToken);
        return accounts
            .OrderBy(account => account.Status)
            .ThenBy(account => account.Nickname)
            .Select(account =>
            {
                var dto = account.MapTo<Account, AccountListItemDto>();
                dto.CurrentBalance = balances.GetValueOrDefault(account.Id);
                return dto;
            })
            .ToList();
    }

    public async Task<AccountDetailDto?> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await GetOwnedAccountAsync(accountId, cancellationToken: cancellationToken);
        if (account is null)
        {
            return null;
        }

        return await ToDetailDtoAsync(account, cancellationToken: cancellationToken);
    }

    public async Task<AccountDetailDto> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        ValidateAccount(request.Nickname, request.Type, request.Currency, request.OpeningBalance, request.CreditLimit, request.InterestRate);

        await using var transaction = _db.BeginMultiTransaction();
        transaction.Open();

        try
        {
            var now = DateTimeOffset.UtcNow;
            var account = new Account
            {
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

            await transaction.Save(account);

            if (request.OpeningBalance != 0)
            {
                var opening = new FinancialTransaction
                {
                    UserId = _currentUser.UserId,
                    AccountId = account.Id,
                    Date = DateOnly.FromDateTime(now.UtcDateTime),
                    Description = "Opening balance",
                    Type = "opening_balance",
                    Classification = "exclude",
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

                await transaction.Save(opening);
            }

            var result = await GetRequiredAsync(account.Id, transaction, cancellationToken);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<AccountDetailDto?> UpdateAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        ValidateAccount(request.Nickname, request.Type, request.Currency, 0, request.CreditLimit, request.InterestRate);
        if (!FinanceValues.AccountStatuses.Contains(request.Status))
        {
            throw new ArgumentException("Invalid account status.");
        }

        await using var transaction = _db.BeginMultiTransaction();
        transaction.Open();

        try
        {
            var account = await GetOwnedAccountAsync(accountId, transaction, cancellationToken);

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

            await transaction.Save(account);
            var result = await GetRequiredAsync(accountId, transaction, cancellationToken);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> ArchiveAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await GetOwnedAccountAsync(accountId, cancellationToken: cancellationToken);

        if (account is null)
        {
            return false;
        }

        account.Status = "archived";
        account.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveAsync(account, auditUserId: _currentUser.UserId.ToString());
        return true;
    }

    private async Task<AccountDetailDto> GetRequiredAsync(Guid accountId, MultiTransaction transaction, CancellationToken cancellationToken)
    {
        var account = await transaction.QuerySelect<Account>()
            .From<Account>()
            .SelectAllFrom<Account>()
            .Where(x => x.UserId == _currentUser.UserId && x.Id == accountId)
            .FirstOrDefaultAsync();
        if (account is null)
        {
            throw new InvalidOperationException("Account was not found after save.");
        }

        return await ToDetailDtoAsync(account, transaction, cancellationToken);
    }

    private async Task<Account?> GetOwnedAccountAsync(
        Guid accountId,
        MultiTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var account = transaction is null
            ? await _db.GetByIdAsync<Account>(accountId)
            : await transaction.GetByIdAsync<Account>(accountId);
        return account?.UserId == _currentUser.UserId ? account : null;
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

    private async Task<AccountDetailDto> ToDetailDtoAsync(
        Account account,
        MultiTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var dto = account.MapTo<Account, AccountDetailDto>();
        dto.CurrentBalance = await CalculateBalanceAsync(account.Id, transaction, cancellationToken);
        return dto;
    }

    private async Task<Dictionary<Guid, decimal>> LoadBalancesAsync(
        MultiTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var query = transaction is null
            ? _db.QuerySelect<FinancialTransaction>()
            : transaction.QuerySelect<FinancialTransaction>();
        var transactions = await query
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(transactionRow => transactionRow.UserId == _currentUser.UserId)
            .ToListAsync();

        return transactions
            .Where(CountsTowardBalance)
            .GroupBy(transactionRow => transactionRow.AccountId)
            .ToDictionary(group => group.Key, group => group.Sum(SignedBalanceAmount));
    }

    private async Task<decimal> CalculateBalanceAsync(
        Guid accountId,
        MultiTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var query = transaction is null
            ? _db.QuerySelect<FinancialTransaction>()
            : transaction.QuerySelect<FinancialTransaction>();
        var transactions = await query
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(transactionRow => transactionRow.UserId == _currentUser.UserId && transactionRow.AccountId == accountId)
            .ToListAsync();

        return transactions.Where(CountsTowardBalance).Sum(SignedBalanceAmount);
    }

    internal static bool CountsTowardBalance(FinancialTransaction transaction)
    {
        return !transaction.IsVoid && transaction.Status is "posted" or "reconciled";
    }

    internal static decimal SignedBalanceAmount(FinancialTransaction transaction)
    {
        return transaction.Direction switch
        {
            "inflow" => transaction.Amount,
            "outflow" => -transaction.Amount,
            _ when transaction.Type is "income" or "opening_balance" => transaction.Amount,
            _ when transaction.Type == "expense" => -transaction.Amount,
            _ => 0
        };
    }
}
