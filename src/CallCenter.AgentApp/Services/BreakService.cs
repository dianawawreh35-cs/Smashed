using CallCenter.AgentApp.Services.Calls;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using CallCenter.Shared.Contracts.Breaks;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// Break in and Break out (A-86): the agent's break, today's total, and the
/// do-not-disturb that goes with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Break in turns do not disturb on, and Break out turns it off</b> (Dia,
/// 1 Oct 2026), so an agent on break is offered no calls (A-18). While the break
/// lasts the rail will not let do not disturb be turned off by hand.
/// </para>
/// <para>
/// <b>The total is the day's, and it adds up</b>: the second break of the day
/// counts on from where the first one stopped. It starts from what the server
/// has for today at sign-in, so signing out and in again keeps it, and goes
/// back to nothing at the laptop's midnight. A break over midnight counts on
/// each day for its own part, as the server counts it.
/// </para>
/// <para>
/// <b>The limit warns, it never stops</b> (Dia, 1 Oct 2026): past
/// <see cref="DailyLimitMinutes"/> the rail says by how much, and Break in
/// still works.
/// </para>
/// <para>
/// <b>Sent straight away, queued when that fails</b> (A-04). A break belongs to
/// no call, so it need not wait in line behind one; only when the server cannot
/// be reached does it go to the buffer, from which the reporter sends it with
/// everything else. It is sent whole every time, so a Break out alone is enough.
/// </para>
/// </remarks>
public class BreakService(
    ApiClient api,
    AgentSession session,
    CallLogQueue queue,
    PhonePreferences preferences,
    AgentSettingsStore settings,
    AgentNotices notices,
    ILogger<BreakService> logger)
{
    private readonly Lock _gate = new();

    /// <summary>The break going, if any.</summary>
    private (Guid Id, DateTimeOffset StartedAt, Guid? SessionId)? _current;

    /// <summary>What the server had for <see cref="_baselineDay"/> at sign-in.</summary>
    private TimeSpan _baseline;

    private DateOnly _baselineDay;

    /// <summary>The breaks this sign-in has finished, for the day's total.</summary>
    private readonly List<(DateTimeOffset Start, DateTimeOffset End)> _finished = [];

    /// <summary>The clock. A property so the tests can move it.</summary>
    internal Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;

    /// <summary>Raised on the thread that changed it, after Break in, Break out and sign-in.</summary>
    public event EventHandler? Changed;

    public bool OnBreak
    {
        get { lock (_gate) return _current is not null; }
    }

    /// <summary>When the break going began; null when there is none.</summary>
    public DateTimeOffset? BreakStartedAt
    {
        get { lock (_gate) return _current?.StartedAt; }
    }

    /// <summary>
    /// The minutes of break the agent has a day, as the server said at sign-in;
    /// null when it could not be asked, and then nothing is warned about.
    /// </summary>
    public int? DailyLimitMinutes { get; private set; }

    /// <summary>The day's break time so far, the break going included.</summary>
    public TimeSpan TodayTotal()
    {
        var now = Now();
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var (start, end) = DayOf(today);

        lock (_gate)
        {
            var total = _baselineDay == today ? _baseline : TimeSpan.Zero;

            foreach (var (s, e) in _finished)
            {
                total += Within(s, e, start, end);
            }

            if (_current is { } current)
            {
                total += Within(current.StartedAt, now, start, end);
            }

            return total;
        }
    }

    /// <summary>How far past the limit the day is; zero within it, or with no limit known.</summary>
    public TimeSpan OverLimit()
    {
        if (DailyLimitMinutes is not { } limit)
        {
            return TimeSpan.Zero;
        }

        var over = TodayTotal() - TimeSpan.FromMinutes(limit);
        return over > TimeSpan.Zero ? over : TimeSpan.Zero;
    }

    /// <summary>
    /// At sign-in: today's total and the limit from the server, and the
    /// do-not-disturb a break left on if the app stopped during one.
    /// </summary>
    /// <remarks>
    /// Signing in ends whatever break the last sign-in left going: the server
    /// ends it with that sign-in (BreakClock), so the agent starts here not on
    /// break.
    /// </remarks>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            _current = null;
            _finished.Clear();
            _baseline = TimeSpan.Zero;
            _baselineDay = DateOnly.FromDateTime(Now().LocalDateTime);
        }

        if (settings.Current.BreakTurnedOnDoNotDisturb)
        {
            logger.LogInformation("The app stopped during a break; do not disturb is turned off again (A-86)");
            preferences.DoNotDisturb = false;
            settings.Update(s => s with { BreakTurnedOnDoNotDisturb = false });
        }

        var today = await api.GetMyBreaksTodayAsync(ct);

        if (today.IsOk && today.Value is { } value)
        {
            lock (_gate)
            {
                _baseline = TimeSpan.FromSeconds(value.Seconds);
            }

            DailyLimitMinutes = value.DailyLimitMinutes;
        }
        else
        {
            // The timer starts from this sign-in's breaks, and nothing is warned
            // about; the breaks themselves are still sent.
            logger.LogWarning("Today's break time could not be fetched ({Code}); counting from zero", today.ErrorCode);
            DailyLimitMinutes = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts a break, and turns do not disturb on (A-86).</summary>
    public async Task BreakInAsync(CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        var started = Now();
        var sessionId = session.SessionId;

        lock (_gate)
        {
            if (_current is not null)
            {
                return;
            }

            _current = (id, started, sessionId);
        }

        settings.Update(s => s with { BreakTurnedOnDoNotDisturb = true });
        preferences.DoNotDisturb = true;

        logger.LogInformation("Break in ({BreakId}); {Total} of break today so far", id, TodayTotal());

        if (OverLimit() > TimeSpan.Zero)
        {
            // The rail says by how much; this is the moment it matters.
            notices.Post("breaks.overLimitNotice");
        }

        Changed?.Invoke(this, EventArgs.Empty);

        await SendAsync(id, new SaveBreakRequest(sessionId, started, null, null), ct);
    }

    /// <summary>Ends the break, and turns do not disturb off (A-86).</summary>
    public Task BreakOutAsync(CancellationToken ct = default) => EndAsync(BreakEndings.BreakOut, ct);

    /// <summary>
    /// Ends the break because the agent is signing out or closing the app.
    /// Called before the sign-in is closed, while the server still takes the
    /// token; the server would otherwise end it with the sign-in.
    /// </summary>
    public Task EndForSignOutAsync(CancellationToken ct = default) => EndAsync(BreakEndings.SignedOut, ct);

    private async Task EndAsync(string endedBy, CancellationToken ct)
    {
        (Guid Id, DateTimeOffset StartedAt, Guid? SessionId) ended;
        var now = Now();

        lock (_gate)
        {
            if (_current is not { } current)
            {
                return;
            }

            ended = current;
            _current = null;
            _finished.Add((current.StartedAt, now));
        }

        preferences.DoNotDisturb = false;
        settings.Update(s => s with { BreakTurnedOnDoNotDisturb = false });

        logger.LogInformation(
            "Break out ({BreakId}, {EndedBy}) after {Length}; {Total} of break today",
            ended.Id, endedBy, now - ended.StartedAt, TodayTotal());

        Changed?.Invoke(this, EventArgs.Empty);

        await SendAsync(ended.Id, new SaveBreakRequest(ended.SessionId, ended.StartedAt, now, endedBy), ct);
    }

    /// <summary>
    /// To the server, or to the buffer if it cannot be reached. Never throws:
    /// the break has happened on this laptop whatever the server says.
    /// </summary>
    private async Task SendAsync(Guid id, SaveBreakRequest request, CancellationToken ct)
    {
        ApiClient.Result<BreakDto> sent;

        try
        {
            sent = await api.SaveBreakAsync(id, request, ct);
        }
        catch (OperationCanceledException)
        {
            // Closing the app gives the sign-out a few seconds (M-A01); a
            // break still on its way when they run out waits in the buffer.
            sent = ApiClient.Result<BreakDto>.Failed(ApiClient.ApiStatus.Unreachable, LoginErrorCodes.ServerUnreachable);
        }

        if (sent.IsOk)
        {
            return;
        }

        if (sent.Status == ApiClient.ApiStatus.Unauthorized)
        {
            // The sign-in has ended (N-05), and the server ended the break with
            // it. Nothing to send.
            logger.LogInformation("Break {BreakId} not sent: the sign-in has ended, and the break with it", id);
            return;
        }

        if (sent.Status == ApiClient.ApiStatus.ServerError && sent.ErrorCode is
            "not_your_break" or "break_ends_before_start" or "bad_break_ending" or "break_too_old")
        {
            logger.LogError("The server refused break {BreakId} ({Code}); it is not sent again", id, sent.ErrorCode);
            return;
        }

        if (await queue.EnqueueAsync(new PendingBreak(id, request), CancellationToken.None))
        {
            logger.LogInformation("Break {BreakId} queued until the server can be reached ({Code})", id, sent.ErrorCode);
            return;
        }

        logger.LogError("Break {BreakId} could not be sent or queued ({Code})", id, sent.ErrorCode);
        notices.Post("breaks.notKept");
    }

    /// <summary>The laptop's day, as instants. Its midnight at its own offset.</summary>
    private static (DateTimeOffset Start, DateTimeOffset End) DayOf(DateOnly day)
    {
        static DateTimeOffset Midnight(DateOnly d)
        {
            var local = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }

        return (Midnight(day), Midnight(day.AddDays(1)));
    }

    private static TimeSpan Within(DateTimeOffset s, DateTimeOffset e, DateTimeOffset from, DateTimeOffset to)
    {
        var a = s > from ? s : from;
        var b = e < to ? e : to;
        return b > a ? b - a : TimeSpan.Zero;
    }
}
