using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly ICurrentUserContext _currentUser;

    public AuthController(ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
    }

    [HttpGet("me")]
    public ActionResult<object> Me()
    {
        return Ok(new
        {
            id = _currentUser.UserId,
            email = _currentUser.Email
        });
    }
}

