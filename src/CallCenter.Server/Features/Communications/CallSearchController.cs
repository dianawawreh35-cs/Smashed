using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Communications;

/// <summary>
/// The supervisor's search across every call, and one call opened (S-02, S-03).
/// </summary>
/// <remarks>
/// <b>Supervisors only.</b> An agent sees their own calls in their own log
/// (A-50) and never another agent's (A-52), so there is no agent version of
/// this with a filter bolted on. A second door that could be left open is the
/// thing A-52 is guarding against.
///
/// Under <c>api/communications</c> rather than <c>api/calls</c>, because app
/// communications (A-70) are to appear in the same search.
/// </remarks>
[ApiController]
[Route("api/communications")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class CallSearchController(CallSearchService search) : ControllerBase
{
    /// <summary>
    /// Calls — or, with <c>kind=App</c>, messages (A-70) — matching every filter
    /// given, newest first, a page at a time.
    /// </summary>
    /// <param name="kind">
    /// <c>Call</c> (the default, so the Calls page never shows a message) or
    /// <c>App</c> for the Applications page.
    /// </param>
    [HttpGet("search")]
    [ProducesResponseType<CallSearchPageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CallSearchPageDto>> Search(
        [FromQuery] string? q,
        [FromQuery] Guid? agentId,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? typeId,
        [FromQuery] string? status,
        [FromQuery] string? direction,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? notes,
        [FromQuery] decimal? minOrder,
        [FromQuery] decimal? maxOrder,
        [FromQuery] bool? hasRecording,
        [FromQuery] bool? classified,
        [FromQuery] Guid? channelId,
        [FromQuery] string kind = CommunicationKinds.Call,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = CallSearchService.DefaultPageSize,
        CancellationToken ct = default) =>
        Ok(await search.SearchAsync(
            new CallSearchService.Filter(
                q, kind, channelId, agentId, branchId, typeId, status, direction, from, to, notes,
                minOrder, maxOrder, hasRecording, classified),
            page,
            pageSize,
            ct));

    /// <summary>
    /// One call in full (S-03). Its classification and the history of changes
    /// to it come from <c>GET /api/classifications/{id}</c> and <c>…/history</c>,
    /// and its audio from <c>GET /api/recordings/{id}</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CallDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CallDetailsDto>> Details(Guid id, CancellationToken ct) =>
        await search.DetailsAsync(id, ct) is { } call ? Ok(call) : NotFound();
}
