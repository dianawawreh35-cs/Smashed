using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// Two switches an agent flips for themselves: do not disturb, and auto answer
/// (A-18).
/// </summary>
/// <remarks>
/// They live here rather than on <see cref="CallService"/> because two very
/// different callers need them: the rail's check boxes set them on the UI
/// thread, and <c>OnIncomingCall</c> reads them on a SIP thread while an INVITE
/// is waiting for an answer. A small object with a lock around it keeps that
/// crossing in one place.
///
/// Remembered per laptop, in the same settings file as the language and the
/// audio devices. A shift that ends with do-not-disturb left on would otherwise
/// look like a phone that has stopped ringing, so the state survives a restart
/// deliberately: it is visible in the rail, and an agent who did not mean to
/// leave it on can see why nothing is coming through.
/// </remarks>
public class PhonePreferences(AgentSettingsStore settings, ILogger<PhonePreferences> logger)
{
    private readonly Lock _gate = new();

    private bool _doNotDisturb = settings.Current.DoNotDisturb;
    private bool _autoAnswer = settings.Current.AutoAnswer;

    /// <summary>Raised after either switch changes, so the rail can redraw.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Turn incoming calls away without ringing. The caller is told this
    /// extension is busy, which is what lets the PBX offer the call to somebody
    /// else instead of leaving it ringing at an empty desk.
    /// </summary>
    public bool DoNotDisturb
    {
        get { lock (_gate) return _doNotDisturb; }
        set
        {
            lock (_gate)
            {
                if (_doNotDisturb == value)
                {
                    return;
                }

                _doNotDisturb = value;
            }

            settings.Update(current => current with { DoNotDisturb = value });
            logger.LogInformation("Do not disturb is now {State}", value ? "on" : "off");
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Answer an incoming call as soon as it arrives, without the agent
    /// pressing Answer. For a headset agent working a busy queue.
    /// </summary>
    /// <remarks>
    /// Ignored while <see cref="DoNotDisturb"/> is on: turning calls away and
    /// picking them up instantly are contradictory instructions, and the one
    /// that says "do not put a caller through to me" has to win — the other way
    /// round would connect a live customer to an agent who has stepped away.
    /// </remarks>
    public bool AutoAnswer
    {
        get { lock (_gate) return _autoAnswer; }
        set
        {
            lock (_gate)
            {
                if (_autoAnswer == value)
                {
                    return;
                }

                _autoAnswer = value;
            }

            settings.Update(current => current with { AutoAnswer = value });
            logger.LogInformation("Auto answer is now {State}", value ? "on" : "off");
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
