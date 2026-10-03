using CallCenter.Server.Features.Auth;
using CallCenter.Server.Features.Communications;
using CallCenter.Server.Features.Reports;
using CallCenter.Shared.Contracts.Breaks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Breaks;

/// <summary>
/// The Agent App's own breaks (A-86): Break in and Break out, and today's total
/// for the timer.
/// </summary>
[ApiController]
[Route("api/breaks/mine")]
[Authorize(AuthPolicies.AgentOnly)]
public class MyBreaksController(BreaksService breaks) : ControllerBase
{
    /// <summary>
    /// Saves a break, sent whole: at Break in with no end, at Break out with
    /// one. Sending the same break again changes nothing.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<BreakDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BreakDto>> Save(Guid id, SaveBreakRequest request, CancellationToken ct)
    {
        var (saved, failure) = await breaks.SaveAsync(id, User.GetRequiredUserId(), User.GetSessionId(), request, ct);

        if (failure is null)
        {
            return Ok(saved);
        }

        var (status, code, detail) = failure switch
        {
            BreaksService.Failure.NotYours =>
                (StatusCodes.Status403Forbidden, "not_your_break", "That break is another agent's."),
            BreaksService.Failure.EndsBeforeStart =>
                (StatusCodes.Status400BadRequest, "break_ends_before_start", "The break ends before it begins."),
            BreaksService.Failure.BadEnding =>
                (StatusCodes.Status400BadRequest, "bad_break_ending", "EndedBy must be BreakOut or SignedOut."),
            BreaksService.Failure.TooOld =>
                (StatusCodes.Status400BadRequest, "break_too_old", "The break began more than a month ago."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "The break could not be saved."),
        };

        var problem = new ProblemDetails { Title = "Break not saved", Detail = detail, Status = status };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }

    /// <summary>
    /// The app's do-not-disturb switch (A-18), sent when it changes and at
    /// sign-in, for the supervisor's monitor (S-66).
    /// </summary>
    [HttpPut("do-not-disturb")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DoNotDisturb(SaveDoNotDisturbRequest request, CancellationToken ct)
    {
        if (User.GetSessionId() is { } sessionId)
        {
            await breaks.SaveDoNotDisturbAsync(sessionId, request, ct);
        }

        return NoContent();
    }

    /// <summary>The agent's break time today and the daily limit, for the app's timer at sign-in.</summary>
    [HttpGet("today")]
    [ProducesResponseType<MyBreaksTodayDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MyBreaksTodayDto>> Today(CancellationToken ct) =>
        Ok(await breaks.MineTodayAsync(User.GetRequiredUserId(), ct));
}

/// <summary>
/// The agents' breaks for the supervisor: who is on break now (S-66), and the
/// break report (R-22).
/// </summary>
/// <remarks>
/// Every date range is the restaurant's days, both ends included, and today
/// when none is given, as every date filter in the web app opens (1 Oct 2026).
/// </remarks>
[ApiController]
[Route("api/breaks")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class BreaksController(BreaksService breaks) : ControllerBase
{
    /// <summary>The longest period one report covers, so a typed year 2062 is refused rather than walked.</summary>
    public const int MaxDays = 366;

    /// <summary>Every active agent now: working, on break since when, or away; and today's break time.</summary>
    [HttpGet("monitor")]
    [ProducesResponseType<BreakMonitorDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BreakMonitorDto>> Monitor(CancellationToken ct) =>
        Ok(await breaks.MonitorAsync(ct));

    /// <summary>Break time per agent and per agent per day, against the daily limit.</summary>
    [HttpGet("report")]
    [ProducesResponseType<BreakReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BreakReportDto>> Report([FromQuery] BreakQuery query, CancellationToken ct) =>
        query.Period() is { } p
            ? Ok(await breaks.ReportAsync(p.From, p.To, query.AgentIds, ct))
            : BadPeriod();

    /// <summary>The single breaks, newest first, a page at a time.</summary>
    [HttpGet]
    [ProducesResponseType<BreakPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BreakPageDto>> List(
        [FromQuery] BreakQuery query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = BreaksService.DefaultPageSize,
        CancellationToken ct = default) =>
        query.Period() is { } p
            ? Ok(await breaks.ListAsync(p.From, p.To, query.AgentIds, page, pageSize, ct))
            : BadPeriod();

    /// <summary>Every break of the period as a CSV file Excel opens (S-05).</summary>
    /// <param name="lang"><c>ar</c> (the default) or <c>en</c>: the language of the headings.</param>
    [HttpGet("export")]
    [Produces(CallExport.ContentType)]
    public async Task<IActionResult> Export([FromQuery] BreakQuery query, [FromQuery] string? lang, CancellationToken ct)
    {
        if (query.Period() is not { } p)
        {
            return BadPeriod();
        }

        var rows = await breaks.ExportAsync(p.From, p.To, query.AgentIds, ct);

        Response.ContentType = CallExport.ContentType;
        Response.Headers.ContentDisposition = $"attachment; filename=\"breaks-{p.From:yyyy-MM-dd}-{p.To:yyyy-MM-dd}.csv\"";
        await BreakExport.WriteAsync(Response.Body, rows, lang, ct);
        return new EmptyResult();
    }

    private ObjectResult BadPeriod()
    {
        var problem = new ProblemDetails
        {
            Title = "Bad period",
            Detail = $"The last day is before the first, or the period is longer than {MaxDays} days.",
            Status = StatusCodes.Status400BadRequest,
        };
        problem.Extensions["code"] = "bad_period";
        return BadRequest(problem);
    }
}

/// <summary>The filters as query parameters, one class so the report, the list and the export take the same ones.</summary>
public class BreakQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }

    /// <summary>Any of these agents (the parameter repeated); none is every agent.</summary>
    [FromQuery(Name = "agentId")] public Guid[] AgentIds { get; set; } = [];

    /// <summary>The period, today for a missing end; null when it is backwards or too long.</summary>
    public (DateOnly From, DateOnly To)? Period()
    {
        var today = DateOnly.FromDateTime(ReportScope.Local(DateTimeOffset.Now));
        var from = From ?? To ?? today;
        var to = To ?? From ?? today;

        return to < from || to.DayNumber - from.DayNumber >= BreaksController.MaxDays ? null : (from, to);
    }
}
