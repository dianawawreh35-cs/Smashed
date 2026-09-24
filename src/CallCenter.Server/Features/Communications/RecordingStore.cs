using Microsoft.Extensions.Options;

namespace CallCenter.Server.Features.Communications;

/// <summary>
/// Where recordings live on the server, from the <c>Recordings</c> section
/// (A-31, A-33).
/// </summary>
/// <remarks>
/// The path is already set in the deployment (`Recordings__Path: /data/recordings`
/// in docker-compose, mounted outside the container) and the nightly backup
/// already mirrors it. Until now nothing read it.
/// </remarks>
public class RecordingOptions
{
    public const string SectionName = "Recordings";

    /// <summary>
    /// The folder recordings are written under. Relative paths resolve against
    /// the server's working directory, which is what the runbook's
    /// <c>data/recordings</c> mount relies on.
    /// </summary>
    public string Path { get; set; } = "data/recordings";
}

/// <summary>
/// Writes and reads the audio files themselves (A-31, S-04).
/// </summary>
/// <remarks>
/// <b>The bytes never go through the database.</b> The row in
/// <c>recordings</c> holds a relative path and the file sits on disk, so a
/// backup is a file copy and serving one is a stream rather than a large object
/// loaded into memory. That split is why the deployment mounts the folder
/// outside the container.
///
/// <b>Laid out by date</b>, <c>yyyy/MM/dd/&lt;communication-id&gt;.wav</c>, as
/// SCHEMA.md specifies. One flat folder would hold six figures of files within
/// a year, which every tool that touches it would hate; the retention job
/// (A-33) also deletes by age, and by-date folders make that a directory walk
/// rather than a query per file.
///
/// Paths are built here and never taken from a request, so nothing a client
/// sends can escape the root.
/// </remarks>
public class RecordingStore(IOptions<RecordingOptions> options, ILogger<RecordingStore> logger)
{
    /// <summary>
    /// The largest recording accepted. An hour of the Agent App's format is
    /// about 55 MB; 120 MB leaves room for a very long call without letting a
    /// broken client fill the disk in one request.
    /// </summary>
    public const long MaxBytes = 120L * 1024 * 1024;

    private readonly string _root = System.IO.Path.GetFullPath(options.Value.Path);

    /// <summary>The path a recording for this call would have, relative to the root.</summary>
    public static string PathFor(Guid communicationId, DateTimeOffset startedAt) =>
        $"{startedAt.UtcDateTime:yyyy/MM/dd}/{communicationId}.wav";

    /// <summary>
    /// Saves the audio and returns its relative path and size. Overwrites a
    /// previous file for the same call, because the Agent App may resend from
    /// its offline queue and a half-written retry must not survive.
    /// </summary>
    public async Task<(string Path, long Size)> SaveAsync(
        Guid communicationId, DateTimeOffset startedAt, Stream audio, CancellationToken ct = default)
    {
        var relative = PathFor(communicationId, startedAt);
        var full = Resolve(relative);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);

        // Written beside the target and moved into place, so a request that
        // fails halfway cannot leave a truncated file that looks like a
        // recording. A supervisor playing silence and concluding the call was
        // not recorded is worse than an honest absence.
        var staging = full + ".part";

        try
        {
            await using (var file = new FileStream(
                staging, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await audio.CopyToAsync(file, ct);
            }

            File.Move(staging, full, overwrite: true);

            var size = new FileInfo(full).Length;
            logger.LogInformation("Stored recording {Path} ({Size} bytes)", relative, size);

            return (relative, size);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }
    }

    /// <summary>Opens a stored recording, or null when the file is not there.</summary>
    /// <remarks>
    /// A missing file is normal rather than exceptional: the retention job
    /// (A-33) removes files and keeps the rows, so every recording older than
    /// the retention period is a row whose file has gone. The caller answers
    /// 404 and the screen says the recording has expired.
    /// </remarks>
    public Stream? Open(string relativePath)
    {
        try
        {
            var full = Resolve(relativePath);

            if (!File.Exists(full))
            {
                logger.LogWarning("Recording {Path} is recorded but missing from disk", relativePath);
                return null;
            }

            return new FileStream(
                full, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Recording {Path} could not be opened", relativePath);
            return null;
        }
    }

    /// <summary>Deletes a recording's file, for the retention job (A-33).</summary>
    public bool Delete(string relativePath)
    {
        try
        {
            var full = Resolve(relativePath);

            if (File.Exists(full))
            {
                File.Delete(full);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recording {Path} could not be deleted", relativePath);
            return false;
        }
    }

    /// <summary>
    /// A relative path turned into a full one, refusing anything that would
    /// land outside the root.
    /// </summary>
    /// <remarks>
    /// Every path this class handles is one it built itself, so this cannot
    /// currently be reached — which is exactly why it is here. The check costs
    /// nothing and stops a later change that takes a path from a request from
    /// quietly becoming a way to read any file on the server.
    /// </remarks>
    private string Resolve(string relativePath)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, relativePath));

        if (!full.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A recording path resolved outside the recordings folder: {relativePath}");
        }

        return full;
    }

    private void TryDelete(string full)
    {
        try
        {
            if (File.Exists(full))
            {
                File.Delete(full);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A part-written recording could not be removed: {Path}", full);
        }
    }
}
