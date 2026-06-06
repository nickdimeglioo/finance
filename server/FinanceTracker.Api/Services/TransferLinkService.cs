using FinanceTracker.Api.Features.Accounts;
using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;
using PipelineRunner.Utils;

namespace FinanceTracker.Api.Services;

public sealed class TransferLinkService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public TransferLinkService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<CreditCardPaymentDrilldownDto?> GetCreditCardPaymentDrilldownAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var selected = await GetOwnedTransactionAsync(transactionId, null, cancellationToken);
        if (selected is null)
        {
            return null;
        }

        var selectedAccount = await GetOwnedAccountAsync(selected.AccountId, null, cancellationToken);
        if (selectedAccount is null)
        {
            return null;
        }

        var payment = selected;
        Guid? sourceTransactionId = null;
        var cardAccount = selectedAccount;
        if (!IsCreditCard(cardAccount))
        {
            cardAccount = await ResolveLinkedReviewAccountAsync(selected, cancellationToken)
                ?? throw new ArgumentException("Transaction is not linked to another account.");
            if (!IsCreditCard(cardAccount))
            {
                throw new ArgumentException("Transaction is not linked to a credit-card account.");
            }

            sourceTransactionId = selected.Id;
            payment = await FindReviewPaymentAsync(selected, cardAccount, cancellationToken) ?? selected;
        }
        else if (selected.TransferPartnerId is Guid partnerId)
        {
            sourceTransactionId = partnerId;
        }

        var usesSelectedAsSyntheticPayment = payment.Id == selected.Id && selected.AccountId != cardAccount.Id;
        if (!usesSelectedAsSyntheticPayment && !IsDebtReduction(payment))
        {
            throw new ArgumentException("Credit-card drilldown requires a payment or refund transaction.");
        }

        var reportDate = payment.Date;
        var transactions = await _db.QuerySelect<FinancialTransaction>()
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(row => row.UserId == _currentUser.UserId && row.AccountId == cardAccount.Id && row.IsVoid == false && row.Date <= reportDate)
            .ToListAsync();

        var ordered = transactions
            .Where(row => row.Status is "posted" or "reconciled")
            .OrderBy(row => row.Date)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToList();

        var lots = new List<CoverageLot>();
        decimal balanceBefore = 0;
        decimal balanceAfter = 0;
        decimal remainingPayment = payment.Amount;
        var coveredRows = new List<CreditCardPaymentCoverageRowDto>();
        var capturedSelectedPayment = usesSelectedAsSyntheticPayment;

        if (usesSelectedAsSyntheticPayment)
        {
            foreach (var row in ordered)
            {
                if (IsDebtIncrease(row))
                {
                    lots.Add(new CoverageLot(row, row.Amount));
                    continue;
                }

                if (IsDebtReduction(row))
                {
                    ApplyReduction(lots, row.Amount);
                }
            }

            balanceBefore = lots.Sum(lot => lot.Remaining);
            remainingPayment = ApplyPaymentReduction(lots, payment.Amount, coveredRows);
            balanceAfter = lots.Sum(lot => lot.Remaining);
        }
        else
        {
            foreach (var row in ordered)
            {
                if (IsDebtIncrease(row))
                {
                    lots.Add(new CoverageLot(row, row.Amount));
                    continue;
                }

                if (IsDebtReduction(row))
                {
                    if (row.Id == payment.Id)
                    {
                        capturedSelectedPayment = true;
                        balanceBefore = lots.Sum(lot => lot.Remaining);
                        remainingPayment = ApplyPaymentReduction(lots, row.Amount, coveredRows);
                        balanceAfter = lots.Sum(lot => lot.Remaining);
                        continue;
                    }

                    ApplyReduction(lots, row.Amount);
                }
            }
        }

        if (!capturedSelectedPayment)
        {
            throw new ArgumentException("Credit-card payment was not found in the posted ledger.");
        }

        var currentUnpaid = lots.Sum(lot => lot.Remaining);
        var appliedAmount = payment.Amount - remainingPayment;
        var unpaidRows = lots
            .Where(lot => lot.Remaining > 0)
            .OrderBy(lot => lot.Transaction.Date)
            .ThenBy(lot => lot.Transaction.CreatedAt)
            .ThenBy(lot => lot.Transaction.Id)
            .Select(lot => new CreditCardUnpaidChargeDto(
                lot.Transaction.Id,
                lot.Transaction.Date,
                lot.Transaction.Description,
                lot.Transaction.Merchant,
                lot.Transaction.Category,
                lot.Transaction.Amount,
                lot.Transaction.Amount - lot.Remaining,
                lot.Remaining,
                Percent(lot.Transaction.Amount - lot.Remaining, lot.Transaction.Amount),
                lot.Transaction.Currency))
            .ToList();

        return new CreditCardPaymentDrilldownDto(
            payment.Id,
            sourceTransactionId,
            cardAccount.Id,
            cardAccount.Nickname,
            payment.Date,
            payment.Amount,
            balanceBefore,
            balanceAfter,
            appliedAmount,
            remainingPayment,
            Percent(appliedAmount, payment.Amount),
            Percent(appliedAmount, balanceBefore),
            currentUnpaid,
            coveredRows,
            unpaidRows);
    }

    internal async Task<ImportTransferLinkPreview> PreviewImportLinkAsync(
        Guid sourceAccountId,
        DateOnly? date,
        decimal? amount,
        Guid? targetAccountId,
        string? targetAccountName,
        string? linkMode,
        int matchWindowDays,
        MultiTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (targetAccountId is null && string.IsNullOrWhiteSpace(targetAccountName))
        {
            return ImportTransferLinkPreview.None;
        }

        var targetAccount = await ResolveTargetAccountAsync(targetAccountId, targetAccountName, transaction, cancellationToken);
        if (targetAccount is null)
        {
            return ImportTransferLinkPreview.Unresolved(targetAccountId, targetAccountName);
        }

        if (date is null || amount is null || amount <= 0)
        {
            return ImportTransferLinkPreview.LinkedAccount(targetAccount, "Target account will be linked when this row is imported.");
        }

        return ImportTransferLinkPreview.LinkedAccount(targetAccount, "Target account will be linked for payment review.");
    }

    internal async Task ApplyImportLinkAsync(
        FinancialTransaction entity,
        Guid? targetAccountId,
        string? targetAccountName,
        string? linkMode,
        int matchWindowDays,
        Account sourceAccount,
        MultiTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (targetAccountId is null && string.IsNullOrWhiteSpace(targetAccountName))
        {
            return;
        }

        var targetAccount = await ResolveTargetAccountAsync(targetAccountId, targetAccountName, transaction, cancellationToken);
        if (targetAccount is null)
        {
            await SaveSuggestionAsync(entity, targetAccountId ?? Guid.Empty, "unresolved", "Target account was not found.", transaction);
            return;
        }

        entity.Direction = DirectionForLinkedSide(sourceAccount, targetAccount);
        await transaction.Save(entity);
        await SaveSuggestionAsync(entity, targetAccount.Id, "suggested", $"Linked to {targetAccount.Nickname}; review payment allocation from the transaction screen.", transaction);
    }

    internal static string DirectionForLinkedSide(Account sourceAccount, Account targetAccount)
    {
        if (IsCreditCard(sourceAccount) && !IsCreditCard(targetAccount))
        {
            return "inflow";
        }

        return "outflow";
    }

    private async Task SaveSuggestionAsync(
        FinancialTransaction entity,
        Guid targetAccountId,
        string status,
        string message,
        MultiTransaction transaction)
    {
        var now = DateTimeOffset.UtcNow;
        await transaction.Save(new TransactionTransferLinkSuggestion
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            TransactionId = entity.Id,
            TargetAccountId = targetAccountId == Guid.Empty ? null : targetAccountId,
            CandidateTransactionId = null,
            LinkMode = "suggest",
            MatchWindowDays = 0,
            CandidateCount = 0,
            Status = status,
            Message = message,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    internal async Task<bool> HasOpenSuggestionAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var suggestions = await _db.QuerySelect<TransactionTransferLinkSuggestion>()
            .From<TransactionTransferLinkSuggestion>()
            .SelectAllFrom<TransactionTransferLinkSuggestion>()
            .Where(row => row.UserId == _currentUser.UserId
                && (row.TransactionId == transactionId || row.CandidateTransactionId == transactionId)
                && row.Status != "dismissed"
                && row.Status != "linked")
            .ToListAsync();
        return suggestions.Count > 0;
    }

    private async Task<Account?> ResolveTargetAccountAsync(Guid? targetAccountId, string? targetAccountName, MultiTransaction? transaction, CancellationToken cancellationToken)
    {
        if (targetAccountId is Guid id)
        {
            return await GetOwnedAccountAsync(id, transaction, cancellationToken);
        }

        var normalized = targetAccountName?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var accounts = await LoadAccountsAsync(transaction, cancellationToken);
        return accounts.FirstOrDefault(account => string.Equals(account.Nickname, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Account?> ResolveLinkedReviewAccountAsync(FinancialTransaction selected, CancellationToken cancellationToken)
    {
        if (selected.TransferPartnerId is Guid partnerId)
        {
            var partner = await GetOwnedTransactionAsync(partnerId, null, cancellationToken);
            if (partner is not null)
            {
                return await GetOwnedAccountAsync(partner.AccountId, null, cancellationToken);
            }
        }

        var suggestions = await _db.QuerySelect<TransactionTransferLinkSuggestion>()
            .From<TransactionTransferLinkSuggestion>()
            .SelectAllFrom<TransactionTransferLinkSuggestion>()
            .Where(row => row.UserId == _currentUser.UserId
                && row.TransactionId == selected.Id
                && row.TargetAccountId != null
                && row.Status != "dismissed")
            .ToListAsync();

        var linkedAccountId = suggestions
            .OrderByDescending(row => row.UpdatedAt)
            .ThenByDescending(row => row.CreatedAt)
            .Select(row => row.TargetAccountId)
            .FirstOrDefault();

        return linkedAccountId is Guid accountId
            ? await GetOwnedAccountAsync(accountId, null, cancellationToken)
            : null;
    }

    private async Task<FinancialTransaction?> FindReviewPaymentAsync(FinancialTransaction sourcePayment, Account cardAccount, CancellationToken cancellationToken)
    {
        var paymentRows = await _db.QuerySelect<FinancialTransaction>()
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(row => row.UserId == _currentUser.UserId
                && row.AccountId == cardAccount.Id
                && row.IsVoid == false
                && row.Amount == sourcePayment.Amount
                && row.Date <= sourcePayment.Date)
            .ToListAsync();

        return paymentRows
            .Where(row => row.Status is "posted" or "reconciled"
                && IsDebtReduction(row)
                && string.Equals(row.Currency, sourcePayment.Currency, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => Math.Abs(row.Date.DayNumber - sourcePayment.Date.DayNumber))
            .ThenByDescending(row => row.Date)
            .ThenByDescending(row => row.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<List<Account>> LoadAccountsAsync(MultiTransaction? transaction, CancellationToken cancellationToken)
    {
        var query = transaction is null ? _db.QuerySelect<Account>() : transaction.QuerySelect<Account>();
        return await query
            .From<Account>()
            .SelectAllFrom<Account>()
            .Where(account => account.UserId == _currentUser.UserId)
            .ToListAsync();
    }

    private async Task<Account?> GetOwnedAccountAsync(Guid accountId, MultiTransaction? transaction, CancellationToken cancellationToken)
    {
        var account = transaction is null
            ? await _db.GetByIdAsync<Account>(accountId, depth: 0)
            : await transaction.GetByIdAsync<Account>(accountId);
        return account?.UserId == _currentUser.UserId ? account : null;
    }

    private async Task<FinancialTransaction?> GetOwnedTransactionAsync(Guid transactionId, MultiTransaction? transaction, CancellationToken cancellationToken)
    {
        var entity = transaction is null
            ? await _db.GetByIdAsync<FinancialTransaction>(transactionId, depth: 0)
            : await transaction.GetByIdAsync<FinancialTransaction>(transactionId);
        return entity?.UserId == _currentUser.UserId ? entity : null;
    }

    private static void ApplyReduction(List<CoverageLot> lots, decimal amount)
    {
        var remaining = amount;
        foreach (var lot in lots.Where(lot => lot.Remaining > 0))
        {
            if (remaining <= 0)
            {
                break;
            }

            var applied = Math.Min(lot.Remaining, remaining);
            lot.Remaining -= applied;
            remaining -= applied;
        }
    }

    private static decimal ApplyPaymentReduction(
        List<CoverageLot> lots,
        decimal amount,
        List<CreditCardPaymentCoverageRowDto> coveredRows)
    {
        var remainingPayment = amount;
        foreach (var lot in lots.Where(lot => lot.Remaining > 0))
        {
            if (remainingPayment <= 0)
            {
                break;
            }

            var outstandingBeforePayment = lot.Remaining;
            var covered = Math.Min(outstandingBeforePayment, remainingPayment);
            lot.Remaining -= covered;
            remainingPayment -= covered;
            coveredRows.Add(new CreditCardPaymentCoverageRowDto(
                lot.Transaction.Id,
                lot.Transaction.Date,
                lot.Transaction.Description,
                lot.Transaction.Merchant,
                lot.Transaction.Category,
                lot.Transaction.Amount,
                outstandingBeforePayment,
                covered,
                lot.Remaining,
                Percent(covered, outstandingBeforePayment),
                lot.Transaction.Currency));
        }

        return remainingPayment;
    }

    private static decimal Percent(decimal numerator, decimal denominator)
    {
        return denominator <= 0 ? 0 : Math.Round(numerator / denominator * 100m, 2);
    }

    private static bool IsCreditCard(Account account) => account.Type == "credit_card";

    private static bool IsDebtIncrease(FinancialTransaction transaction)
    {
        return transaction.Direction == "outflow" || transaction.Direction == "neutral" && transaction.Type == "expense";
    }

    private static bool IsDebtReduction(FinancialTransaction transaction)
    {
        return transaction.Direction == "inflow"
            || transaction.Direction == "neutral" && transaction.Type is "income" or "transfer" or "opening_balance";
    }

    private sealed class CoverageLot
    {
        public CoverageLot(FinancialTransaction transaction, decimal remaining)
        {
            Transaction = transaction;
            Remaining = remaining;
        }

        public FinancialTransaction Transaction { get; }
        public decimal Remaining { get; set; }
    }
}

public sealed record ImportTransferLinkPreview(
    Guid? TargetAccountId,
    string? TargetAccountName,
    string? LinkMode,
    int MatchWindowDays,
    int CandidateCount,
    string Status,
    string? Message)
{
    public static ImportTransferLinkPreview None { get; } = new(null, null, null, 0, 0, "none", null);

    public static ImportTransferLinkPreview Unresolved(Guid? targetAccountId, string? targetAccountName)
        => new(targetAccountId, targetAccountName, null, 0, 0, "unresolved", "Target account was not found.");

    public static ImportTransferLinkPreview LinkedAccount(Account targetAccount, string message)
        => new(targetAccount.Id, targetAccount.Nickname, null, 0, 0, "account-linked", message);
}
