using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IObjectStorageService _storage;

    public DiagnosticsController(IObjectStorageService storage)
    {
        _storage = storage;
    }

    [HttpGet("contracts")]
    public ActionResult<object> Contracts()
    {
        return Ok(new
        {
            FinanceValues.AccountTypes,
            FinanceValues.AccountStatuses,
            FinanceValues.TransactionTypes,
            FinanceValues.Classifications,
            FinanceValues.TransactionStatuses,
            FinanceValues.TransactionSources,
            storage = _storage.GetConfiguration()
        });
    }
}

