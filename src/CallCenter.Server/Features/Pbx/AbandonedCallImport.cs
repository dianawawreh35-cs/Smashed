using System.Globalization;
using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Server.Features.Communications;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Pbx;
using CallCenter.Shared.Phone;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Pbx;

/// <summary>One import at a time, whether the timer or the supervisor's button started it (S-55).</summary>
public sealed class AbandonedImportGate
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
}

/// <summary>
/// The abandoned calls (S-55): downloaded from the PBX's Calls Detail report,
/// saved once each, and joined to the Agent App's rings of the same call.
/// </summary>
/// <remarks>
/// <b>Whole days, downloaded again.</b> The report filters by date only. Each
/// scheduled check downloads from the day of the last successful one to today,
/// so a call at 23:59 is still picked up by a check just after midnight, and a
/// server that was off for two days catches up on its own. Downloading a day
/// twice is harmless: each call's key is unique (<see cref="AbandonedRow.Key"/>),
/// and a key already saved is skipped.
///
/// <b>The rings.</b> The queue rings an agent again and again while the caller
/// waits, and the Agent App records every ring it did not take as Missed,
/// Rejected or Blocked. A ring from the same number between joining the queue
/// and hanging up was a ring of that call, and is pointed at it
/// (<c>abandoned_call_id</c>), so the reports count the customer once. Rings
/// are joined on every check, not only when the call is new: a laptop that was
/// offline sends its rings late (A-14).
///
/// <b>A blocked caller is not an abandoned one.</b> When the laptops refused
/// every ring because the number is on the block list, the caller waited until
/// they gave up; the call is saved as Blocked, and never goes on the call-back
/// list.
///
/// <b>Settings live in the <c>settings</c> table, outside the catalogue</b>
/// (<see cref="Settings.SettingsCatalog"/>), so the generic settings screen can
/// neither show nor overwrite them. The password is encrypted with the key the
/// SIP secrets use (N-05).
/// </remarks>
public class AbandonedCallImport(
    CallCenterDbContext db,
    IPbxCallsDetailSource pbx,
    ISipSecretProtector protector,
    CommunicationsService communications,
    AbandonedImportGate gate,
    TimeProvider clock,
    ILogger<AbandonedCallImport> logger)
{
    public const int DefaultIntervalMinutes = 1;
    public const int MinIntervalMinutes = 1;
    public const int MaxIntervalMinutes = 60;

    /// <summary>How far back the very first check reaches.</summary>
    public const int FirstCheckDays = 30;

    /// <summary>The longest period one download may cover.</summary>
    public const int MaxDays = 366;

    /// <summary>
    /// How far a ring may start before the PBX's queue time and still be the
    /// same call: the laptops' clocks and the PBX's differ by a second or so
    /// (26 Sep: the Agent App's ring at 00:36:06 for a call queued at 00:36:07).
    /// </summary>
    public static readonly TimeSpan RingLead = TimeSpan.FromSeconds(5);

    /// <summary>How long after the hang-up a ring may still start.</summary>
    public static readonly TimeSpan RingTail = TimeSpan.FromSeconds(2);

    public static class Keys
    {
        public const string Url = "pbx.calls.url";
        public const string Username = "pbx.calls.username";
        public const string Password = "pbx.calls.password";
        public const string IntervalMinutes = "pbx.calls.interval_minutes";
        public const string LastCheckedAt = "pbx.calls.last_checked_at";
        public const string LastSucceededAt = "pbx.calls.last_succeeded_at";
        public const string LastError = "pbx.calls.last_error";
        public const string LastAdded = "pbx.calls.last_added";
        public const string SyncedThrough = "pbx.calls.synced_through";

        public const string Prefix = "pbx.calls.";
    }

    // ---- settings ---------------------------------------------------------------

    public async Task<AbandonedImportDto> StatusAsync(CancellationToken ct = default) =>
        ToDto(await LoadAsync(ct));

    /// <summary>Saves the address, login and interval.</summary>
    /// <returns>The problems found, by field; empty when saved.</returns>
    public async Task<IReadOnlyDictionary<string, string>> SaveAsync(
        UpdateAbandonedImportRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var problems = new Dictionary<string, string>();

        var url = (request.Url ?? string.Empty).Trim();
        if (url.Length > 0)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                // The web interface's root; a pasted link to a page inside it
                // would otherwise become part of every request.
                url = uri.GetLeftPart(UriPartial.Authority);
            }
            else
            {
                problems["url"] = "must be the PBX's web address, such as https://10.8.0.1";
            }
        }

        if (request.IntervalMinutes is < MinIntervalMinutes or > MaxIntervalMinutes)
        {
            problems["intervalMinutes"] = $"must be a whole number between {MinIntervalMinutes} and {MaxIntervalMinutes}";
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        var stored = await LoadAsync(ct);
        var username = (request.Username ?? string.Empty).Trim();
        var values = new Dictionary<string, string>
        {
            [Keys.Url] = url,
            [Keys.Username] = username,
            [Keys.IntervalMinutes] = request.IntervalMinutes.ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrEmpty(request.Password))
        {
            values[Keys.Password] = protector.Protect(request.Password)!;
        }

        var changed = values
            .Where(v => stored.GetValueOrDefault(v.Key) != v.Value)
            .Select(v => v.Key)
            .ToList();

        if (changed.Count == 0)
        {
            return problems;
        }

        // Never the password, in either direction: only that it changed.
        string? Shown(string key, string? value) => key == Keys.Password ? (value is null ? null : "(set)") : value;

        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = actingUserId,
            Entity = "settings",
            Action = "update",
            Before = JsonSerializer.SerializeToDocument(changed.ToDictionary(k => k, k => Shown(k, stored.GetValueOrDefault(k)))),
            After = JsonSerializer.SerializeToDocument(changed.ToDictionary(k => k, k => Shown(k, values[k]))),
        });

        await WriteAsync(changed.ToDictionary(k => k, k => values[k]), actingUserId, ct);
        logger.LogInformation("Abandoned-call import settings changed: {Keys}", string.Join(", ", changed));

        return problems;
    }

    // ---- checks -----------------------------------------------------------------

    /// <summary>
    /// The timer's check: runs when the import is set up and the interval has
    /// passed since the last check, from the day of the last successful one to
    /// today. Null when it did not run.
    /// </summary>
    public async Task<AbandonedFetchResultDto?> RunIfDueAsync(CancellationToken ct = default)
    {
        var stored = await LoadAsync(ct);
        if (Connection(stored) is null)
        {
            return null;
        }

        var interval = TimeSpan.FromMinutes(Interval(stored));
        var now = clock.GetUtcNow();

        // A few seconds' grace, so a timer ticking a moment early does not
        // skip a whole interval.
        if (Instant(stored, Keys.LastCheckedAt) is { } last && now - last < interval - TimeSpan.FromSeconds(10))
        {
            return null;
        }

        var today = Today();
        var from = DateOnly.TryParseExact(stored.GetValueOrDefault(Keys.SyncedThrough), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var synced)
            ? synced
            : today.AddDays(-FirstCheckDays);

        if (from < today.AddDays(-MaxDays + 1)) from = today.AddDays(-MaxDays + 1);
        if (from > today) from = today;

        return await RunAsync(from, today, scheduled: true, ct);
    }

    /// <summary>
    /// The supervisor's Fetch button: downloads the chosen days now, whatever
    /// the timer has done. Local days, both included.
    /// </summary>
    public async Task<AbandonedFetchResultDto> FetchAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var today = Today();
        if (to > today) to = today;
        if (from > to) from = to;
        if (to.DayNumber - from.DayNumber >= MaxDays) from = to.AddDays(-MaxDays + 1);

        return await RunAsync(from, to, scheduled: false, ct);
    }

    private async Task<AbandonedFetchResultDto> RunAsync(DateOnly from, DateOnly to, bool scheduled, CancellationToken ct)
    {
        await gate.Lock.WaitAsync(ct);
        try
        {
            var stored = await LoadAsync(ct);
            if (Connection(stored) is not { } connection)
            {
                return Result(false, "The PBX import is not set up: enter the PBX's address, username and password in Settings.",
                    from, to, 0, 0, 0, 0, stored);
            }

            var checkedAt = clock.GetUtcNow();
            var today = Today();
            var state = new Dictionary<string, string>
            {
                [Keys.LastCheckedAt] = checkedAt.ToString("O", CultureInfo.InvariantCulture),
            };

            try
            {
                var csv = await pbx.DownloadCsvAsync(connection, from, to, ct);
                var parsed = CallsDetailCsv.Parse(csv);

                var (added, linked) = await ApplyAsync(parsed, from, to, ct);

                state[Keys.LastSucceededAt] = state[Keys.LastCheckedAt];
                state[Keys.LastError] = string.Empty;
                state[Keys.LastAdded] = added.ToString(CultureInfo.InvariantCulture);

                // Everything before today has now been downloaded after it
                // ended - if this check started at or before the old mark. A
                // manual fetch before the first scheduled check sets no mark,
                // so the first check still reaches back FirstCheckDays.
                var synced = stored.GetValueOrDefault(Keys.SyncedThrough);
                var mark = DateOnly.TryParseExact(synced, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
                    ? m
                    : (DateOnly?)null;
                if (scheduled || (to >= today && mark is not null && from <= mark))
                {
                    state[Keys.SyncedThrough] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }

                await WriteAsync(state, null, ct);

                if (added > 0 || linked > 0 || !scheduled)
                {
                    logger.LogInformation(
                        "PBX abandoned calls {From} to {To}: {Calls} calls, {Abandoned} abandoned, {Added} new, {Linked} rings joined",
                        from, to, parsed.Calls, parsed.Abandoned.Count, added, linked);
                }

                return Result(true, null, from, to, parsed.Calls, parsed.Abandoned.Count, added, linked, await LoadAsync(ct));
            }
            catch (PbxImportException ex)
            {
                // Not the server's failure, and not worth a stack trace every
                // minute: the settings screen shows the message.
                logger.LogWarning("PBX abandoned-call check failed: {Reason}", ex.Message);

                state[Keys.LastError] = ex.Message;
                await WriteAsync(state, null, ct);

                return Result(false, ex.Message, from, to, 0, 0, 0, 0, await LoadAsync(ct));
            }
        }
        finally
        {
            gate.Lock.Release();
        }
    }

    /// <summary>
    /// What a download changes: the abandoned calls not saved before, then the
    /// rings of every abandoned call in the period joined to it.
    /// </summary>
    public async Task<(int Added, int RingsLinked)> ApplyAsync(
        CallsDetailCsv.Parsed parsed, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var added = await AddAsync(parsed.Abandoned, ct);
        var linked = await LinkRingsAsync(from, to, ct);
        return (added, linked);
    }

    /// <summary>Saves the abandoned calls not saved before. Returns how many.</summary>
    private async Task<int> AddAsync(IReadOnlyList<AbandonedRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var keys = rows.Select(r => r.Key).Distinct().ToList();
        var seen = (await db.Communications
                .Where(c => c.PbxUniqueId != null && keys.Contains(c.PbxUniqueId))
                .Select(c => c.PbxUniqueId!)
                .ToListAsync(ct))
            .ToHashSet();

        var fresh = rows.Where(r => seen.Add(r.Key)).ToList();
        if (fresh.Count == 0)
        {
            return 0;
        }

        var channelId = await db.Channels
            .Where(c => c.Name == ChannelNames.Phone)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(ct);

        if (channelId == Guid.Empty)
        {
            throw new InvalidOperationException("No Phone channel exists; the database has not been seeded.");
        }

        var now = clock.GetUtcNow();
        foreach (var row in fresh)
        {
            var normalised = PhoneNormalizer.Normalize(row.Phone);
            db.Communications.Add(new Communication
            {
                Kind = CommunicationKinds.Call,
                ChannelId = channelId,
                Direction = Directions.In,
                Status = CommunicationStatuses.Abandoned,
                RemoteNumberRaw = row.Phone.Length == 0 ? null : row.Phone,
                RemoteNormalised = normalised.Length == 0 ? null : normalised,
                // The time the customer joined the queue, as the Agent App's
                // ring start is: both land in the same hour of the reports.
                StartedAt = Utc(row.QueuedAt),
                EndedAt = Utc(row.EndedAt),
                WaitSec = row.WaitSec,
                QueueName = row.Queue.Length == 0 ? null : row.Queue,
                PbxUniqueId = row.Key,
                Source = CommunicationSources.Cdr,
                ContactId = await communications.MatchContactAsync(row.Phone, ct),
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        return fresh.Count;
    }

    /// <summary>
    /// Points the Agent App's untaken rings at the abandoned call they were
    /// part of, and saves a call every ring of which was refused as Blocked.
    /// Returns how many rings were joined.
    /// </summary>
    private async Task<int> LinkRingsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        // An hour either side: a call queued before midnight and abandoned after.
        var start = Utc(from.ToDateTime(TimeOnly.MinValue)).AddHours(-1);
        var end = Utc(to.AddDays(1).ToDateTime(TimeOnly.MinValue)).AddHours(1);

        var calls = await db.Communications.AsNoTracking()
            .Where(c => c.Source == CommunicationSources.Cdr
                && c.PbxUniqueId != null && c.PbxUniqueId.StartsWith("issabel:")
                && c.RemoteNormalised != null
                && c.StartedAt >= start && c.StartedAt < end)
            .Select(c => new { c.Id, c.Status, Number = c.RemoteNormalised!, c.StartedAt, EndedAt = c.EndedAt ?? c.StartedAt })
            .ToListAsync(ct);

        if (calls.Count == 0)
        {
            return 0;
        }

        var numbers = calls.Select(c => c.Number).Distinct().ToList();
        var untaken = new[] { CommunicationStatuses.Missed, CommunicationStatuses.Rejected, CommunicationStatuses.Blocked };

        var rings = await db.Communications
            .Where(r => r.Source == CommunicationSources.AgentApp
                && r.Kind == CommunicationKinds.Call
                && r.Direction == Directions.In
                && untaken.Contains(r.Status)
                && r.AbandonedCallId == null
                && r.RemoteNormalised != null && numbers.Contains(r.RemoteNormalised)
                && r.StartedAt >= start && r.StartedAt < end)
            .ToListAsync(ct);

        var byNumber = calls.ToLookup(c => c.Number);
        var linked = 0;
        foreach (var ring in rings)
        {
            // The earliest call from the number whose wait the ring falls in.
            var call = byNumber[ring.RemoteNormalised!]
                .Where(c => ring.StartedAt >= c.StartedAt - RingLead && ring.StartedAt <= c.EndedAt + RingTail)
                .OrderBy(c => c.EndedAt)
                .FirstOrDefault();

            if (call is not null)
            {
                ring.AbandonedCallId = call.Id;
                linked++;
            }
        }

        if (linked > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        // Every call here with a Blocked ring is a blocked caller's (S-55).
        var ids = calls.Where(c => c.Status == CommunicationStatuses.Abandoned).Select(c => c.Id).ToList();
        await db.Communications
            .Where(c => ids.Contains(c.Id)
                && db.Communications.Any(r => r.AbandonedCallId == c.Id && r.Status == CommunicationStatuses.Blocked))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CommunicationStatuses.Blocked), ct);

        return linked;
    }

    // ---- storage ----------------------------------------------------------------

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith(Keys.Prefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    private async Task WriteAsync(IReadOnlyDictionary<string, string> values, Guid? by, CancellationToken ct)
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

    private PbxConnection? Connection(IReadOnlyDictionary<string, string> stored)
    {
        var url = stored.GetValueOrDefault(Keys.Url);
        var username = stored.GetValueOrDefault(Keys.Username);
        var password = protector.Unprotect(stored.GetValueOrDefault(Keys.Password));

        return !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && !string.IsNullOrWhiteSpace(username) && !string.IsNullOrEmpty(password)
            ? new PbxConnection(uri, username, password)
            : null;
    }

    private static int Interval(IReadOnlyDictionary<string, string> stored) =>
        int.TryParse(stored.GetValueOrDefault(Keys.IntervalMinutes), out var minutes)
            ? Math.Clamp(minutes, MinIntervalMinutes, MaxIntervalMinutes)
            : DefaultIntervalMinutes;

    private static DateTimeOffset? Instant(IReadOnlyDictionary<string, string> stored, string key) =>
        DateTimeOffset.TryParse(stored.GetValueOrDefault(key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;

    private AbandonedImportDto ToDto(IReadOnlyDictionary<string, string> stored)
    {
        var error = stored.GetValueOrDefault(Keys.LastError);
        return new AbandonedImportDto(
            stored.GetValueOrDefault(Keys.Url) ?? string.Empty,
            stored.GetValueOrDefault(Keys.Username) ?? string.Empty,
            !string.IsNullOrEmpty(stored.GetValueOrDefault(Keys.Password)),
            Interval(stored),
            Connection(stored) is not null,
            Instant(stored, Keys.LastCheckedAt),
            Instant(stored, Keys.LastSucceededAt),
            string.IsNullOrEmpty(error) ? null : error,
            int.TryParse(stored.GetValueOrDefault(Keys.LastAdded), out var added) ? added : null,
            stored.GetValueOrDefault(Keys.SyncedThrough));
    }

    private AbandonedFetchResultDto Result(bool ok, string? error, DateOnly from, DateOnly to,
        int calls, int abandoned, int added, int linked, IReadOnlyDictionary<string, string> stored) =>
        new(ok, error, Day(from), Day(to), calls, abandoned, added, linked, ToDto(stored));

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The restaurant's today. The PBX's clock is the restaurant's too (26 Sep: its times match the Agent App's to the second).</summary>
    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.Local).DateTime);

    /// <summary>A PBX local time as an instant, in UTC for PostgreSQL.</summary>
    public static DateTimeOffset Utc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified)).ToUniversalTime();
    }
}
