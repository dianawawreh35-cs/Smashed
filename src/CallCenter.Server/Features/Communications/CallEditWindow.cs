using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Settings;

namespace CallCenter.Server.Features.Communications;

/// <summary>
/// Whether a user may still change what a call says (A-42).
/// </summary>
/// <remarks>
/// One rule for everything an agent writes onto a call — the classification on
/// an answered call and the note on a missed, rejected or unanswered one. Two copies would
/// drift, and an agent would find one editable the morning after and the other
/// not.
///
/// Supervisors may always. An agent may only their own calls, and only within
/// the window the supervisor set — the default is the day of the call, so what
/// an agent recorded during a shift stops being editable once the shift is over
/// and the reports have been read.
///
/// The window is measured against the call's start, not against when it was
/// written: a call taken at 23:55 and classified at 00:05 belongs to the day it
/// happened.
/// </remarks>
public class CallEditWindow(SettingsService settings)
{
    public enum Refusal
    {
        /// <summary>The call belongs to another agent.</summary>
        NotYours,

        /// <summary>The agent's window to edit their own call has closed.</summary>
        Closed,
    }

    /// <summary>Null when the change is allowed, otherwise why not.</summary>
    public async Task<Refusal?> CheckAsync(
        Communication communication, Guid actingUserId, bool actorIsSupervisor,
        CancellationToken ct = default)
    {
        if (actorIsSupervisor)
        {
            return null;
        }

        if (communication.AgentId != actingUserId)
        {
            return Refusal.NotYours;
        }

        var window = await settings.GetStringAsync("agent.edit_window", "SameDay", ct);

        if (window.Equals("Always", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Local time, not UTC: "the same day" means the agent's day. A shift
        // ending after midnight UTC is still the same evening in Hebron.
        var startedLocal = communication.StartedAt.ToLocalTime().Date;

        return startedLocal == DateTimeOffset.Now.Date
            ? null
            : Refusal.Closed;
    }
}
