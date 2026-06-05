using FinanceTracker.Api.Features.Guidance;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/profile")]
public sealed class ProfileController : ControllerBase
{
    private readonly ProfileService _profiles;

    public ProfileController(ProfileService profiles)
    {
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<ActionResult<UserFinanceProfileDto>> Get(CancellationToken cancellationToken)
        => Ok(await _profiles.GetAsync(cancellationToken));

    [HttpPut]
    public async Task<ActionResult<UserFinanceProfileDto>> Update(UpdateUserFinanceProfileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _profiles.UpdateAsync(request, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

