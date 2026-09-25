using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Channels;

/// <summary>
/// The channels a message can arrive on (S-41, A-70).
/// </summary>
/// <remarks>
/// Reading is open to any signed-in account: the Agent App offers the list when
/// recording a message. Changing it is the supervisor's.
/// </remarks>
[ApiController]
[Route("api/channels")]
[Authorize(AuthPolicies.SignedIn)]
public class ChannelsController(ChannelsService channels) : ControllerBase
{
    /// <summary>Every channel in the supervisor's order. Hidden ones only when asked, for the management screen.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ChannelDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChannelDto>>> List(
        [FromQuery] bool includeInactive, CancellationToken ct) =>
        Ok(await channels.ListAsync(includeInactive, ct));

    [HttpPost]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<ChannelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChannelDto>> Create(UpsertChannelRequest request, CancellationToken ct)
    {
        var (channel, failure) = await channels.CreateAsync(request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(channel);
    }

    /// <summary>Renames, reorders or hides a channel. Phone can only be moved.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<ChannelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ChannelDto>> Update(
        Guid id, UpsertChannelRequest request, CancellationToken ct)
    {
        var (channel, failure) = await channels.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(channel);
    }

    private ObjectResult Problem(ChannelsService.Failure failure)
    {
        var (status, code, detail) = failure switch
        {
            ChannelsService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "channel_not_found", "No such channel."),
            ChannelsService.Failure.BadName =>
                (StatusCodes.Status400BadRequest, "bad_name",
                    "The name is blank, or another channel already has it."),
            ChannelsService.Failure.SystemChannel =>
                (StatusCodes.Status409Conflict, "system_channel",
                    "Phone is a system channel: every call is filed under it, so it cannot be renamed or hidden."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "The channel could not be saved."),
        };

        var problem = new ProblemDetails { Title = "Channel not saved", Detail = detail, Status = status };
        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
