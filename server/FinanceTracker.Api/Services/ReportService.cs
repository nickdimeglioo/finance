namespace FinanceTracker.Api.Services;

public sealed class ReportService
{
    public Task<object> GetPhaseTwoSummaryAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<object>(new { reports = "Phase 6" });
    }
}

