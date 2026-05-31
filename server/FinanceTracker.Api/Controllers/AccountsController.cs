using FinanceTracker.Api.Features.Accounts;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/accounts")]
public sealed class AccountsController : ControllerBase
{
    private readonly AccountService _accountService;

    public AccountsController(AccountService accountService)
    {
        _accountService = accountService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AccountListItemDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _accountService.ListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var account = await _accountService.GetAsync(id, cancellationToken);
        return account is null ? NotFound() : Ok(account);
    }

    [HttpPost]
    public async Task<ActionResult<AccountDetailDto>> Create(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await _accountService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = account.Id }, account);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AccountDetailDto>> Update(Guid id, UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await _accountService.UpdateAsync(id, request, cancellationToken);
            return account is null ? NotFound() : Ok(account);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var archived = await _accountService.ArchiveAsync(id, cancellationToken);
        return archived ? NoContent() : NotFound();
    }
}
