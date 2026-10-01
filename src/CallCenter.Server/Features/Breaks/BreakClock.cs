using CallCenter.Server.Data;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Breaks;

/// <summary>
/// When a break ended, including one the Agent App never ended (A-86), and how
/// much of it falls on a given day.
/// </summary>
/// <remarks>
/// <para>
/// An agent on break whose laptop crashes, or who signs in on another laptop,
/// never presses Break out, and a break left open would count for ever. So a
/// break with no end takes it from the sign-in it was taken under:
/// </para>
/// <list type="bullet">
/// <item>that sign-in ended → the break ended then
/// (<see cref="BreakEndings.SessionEnded"/>);</item>
/// <item>but the app had not been heard from for more than
/// <see cref="SessionPresence.OnlineWindow"/> before it ended, or it is still
/// open and has not been heard from for that long → the break ended when the app
/// was last heard from (<see cref="BreakEndings.NotHeard"/>). A laptop that lost
/// power at two in the afternoon and is signed in to again the next morning is
/// not eighteen hours of break;</item>
/// <item>otherwise it is still going.</item>
/// </list>
/// <para>
/// "Heard from" is <c>agent_sessions.last_seen_at</c>, the same stamp the
/// dashboard's agents online uses (S-20).
/// </para>
/// </remarks>
public static class BreakClock
{
    /// <summary>A break's end: both null while it is still going.</summary>
    public readonly record struct Ending(DateTimeOffset? At, string? By)
    {
        public static readonly Ending Ongoing = new(null, null);
    }

    /// <summary>What a break's end is worked out from: the break and its sign-in.</summary>
    /// <param name="HasSession">False when the break's sign-in row is gone.</param>
    public readonly record struct Facts(
        DateTimeOffset StartedAt,
        DateTimeOffset? EndedAt,
        string? EndedBy,
        bool HasSession,
        DateTimeOffset? LoggedInAt,
        DateTimeOffset? LoggedOutAt,
        DateTimeOffset? LastSeenAt);

    public static Ending EndOf(Facts f, DateTimeOffset now)
    {
        if (f.EndedAt is { } ended)
        {
            return new Ending(ended, f.EndedBy);
        }

        if (!f.HasSession)
        {
            return new Ending(f.StartedAt, BreakEndings.NotHeard);
        }

        // The stamp is written at most once a minute, so a break begun since
        // the last one was heard from at its start, at least.
        var heard = Max(f.LastSeenAt ?? f.LoggedInAt ?? f.StartedAt, f.StartedAt);

        if (f.LoggedOutAt is { } out_)
        {
            return out_ - heard > SessionPresence.OnlineWindow
                ? new Ending(heard, BreakEndings.NotHeard)
                : new Ending(Max(out_, f.StartedAt), BreakEndings.SessionEnded);
        }

        return now - heard > SessionPresence.OnlineWindow
            ? new Ending(heard, BreakEndings.NotHeard)
            : Ending.Ongoing;
    }

    /// <summary>
    /// How much of a break from <paramref name="start"/> to <paramref name="end"/>
    /// falls between <paramref name="from"/> and <paramref name="to"/>, in whole
    /// seconds. A break over midnight counts on each day for its part of it.
    /// </summary>
    public static int SecondsWithin(DateTimeOffset start, DateTimeOffset end, DateTimeOffset from, DateTimeOffset to)
    {
        var a = Max(start, from);
        var b = Min(end, to);
        return b > a ? (int)(b - a).TotalSeconds : 0;
    }

    /// <summary>
    /// Writes the end into every open break taken under <paramref name="sessionIds"/>,
    /// as those sign-ins close (A-86). The caller saves.
    /// </summary>
    /// <remarks>
    /// Not needed for the figures, which work the end out either way; it keeps
    /// the open rows to the breaks that may really still be going. Call it after
    /// the sessions' <c>LoggedOutAt</c> is set on the tracked rows: the sessions
    /// loaded here are those same objects.
    /// </remarks>
    public static async Task CloseForSessionsAsync(
        CallCenterDbContext db, IReadOnlyCollection<Guid> sessionIds, DateTimeOffset now, CancellationToken ct)
    {
        if (sessionIds.Count == 0)
        {
            return;
        }

        var open = await db.AgentBreaks
            .Include(b => b.Session)
            .Where(b => b.EndedAt == null && b.SessionId != null && sessionIds.Contains(b.SessionId.Value))
            .ToListAsync(ct);

        foreach (var row in open)
        {
            var s = row.Session;
            var ending = EndOf(new Facts(
                row.StartedAt, null, null, s is not null, s?.LoggedInAt, s?.LoggedOutAt ?? now, s?.LastSeenAt), now);

            row.EndedAt = ending.At ?? now;
            row.EndedBy = ending.By ?? BreakEndings.SessionEnded;
            row.UpdatedAt = now;
        }
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
