using System.IO;
using System.Text.Json;
using CallCenter.AgentApp.Data;
using CallCenter.Shared.Contracts.Classifications;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// Calls waiting to reach the server, held in the offline buffer (A-04, A-14).
/// </summary>
/// <remarks>
/// A-04 requires the app to keep working when the server is unreachable, and a
/// call log that quietly loses a shift's calls whenever the network blinks would
/// be worse than no call log — the supervisor's reports would be wrong without
/// anybody knowing they were wrong.
///
/// So every call is written to the buffer first and removed only once the server
/// has acknowledged it. The queue survives a crash, a reboot and a flat battery.
///
/// Resending is safe because the server keys a call on its SIP Call-ID and
/// extension: a call sent twice updates rather than duplicates, so this never
/// has to work out what already arrived.
///
/// <b>Every row belongs to the agent who made it (F-03)</b>, and is only read
/// back for them. <b>A row the server keeps refusing is set aside (F-08)</b>,
/// not deleted: it stays in the file, off the queue, until the agent asks for
/// it to be tried again.
/// </remarks>
public class CallLogQueue(
    IDbContextFactory<AgentBufferDbContext> buffer,
    AgentSession session,
    ILogger<CallLogQueue> logger)
{
    /// <summary>
    /// How many queued calls are kept. A laptop offline for a week should not
    /// fill its disk; the oldest go first, because a recent call is the one
    /// somebody is still asking about.
    /// </summary>
    public const int MaxQueued = 5000;

    /// <summary>The file the queue used before the buffer existed.</summary>
    private static readonly string LegacyPath =
        Path.Combine(App.AppDataDirectory, "pending-calls.jsonl");

    /// <summary>
    /// The old queue file to import, or null for none. A property so the tests
    /// can point it away from the real one, which the import deletes.
    /// </summary>
    internal string? LegacyFile { get; init; } = LegacyPath;

    /// <summary>
    /// Creates the buffer if it is not there, brings an old one up to date, and
    /// moves across anything left in the old file.
    /// </summary>
    /// <remarks>
    /// The import matters on exactly one day: the first run after the update, on
    /// a laptop that had calls waiting. Skipping it would lose a shift's work
    /// silently, which is the failure this whole queue exists to prevent.
    /// </remarks>
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(App.AppDataDirectory);

            await using var db = await buffer.CreateDbContextAsync(ct);
            await db.Database.EnsureCreatedAsync(ct);

            // F-03, F-08: the owner and set-aside columns, on a file made
            // before they existed.
            await db.UpgradeAsync(ct);

            await ImportLegacyFileAsync(db, ct);
        }
        catch (Exception ex)
        {
            // The app must still start. Calls will fail to queue and say so,
            // which is visible, rather than the app refusing to run.
            logger.LogError(ex, "The offline buffer at {Path} could not be prepared",
                AgentBufferDbContext.DatabasePath);
        }
    }

    /// <summary>
    /// Adds a classification to the queue (A-40). False if it could not be
    /// written (M-A03).
    /// </summary>
    /// <remarks>
    /// Replaces whatever was queued for the same call rather than adding a
    /// second row: an agent who corrects the order value three times during one
    /// call means three edits to one classification, not three classifications.
    /// The replacement keeps its place in the sequence, so it still arrives
    /// after the call it belongs to.
    /// </remarks>
    public Task<bool> EnqueueAsync(
        SaveClassificationByCallRequest classification, CancellationToken ct = default) =>
        UpsertAsync(
            PendingUploadKinds.Classification,
            $"{classification.SipCallId}|{classification.Extension}",
            JsonSerializer.Serialize(classification),
            "A classification",
            ct);

    /// <summary>
    /// Adds a call's note to the queue (A-41). False if it could not be written.
    /// </summary>
    /// <remarks>
    /// Replaces a note already queued for the same call, as a classification
    /// does: a corrected note is one note, not two.
    /// </remarks>
    public Task<bool> EnqueueAsync(SaveCallNotesByCallRequest notes, CancellationToken ct = default) =>
        UpsertAsync(
            PendingUploadKinds.Notes,
            $"{notes.SipCallId}|{notes.Extension}",
            JsonSerializer.Serialize(notes),
            "A call note",
            ct);

    /// <summary>
    /// Adds a finished recording to the queue (A-31). False if it could not be
    /// written.
    /// </summary>
    /// <remarks>
    /// The path, not the audio. Re-recording the same call replaces the entry
    /// rather than adding a second, which matters when the offline queue has
    /// been sitting for a while.
    /// </remarks>
    public Task<bool> EnqueueAsync(PendingRecording recording, CancellationToken ct = default) =>
        UpsertAsync(
            PendingUploadKinds.Recording,
            $"{recording.SipCallId}|{recording.Extension}",
            JsonSerializer.Serialize(recording),
            "A recording",
            ct);

    /// <summary>
    /// Adds a call to the queue. Called before the send is attempted. False if
    /// it could not be written, which the caller must not treat as queued
    /// (M-A03).
    /// </summary>
    public async Task<bool> EnqueueAsync(LogCallRequest call, CancellationToken ct = default)
    {
        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);

            db.PendingUploads.Add(new PendingUpload
            {
                Kind = PendingUploadKinds.Call,
                Payload = JsonSerializer.Serialize(call),
                Reference = Reference(call),
                CreatedAt = DateTimeOffset.Now,
                UserId = session.User?.Id,
            });

            await db.SaveChangesAsync(ct);
            await TrimAsync(db, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A call could not be queued to the offline buffer");
            return false;
        }
    }

    /// <summary>
    /// Writes one row, replacing an earlier one of the same kind for the same
    /// call. A replaced row goes back on the queue even if it had been set
    /// aside: it is new work, and may be the agent correcting what was refused.
    /// </summary>
    private async Task<bool> UpsertAsync(
        string kind, string reference, string payload, string what, CancellationToken ct)
    {
        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);

            var existing = await db.PendingUploads
                .Where(u => u.Kind == kind && u.Reference == reference)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                existing.Payload = payload;
                existing.Attempts = 0;
                existing.LastError = null;
                existing.SetAsideAt = null;
                existing.UserId = session.User?.Id ?? existing.UserId;
            }
            else
            {
                db.PendingUploads.Add(new PendingUpload
                {
                    Kind = kind,
                    Payload = payload,
                    Reference = reference,
                    CreatedAt = DateTimeOffset.Now,
                    UserId = session.User?.Id,
                });
            }

            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{What} could not be queued to the offline buffer", what);
            return false;
        }
    }

    /// <summary>
    /// Everything waiting for this agent, of every kind, oldest first (F-03).
    /// Rows set aside are left out, and so are other agents' rows.
    /// </summary>
    /// <remarks>
    /// One list rather than one per kind, because the order across kinds is the
    /// whole point: a classification replayed before its call would be refused
    /// by a server that has never heard of the call.
    ///
    /// A row that cannot be read is set aside here, not skipped: skipped, it was
    /// read and logged again on every pass, for ever.
    /// </remarks>
    public async Task<IReadOnlyList<PendingItem>> PendingItemsAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);

            var rows = await db.PendingUploads
                .Where(u => u.SetAsideAt == null && u.UserId == userId)
                .OrderBy(u => u.Id)
                .ToListAsync(ct);

            var items = new List<PendingItem>();
            var unreadable = new List<long>();

            foreach (var row in rows)
            {
                try
                {
                    var item = Read(row);

                    if (item is null)
                    {
                        unreadable.Add(row.Id);
                    }
                    else
                    {
                        items.Add(item);
                    }
                }
                catch (JsonException)
                {
                    unreadable.Add(row.Id);
                }
            }

            if (unreadable.Count > 0)
            {
                logger.LogError(
                    "{Count} queued item(s) could not be read and are set aside: {Ids}",
                    unreadable.Count, string.Join(", ", unreadable));

                await SetAsideAsync(unreadable.Select(id => (id, "unreadable")), ct);
            }

            return items;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The offline buffer could not be read");
            return [];
        }
    }

    private static PendingItem? Read(PendingUpload row)
    {
        var item = new PendingItem(row.Id, null, null)
        {
            Reference = row.Reference,
            Attempts = row.Attempts,
            CreatedAt = row.CreatedAt,
        };

        return row.Kind switch
        {
            PendingUploadKinds.Call =>
                JsonSerializer.Deserialize<LogCallRequest>(row.Payload) is { } call
                    ? item with { Call = call }
                    : null,
            PendingUploadKinds.Classification =>
                JsonSerializer.Deserialize<SaveClassificationByCallRequest>(row.Payload) is { } classification
                    ? item with { Classification = classification }
                    : null,
            PendingUploadKinds.Notes =>
                JsonSerializer.Deserialize<SaveCallNotesByCallRequest>(row.Payload) is { } notes
                    ? item with { Notes = notes }
                    : null,
            PendingUploadKinds.Recording =>
                JsonSerializer.Deserialize<PendingRecording>(row.Payload) is { } recording
                    ? item with { Recording = recording }
                    : null,
            _ => null,
        };
    }

    /// <summary>One queued thing: exactly one of the four is set.</summary>
    public record PendingItem(
        long Id,
        LogCallRequest? Call,
        SaveClassificationByCallRequest? Classification,
        SaveCallNotesByCallRequest? Notes = null,
        PendingRecording? Recording = null)
    {
        /// <summary>The call it belongs to, as <c>SipCallId|Extension</c>.</summary>
        public string? Reference { get; init; }

        /// <summary>Failed attempts that counted towards setting it aside (F-08).</summary>
        public int Attempts { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
    }

    /// <summary>
    /// Gives this agent every row that has no owner: the ones queued before
    /// the owner was recorded (F-03). Returns how many.
    /// </summary>
    /// <remarks>
    /// They are sent as the next agent to sign in, once, which is what happened
    /// to them before (Dia's call, 27 Sep): nobody can say now whose they were,
    /// and holding them for a supervisor would leave them on a laptop nobody
    /// looks into. The log says how many, and as whom. After this nothing is
    /// ever unowned again.
    /// </remarks>
    public async Task<int> ClaimUnownedAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);

            return await db.PendingUploads
                .Where(u => u.UserId == null)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.UserId, userId), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unowned queued items could not be given an owner");
            return 0;
        }
    }

    /// <summary>Removes rows the server has accepted.</summary>
    public async Task AcknowledgeAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        var done = ids.ToList();
        if (done.Count == 0)
        {
            return;
        }

        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);

            await db.PendingUploads
                .Where(u => done.Contains(u.Id))
                .ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            // The calls stay queued and are retried, which is the safe failure:
            // a duplicate send is harmless, a lost call is not.
            logger.LogWarning(ex, "Sent calls could not be removed from the offline buffer");
        }
    }

    /// <summary>
    /// Records that an attempt failed. <paramref name="counts"/> says whether
    /// it counts towards setting the row aside (F-08): a server that could not
    /// be reached says nothing about the row, and must not wear it out.
    /// </summary>
    public async Task RecordFailureAsync(long id, string? error, bool counts, CancellationToken ct = default)
    {
        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);

            var step = counts ? 1 : 0;

            await db.PendingUploads
                .Where(u => u.Id == id)
                .ExecuteUpdateAsync(
                    u => u.SetProperty(x => x.Attempts, x => x.Attempts + step)
                          .SetProperty(x => x.LastError, error),
                    ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "A failed attempt could not be recorded");
        }
    }

    /// <summary>
    /// Takes rows off the queue as hopeless (F-08), each with why. They stay in
    /// the file, and in the count the call log shows.
    /// </summary>
    public async Task SetAsideAsync(IEnumerable<(long Id, string Reason)> items, CancellationToken ct = default)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);
            var ids = list.Select(i => i.Id).ToList();
            var rows = await db.PendingUploads.Where(u => ids.Contains(u.Id)).ToListAsync(ct);
            var now = DateTimeOffset.Now;

            foreach (var row in rows)
            {
                row.SetAsideAt = now;
                row.LastError = list.First(i => i.Id == row.Id).Reason;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Queued items could not be set aside");
        }
    }

    /// <summary>
    /// Sets aside whatever of this agent's is still waiting behind calls that
    /// have themselves been set aside (F-08): a classification, note or
    /// recording for a call the server will never have can never succeed, and
    /// would otherwise wait for it for ever.
    /// </summary>
    public async Task<int> SetAsideFollowersAsync(
        Guid userId, IReadOnlyCollection<string> references, CancellationToken ct = default)
    {
        if (references.Count == 0)
        {
            return 0;
        }

        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);
            var now = DateTimeOffset.Now;

            return await db.PendingUploads
                .Where(u => u.UserId == userId && u.SetAsideAt == null
                            && u.Kind != PendingUploadKinds.Call
                            && u.Reference != null && references.Contains(u.Reference))
                .ExecuteUpdateAsync(
                    u => u.SetProperty(x => x.SetAsideAt, now)
                          .SetProperty(x => x.LastError, "call_set_aside"),
                    ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Items waiting on a set-aside call could not be set aside");
            return 0;
        }
    }

    /// <summary>This agent's rows that have been set aside, oldest first (F-08).</summary>
    public async Task<IReadOnlyList<SetAsideItem>> SetAsideItemsAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);

            var rows = await db.PendingUploads
                .Where(u => u.UserId == userId && u.SetAsideAt != null)
                .OrderBy(u => u.Id)
                .ToListAsync(ct);

            return rows.Select(row => new SetAsideItem(
                row.Id, row.Kind, NumberOf(row), row.CreatedAt, row.LastError)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The set-aside items could not be read");
            return [];
        }
    }

    /// <summary>
    /// Puts this agent's set-aside rows back on the queue with their attempts
    /// cleared, in their old places. Returns how many.
    /// </summary>
    public async Task<int> RetrySetAsideAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);

            return await db.PendingUploads
                .Where(u => u.UserId == userId && u.SetAsideAt != null)
                .ExecuteUpdateAsync(
                    u => u.SetProperty(x => x.SetAsideAt, (DateTimeOffset?)null)
                          .SetProperty(x => x.Attempts, 0),
                    ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The set-aside items could not be put back on the queue");
            return 0;
        }
    }

    /// <summary>The customer's number on a queued call, for the list of what was set aside.</summary>
    private static string? NumberOf(PendingUpload row)
    {
        if (row.Kind != PendingUploadKinds.Call)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LogCallRequest>(row.Payload)?.RemoteNumber;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Keeps the queue from growing without limit. The oldest go, because a
    /// recent call is the one somebody is still asking about.
    /// </summary>
    private async Task TrimAsync(AgentBufferDbContext db, CancellationToken ct)
    {
        var count = await db.PendingUploads.CountAsync(ct);
        if (count <= MaxQueued)
        {
            return;
        }

        var excess = count - MaxQueued;

        var oldest = await db.PendingUploads
            .OrderBy(u => u.Id)
            .Take(excess)
            .Select(u => u.Id)
            .ToListAsync(ct);

        await db.PendingUploads.Where(u => oldest.Contains(u.Id)).ExecuteDeleteAsync(ct);

        logger.LogError(
            "The offline buffer exceeded {Max}; {Dropped} of the oldest item(s) were discarded",
            MaxQueued, excess);
    }

    /// <summary>
    /// Moves anything left in the old JSON-per-line file into the buffer, then
    /// deletes it. Runs once, on the first start after the update.
    /// </summary>
    private async Task ImportLegacyFileAsync(AgentBufferDbContext db, CancellationToken ct)
    {
        if (LegacyFile is null || !File.Exists(LegacyFile))
        {
            return;
        }

        var imported = 0;

        foreach (var line in await File.ReadAllLinesAsync(LegacyFile, ct))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<LogCallRequest>(line) is not { } call)
                {
                    continue;
                }

                // No owner: nobody knows whose they were. They are claimed by
                // the next agent to sign in, like every row from before F-03.
                db.PendingUploads.Add(new PendingUpload
                {
                    Kind = PendingUploadKinds.Call,
                    Payload = line,
                    Reference = Reference(call),
                    CreatedAt = DateTimeOffset.Now,
                });

                imported++;
            }
            catch (JsonException)
            {
                // A torn last line, the usual result of a power cut mid-append.
                logger.LogWarning("A call in the old queue file could not be read and was skipped");
            }
        }

        await db.SaveChangesAsync(ct);

        // Only after the rows are safely committed.
        File.Delete(LegacyFile);

        logger.LogInformation(
            "{Count} call(s) moved from the old queue file into the offline buffer", imported);
    }

    private static string Reference(LogCallRequest call) => $"{call.SipCallId}|{call.Extension}";
}

/// <summary>
/// A queued row the server kept refusing, as the call log lists it (F-08).
/// </summary>
/// <param name="Kind">One of <see cref="PendingUploadKinds"/>.</param>
/// <param name="Number">The customer's number, for a call; null for the rest.</param>
/// <param name="Reason">The server's code, or why the app gave up.</param>
public record SetAsideItem(long Id, string Kind, string? Number, DateTimeOffset QueuedAt, string? Reason);
