using FinanceTracker.Api.Features.Rules;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/rulesets")]
public sealed class RulesetsController : ControllerBase
{
    private readonly RulesetService _rulesets;

    public RulesetsController(RulesetService rulesets)
    {
        _rulesets = rulesets;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RulesetDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _rulesets.ListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RulesetDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var ruleset = await _rulesets.GetAsync(id, cancellationToken);
        return ruleset is null ? NotFound() : Ok(ruleset);
    }

    [HttpPost]
    public async Task<ActionResult<RulesetDto>> Create(UpsertRulesetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var ruleset = await _rulesets.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = ruleset.Id }, ruleset);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RulesetDto>> Update(Guid id, UpsertRulesetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var ruleset = await _rulesets.UpdateAsync(id, request, cancellationToken);
            return ruleset is null ? NotFound() : Ok(ruleset);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _rulesets.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("import-json")]
    public async Task<ActionResult<RulesetDto>> ImportJson(ImportRulesetJsonRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var ruleset = await _rulesets.ImportAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = ruleset.Id }, ruleset);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/export")]
    public async Task<ActionResult<RulesetDto>> Export(Guid id, CancellationToken cancellationToken)
    {
        var ruleset = await _rulesets.GetAsync(id, cancellationToken);
        return ruleset is null ? NotFound() : Ok(ruleset);
    }
}
