using FinanceTracker.Api.Features.Rules;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class RulesetService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public RulesetService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<IReadOnlyList<RulesetDto>> ListAsync(CancellationToken cancellationToken)
    {
        var rulesets = await _db.QuerySelect<Ruleset>()
            .From<Ruleset>()
            .SelectAllFrom<Ruleset>()
            .Where(ruleset => ruleset.UserId == _currentUser.UserId)
            .ToListAsync();

        return rulesets.OrderBy(ruleset => ruleset.Name).Select(ToDto).ToList();
    }

    public async Task<RulesetDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var ruleset = await GetOwnedAsync(id, cancellationToken);
        return ruleset is null ? null : ToDto(ruleset);
    }

    public async Task<RulesetDto> CreateAsync(UpsertRulesetRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var now = DateTimeOffset.UtcNow;
        var ruleset = new Ruleset
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            Name = request.Name.Trim(),
            Description = EmptyToNull(request.Description),
            Version = 1,
            SourceConfig = RulesetJson.Write(Normalize(request.SourceConfig)),
            Rules = RulesetJson.Write(Normalize(request.Rules)),
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _db.SaveAsync(ruleset, auditUserId: _currentUser.UserId.ToString());
        return ToDto(ruleset);
    }

    public async Task<RulesetDto?> UpdateAsync(Guid id, UpsertRulesetRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var ruleset = await GetOwnedAsync(id, cancellationToken);
        if (ruleset is null)
        {
            return null;
        }

        ruleset.Name = request.Name.Trim();
        ruleset.Description = EmptyToNull(request.Description);
        ruleset.Version += 1;
        ruleset.SourceConfig = RulesetJson.Write(Normalize(request.SourceConfig));
        ruleset.Rules = RulesetJson.Write(Normalize(request.Rules));
        ruleset.IsActive = request.IsActive;
        ruleset.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveAsync(ruleset, auditUserId: _currentUser.UserId.ToString());
        return ToDto(ruleset);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var ruleset = await GetOwnedAsync(id, cancellationToken);
        if (ruleset is null)
        {
            return false;
        }

        ruleset.IsActive = false;
        ruleset.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(ruleset, auditUserId: _currentUser.UserId.ToString());
        return true;
    }

    public async Task<RulesetDto> ImportAsync(ImportRulesetJsonRequest request, CancellationToken cancellationToken)
    {
        var source = request.Ruleset;
        return await CreateAsync(new UpsertRulesetRequest(
            source.Name,
            source.Description,
            source.SourceConfig,
            source.Rules,
            source.IsActive), cancellationToken);
    }

    internal async Task<Ruleset?> GetOwnedActiveEntityAsync(Guid id, CancellationToken cancellationToken)
    {
        var ruleset = await GetOwnedAsync(id, cancellationToken);
        return ruleset?.IsActive == true ? ruleset : null;
    }

    internal static RulesetDto ToDto(Ruleset ruleset)
    {
        return new RulesetDto(
            ruleset.Id,
            ruleset.Name,
            ruleset.Description,
            ruleset.Version,
            RulesetJson.Read(ruleset.SourceConfig, DefaultSourceConfig()),
            RulesetJson.Read(ruleset.Rules, DefaultRules()),
            ruleset.IsActive,
            ruleset.CreatedAt,
            ruleset.UpdatedAt);
    }

    private async Task<Ruleset?> GetOwnedAsync(Guid id, CancellationToken cancellationToken)
    {
        var ruleset = await _db.GetByIdAsync<Ruleset>(id, depth: 0);
        return ruleset?.UserId == _currentUser.UserId ? ruleset : null;
    }

    private static void Validate(UpsertRulesetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Ruleset name is required.");
        }

        var rules = Normalize(request.Rules);
        foreach (var rule in rules.Rules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                throw new ArgumentException("Every rule must include an id.");
            }

            if (string.IsNullOrWhiteSpace(rule.Kind))
            {
                throw new ArgumentException($"Rule '{rule.Id}' must include a kind.");
            }

            if (string.Equals(rule.Kind, "field", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(rule.Target))
                {
                    throw new ArgumentException($"Field rule '{rule.Id}' must include a target.");
                }

                if (rule.Flow is null || rule.Flow.Count == 0)
                {
                    throw new ArgumentException($"Field rule '{rule.Id}' must include at least one flow step.");
                }
            }
        }
    }

    private static RulesetSourceConfigDto Normalize(RulesetSourceConfigDto? source)
    {
        return source ?? DefaultSourceConfig();
    }

    private static RulesetRulesDocumentDto Normalize(RulesetRulesDocumentDto? rules)
    {
        var normalized = rules ?? DefaultRules();
        return normalized with { Rules = normalized.Rules ?? [] };
    }

    private static RulesetSourceConfigDto DefaultSourceConfig()
    {
        return new RulesetSourceConfigDto(",", "utf-8", []);
    }

    private static RulesetRulesDocumentDto DefaultRules()
    {
        return new RulesetRulesDocumentDto("skip", new RulesetFallbackDto(null, "Uncategorized", null, "unknown", []), []);
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
