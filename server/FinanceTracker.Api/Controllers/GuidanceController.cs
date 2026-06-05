using FinanceTracker.Api.Features.Guidance;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/guidance")]
public sealed class GuidanceController : ControllerBase
{
    private readonly GuidanceService _guidance;

    public GuidanceController(GuidanceService guidance)
    {
        _guidance = guidance;
    }

    [HttpGet]
    public async Task<ActionResult<GuidanceSummaryDto>> Get(CancellationToken cancellationToken)
        => Ok(await _guidance.GetAsync(cancellationToken));
}

