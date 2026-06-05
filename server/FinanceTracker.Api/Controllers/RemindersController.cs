using FinanceTracker.Api.Features.Organization;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController, Authorize, Route("api/v1/reminders")]
public sealed class RemindersController : ControllerBase
{
    private readonly ReminderService _reminders;
    public RemindersController(ReminderService reminders) { _reminders = reminders; }
    [HttpGet] public async Task<ActionResult<IReadOnlyList<ReminderDto>>> List([FromQuery] bool includeResolved, CancellationToken token) => Ok(await _reminders.ListAsync(includeResolved, token));
    [HttpPatch("{id:guid}/dismiss")] public async Task<ActionResult<ReminderDto>> Dismiss(Guid id, CancellationToken token) { var item = await _reminders.SetStatusAsync(id, "dismissed", token); return item is null ? NotFound() : Ok(item); }
    [HttpPatch("{id:guid}/complete")] public async Task<ActionResult<ReminderDto>> Complete(Guid id, CancellationToken token) { var item = await _reminders.SetStatusAsync(id, "completed", token); return item is null ? NotFound() : Ok(item); }
}
