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
/// instants by the browser the way the call search sends them.
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
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? agentId, [FromQuery] Guid? branchId, [FromQuery] Guid? channelId, [FromQuery] Guid? typeId,
        CancellationToken ct) =>
        Ok(await reports.ByChannelAsync(Filter(from, to, agentId, branchId, channelId, typeId), ct));

    /// <summary>Orders and order value per channel, branch, agent or day (R-12, R-13).</summary>
    /// <param name="groupBy"><c>channel</c> (the default), <c>branch</c>, <c>agent</c> or <c>day</c>.</param>
    [HttpGet("orders")]
    [ProducesResponseType<IReadOnlyList<OrdersReportRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrdersReportRowDto>>> Orders(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? agentId, [FromQuery] Guid? branchId, [FromQuery] Guid? channelId, [FromQuery] Guid? typeId,
        [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.OrdersAsync(Filter(from, to, agentId, branchId, channelId, typeId), groupBy, ct));

    /// <summary>Messages per day, week, month or hour of the day.</summary>
    /// <param name="groupBy"><c>day</c> (the default), <c>week</c>, <c>month</c> or <c>hour</c>.</param>
    [HttpGet("trend")]
    [ProducesResponseType<IReadOnlyList<TrendPointDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TrendPointDto>>> Trend(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? agentId, [FromQuery] Guid? branchId, [FromQuery] Guid? channelId, [FromQuery] Guid? typeId,
        [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.TrendAsync(Filter(from, to, agentId, branchId, channelId, typeId), groupBy, ct));

    /// <summary>Per agent: messages recorded, orders, order value.</summary>
    [HttpGet("by-agent")]
    [ProducesResponseType<IReadOnlyList<AgentReportRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AgentReportRowDto>>> ByAgent(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? agentId, [FromQuery] Guid? branchId, [FromQuery] Guid? channelId, [FromQuery] Guid? typeId,
        CancellationToken ct) =>
        Ok(await reports.ByAgentAsync(Filter(from, to, agentId, branchId, channelId, typeId), ct));

    /// <summary>Cancellations and complaints per channel.</summary>
    [HttpGet("issues")]
    [ProducesResponseType<IReadOnlyList<IssuesReportRowDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<IssuesReportRowDto>>> Issues(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? agentId, [FromQuery] Guid? branchId, [FromQuery] Guid? channelId, [FromQuery] Guid? typeId,
        CancellationToken ct) =>
        Ok(await reports.IssuesAsync(Filter(from, to, agentId, branchId, channelId, typeId), ct));

    /// <summary>Messages only. The call reports will pass <see cref="CommunicationKinds.Call"/> here when they are built.</summary>
    private static ReportFilter Filter(
        DateTimeOffset? from, DateTimeOffset? to, Guid? agentId, Guid? branchId, Guid? channelId, Guid? typeId) =>
        new(from, to, agentId, branchId, channelId, typeId, CommunicationKinds.App);
}
