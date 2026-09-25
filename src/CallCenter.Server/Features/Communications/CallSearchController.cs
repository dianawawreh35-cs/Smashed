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
        [FromQuery] SearchQuery query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = CallSearchService.DefaultPageSize,
        CancellationToken ct = default) =>
        Ok(await search.SearchAsync(query.ToFilter(), page, pageSize, ct));

    /// <summary>
    /// R-02: every call matching the same filters as the search, as a CSV file
    /// Excel opens (S-05) — the whole result, not the page on screen.
    /// </summary>
    /// <param name="lang"><c>ar</c> (the default) or <c>en</c>: the language of the headings and the words in it.</param>
    [HttpGet("search/export")]
    [Produces(CallExport.ContentType)]
    public async Task Export([FromQuery] SearchQuery query, [FromQuery] string? lang, CancellationToken ct)
    {
        var name = query.Kind == CommunicationKinds.App ? "applications" : "calls";
        Response.ContentType = CallExport.ContentType;
        Response.Headers.ContentDisposition = $"attachment; filename=\"{name}-{DateTime.Now:yyyy-MM-dd}.csv\"";
        await CallExport.WriteAsync(Response.Body, search.ExportAsync(query.ToFilter()), lang ?? "ar", ct);
    }

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

/// <summary>
/// The search's filters as query parameters, one class so the search and its
/// export cannot take different ones. Every one is optional.
/// </summary>
public class SearchQuery
{
    public string? Q { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? TypeId { get; set; }
    public string? Status { get; set; }
    public string? Direction { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? Notes { get; set; }
    public decimal? MinOrder { get; set; }
    public decimal? MaxOrder { get; set; }
    public bool? HasRecording { get; set; }
    public bool? Classified { get; set; }
    public Guid? ChannelId { get; set; }

    /// <summary><c>Call</c> (the default, so the Calls page never shows a message) or <c>App</c>.</summary>
    public string Kind { get; set; } = CommunicationKinds.Call;

    public CallSearchService.Filter ToFilter() => new(
        Q, Kind, ChannelId, AgentId, BranchId, TypeId, Status, Direction, From, To, Notes,
        MinOrder, MaxOrder, HasRecording, Classified);
}
