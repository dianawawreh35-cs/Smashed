using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Pbx;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// Opening and closing the call queue (S-60). Supervisors only.
/// </summary>
[ApiController]
[Route("api/pbx/queue")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class PbxQueueController(PbxQueueSwitch queue) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<QueueStatusDto>> Status(CancellationToken ct) =>
        Ok(await queue.StatusAsync(ct));

    /// <summary>
    /// Opens or closes the queue: dials <c>*280</c> if that changes anything.
    /// A PBX that refuses or misbehaves is an answer, not an error: <c>ok</c> is
    /// false and <c>code</c> and <c>error</c> say why.
    /// </summary>
    [HttpPost("switch")]
    public async Task<ActionResult<QueueSwitchResultDto>> Switch(SetQueueRequest request, CancellationToken ct) =>
        Ok(await queue.SwitchAsync(request.Open, User.GetRequiredUserId(), ct));

    /// <summary>Says which state the queue is in right now, without calling the PBX.</summary>
    [HttpPut("state")]
    public async Task<ActionResult<QueueStatusDto>> Mark(SetQueueRequest request, CancellationToken ct) =>
        Ok(await queue.MarkAsync(request.Open, User.GetRequiredUserId(), ct));
}
