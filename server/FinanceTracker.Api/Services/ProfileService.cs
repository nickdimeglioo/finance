using FinanceTracker.Api.Features.Guidance;
using FinanceTracker.Api.Features.Shared;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class ProfileService
{
    private static readonly string[] IncomeTypes = ["salaried", "freelance", "mixed", "retired", "student", "other"];
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public ProfileService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<UserFinanceProfileDto> GetAsync(CancellationToken cancellationToken)
    {
        var profile = await GetOrCreateAsync(cancellationToken);
        return ToDto(profile);
    }

    public async Task<UserFinanceProfileDto> UpdateAsync(UpdateUserFinanceProfileRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var profile = await GetOrCreateAsync(cancellationToken);
        profile.DateOfBirth = request.DateOfBirth;
        profile.AnnualIncome = request.AnnualIncome;
        profile.IncomeType = request.IncomeType;
        profile.Dependents = request.Dependents;
        profile.FinancialGoals = RulesetJson.Write(request.FinancialGoals ?? []);
        profile.CategoryMappings = RulesetJson.Write(request.CategoryMappings ?? new Dictionary<string, string>());
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(profile, auditUserId: _currentUser.UserId.ToString());
        return ToDto(profile);
    }

    internal async Task<UserFinanceProfile> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.QuerySelect<UserFinanceProfile>()
            .From<UserFinanceProfile>()
            .SelectAllFrom<UserFinanceProfile>()
            .Where(profile => profile.UserId == _currentUser.UserId)
            .FirstOrDefaultAsync();
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var profile = new UserFinanceProfile
        {
            UserId = _currentUser.UserId,
            IncomeType = "other",
            Dependents = 0,
            FinancialGoals = "[]",
            CategoryMappings = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };
        await _db.SaveAsync(profile, auditUserId: _currentUser.UserId.ToString());
        return profile;
    }

    internal static UserFinanceProfileDto ToDto(UserFinanceProfile profile)
        => new(
            profile.Id,
            profile.DateOfBirth,
            profile.AnnualIncome,
            profile.IncomeType,
            profile.Dependents,
            RulesetJson.Read<IReadOnlyList<string>>(profile.FinancialGoals, []),
            RulesetJson.Read<IReadOnlyDictionary<string, string>>(profile.CategoryMappings, new Dictionary<string, string>()),
            profile.CreatedAt,
            profile.UpdatedAt);

    private static void Validate(UpdateUserFinanceProfileRequest request)
    {
        if (!IncomeTypes.Contains(request.IncomeType))
        {
            throw new ArgumentException("Invalid income type.");
        }

        if (request.AnnualIncome < 0 || request.Dependents < 0)
        {
            throw new ArgumentException("Annual income and dependents cannot be negative.");
        }

        var invalidMapping = request.CategoryMappings?.Values.FirstOrDefault(value => value is not ("needs" or "wants" or "savings"));
        if (invalidMapping is not null)
        {
            throw new ArgumentException("Category mappings must be needs, wants, or savings.");
        }
    }
}

