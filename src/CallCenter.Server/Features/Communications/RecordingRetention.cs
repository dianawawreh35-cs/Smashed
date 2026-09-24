using CallCenter.Server.Data;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Communications;

/// <summary>
/// Deleting recordings once they are older than the supervisor's retention
/// period (A-33), and reporting what the recordings occupy (S-43).
/// </summary>
/// <remarks>
/// <b>The file goes, the row stays.</b> N-08 keeps communications and their
/// classifications indefinitely and expires only the audio, so a report over
/// last year stays correct and an old call reads as <i>recorded, expired</i>
/// rather than as never recorded. Nothing here deletes a row; it sets
/// <c>deleted_at</c>.
///
/// <b>The setting is read on every run</b>, not captured at startup. A
/// supervisor changing 90 days to 30 on the settings screen (S-43, S-47) should
/// see it take effect tonight, without anyone restarting the server.
///
/// <b>Nothing in here is allowed to take the server down.</b> One unreadable
/// file, or a folder that has been moved, is a logged warning and a job that
/// carries on to the next recording.
/// </remarks>
public class RecordingRetention(
    CallCenterDbContext db,
    RecordingStore store,
    Settings.SettingsService settings,
    TimeProvider clock,
    ILogger<RecordingRetention> logger)
{
    /// <summary>The setting the run reads (S-43), and its default (N-08).</summary>
    public const string RetentionDaysKey = "recording.retention_days";
    public const int DefaultRetentionDays = 90;

    /// <summary>
    /// How many rows are loaded at a time. A year of four agents is six figures
    /// of recordings, and the first run after this ships may have to expire a
    /// large backlog; loading it all into memory at once is how a nightly job
    /// becomes an outage.
    /// </summary>
    private const int BatchSize = 200;

    /// <summary>What one pass did, for the log and for the tests.</summary>
    public readonly record struct Result(int Expired, int AlreadyGone, int Failed, bool Ran)
    {
        public static Result NotRun => new(0, 0, 0, Ran: false);
    }

    /// <summary>
    /// One pass: every recording uploaded longer ago than the retention period
    /// loses its file and gains a <c>deleted_at</c> (A-33).
    /// </summary>
    public async Task<Result> RunOnceAsync(CancellationToken ct = default)
    {
        var days = await settings.GetIntAsync(RetentionDaysKey, DefaultRetentionDays, ct);

        // The settings screen validates 1-3650 (S-47), but this job runs
        // against whatever is in the table, including a row edited by hand. A
        // zero or a negative would expire everything ever recorded, so it is
        // refused rather than obeyed.
        if (days < 1)
        {
            logger.LogWarning(
                "Recording retention is set to {Days} days, which would delete recordings as they arrive. "
                + "Using {Default} until it is corrected.", days, DefaultRetentionDays);
            days = DefaultRetentionDays;
        }

        // A mount that is not there would make every file look "already gone"
        // and mark a year of recordings expired while they sat safely on a disk
        // nobody had attached. Skip the run instead; tomorrow's will do it.
        if (!store.RootExists())
        {
            logger.LogWarning(
                "The recordings folder is not there, so retention did nothing this run. "
                + "Nothing has been marked as expired.");
            return Result.NotRun;
        }

        var cutoff = clock.GetUtcNow().AddDays(-days);
        var expired = 0;
        var alreadyGone = 0;
        var failed = 0;

        while (!ct.IsCancellationRequested)
        {
            // Ordered oldest first, so an interrupted run has still done the
            // most overdue recordings. Failures are skipped rather than
            // marked, so they would be picked up again by the next batch --
            // hence the offset, which keeps that from looping forever.
            var due = await db.Recordings
                .Where(r => r.DeletedAt == null && r.UploadedAt < cutoff)
                .OrderBy(r => r.UploadedAt)
                .Skip(failed)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (due.Count == 0)
            {
                break;
            }

            foreach (var recording in due)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                switch (store.DeleteFile(recording.Path))
                {
                    case RecordingStore.DeleteOutcome.Deleted:
                        recording.DeletedAt = clock.GetUtcNow();
                        expired++;
                        break;

                    // The audio is not there, whatever happened to it. The row
                    // still has to say so, or the call would read as one that
                    // was never recorded.
                    case RecordingStore.DeleteOutcome.AlreadyGone:
                        recording.DeletedAt = clock.GetUtcNow();
                        alreadyGone++;
                        break;

                    // Left untouched on purpose: recording an expiry that did
                    // not happen would hide a file that is still on the disk.
                    case RecordingStore.DeleteOutcome.Failed:
                        failed++;
                        break;
                }
            }

            await db.SaveChangesAsync(ct);
        }

        if (expired + alreadyGone + failed > 0)
        {
            logger.LogInformation(
                "Recording retention ({Days} days): {Expired} deleted, {AlreadyGone} already gone, {Failed} failed",
                days, expired, alreadyGone, failed);
        }
        else
        {
            logger.LogDebug("Recording retention ({Days} days): nothing was old enough", days);
        }

        return new Result(expired, alreadyGone, failed, Ran: true);
    }

    /// <summary>
    /// What the recordings occupy and how many are kept (S-43) — the number
    /// that says whether 90 days is affordable.
    /// </summary>
    public async Task<RecordingStorageDto> UsageAsync(CancellationToken ct = default)
    {
        var days = await settings.GetIntAsync(RetentionDaysKey, DefaultRetentionDays, ct);

        var kept = await db.Recordings
            .Where(r => r.DeletedAt == null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                // Sum over a nullable column: a row uploaded before sizes were
                // recorded counts as a recording but contributes nothing.
                Bytes = g.Sum(r => r.SizeBytes ?? 0),
                Oldest = g.Min(r => r.UploadedAt),
                Newest = g.Max(r => r.UploadedAt),
            })
            .FirstOrDefaultAsync(ct);

        var expired = await db.Recordings.CountAsync(r => r.DeletedAt != null, ct);

        var disk = store.Measure();

        return new RecordingStorageDto(
            RetentionDays: days,
            Kept: kept?.Count ?? 0,
            KeptBytes: kept?.Bytes ?? 0,
            Expired: expired,
            OldestKept: kept is null ? null : kept.Oldest,
            NewestKept: kept is null ? null : kept.Newest,
            DiskBytes: disk.Bytes,
            DiskFiles: disk.Files,
            EmptyFolders: disk.EmptyFolders,
            DiskReadable: disk.Readable);
    }
}
