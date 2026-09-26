using CallCenter.Shared.Contracts.Pbx;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// What the PBX last said about each extension (S-61). Written by
/// <see cref="Workers.ExtensionWatchWorker"/>, read by the supervisor's
/// screens.
/// </summary>
/// <remarks>
/// <para>
/// <b>In memory, not in the database.</b> It is only ever "now", the PBX sends
/// the whole picture again whenever a subscription is renewed, and after a
/// restart the first renewals refill it within seconds. Nothing is lost that
/// was worth keeping.
/// </para>
/// <para>
/// <b>One state from two reports.</b> The dialog report is trusted for
/// ringing and talking. Presence decides connected or not, because the dialog
/// report says "terminated" for a phone that is switched off as well as for a
/// free one (see <see cref="PbxNotify"/>).
/// </para>
/// <para>
/// <b>Stale is unknown.</b> A report counts only while its subscription keeps
/// being renewed. If the PBX stops answering, every extension it last reported
/// on turns <see cref="PhoneStates.Unknown"/> after <see cref="StaleAfter"/>,
/// rather than showing an agent in a call that ended long ago.
/// </para>
/// </remarks>
public sealed class ExtensionWatch(TimeProvider clock)
{
    /// <summary>
    /// A report older than this, with no renewal since, no longer counts.
    /// Subscriptions are renewed every minute, so this is two renewals missed
    /// and a little over.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2.5);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private string? _problem = Problems.NotStarted;
    private DateTimeOffset? _lastHeard;

    public static class Problems
    {
        public const string NotStarted = "not_started";
        public const string NotConfigured = "not_configured";
        public const string NoAnswer = "no_answer";
    }

    private sealed class Entry
    {
        public bool? Connected;
        public DialogState? Dialog;
        public DateTimeOffset? PresenceAt;
        public DateTimeOffset? DialogAt;
        public string State = PhoneStates.Unknown;
        public DateTimeOffset? Since;
    }

    /// <summary>The PBX answered a subscription: the watch is working.</summary>
    public void Heard()
    {
        lock (_gate)
        {
            _lastHeard = clock.GetUtcNow();
            _problem = null;
        }
    }

    /// <summary>The watch is not working, and why (<see cref="Problems"/>).</summary>
    public void Failing(string problem)
    {
        lock (_gate)
        {
            _problem = problem;
            if (problem == Problems.NotConfigured)
            {
                _entries.Clear();
            }
        }
    }

    /// <summary>A presence report: whether a phone is connected on <paramref name="extension"/>.</summary>
    /// <returns>The new state when this report changed it, otherwise null.</returns>
    public string? Presence(string extension, bool connected) => Update(extension, e =>
    {
        e.Connected = connected;
        e.PresenceAt = clock.GetUtcNow();
    });

    /// <summary>A dialog report: free, ringing or in a call on <paramref name="extension"/>.</summary>
    /// <returns>The new state when this report changed it, otherwise null.</returns>
    public string? Dialog(string extension, DialogState state) => Update(extension, e =>
    {
        e.Dialog = state;
        e.DialogAt = clock.GetUtcNow();
    });

    /// <summary>
    /// A renewal the PBX accepted counts as a fresh report of what it said last,
    /// since it would have sent a NOTIFY had anything changed.
    /// </summary>
    public void Confirmed(string extension, bool presence) => _ = Update(extension, e =>
    {
        if (presence && e.Connected is not null)
        {
            e.PresenceAt = clock.GetUtcNow();
        }
        else if (!presence && e.Dialog is not null)
        {
            e.DialogAt = clock.GetUtcNow();
        }
    });

    /// <summary>Stops tracking extensions that no agent has any more.</summary>
    public void Keep(IReadOnlyCollection<string> extensions)
    {
        lock (_gate)
        {
            foreach (var gone in _entries.Keys.Where(k => !extensions.Contains(k)).ToList())
            {
                _entries.Remove(gone);
            }
        }
    }

    /// <summary>The watch is working: subscribed, and heard from within <see cref="StaleAfter"/>.</summary>
    public bool IsLive
    {
        get
        {
            lock (_gate)
            {
                return _problem is null && _lastHeard is { } heard && clock.GetUtcNow() - heard < StaleAfter;
            }
        }
    }

    /// <summary>Why the watch is not live, or null when it is.</summary>
    public string? Problem
    {
        get
        {
            lock (_gate)
            {
                if (_problem is not null)
                {
                    return _problem;
                }

                return _lastHeard is { } heard && clock.GetUtcNow() - heard < StaleAfter ? null : Problems.NoAnswer;
            }
        }
    }

    /// <summary>The extension's state now, and since when.</summary>
    public (string State, DateTimeOffset? Since) Get(string extension)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(extension, out var e))
            {
                return (PhoneStates.Unknown, null);
            }

            Recompute(e);
            return (e.State, e.Since);
        }
    }

    private string? Update(string extension, Action<Entry> change)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(extension, out var e))
            {
                e = new Entry();
                _entries[extension] = e;
            }

            var before = e.State;
            change(e);
            Recompute(e);
            return e.State != before ? e.State : null;
        }
    }

    /// <summary>Works the state out again, and moves <see cref="Entry.Since"/> only when it changes.</summary>
    private void Recompute(Entry e)
    {
        var now = clock.GetUtcNow();
        bool Fresh(DateTimeOffset? at) => at is { } t && now - t < StaleAfter;

        var dialog = Fresh(e.DialogAt) ? e.Dialog : null;
        var connected = Fresh(e.PresenceAt) ? e.Connected : null;

        var state = dialog switch
        {
            DialogState.InCall => PhoneStates.InCall,
            DialogState.Ringing => PhoneStates.Ringing,
            _ => connected switch
            {
                false => PhoneStates.Offline,
                true => PhoneStates.Free,
                null => PhoneStates.Unknown,
            },
        };

        if (state != e.State)
        {
            e.State = state;
            e.Since = state == PhoneStates.Unknown ? null : now;
        }
    }
}
