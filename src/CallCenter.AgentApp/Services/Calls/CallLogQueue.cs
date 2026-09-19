using System.IO;
using System.Text.Json;
using CallCenter.AgentApp.Data;
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
/// </remarks>
public class CallLogQueue(IDbContextFactory<AgentBufferDbContext> buffer, ILogger<CallLogQueue> logger)
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
    /// Creates the buffer if it is not there, and moves across anything left in
    /// the old file.
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

    /// <summary>Adds a call to the queue. Called before the send is attempted.</summary>
    public async Task EnqueueAsync(LogCallRequest call, CancellationToken ct = default)
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
            });

            await db.SaveChangesAsync(ct);
            await TrimAsync(db, ct);
        }
        catch (Exception ex)
        {
            // Nothing else to do: the call is about to be sent anyway, and
            // failing here must not interrupt the agent.
            logger.LogWarning(ex, "A call could not be queued to the offline buffer");
        }
    }

    /// <summary>Everything still waiting, oldest first.</summary>
    public async Task<IReadOnlyList<(long Id, LogCallRequest Call)>> PendingAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);

            var rows = await db.PendingUploads
                .Where(u => u.Kind == PendingUploadKinds.Call)
                .OrderBy(u => u.Id)
                .ToListAsync(ct);

            var calls = new List<(long, LogCallRequest)>();

            foreach (var row in rows)
            {
                try
                {
                    if (JsonSerializer.Deserialize<LogCallRequest>(row.Payload) is { } call)
                    {
                        calls.Add((row.Id, call));
                    }
                }
                catch (JsonException)
                {
                    // Unreadable, so it can never be sent. Left in place and
                    // reported rather than deleted quietly: a row nobody can
                    // explain is worth someone looking at.
                    logger.LogError("Queued call {Id} could not be read and will be skipped", row.Id);
                }
            }

            return calls;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The offline buffer could not be read");
            return [];
        }
    }

    /// <summary>Removes calls the server has accepted.</summary>
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

    /// <summary>Records that an attempt failed, so a row that never succeeds can be found.</summary>
    public async Task RecordFailureAsync(long id, string? error, CancellationToken ct = default)
    {
        try
        {
            await using var db = await buffer.CreateDbContextAsync(ct);

            await db.PendingUploads
                .Where(u => u.Id == id)
                .ExecuteUpdateAsync(
                    u => u.SetProperty(x => x.Attempts, x => x.Attempts + 1)
                          .SetProperty(x => x.LastError, error),
                    ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "A failed attempt could not be recorded");
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
        if (!File.Exists(LegacyPath))
        {
            return;
        }

        var imported = 0;

        foreach (var line in await File.ReadAllLinesAsync(LegacyPath, ct))
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
        File.Delete(LegacyPath);

        logger.LogInformation(
            "{Count} call(s) moved from the old queue file into the offline buffer", imported);
    }

    private static string Reference(LogCallRequest call) => $"{call.SipCallId}|{call.Extension}";
}
