using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Contacts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Contacts;

/// <summary>
/// The VIP and Blocked flags (S-45).
/// </summary>
/// <remarks>
/// Its own controller rather than more actions on
/// <see cref="ContactsController"/>, which any signed-in account may reach.
/// S-45 says agents see the flags but cannot change them, and that is enforced
/// here by the door: reading is open to anyone signed in, and every write
/// carries <see cref="AuthPolicies.SupervisorOnly"/>.
///
/// Reading is deliberately not supervisor-only. Agents need the flags: the
/// pop-up shows a VIP badge (A-16) and the Agent App rejects a blocked caller
/// from a locally cached copy of the block list (A-17).
/// </remarks>
[ApiController]
[Route("api/contacts")]
[Authorize(AuthPolicies.SignedIn)]
public class ContactFlagsController(ContactFlagsService flags) : ControllerBase
{
    // The list of VIP and blocked numbers S-45 asks for is not here. It is
    // GET /api/contacts?flag=vip|blocked - the same search, narrowed - so that
    // filtering and searching compose and there is one list in the app rather
    // than two that can disagree.

    /// <summary>
    /// The block list as normalised numbers, for the Agent App to cache (A-17).
    /// </summary>
    [HttpGet("blocked-numbers")]
    [ProducesResponseType<BlockedNumbersDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BlockedNumbersDto>> BlockedNumbers(CancellationToken ct) =>
        Ok(await flags.BlockedNumbersAsync(ct));

    /// <summary>
    /// Sets, changes or removes a contact's flags (S-45). Both false removes
    /// them.
    /// </summary>
    [HttpPut("{id:guid}/flags")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<FlaggedContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlaggedContactDto>> Set(
        Guid id, SetContactFlagsRequest request, CancellationToken ct)
    {
        var (contact, failure) = await flags.SetAsync(id, request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(contact);
    }

    /// <summary>
    /// Flags a bare phone number (S-45). The number is matched against existing
    /// contacts first; only an unknown number creates a nameless contact.
    /// </summary>
    [HttpPost("flags/by-number")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<FlaggedContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FlaggedContactDto>> FlagNumber(
        FlagNumberRequest request, CancellationToken ct)
    {
        var (contact, failure) = await flags.FlagNumberAsync(request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(contact);
    }

    /// <summary>
    /// Every flag change made to this contact (S-45, N-06). Supervisor-only:
    /// it names who blocked whom, which is management information rather than
    /// something an agent needs mid-call.
    /// </summary>
    [HttpGet("{id:guid}/flags/history")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<IReadOnlyList<ContactFlagChangeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ContactFlagChangeDto>>> History(
        Guid id, CancellationToken ct) =>
        Ok(await flags.HistoryAsync(id, ct));

    /// <summary>
    /// Turns a refusal into a reply carrying a <c>code</c>, so each client shows
    /// its own translation (A-80).
    /// </summary>
    private ObjectResult Problem(ContactFlagsService.Failure failure)
    {
        var (status, code, detail) = failure switch
        {
            ContactFlagsService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "contact_not_found", "No such contact."),
            ContactFlagsService.Failure.NoUsableNumber =>
                (StatusCodes.Status400BadRequest, "no_usable_number", "That is not a usable phone number."),
            ContactFlagsService.Failure.VipAndBlocked =>
                (StatusCodes.Status400BadRequest, "vip_and_blocked",
                    "A contact cannot be VIP and Blocked at the same time."),
            ContactFlagsService.Failure.ReasonRequired =>
                (StatusCodes.Status400BadRequest, "reason_required", "Give a reason for the flag."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "The flag could not be changed."),
        };

        var problem = new ProblemDetails
        {
            Title = "Flag not changed",
            Detail = detail,
            Status = status,
        };

        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
