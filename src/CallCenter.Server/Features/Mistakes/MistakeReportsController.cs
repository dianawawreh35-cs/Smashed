using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Mistakes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Mistakes;

/// <summary>
/// The mistakes report (R-23). Each endpoint is one report card, scoped by the
/// period, the branch and the agent. Supervisors only, as the mistakes are.
/// </summary>
[ApiController]
[Route("api/mistakes/reports")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class MistakeReportsController(MistakeReportsService reports) : ControllerBase
{
    [HttpGet("by-branch")]
    [ProducesResponseType<IReadOnlyList<MistakeBranchRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MistakeBranchRowDto>>> ByBranch(
        [FromQuery] MistakeReportQuery query, CancellationToken ct) =>
        Ok(await reports.ByBranchAsync(query.ToFilter(), ct));

    [HttpGet("by-agent")]
    [ProducesResponseType<IReadOnlyList<MistakeAgentRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MistakeAgentRowDto>>> ByAgent(
        [FromQuery] MistakeReportQuery query, CancellationToken ct) =>
        Ok(await reports.ByAgentAsync(query.ToFilter(), ct));

    /// <param name="groupBy"><c>day</c> (the default), <c>week</c> or <c>month</c>.</param>
    [HttpGet("trend")]
    [ProducesResponseType<IReadOnlyList<MistakeTrendPointDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MistakeTrendPointDto>>> Trend(
        [FromQuery] MistakeReportQuery query, [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.TrendAsync(query.ToFilter(), groupBy, ct));

    [HttpGet("repeat-customers")]
    [ProducesResponseType<IReadOnlyList<MistakeCustomerRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MistakeCustomerRowDto>>> RepeatCustomers(
        [FromQuery] MistakeReportQuery query, CancellationToken ct) =>
        Ok(await reports.RepeatCustomersAsync(query.ToFilter(), ct));
}

/// <summary>The report's filters: days, inclusive, a branch and an agent. Every one is optional.</summary>
public class MistakeReportQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? AgentId { get; set; }

    public MistakesService.Filter ToFilter() => new(From, To, BranchId, AgentId: AgentId);
}
