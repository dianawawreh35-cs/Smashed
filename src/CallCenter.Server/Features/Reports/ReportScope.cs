using System.Globalization;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Phone;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Reports;

/// <summary>
/// S-07's common filters, shared by the application reports and the call
/// reports. <paramref name="From"/> inclusive, <paramref name="To"/> exclusive,
/// as instants: the browser sends the start of the first day and the start of
/// the day after the last, in its own time zone.
/// </summary>
/// <param name="Kind">
/// <c>Call</c>, <c>App</c>, or null for both — the few reports whose point is
/// comparing the phone with the apps (R-01's calls vs app, R-12 to R-14, the
/// dashboard's by-channel figures; Dia, 25 Sep).
/// </param>
/// <param name="WithRings">
/// Also count the Agent App's untaken rings of an abandoned call (S-55). Off
/// everywhere but the agents' own figures (R-15): the abandoned call already
/// counts the customer once, and each ring would count them again.
/// </param>
/// <param name="WithoutUntaken">
/// Also leave out every incoming ring nobody took, Missed or Rejected (Dia,
/// 26 Sep). On the dashboard only: the queue passes a call from one agent to
/// the next, so an untaken ring is a leg of a call that another agent
/// answered or that was abandoned, and that row already counts it. The
/// reports about missed calls and the agents (R-11, R-15) still count them.
/// </param>
/// <param name="Direction">
/// <c>In</c> or <c>Out</c>: only the calls that way. Messages from the apps,
/// which have no direction, are let through, so a report that compares the
/// phone with the apps still has the apps. Null for both, which the screens
/// never ask for: incoming and outgoing are never added together on them
/// (Dia, 6 Oct 2026).
/// </param>
/// <remarks>
/// The id lists match any of their ids; empty or null is no filter (several
/// agents, branches, channels or types at once: Dia, 2 Oct 2026).
/// </remarks>
public record ReportFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    IReadOnlyList<Guid>? AgentIds = null,
    IReadOnlyList<Guid>? BranchIds = null,
    IReadOnlyList<Guid>? ChannelIds = null,
    IReadOnlyList<Guid>? TypeIds = null,
    string? Kind = CommunicationKinds.App,
    bool WithRings = false,
    bool WithoutUntaken = false,
    string? Direction = null);

/// <summary>
/// What every report agrees on, written once: which rows a filter means, which
/// calls are internal, and what a day, a week and an hour are.
/// </summary>
/// <remarks>
/// <b>Local time</b> is the server's, which is the restaurant's — the same rule
/// as the edit window (A-42): a shift that ends after midnight UTC is still one
/// evening in Hebron. The rows are narrowed in the database and bucketed here,
/// because bucketing a <c>timestamptz</c> by local date inside PostgreSQL
/// through EF is where the 20 September 500 came from.
/// </remarks>
public static class ReportScope
{
    /// <summary>The rows <paramref name="f"/> names. Every filter is part of the query (20 Sep, "filtering a page lies").</summary>
    /// <param name="internalNumbers">
    /// Normalised numbers whose calls are left out (S-48). Empty for none.
    /// </param>
    public static IQueryable<Communication> Narrow(
        IQueryable<Communication> q, ReportFilter f, IReadOnlyList<string>? internalNumbers = null)
    {
        if (f.Kind is { } kind) q = q.Where(c => c.Kind == kind);
        if (f.Direction is { } direction) q = q.Where(c => c.Kind != CommunicationKinds.Call || c.Direction == direction);
        if (!f.WithRings) q = q.Where(c => c.AbandonedCallId == null);
        if (f.WithoutUntaken)
        {
            q = q.Where(c => !(c.Direction == Directions.In
                && (c.Status == CommunicationStatuses.Missed || c.Status == CommunicationStatuses.Rejected)));
        }

        // Instants, converted to UTC: PostgreSQL's timestamptz takes nothing
        // else from Npgsql, and a local offset here was the 20 September 500.
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

        if (f.AgentIds is { Count: > 0 })
        {
            var agents = f.AgentIds.ToArray();
            q = q.Where(c => c.AgentId != null && agents.Contains(c.AgentId.Value));
        }

        if (f.BranchIds is { Count: > 0 })
        {
            var branches = f.BranchIds.ToArray();
            q = q.Where(c => c.BranchId != null && branches.Contains(c.BranchId.Value));
        }

        if (f.ChannelIds is { Count: > 0 })
        {
            var channels = f.ChannelIds.ToArray();
            q = q.Where(c => channels.Contains(c.ChannelId));
        }

        if (f.TypeIds is { Count: > 0 })
        {
            var types = f.TypeIds.ToArray();
            q = q.Where(c => c.Classification != null && types.Contains(c.Classification.TypeId));
        }

        // S-48: internal calls are recorded and kept, and left out of the
        // customer-facing reports. A row with no number is a customer's
        // (withheld caller id), so the null has to be let through explicitly:
        // NOT (NULL = ANY(...)) is NULL, which a WHERE drops.
        if (internalNumbers is { Count: > 0 })
        {
            var list = internalNumbers.ToList();
            q = q.Where(c => c.RemoteNormalised == null || !list.Contains(c.RemoteNormalised));
        }

        // A call whose other party is an extension is internal without being
        // listed (Dia, 6 Oct 2026): agent to agent, agent to branch, branch to
        // agent. Customers never have a number that short; the list is still
        // there for a branch reached on a full number.
        q = q.Where(c => c.Kind != CommunicationKinds.Call
                         || c.RemoteNormalised == null
                         || c.RemoteNormalised.Length > PhoneNormalizer.MaxExtensionLength);

        return q;
    }

