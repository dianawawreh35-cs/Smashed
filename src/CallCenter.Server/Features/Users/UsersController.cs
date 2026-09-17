using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Users;

/// <summary>
/// Managing accounts and extensions (S-42). Supervisors only — this is where
/// agent credentials are set, so an agent must not reach it.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class UsersController(UsersService users) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List(CancellationToken ct) =>
        Ok(await users.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken ct)
    {
        var user = await users.GetAsync(id, ct);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var (user, failure) = await users.CreateAsync(request, User.GetRequiredUserId(), ct);

        return failure is not null
            ? Problem(failure.Value)
            : CreatedAtAction(nameof(Get), new { id = user!.Id }, user);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var (user, failure) = await users.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(user);
    }

    /// <summary>Assigns the agent's extension (S-42, SRS 2.3).</summary>
    [HttpPut("{id:guid}/extension")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> SetExtension(
        Guid id, SetExtensionRequest request, CancellationToken ct)
    {
        var (user, failure) = await users.SetExtensionAsync(id, request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(user);
    }

    [HttpPost("{id:guid}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(
        Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        var failure = await users.ResetPasswordAsync(id, request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : NoContent();
    }

    /// <summary>
    /// Turns a refusal into a reply. Each carries a <c>code</c> so the browser
    /// shows its own translation rather than this English text (A-80).
    /// </summary>
    private ObjectResult Problem(UsersService.Failure failure)
    {
        var (status, code, detail) = failure switch
        {
            UsersService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "user_not_found", "No such account."),
            UsersService.Failure.LoginTaken =>
                (StatusCodes.Status409Conflict, "login_taken", "That username is already in use."),
            UsersService.Failure.UnknownRole =>
                (StatusCodes.Status400BadRequest, "unknown_role", "Role must be Agent or Supervisor."),
            UsersService.Failure.NotAnAgent =>
                (StatusCodes.Status409Conflict, "not_an_agent", "Only agents have an extension."),
            UsersService.Failure.LastSupervisor =>
                (StatusCodes.Status409Conflict, "last_supervisor",
                    "This is the only active supervisor; disabling it would lock everyone out."),
            UsersService.Failure.CannotDisableSelf =>
                (StatusCodes.Status409Conflict, "cannot_disable_self", "You cannot disable your own account."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "The request could not be applied."),
        };

        var problem = new ProblemDetails
        {
            Title = "Change refused",
            Detail = detail,
            Status = status,
        };

        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
