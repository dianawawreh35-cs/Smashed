using System.Globalization;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Reports;

/// <summary>
/// The application reports (A-72, R-12, R-13, S-06, S-07): what came in on
/// each app, what it was about, what it was worth, who recorded it.
/// </summary>
/// <remarks>
/// <b>The database narrows, the server counts.</b> Every filter in
/// <see cref="Filter"/> is part of the query, so the rows fetched are exactly
/// the messages the supervisor asked about, projected to the dozen columns the
/// reports use. The grouping is then done here rather than in SQL, for one
/// reason: a day, a week and an hour are the restaurant's, not UTC's, and
/// bucketing a <c>timestamptz</c> by local date inside PostgreSQL through EF is
/// where the 20 September 500 came from. At the volume A-70 describes (a few
/// dozen messages a day) a year is a few thousand rows, which is a query that
/// returns in milliseconds either way.
///
/// <b>Written for calls to reuse.</b> <see cref="Filter.Kind"/> is
/// <c>App</c> from the applications controller and nothing here reads a
/// message-only column, so the call reports (R-01 and on) can group the same
/// rows later. Only the message reports are wired up now, as the task said.
///
/// <b>Local time</b> is the server's, which is the restaurant's — the same rule
/// as the edit window (A-42): a shift that ends after midnight UTC is still
/// one evening in Hebron.
/// </remarks>
public class ApplicationReportsService(CallCenterDbContext db)
{
    /// <summary>S-07's common set. <paramref name="From"/> inclusive, <paramref name="To"/> exclusive, as instants.</summary>
    public record Filter(
        DateTimeOffset? From = null,
        DateTimeOffset? To = null,
        Guid? AgentId = null,
        Guid? BranchId = null,
        Guid? ChannelId = null,
        Guid? TypeId = null,
        string Kind = CommunicationKinds.App);

    /// <summary>What every report groups from: one message, flattened.</summary>
    private sealed record Row(
        DateTimeOffset StartedAt,
        Guid ChannelId,
        string Channel,
        Guid? BranchId,
        string? Branch,
        Guid? AgentId,
        string? Agent,
        string? TypeName,
        string? TypeLabelAr,
        string? TypeLabelEn,
        decimal? OrderValue)
    {
        public bool IsOrder => TypeName == "Order";
        public decimal Value => OrderValue ?? 0m;
        public DateTime Local => StartedAt.ToLocalTime().DateTime;
    }

    /// <summary>Messages per channel, and per type within each (the first report the prompt asks for).</summary>
    public async Task<IReadOnlyList<ChannelReportRowDto>> ByChannelAsync(Filter filter, CancellationToken ct = default)
    {
        var rows = await RowsAsync(filter, ct);

        return rows
            .GroupBy(r => (r.ChannelId, r.Channel))
            .Select(g => new ChannelReportRowDto(
                g.Key.ChannelId,
                g.Key.Channel,
                g.Count(),
                g.Where(r => r.TypeName is not null)
                    .GroupBy(r => r.TypeName!)
                    .Select(t => new TypeCountDto(t.Key, t.First().TypeLabelAr!, t.First().TypeLabelEn!, t.Count()))
                    .OrderByDescending(t => t.Count).ThenBy(t => t.TypeName)
                    .ToList()))
            .OrderByDescending(r => r.Messages).ThenBy(r => r.Channel)
            .ToList();
    }

