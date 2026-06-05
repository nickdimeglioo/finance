using FinanceTracker.Api.Features.Organization;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController, Authorize, Route("api/v1/notes")]
public sealed class NotesController : ControllerBase
{
    private readonly NoteService _notes;
    public NotesController(NoteService notes) { _notes = notes; }
    [HttpGet] public async Task<ActionResult<IReadOnlyList<NoteDto>>> List([FromQuery] string? status, CancellationToken token) => Ok(await _notes.ListAsync(status, token));
    [HttpPost] public async Task<ActionResult<NoteDto>> Create(UpsertNoteRequest request, CancellationToken token) { try { return Ok(await _notes.CreateAsync(request, token)); } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); } }
    [HttpPut("{id:guid}")] public async Task<ActionResult<NoteDto>> Update(Guid id, UpsertNoteRequest request, CancellationToken token) { try { var item = await _notes.UpdateAsync(id, request, token); return item is null ? NotFound() : Ok(item); } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); } }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken token) => await _notes.DeleteAsync(id, token) ? NoContent() : NotFound();
    [HttpPost("match")] public async Task<ActionResult<IReadOnlyList<NoteMatchSuggestionDto>>> Match(NoteMatchRequest request, CancellationToken token) { var items = await _notes.MatchAsync(request.TransactionId, token); return items is null ? NotFound() : Ok(items); }
    [HttpPatch("{id:guid}/match")] public async Task<ActionResult<NoteDto>> Accept(Guid id, NoteMatchRequest request, CancellationToken token) { var item = await _notes.AcceptMatchAsync(id, request.TransactionId, token); return item is null ? NotFound() : Ok(item); }
    [HttpPatch("{id:guid}/dismiss")] public async Task<ActionResult<NoteDto>> Dismiss(Guid id, CancellationToken token) { var item = await _notes.DismissAsync(id, token); return item is null ? NotFound() : Ok(item); }
}
