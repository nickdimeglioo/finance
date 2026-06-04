using FinanceTracker.Api.Features.Imports;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/import")]
public sealed class RulesetImportsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
        [FromForm] string? acceptedRowNumbers,
        [FromForm] string? rowOverrides,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new RulesetImportRequest(
                accountId,
                rulesetId,
                deduplicationStrategy,
                ReadJson<IReadOnlyList<int>>(acceptedRowNumbers),
                ReadJson<IReadOnlyList<RulesetImportRowOverrideDto>>(rowOverrides));
            return Ok(await _imports.RunAsync(request, file, cancellationToken));
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
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

    private static T? ReadJson<T>(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
