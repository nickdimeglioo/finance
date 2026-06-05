using FinanceTracker.Api.Features.Reports;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly ReportService _reports;

    public ReportsController(ReportService reports)
    {
        _reports = reports;
    }

    [HttpGet("cash-flow")]
    public async Task<ActionResult<IReadOnlyList<CashFlowPointDto>>> CashFlow([FromQuery] int months = 6, CancellationToken cancellationToken = default)
        => Ok(await _reports.GetCashFlowAsync(months, cancellationToken));

    [HttpGet("category-breakdown")]
    public async Task<ActionResult<IReadOnlyList<BreakdownItemDto>>> CategoryBreakdown([FromQuery] ReportRangeRequest request, CancellationToken cancellationToken)
        => Ok(await _reports.GetCategoryBreakdownAsync(request, cancellationToken));

    [HttpGet("business-personal")]
    public async Task<ActionResult<BusinessPersonalSummaryDto>> BusinessPersonal([FromQuery] ReportRangeRequest request, CancellationToken cancellationToken)
        => Ok(await _reports.GetBusinessPersonalAsync(request, cancellationToken));

    [HttpGet("tag-breakdown")]
    public async Task<ActionResult<IReadOnlyList<BreakdownItemDto>>> TagBreakdown([FromQuery] ReportRangeRequest request, CancellationToken cancellationToken)
        => Ok(await _reports.GetTagBreakdownAsync(request, cancellationToken));

    [HttpGet("net-worth")]
    public async Task<ActionResult<IReadOnlyList<NetWorthPointDto>>> NetWorth([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
        => Ok(await _reports.GetNetWorthAsync(from, to, cancellationToken));
}

