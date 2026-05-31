using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/transactions")]
public sealed class TransactionsController : ControllerBase
{
    private readonly TransactionService _transactionService;

    public TransactionsController(TransactionService transactionService)
    {
        _transactionService = transactionService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<TransactionListItemDto>>> List(
        [FromQuery] TransactionFiltersRequest filters,
        CancellationToken cancellationToken)
    {
        return Ok(await _transactionService.ListAsync(filters, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransactionDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var transaction = await _transactionService.GetAsync(id, cancellationToken);
        return transaction is null ? NotFound() : Ok(transaction);
    }

    [HttpPost]
    public async Task<ActionResult<TransactionDetailDto>> Create(CreateTransactionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await _transactionService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = transaction.Id }, transaction);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TransactionDetailDto>> Update(Guid id, UpdateTransactionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await _transactionService.UpdateAsync(id, request, cancellationToken);
            return transaction is null ? NotFound() : Ok(transaction);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("transfer")]
    public async Task<ActionResult<TransactionDetailDto>> CreateTransfer(
        CreateTransferRequest request,
        [FromServices] TransferService transferService,
        CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await transferService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = transaction.Id }, transaction);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/void")]
    public async Task<IActionResult> Void(Guid id, [FromQuery] bool includeTransferPartner, CancellationToken cancellationToken)
    {
        var voided = await _transactionService.VoidAsync(id, includeTransferPartner, cancellationToken);
        return voided ? NoContent() : NotFound();
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<TransactionDetailDto>> UpdateStatus(Guid id, UpdateTransactionStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await _transactionService.UpdateStatusAsync(id, request.Status, cancellationToken);
            return transaction is null ? NotFound() : Ok(transaction);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public sealed record UpdateTransactionStatusRequest(string Status);
