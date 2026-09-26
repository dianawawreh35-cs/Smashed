using System.Globalization;
using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared.Contracts.Pbx;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Pbx;

/// <summary>One switch at a time, whoever pressed the button (S-60).</summary>
public sealed class PbxQueueGate
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
}

/// <summary>
/// Opens and closes the call queue from the supervisor's dashboard by dialling
/// <c>*280</c> from the server's extension, and opens it every morning at
/// <c>queue.auto_open_time</c> (S-60).
/// </summary>
/// <remarks>
/// <b><c>*280</c> is a toggle.</b> The same code opens a closed queue and
/// closes an open one, and says which. The server cannot tell the two
/// announcements apart, so it <b>remembers</b> the state instead: whatever the
/// last switch through this screen left it at. Dia chose that over reading the
/// state from the PBX. What it costs: someone dialling <c>*280</c> from a desk
/// phone leaves the screen showing the opposite. So the supervisor can correct
/// the display without a call (<see cref="MarkAsync"/>). Until someone has said
/// which state the queue is in, the switch refuses to dial, because a toggle
/// from an unknown state could go either way.
///
/// <b>Pressing Open on an open queue makes no call.</b> A second supervisor, or
/// a double click, must not close it again.
///
/// <b>The daily opening</b> (<see cref="AutoOpenIfDueAsync"/>) is the same
/// press of Open, made by nobody, once a day. It stands aside for a person:
/// once anyone has switched or corrected the queue after the opening time, that
/// day is theirs. It is never late by more than <see cref="AutoOpenWindow"/>:
/// a server that was off until the afternoon does not open a queue the
/// restaurant may have kept closed. And it retries only a call the PBX never
/// answered, because an answered <c>*280</c> may have switched the queue
/// already, and a second one would close it again.
///
/// Every switch and every correction goes in the audit log (N-06) with who and
/// when; the daily opening has no user. The state lives in the <c>settings</c>
/// table, outside the catalogue.
/// </remarks>
public class PbxQueueSwitch(
    CallCenterDbContext db,
    IPbxFeatureDialer dialer,
    PbxFeatureLine line,
    PbxQueueGate gate,
    TimeProvider clock,
    ILogger<PbxQueueSwitch> logger)
{
    /// <summary>The PBX's feature code that toggles the queue.</summary>
    public const string ToggleCode = "*280";

    /// <summary>The daily opening time, in the settings catalogue (S-47). Blank turns it off.</summary>
    public const string AutoOpenTimeKey = "queue.auto_open_time";

    /// <summary>How long after the opening time it may still happen: a restart at 07:20 still opens.</summary>
    public static readonly TimeSpan AutoOpenWindow = TimeSpan.FromHours(1);

    /// <summary>Between tries of a daily opening the PBX did not answer.</summary>
    public static readonly TimeSpan AutoOpenRetry = TimeSpan.FromMinutes(5);

    public static class Keys
    {
        public const string IsOpen = "pbx.queue.is_open";
        public const string ChangedAt = "pbx.queue.changed_at";

        /// <summary>A user id, or <see cref="Automatic"/>.</summary>
        public const string ChangedBy = "pbx.queue.changed_by";

        /// <summary>The local day the daily opening was dealt with, <c>yyyy-MM-dd</c>.</summary>
        public const string AutoDoneOn = "pbx.queue.auto_done_on";
        public const string AutoTriedAt = "pbx.queue.auto_tried_at";

        /// <summary>Why the daily opening did not happen, on <see cref="AutoProblemOn"/>.</summary>
        public const string AutoProblem = "pbx.queue.auto_problem";
        public const string AutoProblemOn = "pbx.queue.auto_problem_on";

        public const string Automatic = "auto";

        public const string Prefix = "pbx.queue.";
    }

    public static class Codes
    {
        public const string NotConfigured = "not_configured";
        public const string UnknownState = "unknown_state";
        public const string Busy = "busy";
        public const string PbxFailed = "pbx_failed";
    }

    public async Task<QueueStatusDto> StatusAsync(CancellationToken ct = default)
    {
        var stored = await LoadAsync(ct);
        var by = stored.GetValueOrDefault(Keys.ChangedBy);

        var changedBy = Guid.TryParse(by, out var userId)
            ? await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        var problem = stored.GetValueOrDefault(Keys.AutoProblemOn) == Day(Today())
            ? stored.GetValueOrDefault(Keys.AutoProblem)
            : null;

        return new QueueStatusDto(
            bool.TryParse(stored.GetValueOrDefault(Keys.IsOpen), out var open) ? open : null,
            Instant(stored, Keys.ChangedAt),
            changedBy,
            by == Keys.Automatic,
            await line.GetAsync(ct) is not null,
            (await AutoOpenTimeAsync(ct))?.ToString("HH:mm", CultureInfo.InvariantCulture),
            string.IsNullOrEmpty(problem) ? null : problem);
    }

    /// <summary>Opens or closes the queue, dialling <c>*280</c> only when that changes something.</summary>
    public async Task<QueueSwitchResultDto> SwitchAsync(bool open, Guid actingUserId, CancellationToken ct = default) =>
        (await SwitchCoreAsync(open, actingUserId, ct)).Result;

    /// <summary>
    /// Says which state the queue is in, without calling the PBX: the first
    /// time, and whenever the screen and the PBX disagree.
    /// </summary>
    public async Task<QueueStatusDto> MarkAsync(bool open, Guid actingUserId, CancellationToken ct = default)
    {
        await SaveAsync(open, actingUserId, open ? "mark_open" : "mark_closed", ct);
        logger.LogInformation("Queue marked {State} by {UserId}, without a call", open ? "open" : "closed", actingUserId);
        return await StatusAsync(ct);
    }

    /// <summary>
    /// The daily opening: presses Open once the opening time has come, unless
    /// today has already been dealt with. Called by the timer every half
    /// minute; true when it dialled.
    /// </summary>
    public async Task<bool> AutoOpenIfDueAsync(CancellationToken ct = default)
    {
        if (await AutoOpenTimeAsync(ct) is not { } time)
        {
            return false;
        }

        var now = clock.GetUtcNow();
        var local = Local(now);
        var today = DateOnly.FromDateTime(local);
        var due = today.ToDateTime(time);

        var stored = await LoadAsync(ct);
        if (local < due || stored.GetValueOrDefault(Keys.AutoDoneOn) == Day(today))
        {
            return false;
        }

        // A person has had the queue since the opening time: today is theirs.
        if (Instant(stored, Keys.ChangedAt) is { } changed && Local(changed) >= due)
        {
            await DoneAsync(today, null, ct);
            return false;
        }

        if (local > due + AutoOpenWindow)
        {
            await DoneAsync(today, $"The queue was not opened at {time:HH:mm}: the server was not running then.", ct);
            logger.LogWarning("Queue: the {Time} opening was missed; the server started too late", time);
            return false;
        }

        if (Instant(stored, Keys.AutoTriedAt) is { } tried && now - tried < AutoOpenRetry)
        {
            return false;
        }

        await WriteAsync(new() { [Keys.AutoTriedAt] = Stamp(now) }, null, ct);

        var (result, retry) = await SwitchCoreAsync(true, null, ct);

        if (result.Ok)
        {
            await DoneAsync(today, null, ct);
            return true;
        }

        var problem = $"The queue was not opened at {time:HH:mm}: {result.Error}";
        if (retry)
        {
            // Not answered, or someone else was switching it: nothing has
            // changed on the PBX, so it is safe to try again shortly.
            await WriteAsync(new() { [Keys.AutoProblem] = problem, [Keys.AutoProblemOn] = Day(today) }, null, ct);
        }
        else
        {
            await DoneAsync(today, problem, ct);
        }

        logger.LogWarning("Queue: {Problem}{Retry}", problem, retry ? " Trying again shortly." : string.Empty);
        return true;
    }

    /// <summary>The switch itself. <c>Retry</c> is whether a failure left the PBX certainly unchanged.</summary>
    private async Task<(QueueSwitchResultDto Result, bool Retry)> SwitchCoreAsync(
        bool open, Guid? actingUserId, CancellationToken ct)
    {
        if (!await gate.Lock.WaitAsync(0, ct))
        {
            return (await FailAsync(Codes.Busy, "Someone else is switching the queue right now.", ct), true);
        }

        try
        {
            if (await line.GetAsync(ct) is not { } extension)
            {
                return (await FailAsync(Codes.NotConfigured,
                    "The server's PBX extension is not set up: enter it on the PBX blacklist card in Settings.", ct), false);
            }

            var current = (await StatusAsync(ct)).IsOpen;
            if (current is null)
            {
                return (await FailAsync(Codes.UnknownState,
                    "Say whether the queue is open or closed right now before switching it.", ct), false);
            }

            if (current == open)
            {
                return (new QueueSwitchResultDto(true, null, null, await StatusAsync(ct)), false);
            }

            try
            {
                await dialer.ToggleAsync(extension, ToggleCode, ct);
            }
            catch (PbxFeatureException ex)
            {
                // Answered and then gone wrong, the PBX may have switched. The
                // display keeps the old state, and the message tells the
                // supervisor to check and correct it.
                logger.LogWarning("Queue {Action} failed: {Reason}", open ? "open" : "close", ex.Message);
                return (await FailAsync(Codes.PbxFailed, ex.Message, ct), !ex.Answered);
            }

            await SaveAsync(open, actingUserId, open ? "open" : "close", ct);
            logger.LogInformation("Queue {Action} by {By}", open ? "opened" : "closed",
                actingUserId?.ToString() ?? "the daily opening");

            return (new QueueSwitchResultDto(true, null, null, await StatusAsync(ct)), false);
        }
        finally
        {
            gate.Lock.Release();
        }
    }

    private async Task<QueueSwitchResultDto> FailAsync(string code, string error, CancellationToken ct) =>
        new(false, code, error, await StatusAsync(ct));

    private Task DoneAsync(DateOnly today, string? problem, CancellationToken ct) =>
        WriteAsync(new()
        {
            [Keys.AutoDoneOn] = Day(today),
            [Keys.AutoProblem] = problem ?? string.Empty,
            [Keys.AutoProblemOn] = Day(today),
        }, null, ct);

    private async Task SaveAsync(bool open, Guid? actingUserId, string action, CancellationToken ct)
    {
        var stored = await LoadAsync(ct);

        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = actingUserId,
            Entity = "queue",
            Action = action,
            Before = JsonSerializer.SerializeToDocument(new { isOpen = stored.GetValueOrDefault(Keys.IsOpen) }),
            After = JsonSerializer.SerializeToDocument(new { isOpen = open ? "true" : "false" }),
        });

        await WriteAsync(new()
        {
            [Keys.IsOpen] = open ? "true" : "false",
            [Keys.ChangedAt] = Stamp(clock.GetUtcNow()),
            [Keys.ChangedBy] = actingUserId?.ToString() ?? Keys.Automatic,
        }, actingUserId, ct);
    }

    /// <summary>The daily opening time; null when it is blank or unreadable, which turns it off.</summary>
    private async Task<TimeOnly?> AutoOpenTimeAsync(CancellationToken ct)
    {
        var value = await db.Settings.AsNoTracking()
            .Where(s => s.Key == AutoOpenTimeKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return TimeOnly.TryParseExact(value?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith(Keys.Prefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    private async Task WriteAsync(Dictionary<string, string> values, Guid? by, CancellationToken ct)
    {
        var keys = values.Keys.ToList();
        var rows = await db.Settings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, ct);
        var now = clock.GetUtcNow();

        foreach (var (key, value) in values)
        {
            if (!rows.TryGetValue(key, out var row))
            {
                row = new Setting { Key = key };
                db.Settings.Add(row);
            }

            row.Value = value;
            row.UpdatedAt = now;
            if (by is not null) row.UpdatedBy = by;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The restaurant's local time. The server runs in its time zone (Asia/Hebron).</summary>
    private static DateTime Local(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Local).DateTime;

    private DateOnly Today() => DateOnly.FromDateTime(Local(clock.GetUtcNow()));

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Stamp(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Instant(IReadOnlyDictionary<string, string> stored, string key) =>
        DateTimeOffset.TryParse(stored.GetValueOrDefault(key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;
}
