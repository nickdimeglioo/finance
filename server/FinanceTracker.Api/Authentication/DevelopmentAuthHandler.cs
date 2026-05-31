using System.Security.Claims;
using System.Text.Encodings.Web;
using FinanceTracker.Api.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FinanceTracker.Api.Authentication;

public sealed class DevelopmentAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "FinanceDevelopment";

    private readonly FinanceAuthOptions _authOptions;

    public DevelopmentAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<FinanceAuthOptions> authOptions)
        : base(options, logger, encoder)
    {
        _authOptions = authOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers.TryGetValue("X-User-Id", out var userHeader)
            && Guid.TryParse(userHeader.ToString(), out var parsedUserId)
                ? parsedUserId
                : _authOptions.DevelopmentUserId;

        var email = Request.Headers.TryGetValue("X-User-Email", out var emailHeader)
            ? emailHeader.ToString()
            : _authOptions.DevelopmentEmail;

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, email)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