    /// <summary>
    /// Orders and their value, grouped by channel, branch, agent or day
    /// (R-12, R-13's message half). Only messages classified as an order count;
    /// a complaint with an order value typed by mistake does not become revenue.
    /// </summary>
    /// <param name="groupBy"><c>channel</c>, <c>branch</c>, <c>agent</c> or <c>day</c>. Anything else is channel.</param>
    public async Task<IReadOnlyList<OrdersReportRowDto>> OrdersAsync(
        Filter filter, string? groupBy, CancellationToken ct = default)
    {
        var rows = (await RowsAsync(filter, ct)).Where(r => r.IsOrder);

        Func<Row, (string Key, string Label)> key = (groupBy ?? string.Empty).ToLowerInvariant() switch
        {
            "branch" => r => (r.BranchId?.ToString() ?? string.Empty, r.Branch ?? "—"),
            "agent" => r => (r.AgentId?.ToString() ?? string.Empty, r.Agent ?? "—"),
            "day" => r => (Day(r.Local), Day(r.Local)),
            _ => r => (r.ChannelId.ToString(), r.Channel),
        };

        var grouped = rows
            .GroupBy(key)
            .Select(g => new OrdersReportRowDto(
                g.Key.Key,
                g.Key.Label,
                g.Count(),
                g.Sum(r => r.Value),
                g.Any() ? Math.Round(g.Sum(r => r.Value) / g.Count(), 2) : null));

        // Days read in order; everything else by what mattered most.
        return (groupBy?.ToLowerInvariant() == "day"
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
        Filter filter, string? groupBy, CancellationToken ct = default)
    {
        var rows = await RowsAsync(filter, ct);

        Func<Row, string> bucket = (groupBy ?? string.Empty).ToLowerInvariant() switch
        {
            "hour" => r => r.Local.ToString("HH", CultureInfo.InvariantCulture),
            "week" => r => Day(StartOfWeek(r.Local)),
            "month" => r => r.Local.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            _ => r => Day(r.Local),
        };

        return rows
            .GroupBy(bucket)
            .Select(g => new TrendPointDto(
                g.Key,
                g.Count(),
                g.Count(r => r.IsOrder),
                g.Where(r => r.IsOrder).Sum(r => r.Value)))
            .OrderBy(p => p.Bucket)
            .ToList();
    }

    /// <summary>Per agent: recorded, orders and their value (R-15's message half).</summary>
    public async Task<IReadOnlyList<AgentReportRowDto>> ByAgentAsync(Filter filter, CancellationToken ct = default)
    {
        var rows = await RowsAsync(filter, ct);

        return rows
            .Where(r => r.AgentId is not null)
            .GroupBy(r => (r.AgentId!.Value, r.Agent ?? "—"))
            .Select(g => new AgentReportRowDto(
                g.Key.Item1,
                g.Key.Item2,
                g.Count(),
                g.Count(r => r.IsOrder),
                g.Where(r => r.IsOrder).Sum(r => r.Value)))
            .OrderByDescending(r => r.Messages).ThenBy(r => r.Agent)
            .ToList();
    }

    /// <summary>Cancellations and complaints per channel (R-14, R-17's message half).</summary>
    public async Task<IReadOnlyList<IssuesReportRowDto>> IssuesAsync(Filter filter, CancellationToken ct = default)
    {
        var rows = await RowsAsync(filter, ct);

        return rows
            .GroupBy(r => (r.ChannelId, r.Channel))
            .Select(g => new IssuesReportRowDto(
                g.Key.ChannelId,
                g.Key.Channel,
                g.Count(),
                g.Count(r => r.TypeName == "Cancellation"),
                g.Count(r => r.TypeName == "Complaint")))
            .OrderByDescending(r => r.Cancellations + r.Complaints).ThenBy(r => r.Channel)
            .ToList();
    }

    // ---- the query ----------------------------------------------------------

    private async Task<List<Row>> RowsAsync(Filter f, CancellationToken ct)
    {
        var q = db.Communications.AsNoTracking().Where(c => c.Kind == f.Kind);

        if (f.From is { } from)
        {
            var start = from.ToUniversalTime();
            q = q.Where(c => c.StartedAt >= start);
        }

        if (f.To is { } to)
        {
            var end = to.ToUniversalTime();
            q = q.Where(c => c.StartedAt < end);
        }

        if (f.AgentId is { } agent) q = q.Where(c => c.AgentId == agent);
        if (f.BranchId is { } branch) q = q.Where(c => c.BranchId == branch);
        if (f.ChannelId is { } channel) q = q.Where(c => c.ChannelId == channel);
        if (f.TypeId is { } type) q = q.Where(c => c.Classification != null && c.Classification.TypeId == type);

        return await q
            .Select(c => new Row(
                c.StartedAt,
                c.ChannelId,
                c.Channel.Name,
                c.BranchId,
                c.Branch != null ? c.Branch.Name : null,
                c.AgentId,
                c.Agent != null ? c.Agent.DisplayName : null,
                c.Classification != null ? c.Classification.Type.Name : null,
                c.Classification != null ? c.Classification.Type.LabelAr : null,
                c.Classification != null ? c.Classification.Type.LabelEn : null,
                c.Classification != null ? c.Classification.OrderValue : null))
            .ToListAsync(ct);
    }

    private static string Day(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The Monday. The restaurant's week starts when its reports do; Monday is the ISO convention and reads the same in both languages.</summary>
    private static DateTime StartOfWeek(DateTime local)
    {
        var back = ((int)local.DayOfWeek + 6) % 7;
        return local.Date.AddDays(-back);
    }
}