    /// <summary>
    /// The internal numbers (S-48), normalised as a caller's number is, so the
    /// supervisor's "2001" matches a call stored as "2001". Blank means none.
    /// </summary>
    public static async Task<IReadOnlyList<string>> InternalNumbersAsync(CallCenterDbContext db, CancellationToken ct)
    {
        var value = await db.Settings.AsNoTracking()
            .Where(s => s.Key == "reports.internal_numbers")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(PhoneNormalizer.Normalize)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
    }

    /// <summary><c>in</c> or <c>out</c>, in any case, as <see cref="Directions"/> has them; anything else is null.</summary>
    public static string? ParseDirection(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "in" => Directions.In,
        "out" => Directions.Out,
        _ => null,
    };

    /// <summary>The instant in the restaurant's own time.</summary>
    public static DateTime Local(DateTimeOffset at) => at.ToLocalTime().DateTime;

    /// <summary>The start of the restaurant's today, and of its tomorrow, as instants.</summary>
    /// <remarks>
    /// Each midnight at its own offset (S-20, F-06 of the 27 Sep review). This
    /// once took today's midnight at the offset in force <i>now</i>, so on the
    /// day the clocks go back (24 October 2026, 02:00 back to 01:00) the
    /// afternoon's dashboard left out the calls from 00:00 to 01:00, and on the
    /// day they go forward it took in the previous evening's last hour. A day
    /// with a clock change is 23 or 25 hours long.
    /// </remarks>
    public static (DateTimeOffset Start, DateTimeOffset End) Today(DateTimeOffset? now = null)
    {
        var date = Local(now ?? DateTimeOffset.Now).Date;
        return (StartOfDay(date), StartOfDay(date.AddDays(1)));
    }

    /// <summary>The instant the restaurant's <paramref name="date"/> begins.</summary>
    /// <remarks>
    /// Hebron's clocks change at 02:00, so its midnight is never skipped or
    /// repeated, but the rule is written for a zone where it is. Clocks going
    /// back over midnight make it happen twice: the day begins at the first,
    /// which has the larger offset. Clocks going forward over it skip it:
    /// <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/> then gives the standard
    /// offset, which lands on the moment the clocks jump, where the day begins.
    /// </remarks>
    public static DateTimeOffset StartOfDay(DateTime date)
    {
        var zone = TimeZoneInfo.Local;
        var midnight = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        var offset = zone.IsAmbiguousTime(midnight)
            ? zone.GetAmbiguousTimeOffsets(midnight).Max()
            : zone.GetUtcOffset(midnight);
        return new DateTimeOffset(midnight, offset);
    }

    public static string Day(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The Monday. Monday is the ISO convention and reads the same in both languages.</summary>
    public static DateTime StartOfWeek(DateTime local)
    {
        var back = ((int)local.DayOfWeek + 6) % 7;
        return local.Date.AddDays(-back);
    }

    /// <summary>
    /// S-07's grouping: <c>yyyy-MM-dd</c> for a day or a week's Monday,
    /// <c>yyyy-MM</c> for a month, <c>HH</c> for an hour of the day summed over
    /// the period. Anything else is a day.
    /// </summary>
    public static string Bucket(DateTime local, string? grouping) => (grouping ?? string.Empty).ToLowerInvariant() switch
    {
        "hour" => local.ToString("HH", CultureInfo.InvariantCulture),
        "week" => Day(StartOfWeek(local)),
        "month" => local.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        _ => Day(local),
    };
}
