using FinanceTracker.Api.Features.Accounts;
using FinanceTracker.Api.Features.Organization;
using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class RecurringRuleService
{
    private static readonly string[] Frequencies = ["daily", "weekly", "biweekly", "monthly", "quarterly", "yearly"];
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public RecurringRuleService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<IReadOnlyList<RecurringRuleDto>> ListAsync(CancellationToken cancellationToken)
        => (await LoadOwnedAsync()).OrderBy(rule => rule.NextExpected).ThenBy(rule => rule.Name).Select(ToDto).ToList();

    public async Task<SubscriptionStatusDto> StatusAsync(CancellationToken cancellationToken)
    {
        var rules = (await LoadOwnedAsync()).Where(rule => rule.IsActive).Select(ToDto).OrderBy(rule => rule.NextExpected).ToList();
        return new SubscriptionStatusDto(
            rules.Where(rule => rule.Type == "expense").Sum(rule => rule.MonthlyNormalizedCost),
            rules.Where(rule => rule.Type == "expense" && rule.Classification == "business").Sum(rule => rule.MonthlyNormalizedCost),
            rules.Where(rule => rule.Type == "expense" && rule.Classification == "personal").Sum(rule => rule.MonthlyNormalizedCost),
            rules);
    }

    public async Task<RecurringRuleDto> CreateAsync(UpsertRecurringRuleRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request);
        var now = DateTimeOffset.UtcNow;
        var entity = new RecurringRule { UserId = _currentUser.UserId, CreatedAt = now, UpdatedAt = now };
        Apply(entity, request);
        await _db.SaveAsync(entity, auditUserId: _currentUser.UserId.ToString());
        return ToDto(entity);
    }

    public async Task<RecurringRuleDto?> UpdateAsync(Guid id, UpsertRecurringRuleRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request);
        var entity = await GetOwnedAsync(id);
        if (entity is null) return null;
        Apply(entity, request);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(entity, auditUserId: _currentUser.UserId.ToString());
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await GetOwnedAsync(id);
        return entity is not null && await _db.DeleteAsync(entity, userId: _currentUser.UserId.ToString());
    }

    public async Task<int> MatchTransactionsAsync(CancellationToken cancellationToken)
    {
        var rules = (await LoadOwnedAsync()).Where(rule => rule.IsActive).ToList();
        var transactions = await _db.QuerySelect<FinancialTransaction>().From<FinancialTransaction>().SelectAllFrom<FinancialTransaction>()
            .Where(transaction => transaction.UserId == _currentUser.UserId && transaction.IsVoid == false).ToListAsync();
        var matched = 0;
        foreach (var transaction in transactions.Where(transaction => transaction.RecurringRuleId is null).OrderBy(transaction => transaction.Date))
        {
            var rule = rules.FirstOrDefault(candidate => Matches(candidate, transaction));
            if (rule is null) continue;
            transaction.RecurringRuleId = rule.Id;
            transaction.UpdatedAt = DateTimeOffset.UtcNow;
            rule.LastMatchedDate = transaction.Date;
            rule.NextExpected = Advance(transaction.Date, rule.Frequency);
            rule.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveAsync(transaction, auditUserId: _currentUser.UserId.ToString());
            await _db.SaveAsync(rule, auditUserId: _currentUser.UserId.ToString());
            matched++;
        }
        return matched;
    }

    public async Task<IReadOnlyList<RecurringRuleSuggestionDto>> SuggestAsync(CancellationToken cancellationToken)
    {
        var existing = await LoadOwnedAsync();
        var transactions = await _db.QuerySelect<FinancialTransaction>().From<FinancialTransaction>().SelectAllFrom<FinancialTransaction>()
            .Where(transaction => transaction.UserId == _currentUser.UserId && transaction.IsVoid == false).ToListAsync();
        return transactions
            .Where(transaction => (transaction.Type is "expense" or "income") && transaction.Amount > 0)
            .GroupBy(transaction => new
            {
                transaction.AccountId,
                transaction.Type,
                transaction.Classification,
                transaction.Currency,
                transaction.Category,
                Keyword = NormalizeKeyword(transaction.Merchant, transaction.Description),
                Amount = decimal.Round(transaction.Amount, 2)
            })
            .Where(group => group.Key.Keyword.Length > 0 && group.Count() >= 3)
            .Select(group => BuildSuggestion(group.Key.AccountId, group.Key.Type, group.Key.Classification, group.Key.Amount, group.Key.Currency, group.Key.Category, group.Key.Keyword, group.OrderBy(item => item.Date).ToList()))
            .Where(suggestion => suggestion is not null)
            .Select(suggestion => suggestion!)
            .Where(suggestion => !existing.Any(rule => rule.IsActive
                && rule.AccountId == suggestion.AccountId
                && rule.Type == suggestion.Type
                && string.Equals(rule.MerchantKeyword, suggestion.MerchantKeyword, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(suggestion => suggestion.NextExpected)
            .ToList();
    }

    internal static DateOnly Advance(DateOnly date, string frequency) => frequency switch
    {
        "daily" => date.AddDays(1), "weekly" => date.AddDays(7), "biweekly" => date.AddDays(14),
        "quarterly" => date.AddMonths(3), "yearly" => date.AddYears(1), _ => date.AddMonths(1)
    };

    private async Task ValidateAsync(UpsertRecurringRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Recurring rule name is required.");
        if (!FinanceValues.TransactionTypes.Contains(request.Type) || request.Type == "opening_balance") throw new ArgumentException("Invalid recurring transaction type.");
        if (!FinanceValues.Classifications.Contains(request.Classification)) throw new ArgumentException("Invalid classification.");
        if (!Frequencies.Contains(request.Frequency)) throw new ArgumentException("Invalid recurring frequency.");
        if (request.Amount <= 0 || request.AmountTolerance is < 0 or > 1) throw new ArgumentException("Amount must be positive and tolerance must be between 0 and 1.");
        if (request.AccountId is Guid accountId)
        {
            var account = await _db.GetByIdAsync<Account>(accountId, depth: 0);
            if (account?.UserId != _currentUser.UserId) throw new ArgumentException("Account was not found.");
        }
    }

    private static void Apply(RecurringRule entity, UpsertRecurringRuleRequest request)
    {
        entity.Name = request.Name.Trim(); entity.AccountId = request.AccountId; entity.Type = request.Type; entity.Classification = request.Classification;
        entity.Amount = request.Amount; entity.Currency = request.Currency.Trim().ToUpperInvariant(); entity.Category = EmptyToNull(request.Category);
        entity.MerchantKeyword = EmptyToNull(request.MerchantKeyword); entity.Frequency = request.Frequency; entity.NextExpected = request.NextExpected;
        entity.AmountTolerance = request.AmountTolerance; entity.Tags = RulesetJson.Write(request.Tags ?? []); entity.IsActive = request.IsActive;
    }

    private static bool Matches(RecurringRule rule, FinancialTransaction transaction)
    {
        if (rule.AccountId.HasValue && rule.AccountId != transaction.AccountId || rule.Type != transaction.Type) return false;
        var text = $"{transaction.Merchant} {transaction.Description}";
        if (!string.IsNullOrWhiteSpace(rule.MerchantKeyword) && !text.Contains(rule.MerchantKeyword, StringComparison.OrdinalIgnoreCase)) return false;
        var tolerance = rule.Amount * rule.AmountTolerance;
        if (Math.Abs(transaction.Amount - rule.Amount) > tolerance) return false;
        var expected = rule.LastMatchedDate.HasValue ? Advance(rule.LastMatchedDate.Value, rule.Frequency) : rule.NextExpected;
        return Math.Abs(transaction.Date.DayNumber - expected.DayNumber) <= FrequencyToleranceDays(rule.Frequency);
    }

    private static RecurringRuleSuggestionDto? BuildSuggestion(
        Guid accountId, string type, string classification, decimal amount, string currency, string? category,
        string keyword, IReadOnlyList<FinancialTransaction> transactions)
    {
        var recent = transactions.TakeLast(6).ToList();
        var intervals = recent.Zip(recent.Skip(1), (left, right) => right.Date.DayNumber - left.Date.DayNumber).ToList();
        if (intervals.Count < 2) return null;
        var average = intervals.Average();
        var frequency = average switch
        {
            >= 0 and <= 2 => "daily",
            >= 5 and <= 9 => "weekly",
            >= 11 and <= 18 => "biweekly",
            >= 24 and <= 38 => "monthly",
            >= 70 and <= 110 => "quarterly",
            >= 320 and <= 410 => "yearly",
            _ => null
        };
        if (frequency is null || intervals.Any(days => Math.Abs(days - average) > FrequencyToleranceDays(frequency))) return null;
        var next = Advance(recent[^1].Date, frequency);
        return new RecurringRuleSuggestionDto(accountId, keyword, type, classification, amount, currency, category, keyword, frequency, next, transactions.Count);
    }

    private static string NormalizeKeyword(string? merchant, string description)
        => (string.IsNullOrWhiteSpace(merchant) ? description : merchant).Trim().ToUpperInvariant();

    private static int FrequencyToleranceDays(string frequency) => frequency switch { "daily" => 1, "weekly" => 3, "biweekly" => 5, "quarterly" => 20, "yearly" => 45, _ => 10 };
    private async Task<List<RecurringRule>> LoadOwnedAsync() => await _db.QuerySelect<RecurringRule>().From<RecurringRule>().SelectAllFrom<RecurringRule>().Where(rule => rule.UserId == _currentUser.UserId).ToListAsync();
    private async Task<RecurringRule?> GetOwnedAsync(Guid id) { var item = await _db.GetByIdAsync<RecurringRule>(id, depth: 0); return item?.UserId == _currentUser.UserId ? item : null; }
    private static RecurringRuleDto ToDto(RecurringRule rule) => new(rule.Id, rule.Name, rule.AccountId, rule.Type, rule.Classification, rule.Amount, rule.Currency, rule.Category, rule.MerchantKeyword, rule.Frequency, rule.NextExpected, rule.LastMatchedDate, rule.AmountTolerance, RulesetJson.Read<IReadOnlyList<string>>(rule.Tags, []), rule.IsActive, Status(rule), Monthly(rule), rule.CreatedAt, rule.UpdatedAt);
    private static string Status(RecurringRule rule) { var today = DateOnly.FromDateTime(DateTime.UtcNow); return rule.NextExpected < today ? "overdue" : rule.NextExpected <= today.AddDays(31) ? "upcoming" : "on_track"; }
    private static decimal Monthly(RecurringRule rule) => rule.Frequency switch { "daily" => rule.Amount * 30m, "weekly" => rule.Amount * 4.33m, "biweekly" => rule.Amount * 2.165m, "quarterly" => rule.Amount / 3m, "yearly" => rule.Amount / 12m, _ => rule.Amount };
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
