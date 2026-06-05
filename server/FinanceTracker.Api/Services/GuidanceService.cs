using FinanceTracker.Api.Features.Accounts;
using FinanceTracker.Api.Features.Guidance;
using FinanceTracker.Api.Features.Organization;
using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class GuidanceService
{
    private const decimal SubscriptionLoadThreshold = 8m;
    private const decimal DebtServiceThreshold = 36m;

    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;
    private readonly ProfileService _profiles;

    public GuidanceService(ICurrentUserContext currentUser, IOrmMapperService db, ProfileService profiles)
    {
        _currentUser = currentUser;
        _db = db;
        _profiles = profiles;
    }

    public async Task<GuidanceSummaryDto> GetAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddMonths(-2).AddDays(1);
        var transactions = await _db.QuerySelect<FinancialTransaction>()
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(transaction => transaction.UserId == _currentUser.UserId && transaction.IsVoid == false && transaction.Date >= from && transaction.Date <= today)
            .ToListAsync();
        var accounts = await _db.QuerySelect<Account>()
            .From<Account>()
            .SelectAllFrom<Account>()
            .Where(account => account.UserId == _currentUser.UserId)
            .ToListAsync();
        var recurringRules = await _db.QuerySelect<RecurringRule>()
            .From<RecurringRule>()
            .SelectAllFrom<RecurringRule>()
            .Where(rule => rule.UserId == _currentUser.UserId && rule.IsActive == true)
            .ToListAsync();
        var profile = await _profiles.GetOrCreateAsync(cancellationToken);
        var categoryMappings = RulesetJson.Read<IReadOnlyDictionary<string, string>>(profile.CategoryMappings, new Dictionary<string, string>());

        var income = transactions.Where(transaction => transaction.Type == "income").Sum(transaction => transaction.Amount);
        var expenses = transactions.Where(transaction => transaction.Type == "expense").Sum(transaction => transaction.Amount);
        var monthlyIncome = income > 0 ? income / 3m : (profile.AnnualIncome ?? 0m) / 12m;
        var monthlyExpenses = expenses / 3m;
        var liquidBalance = await CalculateLiquidBalanceAsync(accounts, cancellationToken);
        var subscriptionLoad = recurringRules.Where(rule => rule.Type == "expense").Sum(MonthlyRecurringAmount);
        var debtPayments = recurringRules.Where(rule => rule.Type == "expense" && IsDebtRule(rule, accounts)).Sum(MonthlyRecurringAmount);
        var businessExpenses = transactions.Where(transaction => transaction.Type == "expense" && transaction.Classification == "business").Sum(transaction => transaction.Amount);
        var needWantSavings = CalculateNeedsWantsSavings(transactions, categoryMappings);

        var guidance = new List<GuidanceItemDto>
        {
            BuildSavingsRateGuidance(income, expenses),
            BuildEmergencyFundGuidance(liquidBalance, monthlyExpenses),
            BuildBreakdownGuidance(needWantSavings, monthlyIncome),
            BuildSubscriptionGuidance(subscriptionLoad, monthlyIncome),
            BuildBusinessExpenseGuidance(businessExpenses, expenses),
            BuildDebtServiceGuidance(debtPayments, monthlyIncome)
        };

        return new GuidanceSummaryDto(from, today, guidance);
    }

    private async Task<decimal> CalculateLiquidBalanceAsync(IReadOnlyList<Account> accounts, CancellationToken cancellationToken)
    {
        var liquidAccountIds = accounts
            .Where(account => account.Type is "checking" or "savings" or "cash")
            .Select(account => account.Id)
            .ToHashSet();
        if (liquidAccountIds.Count == 0)
        {
            return 0m;
        }

        var transactions = await _db.QuerySelect<FinancialTransaction>()
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(transaction => transaction.UserId == _currentUser.UserId && transaction.IsVoid == false)
            .ToListAsync();
        return transactions
            .Where(transaction => liquidAccountIds.Contains(transaction.AccountId) && AccountService.CountsTowardBalance(transaction))
            .Sum(AccountService.SignedBalanceAmount);
    }

    private static GuidanceItemDto BuildSavingsRateGuidance(decimal income, decimal expenses)
    {
        if (income <= 0)
        {
            return NoData("savings_rate", "Savings Rate", "Add income transactions or profile income to calculate savings rate.");
        }

        var rate = decimal.Round((income - expenses) / income * 100m, 2);
        var status = rate >= 20m ? "on_track" : rate >= 10m ? "warning" : "below_target";
        return new GuidanceItemDto(
            "savings_rate",
            "Savings Rate",
            status,
            $"Savings rate is {rate:0.##}% over the current 3-month window. Target is 20%.",
            new Dictionary<string, decimal> { ["income"] = income, ["expenses"] = expenses, ["savingsRatePercent"] = rate, ["targetPercent"] = 20m });
    }

    private static GuidanceItemDto BuildEmergencyFundGuidance(decimal liquidBalance, decimal monthlyExpenses)
    {
        if (monthlyExpenses <= 0)
        {
            return NoData("emergency_fund", "Emergency Fund", "Add expense history to calculate the 3-month emergency fund target.");
        }

        var target = monthlyExpenses * 3m;
        var coverage = target == 0 ? 0 : decimal.Round(liquidBalance / target * 100m, 2);
        var status = liquidBalance >= target ? "on_track" : liquidBalance >= target / 2m ? "warning" : "below_target";
        return new GuidanceItemDto(
            "emergency_fund",
            "Emergency Fund",
            status,
            $"Liquid balances cover {coverage:0.##}% of a 3-month expense target.",
            new Dictionary<string, decimal> { ["liquidBalance"] = liquidBalance, ["monthlyExpenses"] = monthlyExpenses, ["targetBalance"] = target, ["coveragePercent"] = coverage });
    }

    private static GuidanceItemDto BuildBreakdownGuidance((decimal Needs, decimal Wants, decimal Savings) amounts, decimal monthlyIncome)
    {
        if (monthlyIncome <= 0)
        {
            return NoData("fifty_thirty_twenty", "50/30/20 Breakdown", "Add monthly income or profile income to compare needs, wants, and savings.");
        }

        var needsPercent = decimal.Round(amounts.Needs / monthlyIncome * 100m, 2);
        var wantsPercent = decimal.Round(amounts.Wants / monthlyIncome * 100m, 2);
        var savingsPercent = decimal.Round(amounts.Savings / monthlyIncome * 100m, 2);
        var status = needsPercent <= 50m && wantsPercent <= 30m && savingsPercent >= 20m ? "on_track" : "warning";
        return new GuidanceItemDto(
            "fifty_thirty_twenty",
            "50/30/20 Breakdown",
            status,
            $"Needs {needsPercent:0.##}%, wants {wantsPercent:0.##}%, savings {savingsPercent:0.##}% against monthly income.",
            new Dictionary<string, decimal> { ["needsPercent"] = needsPercent, ["wantsPercent"] = wantsPercent, ["savingsPercent"] = savingsPercent, ["needsTarget"] = 50m, ["wantsTarget"] = 30m, ["savingsTarget"] = 20m });
    }

    private static GuidanceItemDto BuildSubscriptionGuidance(decimal subscriptionLoad, decimal monthlyIncome)
    {
        if (monthlyIncome <= 0)
        {
            return NoData("subscription_load", "Subscription Load", "Add income to compare recurring subscriptions to monthly income.");
        }

        var percent = decimal.Round(subscriptionLoad / monthlyIncome * 100m, 2);
        var status = percent <= SubscriptionLoadThreshold ? "on_track" : percent <= SubscriptionLoadThreshold * 1.5m ? "warning" : "below_target";
        return new GuidanceItemDto(
            "subscription_load",
            "Subscription Load",
            status,
            $"Recurring subscriptions are {percent:0.##}% of monthly income. Threshold is {SubscriptionLoadThreshold:0.##}%.",
            new Dictionary<string, decimal> { ["subscriptionMonthlyTotal"] = subscriptionLoad, ["monthlyIncome"] = monthlyIncome, ["loadPercent"] = percent, ["thresholdPercent"] = SubscriptionLoadThreshold });
    }

    private static GuidanceItemDto BuildBusinessExpenseGuidance(decimal businessExpenses, decimal totalExpenses)
    {
        if (totalExpenses <= 0)
        {
            return NoData("business_expense_ratio", "Business Expense Ratio", "Add expenses to calculate the business expense ratio.");
        }

        var percent = decimal.Round(businessExpenses / totalExpenses * 100m, 2);
        return new GuidanceItemDto(
            "business_expense_ratio",
            "Business Expense Ratio",
            "on_track",
            $"Business expenses are {percent:0.##}% of total outflows.",
            new Dictionary<string, decimal> { ["businessExpenses"] = businessExpenses, ["totalExpenses"] = totalExpenses, ["businessExpensePercent"] = percent });
    }

    private static GuidanceItemDto BuildDebtServiceGuidance(decimal debtPayments, decimal monthlyIncome)
    {
        if (monthlyIncome <= 0)
        {
            return NoData("debt_service", "Debt Service", "Add income to compare debt service to monthly income.");
        }

        var percent = decimal.Round(debtPayments / monthlyIncome * 100m, 2);
        var status = percent <= DebtServiceThreshold ? "on_track" : percent <= 45m ? "warning" : "below_target";
        return new GuidanceItemDto(
            "debt_service",
            "Debt Service",
            status,
            $"Debt service is {percent:0.##}% of monthly income. Threshold is {DebtServiceThreshold:0.##}%.",
            new Dictionary<string, decimal> { ["monthlyDebtPayments"] = debtPayments, ["monthlyIncome"] = monthlyIncome, ["debtServicePercent"] = percent, ["thresholdPercent"] = DebtServiceThreshold });
    }

    private static (decimal Needs, decimal Wants, decimal Savings) CalculateNeedsWantsSavings(
        IReadOnlyList<FinancialTransaction> transactions,
        IReadOnlyDictionary<string, string> mappings)
    {
        var needs = 0m;
        var wants = 0m;
        var savings = 0m;
        foreach (var transaction in transactions.Where(transaction => transaction.Type is "expense" or "income"))
        {
            var bucket = BucketForCategory(transaction.Category, transaction.Type, mappings);
            if (bucket == "needs")
            {
                needs += transaction.Amount / 3m;
            }
            else if (bucket == "wants")
            {
                wants += transaction.Amount / 3m;
            }
            else if (bucket == "savings")
            {
                savings += transaction.Type == "income" ? transaction.Amount / 3m : -transaction.Amount / 3m;
            }
        }

        return (needs, wants, Math.Max(0m, savings));
    }

    private static string BucketForCategory(string? category, string type, IReadOnlyDictionary<string, string> mappings)
    {
        if (type == "income")
        {
            return "savings";
        }

        if (!string.IsNullOrWhiteSpace(category) && mappings.TryGetValue(category, out var mapped))
        {
            return mapped;
        }

        var normalized = (category ?? string.Empty).ToLowerInvariant();
        if (normalized.Contains("rent") || normalized.Contains("mortgage") || normalized.Contains("utilities") || normalized.Contains("grocery") || normalized.Contains("insurance") || normalized.Contains("medical") || normalized.Contains("loan"))
        {
            return "needs";
        }

        if (normalized.Contains("saving") || normalized.Contains("investment") || normalized.Contains("retirement"))
        {
            return "savings";
        }

        return "wants";
    }

    private static bool IsDebtRule(RecurringRule rule, IReadOnlyList<Account> accounts)
    {
        if (rule.AccountId is Guid accountId && accounts.FirstOrDefault(account => account.Id == accountId)?.Type is "loan" or "credit_card")
        {
            return true;
        }

        var text = $"{rule.Name} {rule.Category} {rule.MerchantKeyword}".ToLowerInvariant();
        return text.Contains("loan") || text.Contains("debt") || text.Contains("credit") || text.Contains("mortgage");
    }

    private static decimal MonthlyRecurringAmount(RecurringRule rule)
        => rule.Frequency switch
        {
            "daily" => rule.Amount * 30m,
            "weekly" => rule.Amount * 4.33m,
            "biweekly" => rule.Amount * 2.165m,
            "quarterly" => rule.Amount / 3m,
            "yearly" => rule.Amount / 12m,
            _ => rule.Amount
        };

    private static GuidanceItemDto NoData(string id, string title, string message)
        => new(id, title, "no_data", message, new Dictionary<string, decimal>());
}
