using CallCenter.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Auth;

/// <summary>
/// Whether an agent's session is still alive: when the app on that laptop was
/// last heard from (S-20).
/// </summary>
/// <remarks>
/// <para>
/// An open session alone is not enough. Quitting the Agent App signs out, but a
/// laptop that crashes, loses power or drops off the network never does, and
/// its session stayed "online" for good: 82 of 93 on the development database
/// on 25 Sep.
/// </para>
/// <para>
/// No heartbeat was added to the app. It already asks for the block list every
/// two minutes while signed in (<c>BlockListCache.RefreshInterval</c>), and every
/// request carries the session in its token. So any signed-in request stamps
/// <c>agent_sessions.last_seen_at</c>, and a session is online when it has been
/// heard from within <see cref="OnlineWindow"/>. Laptops already installed need
/// nothing new.
/// </para>
/// </remarks>
public static class SessionPresence
{
    /// <summary>
    /// How recently a session must have been heard from to count as online.
    /// Two and a half block-list refreshes: one missed refresh is not enough to
    /// drop an agent, two are.
    /// </summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The stamp is written at most this often per session, so a burst of
    /// requests while an agent works is one write, not one per request.
    /// </summary>
    public static readonly TimeSpan WriteEvery = TimeSpan.FromMinutes(1);

    /// <summary>The earliest last-seen time that still counts as online at <paramref name="now"/>.</summary>
    public static DateTimeOffset OnlineSince(DateTimeOffset now) => now - OnlineWindow;

    /// <summary>
    /// Runs after authentication: stamps the caller's session, if the token has
    /// one. Supervisors and the web app have no session and are left alone.
    /// </summary>
    public static async Task StampAsync(HttpContext context, Func<Task> next)
    {
        if (context.User.GetSessionId() is { } sessionId)
        {
            var now = DateTimeOffset.UtcNow;
            var due = now - WriteEvery;

            // One conditional update: it touches no row when the session was
            // stamped within the last minute, or has been signed out.
            await context.RequestServices.GetRequiredService<CallCenterDbContext>().AgentSessions
                .Where(s => s.Id == sessionId
                            && s.LoggedOutAt == null
                            && (s.LastSeenAt == null || s.LastSeenAt < due))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), context.RequestAborted);
        }

        await next();
    }
}
