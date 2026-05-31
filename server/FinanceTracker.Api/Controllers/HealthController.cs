using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Route("api/v1/health")]
public sealed class HealthController : ControllerBase
{
    private readonly HealthDiagnosticService _healthDiagnosticService;

    public HealthController(HealthDiagnosticService healthDiagnosticService)
    {
        _healthDiagnosticService = healthDiagnosticService;
    }

    [HttpGet]
    public async Task<ActionResult<object>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _healthDiagnosticService.CheckAsync(cancellationToken));
    }
}

