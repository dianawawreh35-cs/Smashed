using CallCenter.Shared.Contracts.Communications;

namespace CallCenter.Server.Features.Reports;

/// <summary>
/// The application reports (A-72, R-12, R-13, S-06, S-07): what came in on
/// each app, what it was about, what it was worth, who recorded it.
/// </summary>
/// <remarks>
/// <b>Counted by the database</b> through <see cref="ReportCube"/>, the same
/// path as the call reports: every filter in <see cref="ReportFilter"/> is
/// part of the query, the restaurant's own day and hour are .NET's, and only
/// the grouped figures come back (N-02, 26 Sep).
///
/// <b>Shared with the call reports.</b> The filter carries a kind: <c>App</c>
/// from the applications controller, and null from the call reports for the
/// one report here whose point is comparing channels (R-13's order value per
/// channel, branch, agent or day), so that report is written once. Only a
/// communication classified as an order counts as one: a cancellation with a
/// value typed by mistake is not revenue.
/// </remarks>
public class ApplicationReportsService(ReportCube cube)
{
    /// <summary>Messages per channel, and per type within each.</summary>
    public async Task<IReadOnlyList<ChannelReportRowDto>> ByChannelAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var cells = await cube.CountAsync(filter, CubeBy.Channel | CubeBy.Type, ct);
        var names = await cube.NamesAsync(ct);

        return cells
            .GroupBy(c => c.ChannelId!.Value)
            .Select(g => new ChannelReportRowDto(
                g.Key,
                names.Channel(g.Key),
                g.Sum(c => c.N),
                g.Where(c => c.TypeId is not null)
                    .Select(c => (Type: names.Types[c.TypeId!.Value], c.N))
                    .Select(x => new TypeCountDto(x.Type.Name, x.Type.LabelAr, x.Type.LabelEn, x.N))
                    .OrderByDescending(t => t.Count).ThenBy(t => t.TypeName)
                    .ToList()))
            .OrderByDescending(r => r.Messages).ThenBy(r => r.Channel)
            .ToList();
    }

    /// <summary>
    /// Orders and their value, grouped by channel, branch, agent or day
    /// (R-12, R-13).
    /// </summary>
    /// <param name="groupBy"><c>channel</c>, <c>branch</c>, <c>agent</c> or <c>day</c>. Anything else is channel.</param>
    public async Task<IReadOnlyList<OrdersReportRowDto>> OrdersAsync(
        ReportFilter filter, string? groupBy, CancellationToken ct = default)
    {
        var by = (groupBy ?? string.Empty).ToLowerInvariant();
        var dimension = by switch { "branch" => CubeBy.Branch, "agent" => CubeBy.Agent, "day" => CubeBy.Day, _ => CubeBy.Channel };
        var cells = (await cube.CountAsync(filter, dimension, ct)).Where(c => c.Orders > 0);
        var names = await cube.NamesAsync(ct);

        Func<CubeCell, (string Key, string Label)> key = by switch
        {
            "branch" => c => (c.BranchId?.ToString() ?? string.Empty, names.Branch(c.BranchId)),
            "agent" => c => (c.AgentId?.ToString() ?? string.Empty, names.Agent(c.AgentId)),
            "day" => c => (ReportScope.Day(c.Day!.Value), ReportScope.Day(c.Day!.Value)),
            _ => c => (c.ChannelId?.ToString() ?? string.Empty, names.Channel(c.ChannelId)),
        };

        var grouped = cells
            .GroupBy(key)
            .Select(g =>
            {
                var orders = g.Sum(c => c.Orders);
                var value = g.Sum(c => c.OrderValue);
                return new OrdersReportRowDto(g.Key.Key, g.Key.Label, orders, value,
                    orders == 0 ? null : Math.Round(value / orders, 2));
            });

        // Days read in order; everything else by what mattered most.
        return (by == "day"
                ? grouped.OrderBy(r => r.Key)
                : grouped.OrderByDescending(r => r.OrderValue).ThenBy(r => r.Label))
            .ToList();
    }

    /// <summary>
    /// Messages over time (S-07's grouping): per day, week or month across the
    /// period, or per hour of the day summed over it, for "when do they write".
    /// </summary>
    /// <param name="groupBy"><c>day</c> (the default), <c>week</c>, <c>month</c> or <c>hour</c>.</param>
    public async Task<IReadOnlyList<TrendPointDto>> TrendAsync(
        ReportFilter filter, string? groupBy, CancellationToken ct = default)
    {
        var hour = groupBy?.ToLowerInvariant() == "hour";
        var cells = await cube.CountAsync(filter, hour ? CubeBy.Hour : CubeBy.Day, ct);

        return cells
            .GroupBy(c => hour ? c.Hour!.Value.ToString("00") : ReportScope.Bucket(c.Day!.Value, groupBy))
            .Select(g => new TrendPointDto(g.Key, g.Sum(c => c.N), g.Sum(c => c.Orders), g.Sum(c => c.OrderValue)))
            .OrderBy(p => p.Bucket)
            .ToList();
    }

    /// <summary>Per agent: recorded, orders and their value (R-15's message half).</summary>
    public async Task<IReadOnlyList<AgentReportRowDto>> ByAgentAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var cells = await cube.CountAsync(filter, CubeBy.Agent, ct);
        var names = await cube.NamesAsync(ct);

        return cells
            .Where(c => c.AgentId is not null)
            .Select(c => new AgentReportRowDto(c.AgentId!.Value, names.Agent(c.AgentId), c.N, c.Orders, c.OrderValue))
            .OrderByDescending(r => r.Messages).ThenBy(r => r.Agent)
            .ToList();
    }

    /// <summary>Cancellations and complaints per channel (R-14, R-17's message half).</summary>
    public async Task<IReadOnlyList<IssuesReportRowDto>> IssuesAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var cells = await cube.CountAsync(filter, CubeBy.Channel, ct);
        var names = await cube.NamesAsync(ct);

        return cells
            .Select(c => new IssuesReportRowDto(c.ChannelId!.Value, names.Channel(c.ChannelId), c.N, c.Cancellations, c.Complaints))
            .OrderByDescending(r => r.Cancellations + r.Complaints).ThenBy(r => r.Channel)
            .ToList();
    }
}
