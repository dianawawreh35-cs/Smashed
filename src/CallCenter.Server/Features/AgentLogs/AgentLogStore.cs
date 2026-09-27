using CallCenter.Shared.Contracts.AgentLogs;
using Microsoft.Extensions.Options;

namespace CallCenter.Server.Features.AgentLogs;

public class AgentLogOptions
{
    public const string SectionName = "AgentLogs";

    /// <summary>
    /// Where the laptops' logs are kept, a folder per laptop. Relative to the
    /// server's working directory: in the container that is <c>/app/logs</c>,
    /// already mounted from <c>data/logs</c> for the server's own log, so the
    /// agents' land beside it with no change to the deployment.
    /// </summary>
    public string Path { get; set; } = "logs/agents";

    /// <summary>How many days a file is kept after it was last written. The server's own log keeps 30.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// The most one day's file may grow to. A day on a busy laptop is under
    /// 1 MB; this is the ceiling for an app stuck in a loop, so it cannot fill
    /// the server's disk.
    /// </summary>
    public long MaxFileBytes { get; set; } = 50L * 1024 * 1024;
}

/// <summary>
/// The server's copy of every Agent App's log (N-12): one file per laptop per
/// day, byte for byte the file on the laptop.
/// </summary>
/// <remarks>
/// <b>The laptop sends what the server does not have yet</b>, and every append
/// says where it starts. The server appends only when that is where its copy
/// ends, and otherwise answers with where it does end, so the laptop picks up
/// from there. A request repeated after its answer was lost therefore cannot
/// write the same lines twice, and a copy that went missing here is sent again
/// from the start. The copy is always a prefix of the laptop's file.
///
/// <b>Paths are built from checked names only</b> (<see cref="AgentLogNames"/>),
/// and checked again to be inside the folder, as <c>RecordingStore</c> does.
///
/// One lock for all of it: a handful of laptops, one small request each
/// every half a minute.
/// </remarks>
public class AgentLogStore(IOptions<AgentLogOptions> options, ILogger<AgentLogStore> logger)
{
    public enum Outcome
    {
        Appended,

        /// <summary>The piece does not start where the server's copy ends. Nothing was written.</summary>
        WrongOffset,

        /// <summary>The file would pass <see cref="AgentLogOptions.MaxFileBytes"/>. Nothing was written.</summary>
        TooLarge,

        BadName,
    }

    private readonly string _root = System.IO.Path.GetFullPath(options.Value.Path);

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>How long each of a laptop's files is here, by file name. None for a laptop never heard from.</summary>
    public IReadOnlyDictionary<string, long> Lengths(string laptop)
    {
        if (!AgentLogNames.IsLaptop(laptop))
        {
            return new Dictionary<string, long>();
        }

        var folder = Folder(laptop);

        if (!Directory.Exists(folder))
        {
            return new Dictionary<string, long>();
        }

        return new DirectoryInfo(folder).EnumerateFiles()
            .Where(f => AgentLogNames.IsFile(f.Name))
            .ToDictionary(f => f.Name, f => f.Length, StringComparer.Ordinal);
    }

    /// <summary>Adds <paramref name="bytes"/> to the file, if they start where it ends.</summary>
    /// <returns>What happened, and how long the file is now.</returns>
    public async Task<(Outcome Outcome, long Length)> AppendAsync(
        string laptop, string file, long offset, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        if (!AgentLogNames.IsLaptop(laptop) || !AgentLogNames.IsFile(file) || offset < 0)
        {
            return (Outcome.BadName, 0);
        }

        var path = System.IO.Path.Combine(Folder(laptop), file);

        if (!System.IO.Path.GetFullPath(path).StartsWith(_root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return (Outcome.BadName, 0);
        }

        await _lock.WaitAsync(ct);

        try
        {
            Directory.CreateDirectory(Folder(laptop));

            await using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

            if (stream.Length != offset)
            {
                return (Outcome.WrongOffset, stream.Length);
            }

            if (offset + bytes.Length > options.Value.MaxFileBytes)
            {
                logger.LogWarning(
                    "The log {Laptop}/{File} reached its limit of {Limit} bytes; the rest of that day is not kept",
                    laptop, file, options.Value.MaxFileBytes);

                return (Outcome.TooLarge, stream.Length);
            }

            stream.Seek(0, SeekOrigin.End);
            await stream.WriteAsync(bytes, ct);

            return (Outcome.Appended, stream.Length);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Deletes every file not written for <see cref="AgentLogOptions.RetentionDays"/>, and emptied folders.</summary>
    /// <returns>How many files went.</returns>
    public int DeleteExpired(DateTime nowUtc)
    {
        if (!Directory.Exists(_root))
        {
            return 0;
        }

        var cutoff = nowUtc.AddDays(-options.Value.RetentionDays);
        var deleted = 0;

        foreach (var folder in new DirectoryInfo(_root).EnumerateDirectories())
        {
            foreach (var file in folder.EnumerateFiles().Where(f => AgentLogNames.IsFile(f.Name)))
            {
                if (file.LastWriteTimeUtc < cutoff)
                {
                    file.Delete();
                    deleted++;
                }
            }

            if (!folder.EnumerateFileSystemInfos().Any())
            {
                folder.Delete();
            }
        }

        return deleted;
    }

    private string Folder(string laptop) => System.IO.Path.Combine(_root, laptop);
}
