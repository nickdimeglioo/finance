using FinanceTracker.Api.Features.Organization;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController, Authorize, Route("api/v1/recurring-rules")]
public sealed class RecurringRulesController : ControllerBase
{
    private readonly RecurringRuleService _rules;
    public RecurringRulesController(RecurringRuleService rules) { _rules = rules; }
    [HttpGet] public async Task<ActionResult<IReadOnlyList<RecurringRuleDto>>> List(CancellationToken token) => Ok(await _rules.ListAsync(token));
    [HttpPost] public async Task<ActionResult<RecurringRuleDto>> Create(UpsertRecurringRuleRequest request, CancellationToken token) { try { return Ok(await _rules.CreateAsync(request, token)); } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); } }
    [HttpPut("{id:guid}")] public async Task<ActionResult<RecurringRuleDto>> Update(Guid id, UpsertRecurringRuleRequest request, CancellationToken token) { try { var item = await _rules.UpdateAsync(id, request, token); return item is null ? NotFound() : Ok(item); } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); } }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken token) => await _rules.DeleteAsync(id, token) ? NoContent() : NotFound();
    [HttpPost("match")] public async Task<ActionResult<object>> Match(CancellationToken token) => Ok(new { matched = await _rules.MatchTransactionsAsync(token) });
    [HttpGet("suggestions")] public async Task<ActionResult<IReadOnlyList<RecurringRuleSuggestionDto>>> Suggestions(CancellationToken token) => Ok(await _rules.SuggestAsync(token));
}

[ApiController, Authorize, Route("api/v1/subscriptions")]
public sealed class SubscriptionsController : ControllerBase
{
    [HttpGet("status")] public async Task<ActionResult<SubscriptionStatusDto>> Status([FromServices] RecurringRuleService rules, CancellationToken token) => Ok(await rules.StatusAsync(token));
}
