using CallCenter.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CallCenter.Server.Features.Auth;

/// <summary>
/// Sign-in for the Agent App and the supervisor SPA (A-01, A-05, S-01).
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, LoginThrottle throttle) : ControllerBase
{
    /// <summary>
    /// Signs in and, for an agent, opens a session and returns the two SIP
    /// extensions to register with.
    /// </summary>
    /// <remarks>
    /// Limited per address and per login (F-12): too many attempts answer 429
    /// with the code <c>too_many_attempts</c>, before any password is checked.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginThrottle.Policy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request, CancellationToken ct)
    {
        var address = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!throttle.TryAcquire(request.Login, address))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, LoginThrottle.Refusal());
        }

        var (response, failure) = await auth.LoginAsync(request, ct);

        if (response is not null)
        {
            throttle.Succeeded(request.Login);
            return Ok(response);
        }

        throttle.Failed(request.Login, address);

        // A disabled account is told apart from a wrong password, because the
        // agent is standing at a laptop that will never work until a supervisor
        // acts, and "wrong password" would send them round in circles. Both
        // answers are 401 and neither says whether the username exists.
        var (code, detail) = failure == AuthService.LoginFailure.AccountDisabled
            ? (LoginErrorCodes.AccountDisabled, "This account has been disabled.")
            : (LoginErrorCodes.InvalidCredentials, "The username or password is not correct.");

        var problem = new ProblemDetails
        {
            Title = "Sign-in failed",
            // English, for logs and for anyone reading the API directly. The
            // clients show their own translation of the code (A-80) and never
            // put this text in front of an agent.
            Detail = detail,
            Status = StatusCodes.Status401Unauthorized,
        };

        problem.Extensions["code"] = code;

        return StatusCode(StatusCodes.Status401Unauthorized, problem);
    }

    /// <summary>Closes the session opened by a login (A-05).</summary>
    [HttpPost("logout")]
    [Authorize(AuthPolicies.SignedIn)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(User.GetRequiredUserId(), request.SessionId, request.Reason, ct);
        return NoContent();
    }

    /// <summary>
    /// The signed-in account. The Agent App calls this at startup to find out
    /// whether a token kept from the last shift is still good.
    /// </summary>
    [HttpGet("me")]
    [Authorize(AuthPolicies.SignedIn)]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        var user = await auth.GetCurrentUserAsync(User.GetRequiredUserId(), ct);

        // The token is valid but the account has been deleted or disabled since
        // it was issued: 401 so the app signs out rather than half-working.
        return user is null ? Unauthorized() : Ok(user);
    }
}
