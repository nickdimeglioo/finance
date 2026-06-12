using FinanceTracker.Api.Features.Planning;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController, Authorize, Route("api/v1/budget-goals")]
public sealed class BudgetGoalsController : ControllerBase
{
    private readonly BudgetGoalService _budgetGoals;

    public BudgetGoalsController(BudgetGoalService budgetGoals)
    {
        _budgetGoals = budgetGoals;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BudgetGoalDto>>> List([FromQuery] string? kind, CancellationToken token)
        => Ok(await _budgetGoals.ListAsync(kind, token));

    [HttpPost]
    public async Task<ActionResult<BudgetGoalDto>> Create(UpsertBudgetGoalRequest request, CancellationToken token)
    {
        try
        {
            return Ok(await _budgetGoals.CreateAsync(request, token));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BudgetGoalDto>> Update(Guid id, UpsertBudgetGoalRequest request, CancellationToken token)
    {
        try
        {
            var item = await _budgetGoals.UpdateAsync(id, request, token);
            return item is null ? NotFound() : Ok(item);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken token)
        => await _budgetGoals.DeleteAsync(id, token) ? NoContent() : NotFound();
}
