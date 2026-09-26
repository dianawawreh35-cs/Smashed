using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Reports;

/// <summary>
/// S-07's common filters as query parameters: <c>from</c> inclusive and
/// <c>to</c> exclusive, as instants, and the four ids. Which kinds of
/// communication a report counts is the report's own business, not the query's.
/// </summary>
public class ReportQuery
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? TypeId { get; set; }

    public ReportFilter ToFilter() => new(From, To, AgentId, BranchId, ChannelId, TypeId);
}

/// <summary>
/// The call reports (R-01 to R-18). Supervisors only, as every report is
/// (section 4). R-02, the full list, is the call search and its export
/// (<c>GET /api/communications/search/export</c>), not a second list.
/// </summary>
[ApiController]
[Route("api/reports/calls")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class CallReportsController(CallReportsService reports, ApplicationReportsService orders) : ControllerBase
{
    /// <summary>R-01: communications — calls vs app, inbound vs outbound, answered vs missed.</summary>
    /// <param name="groupBy"><c>day</c> (the default), <c>week</c> or <c>month</c>.</param>
    [HttpGet("summary")]
    public async Task<ActionResult<IReadOnlyList<CallSummaryRowDto>>> Summary(
        [FromQuery] ReportQuery query, [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.SummaryAsync(query.ToFilter(), groupBy, ct));

    /// <summary>R-03: calls per type, with percentage share.</summary>
    [HttpGet("by-type")]
    public async Task<ActionResult<IReadOnlyList<TypeShareRowDto>>> ByType([FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.ByTypeAsync(query.ToFilter(), ct));

    /// <summary>R-04: per day, week, month, agent or branch, and per type within each.</summary>
    [HttpGet("breakdown")]
    public async Task<ActionResult<IReadOnlyList<CallBreakdownRowDto>>> Breakdown(
        [FromQuery] ReportQuery query, [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.BreakdownAsync(query.ToFilter(), groupBy, ct));

    /// <summary>R-04: customers with more than one call in the period, ranked.</summary>
    [HttpGet("recurring-customers")]
    public async Task<ActionResult<IReadOnlyList<CustomerRankRowDto>>> RecurringCustomers(
        [FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.RecurringCustomersAsync(query.ToFilter(), ct));

    /// <summary>R-05: every complaint in the period, with notes and follow-up status.</summary>
    [HttpGet("complaints")]
    public async Task<ActionResult<IReadOnlyList<ProblemRowDto>>> Complaints([FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.ProblemsAsync(query.ToFilter(), "Complaint", allChannels: false, ct));

    /// <summary>R-05 per branch or agent, and R-17's handling figures.</summary>
    /// <param name="groupBy"><c>branch</c> (the default), <c>agent</c>, <c>day</c>, <c>week</c> or <c>month</c>.</param>
    [HttpGet("complaints/by")]
    public async Task<ActionResult<IReadOnlyList<ComplaintsRowDto>>> ComplaintsBy(
        [FromQuery] ReportQuery query, [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.ComplaintsByAsync(query.ToFilter(), groupBy, ct));

    /// <summary>R-05: customers who complained more than once.</summary>
    [HttpGet("repeat-complainers")]
    public async Task<ActionResult<IReadOnlyList<CustomerRankRowDto>>> RepeatComplainers(
        [FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.RepeatComplainersAsync(query.ToFilter(), ct));

    /// <summary>
    /// R-13: order value, total and average, per channel, branch, agent or day,
    /// phone and apps together. The application reports' own report, with no
    /// kind: written once.
    /// </summary>
    [HttpGet("orders")]
    public async Task<ActionResult<IReadOnlyList<OrdersReportRowDto>>> Orders(
        [FromQuery] ReportQuery query, [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await orders.OrdersAsync(query.ToFilter() with { Kind = null }, groupBy, ct));

    // ---- Phase 2 (R-10 to R-18) --------------------------------------------------

    /// <summary>R-10: incoming calls per hour of the day and weekday, Monday first.</summary>
    [HttpGet("peak-hours")]
    public async Task<ActionResult<IReadOnlyList<PeakHourRowDto>>> PeakHours([FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.PeakHoursAsync(query.ToFilter(), ct));

    /// <summary>R-11: missed calls per day, week, month, hour, agent or branch, and their rate.</summary>
    [HttpGet("missed")]
    public async Task<ActionResult<IReadOnlyList<MissedRowDto>>> Missed(
        [FromQuery] ReportQuery query, [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.MissedAsync(query.ToFilter(), groupBy, ct));

    /// <summary>R-11: every missed call, with the agent's note.</summary>
    [HttpGet("missed/list")]
    public async Task<ActionResult<IReadOnlyList<MissedCallRowDto>>> MissedList([FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.MissedListAsync(query.ToFilter(), ct));

    /// <summary>R-20: abandoned calls per day, week, month or hour, their rate, wait and call-back (S-55).</summary>
    [HttpGet("abandoned")]
    public async Task<ActionResult<IReadOnlyList<AbandonedRowDto>>> Abandoned(
        [FromQuery] ReportQuery query, [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.AbandonedAsync(query.ToFilter(), groupBy, ct));

    /// <summary>R-20: every abandoned call, with its wait and whether it was called back.</summary>
    [HttpGet("abandoned/list")]
    public async Task<ActionResult<IReadOnlyList<AbandonedCallRowDto>>> AbandonedList([FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.AbandonedListAsync(query.ToFilter(), ct));

    /// <summary>R-12: orders per channel, phone and apps, with each one's share.</summary>
    [HttpGet("orders-by-channel")]
    public async Task<ActionResult<IReadOnlyList<ChannelOrdersRowDto>>> OrdersByChannel([FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.OrdersByChannelAsync(query.ToFilter(), ct));

    /// <summary>R-12: orders by phone and through the apps, per day, week or month.</summary>
    [HttpGet("orders-trend")]
    public async Task<ActionResult<IReadOnlyList<ChannelOrdersTrendPointDto>>> OrdersTrend(
        [FromQuery] ReportQuery query, [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.OrdersTrendAsync(query.ToFilter(), groupBy, ct));

    /// <summary>R-14: cancellations ÷ orders per branch, channel or agent.</summary>
    [HttpGet("cancellations")]
    public async Task<ActionResult<IReadOnlyList<CancellationRateRowDto>>> Cancellations(
        [FromQuery] ReportQuery query, [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.CancellationsAsync(query.ToFilter(), groupBy, ct));

    /// <summary>R-14: every cancellation, with its notes, phone and apps together.</summary>
    [HttpGet("cancellations/list")]
    public async Task<ActionResult<IReadOnlyList<ProblemRowDto>>> CancellationList([FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.ProblemsAsync(query.ToFilter(), "Cancellation", allChannels: true, ct));

    /// <summary>R-15: agent productivity.</summary>
    [HttpGet("agents")]
    public async Task<ActionResult<IReadOnlyList<AgentProductivityRowDto>>> Agents([FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.AgentsAsync(query.ToFilter(), ct));

    /// <summary>R-16: new and returning customers per day, week or month.</summary>
    [HttpGet("customers")]
    public async Task<ActionResult<IReadOnlyList<CustomerBaseRowDto>>> Customers(
        [FromQuery] ReportQuery query, [FromQuery] string? groupBy, CancellationToken ct) =>
        Ok(await reports.CustomerBaseAsync(query.ToFilter(), groupBy, ct));

    /// <summary>R-16: the top 50 customers by orders, or by order value.</summary>
    /// <param name="by"><c>orders</c> (the default) or <c>value</c>.</param>
    [HttpGet("top-customers")]
    public async Task<ActionResult<IReadOnlyList<CustomerRankRowDto>>> TopCustomers(
        [FromQuery] ReportQuery query, [FromQuery] string? by, CancellationToken ct) =>
        Ok(await reports.TopCustomersAsync(query.ToFilter(), by, ct: ct));

    /// <summary>R-16: customers with no order in the last <paramref name="days"/> days (30 by default), measured from today.</summary>
    [HttpGet("inactive-customers")]
    public async Task<ActionResult<IReadOnlyList<CustomerRankRowDto>>> InactiveCustomers(
        [FromQuery] ReportQuery query, [FromQuery] int days = 30, CancellationToken ct = default) =>
        Ok(await reports.InactiveCustomersAsync(query.ToFilter(), days, ct));

    /// <summary>R-18: unclassified calls, calls from unknown numbers, duplicate names.</summary>
    [HttpGet("data-quality")]
    public async Task<ActionResult<DataQualityDto>> DataQuality([FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.DataQualityAsync(query.ToFilter(), ct));

    /// <summary>R-18: the numbers nobody saved, most calls first.</summary>
    [HttpGet("unknown-numbers")]
    public async Task<ActionResult<IReadOnlyList<UnknownNumberRowDto>>> UnknownNumbers([FromQuery] ReportQuery query, CancellationToken ct) =>
        Ok(await reports.UnknownNumbersAsync(query.ToFilter(), ct));

    /// <summary>R-18: names more than one contact shares. Not limited by the period: contacts have none.</summary>
    [HttpGet("duplicate-names")]
    public async Task<ActionResult<IReadOnlyList<DuplicateNameRowDto>>> DuplicateNames(CancellationToken ct) =>
        Ok(await reports.DuplicateNamesAsync(ct));
}

/// <summary>The supervisor's home page (S-20).</summary>
[ApiController]
[Route("api/reports/dashboard")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class DashboardController(CallReportsService reports) : ControllerBase
{
    /// <summary>Today's figures, today being the restaurant's.</summary>
    [HttpGet("today")]
    public async Task<ActionResult<DashboardTodayDto>> Today(CancellationToken ct) =>
        Ok(await reports.TodayAsync(ct));

    /// <summary>The four charts for the chosen period.</summary>
    [HttpGet("period")]
    public async Task<ActionResult<DashboardPeriodDto>> Period(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct) =>
        Ok(await reports.PeriodAsync(new ReportFilter(from, to), ct));
}
