using FinanceTracker.Api.Features.Rules;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/classification-rules")]
public sealed class ClassificationRulesController : ControllerBase
{
    private readonly ClassificationRuleService _rules;

    public ClassificationRulesController(ClassificationRuleService rules)
    {
        _rules = rules;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClassificationRuleDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _rules.ListAsync(cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<ClassificationRuleDto>> Create(UpsertClassificationRuleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var rule = await _rules.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), new { id = rule.Id }, rule);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClassificationRuleDto>> Update(Guid id, UpsertClassificationRuleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var rule = await _rules.UpdateAsync(id, request, cancellationToken);
            return rule is null ? NotFound() : Ok(rule);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _rules.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder(ReorderRulesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _rules.ReorderAsync(request, cancellationToken);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("test")]
    public async Task<ActionResult<TestClassificationRuleResult>> Test(TestClassificationRuleRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _rules.TestAsync(request, cancellationToken));
    }
}
