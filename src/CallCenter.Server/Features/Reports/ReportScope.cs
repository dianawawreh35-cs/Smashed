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
public record ReportFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? AgentId = null,
    Guid? BranchId = null,
    Guid? ChannelId = null,
    Guid? TypeId = null,
    string? Kind = CommunicationKinds.App,
    bool WithRings = false);

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
        if (!f.WithRings) q = q.Where(c => c.AbandonedCallId == null);

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

        if (f.AgentId is { } agent) q = q.Where(c => c.AgentId == agent);
        if (f.BranchId is { } branch) q = q.Where(c => c.BranchId == branch);
        if (f.ChannelId is { } channel) q = q.Where(c => c.ChannelId == channel);
        if (f.TypeId is { } type) q = q.Where(c => c.Classification != null && c.Classification.TypeId == type);

        // S-48: internal calls are recorded and kept, and left out of the
        // customer-facing reports. A row with no number is a customer's
        // (withheld caller id), so the null has to be let through explicitly:
        // NOT (NULL = ANY(...)) is NULL, which a WHERE drops.
        if (internalNumbers is { Count: > 0 })
        {
            var list = internalNumbers.ToList();
            q = q.Where(c => c.RemoteNormalised == null || !list.Contains(c.RemoteNormalised));
        }

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

    /// <summary>The instant in the restaurant's own time.</summary>
    public static DateTime Local(DateTimeOffset at) => at.ToLocalTime().DateTime;

    /// <summary>The start of the restaurant's today, and of its tomorrow, as instants.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Today(DateTimeOffset? now = null)
    {
        var local = (now ?? DateTimeOffset.Now).ToLocalTime();
        var start = new DateTimeOffset(local.Date, local.Offset);
        // The offset of tomorrow's midnight, not today's: a day with a clock
        // change is 23 or 25 hours long.
        var next = local.Date.AddDays(1);
        return (start, new DateTimeOffset(next, TimeZoneInfo.Local.GetUtcOffset(next)));
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
