namespace FinanceTracker.Api.Services;

public sealed class GuidanceService
{
    public Task<object> GetPhaseTwoSummaryAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<object>(new { guidance = "Phase 7" });
    }
}

