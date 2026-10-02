using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Reports;

/// <summary>
/// The application reports (A-72, R-12, R-13). Supervisors only, as every
/// report is (section 4). Each takes S-07's common filters as query
/// parameters; <c>from</c> is inclusive and <c>to</c> exclusive, sent as
/// instants by the browser the way the call search sends them. Messages
/// only: <see cref="ReportQuery.ToFilter"/> counts <see cref="CommunicationKinds.App"/>.
/// </summary>
[ApiController]
[Route("api/reports/applications")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class ApplicationReportsController(ApplicationReportsService reports) : ControllerBase
{
    /// <summary>Messages per channel, and per type within each channel.</summary>
    [HttpGet("by-channel")]
    [ProducesResponseType<IReadOnlyList<ChannelReportRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChannelReportRowDto>>> ByChannel(
        [FromQuery] ReportQuery query,
        CancellationToken ct) =>
        Ok(await reports.ByChannelAsync(query.ToFilter(), ct));

    /// <summary>Orders and order value per channel, branch, agent or day (R-12, R-13).</summary>
    /// <param name="groupBy"><c>channel</c> (the default), <c>branch</c>, <c>agent</c> or <c>day</c>.</param>
    [HttpGet("orders")]
    [ProducesResponseType<IReadOnlyList<OrdersReportRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrdersReportRowDto>>> Orders(
        [FromQuery] ReportQuery query,
        [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.OrdersAsync(query.ToFilter(), groupBy, ct));

    /// <summary>Messages per day, week, month or hour of the day.</summary>
    /// <param name="groupBy"><c>day</c> (the default), <c>week</c>, <c>month</c> or <c>hour</c>.</param>
    [HttpGet("trend")]
    [ProducesResponseType<IReadOnlyList<TrendPointDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TrendPointDto>>> Trend(
        [FromQuery] ReportQuery query,
        [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.TrendAsync(query.ToFilter(), groupBy, ct));

    /// <summary>Per agent: messages recorded, orders, order value.</summary>
    [HttpGet("by-agent")]
    [ProducesResponseType<IReadOnlyList<AgentReportRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AgentReportRowDto>>> ByAgent(
        [FromQuery] ReportQuery query,
        CancellationToken ct) =>
        Ok(await reports.ByAgentAsync(query.ToFilter(), ct));

    /// <summary>Cancellations and complaints per channel.</summary>
    [HttpGet("issues")]
    [ProducesResponseType<IReadOnlyList<IssuesReportRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<IssuesReportRowDto>>> Issues(
        [FromQuery] ReportQuery query,
        CancellationToken ct) =>
        Ok(await reports.IssuesAsync(query.ToFilter(), ct));

}
