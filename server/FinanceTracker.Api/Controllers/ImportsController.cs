using FinanceTracker.Api.Features.Imports;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/imports")]
public sealed class ImportsController : ControllerBase
{
    private readonly ImportService _importService;

    public ImportsController(ImportService importService)
    {
        _importService = importService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ImportBatchDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _importService.ListAsync(cancellationToken));
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<UploadImportResponse>> Upload(
        [FromForm] Guid accountId,
        [FromForm] string? institution,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _importService.UploadAsync(accountId, institution, file, cancellationToken);
            return CreatedAtAction(nameof(GetPreview), new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/parse")]
    public async Task<ActionResult<ParsedImportDto>> Parse(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var parsed = await _importService.ParseAsync(id, cancellationToken);
            return parsed is null ? NotFound() : Ok(parsed);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/preview")]
    public async Task<ActionResult<IReadOnlyList<ImportPreviewRowDto>>> Preview(
        Guid id,
        PreviewImportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _importService.PreviewAsync(id, request, cancellationToken);
            return rows is null ? NotFound() : Ok(rows);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/preview")]
    public async Task<ActionResult<IReadOnlyList<ImportPreviewRowDto>>> GetPreview(Guid id, CancellationToken cancellationToken)
    {
        var rows = await _importService.GetPreviewAsync(id, cancellationToken);
        return rows is null ? NotFound() : Ok(rows);
    }

    [HttpPut("{id:guid}/preview-rows/{rowId:guid}")]
    public async Task<ActionResult<ImportPreviewRowDto>> UpdatePreviewRow(
        Guid id,
        Guid rowId,
        UpdateImportPreviewRowRequest request,
        CancellationToken cancellationToken)
    {
        var row = await _importService.UpdatePreviewRowAsync(id, rowId, request, cancellationToken);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost("{id:guid}/commit")]
    public async Task<ActionResult<ImportCommitResult>> Commit(Guid id, CancellationToken cancellationToken)
    {
        var result = await _importService.CommitAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
