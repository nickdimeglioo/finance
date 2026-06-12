using FinanceTracker.Api.Features.Attachments;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController, Authorize, Route("api/v1/receipts")]
public sealed class ReceiptsController : ControllerBase
{
    private readonly ReceiptAttachmentService _receipts;

    public ReceiptsController(ReceiptAttachmentService receipts)
    {
        _receipts = receipts;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReceiptAttachmentDto>>> List([FromQuery] string? status, [FromQuery] Guid? transactionId, CancellationToken token)
        => Ok(await _receipts.ListAsync(status, transactionId, token));

    [HttpPost]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<ActionResult<ReceiptAttachmentDto>> Upload(
        [FromForm] string title,
        [FromForm] string? notes,
        [FromForm] decimal? amountHint,
        [FromForm] string? merchantHint,
        [FromForm] DateOnly? dateHint,
        [FromForm] Guid? transactionId,
        [FromForm] IFormFile file,
        CancellationToken token)
    {
        try
        {
            return Ok(await _receipts.UploadAsync(title, notes, amountHint, merchantHint, dateHint, transactionId, file, token));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ReceiptAttachmentDto>> Update(Guid id, UpdateReceiptAttachmentRequest request, CancellationToken token)
    {
        try
        {
            var item = await _receipts.UpdateAsync(id, request, token);
            return item is null ? NotFound() : Ok(item);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken token)
        => await _receipts.DeleteAsync(id, token) ? NoContent() : NotFound();

    [HttpPost("match")]
    public async Task<ActionResult<IReadOnlyList<ReceiptMatchSuggestionDto>>> Match(ReceiptMatchRequest request, CancellationToken token)
    {
        var items = await _receipts.MatchAsync(request.TransactionId, token);
        return items is null ? NotFound() : Ok(items);
    }

    [HttpPatch("{id:guid}/match")]
    public async Task<ActionResult<ReceiptAttachmentDto>> Accept(Guid id, ReceiptMatchRequest request, CancellationToken token)
    {
        var item = await _receipts.AcceptMatchAsync(id, request.TransactionId, token);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPatch("{id:guid}/dismiss")]
    public async Task<ActionResult<ReceiptAttachmentDto>> Dismiss(Guid id, CancellationToken token)
    {
        var item = await _receipts.DismissAsync(id, token);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken token)
    {
        var result = await _receipts.DownloadAsync(id, token);
        if (result is null) return NotFound();
        return File(result.Value.File.Content, result.Value.File.ContentType, result.Value.Metadata.OriginalFileName);
    }
}
