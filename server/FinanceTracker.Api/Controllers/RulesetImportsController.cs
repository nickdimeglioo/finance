using FinanceTracker.Api.Features.Imports;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/import")]
public sealed class RulesetImportsController : ControllerBase
{
    private readonly ImportOrchestrator _imports;

    public RulesetImportsController(ImportOrchestrator imports)
    {
        _imports = imports;
    }

    [HttpPost("preview")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<RulesetImportResult>> Preview(
        [FromForm] Guid accountId,
        [FromForm] Guid rulesetId,
        [FromForm] string? deduplicationStrategy,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _imports.PreviewAsync(new RulesetImportRequest(accountId, rulesetId, deduplicationStrategy), file, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("run")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<RulesetImportResult>> Run(
        [FromForm] Guid accountId,
        [FromForm] Guid rulesetId,
        [FromForm] string? deduplicationStrategy,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _imports.RunAsync(new RulesetImportRequest(accountId, rulesetId, deduplicationStrategy), file, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("jobs/{jobId:guid}")]
    public async Task<ActionResult<ImportJobDto>> GetJob(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _imports.GetJobAsync(jobId, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }
}
