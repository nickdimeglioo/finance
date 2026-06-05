using FinanceTracker.Api.Features.Organization;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController, Authorize, Route("api/v1/tags")]
public sealed class TagsController : ControllerBase
{
    private readonly TagService _tags;
    public TagsController(TagService tags) { _tags = tags; }
    [HttpGet] public async Task<ActionResult<IReadOnlyList<TagDto>>> List(CancellationToken token) => Ok(await _tags.ListAsync(token));
    [HttpPost] public async Task<ActionResult<TagDto>> Create(UpsertTagRequest request, CancellationToken token) { try { var item = await _tags.CreateAsync(request, token); return CreatedAtAction(nameof(List), item); } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); } }
    [HttpPut("{id:guid}")] public async Task<ActionResult<TagDto>> Update(Guid id, UpsertTagRequest request, CancellationToken token) { try { var item = await _tags.UpdateAsync(id, request, token); return item is null ? NotFound() : Ok(item); } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); } }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken token) => await _tags.DeleteAsync(id, token) ? NoContent() : NotFound();
}
