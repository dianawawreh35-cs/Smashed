using CallCenter.Server.Features.Auth;
using CallCenter.Server.Features.Communications;
using CallCenter.Shared.Contracts.Mistakes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Mistakes;

/// <summary>
/// The mistakes made by the branches and the agents (S-65).
/// </summary>
/// <remarks>
/// <b>Supervisors only</b>, reading included. An agent does not see the
/// mistakes recorded against them or their colleagues in either app.
/// </remarks>
[ApiController]
[Route("api/mistakes")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class MistakesController(MistakesService mistakes) : ControllerBase
{
    /// <summary>The mistakes matching every filter given, newest day first, a page at a time.</summary>
    [HttpGet]
    [ProducesResponseType<MistakePageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MistakePageDto>> Search(
        [FromQuery] MistakeQuery query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MistakesService.DefaultPageSize,
        CancellationToken ct = default) =>
        Ok(await mistakes.SearchAsync(query.ToFilter(), page, pageSize, ct));

    /// <summary>Every mistake matching the same filters, as a CSV file Excel opens (S-05).</summary>
    /// <param name="lang"><c>ar</c> (the default) or <c>en</c>: the language of the headings.</param>
    [HttpGet("export")]
    [Produces(CallExport.ContentType)]
    public async Task Export([FromQuery] MistakeQuery query, [FromQuery] string? lang, CancellationToken ct)
    {
        Response.ContentType = CallExport.ContentType;
        Response.Headers.ContentDisposition = $"attachment; filename=\"mistakes-{DateTime.Now:yyyy-MM-dd}.csv\"";
        await MistakeExport.WriteAsync(Response.Body, mistakes.ExportAsync(query.ToFilter()), lang, ct);
    }

    [HttpPost]
    [ProducesResponseType<MistakeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MistakeDto>> Create(UpsertMistakeRequest request, CancellationToken ct)
    {
        var (mistake, failure) = await mistakes.CreateAsync(request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(mistake);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<MistakeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MistakeDto>> Update(Guid id, UpsertMistakeRequest request, CancellationToken ct)
    {
        var (mistake, failure) = await mistakes.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(mistake);
    }

    /// <summary>Removes a mistake entered by error (Dia, 1 Oct). The audit log keeps what it said.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var failure = await mistakes.DeleteAsync(id, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : NoContent();
    }

    private ObjectResult Problem(MistakesService.Failure failure)
    {
        var (status, code, detail) = failure switch
        {
            MistakesService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "mistake_not_found", "No such mistake."),
            MistakesService.Failure.UnknownBranch =>
                (StatusCodes.Status400BadRequest, "unknown_branch", "No such branch."),
            MistakesService.Failure.BranchInactive =>
                (StatusCodes.Status400BadRequest, "branch_inactive", "That branch is disabled."),
            MistakesService.Failure.BadResponsible =>
                (StatusCodes.Status400BadRequest, "bad_responsible", "Responsible must be Branch or Agent."),
            MistakesService.Failure.AgentRequired =>
                (StatusCodes.Status400BadRequest, "agent_required", "An agent's mistake names the agent."),
            MistakesService.Failure.AgentNotAllowed =>
                (StatusCodes.Status400BadRequest, "agent_not_allowed", "A branch's mistake names no agent."),
            MistakesService.Failure.UnknownAgent =>
                (StatusCodes.Status400BadRequest, "unknown_agent", "No such agent."),
            MistakesService.Failure.AgentInactive =>
                (StatusCodes.Status400BadRequest, "agent_inactive", "That agent's account is disabled."),
            MistakesService.Failure.BadNumber =>
                (StatusCodes.Status400BadRequest, "bad_number", "The customer's number has no digits."),
            MistakesService.Failure.NotesRequired =>
                (StatusCodes.Status400BadRequest, "notes_required", "Say what the mistake was."),
            MistakesService.Failure.FutureDate =>
                (StatusCodes.Status400BadRequest, "future_date", "The date is after today."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "The mistake could not be saved."),
        };

        var problem = new ProblemDetails { Title = "Mistake not saved", Detail = detail, Status = status };
        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}

/// <summary>The search's filters as query parameters, one class so the search and its export take the same ones.</summary>
/// <remarks>Branch, responsible and agent can each be given several times, and match any of them (Dia, 2 Oct 2026).</remarks>
public class MistakeQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    [FromQuery(Name = "branchId")] public Guid[] BranchIds { get; set; } = [];
    [FromQuery(Name = "responsible")] public string[] Responsible { get; set; } = [];
    [FromQuery(Name = "agentId")] public Guid[] AgentIds { get; set; } = [];
    public string? Q { get; set; }

    /// <summary><c>true</c>, <c>false</c>, or left out for both (Dia, 3 Oct 2026).</summary>
    public bool? Compensated { get; set; }

    public MistakesService.Filter ToFilter() => new(From, To, BranchIds, Responsible, AgentIds, Q, Compensated);
}
