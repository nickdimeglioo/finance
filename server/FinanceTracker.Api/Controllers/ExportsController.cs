using FinanceTracker.Api.Features.Reports;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/exports")]
public sealed class ExportsController : ControllerBase
{
    private readonly ExportService _exports;

    public ExportsController(ExportService exports)
    {
        _exports = exports;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExportFileDto>>> List(CancellationToken cancellationToken)
        => Ok(await _exports.ListAsync(cancellationToken));

    [HttpPost("transactions")]
    public async Task<ActionResult<ExportFileDto>> Transactions(ExportRequest request, CancellationToken cancellationToken)
    {
        var export = await _exports.CreateTransactionsExportAsync(request with { ExportType = "transactions" }, cancellationToken);
        return CreatedAtAction(nameof(GetDownloadUrl), new { exportId = export.Id }, export);
    }

    [HttpPost("report")]
    public async Task<ActionResult<ExportFileDto>> Report(ExportRequest request, CancellationToken cancellationToken)
    {
        var export = await _exports.CreateTransactionsExportAsync(request with { ExportType = "report" }, cancellationToken);
        return CreatedAtAction(nameof(GetDownloadUrl), new { exportId = export.Id }, export);
    }

    [HttpGet("{exportId:guid}/download-url")]
    public async Task<IActionResult> GetDownloadUrl(Guid exportId, CancellationToken cancellationToken)
    {
        var url = await _exports.GetDownloadUrlAsync(exportId, cancellationToken);
        return url is null ? NotFound() : Ok(url);
    }

    [HttpGet("{exportId:guid}/content")]
    public async Task<IActionResult> Content(Guid exportId, CancellationToken cancellationToken)
    {
        var content = await _exports.GetContentAsync(exportId, cancellationToken);
        if (content is null)
        {
            return NotFound();
        }

        return File(content.Value.Object.Content, content.Value.Entity.ContentType, content.Value.Entity.FileName);
    }
}

