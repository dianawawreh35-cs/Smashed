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

    /// <summary>
    /// Errors and warnings per file, as far as it had been counted. A file only
    /// grows, so a longer one is counted from where the count stopped, not
    /// again from the start: the Logs page asks for every day of every laptop
    /// each time it opens.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, int Errors, int Warnings)> _counts = new();

    /// <summary>Every laptop that has sent a log, the most recently heard from first (the Logs page, N-12).</summary>
    public IReadOnlyList<AgentLogLaptopDto> Laptops()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var laptops = new List<AgentLogLaptopDto>();

        foreach (var folder in new DirectoryInfo(_root).EnumerateDirectories().Where(d => AgentLogNames.IsLaptop(d.Name)))
        {
            var days = folder.EnumerateFiles()
                .Where(f => AgentLogNames.IsFile(f.Name))
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .Select(f =>
                {
                    var (errors, warnings) = Counted(f);
                    return new AgentLogDayDto(
                        f.Name, DateOf(f.Name), f.Length, errors, warnings,
                        new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero));
                })
                .ToList();

            if (days.Count > 0)
            {
                laptops.Add(new AgentLogLaptopDto(folder.Name, days.Max(d => d.LastWriteAt), days));
            }
        }

        return laptops.OrderByDescending(l => l.LastWriteAt).ToList();
    }

    /// <summary>One day's file as text, or null when there is no such file.</summary>
    public async Task<string?> ReadAsync(string laptop, string file, CancellationToken ct)
    {
        if (!AgentLogNames.IsLaptop(laptop) || !AgentLogNames.IsFile(file))
        {
            return null;
        }

        var path = System.IO.Path.Combine(Folder(laptop), file);

        if (!File.Exists(path))
        {
            return null;
        }

        // Shared, so a laptop can go on appending while it is read.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

        return await reader.ReadToEndAsync(ct);
    }

    private (int Errors, int Warnings) Counted(FileInfo file)
    {
        var known = _counts.GetValueOrDefault(file.FullName);

        if (known.Length == file.Length)
        {
            return (known.Errors, known.Warnings);
        }

        // Shorter than counted: not the file that was counted (deleted and
        // sent again). Start over.
        var from = known.Length < file.Length ? known.Length : 0;
        var (errors, warnings) = from == 0 ? (0, 0) : (known.Errors, known.Warnings);

        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var bytes = new byte[length - from];
        stream.Seek(from, SeekOrigin.Begin);
        stream.ReadExactly(bytes);

        // Up to the last whole line: a piece may be half written as this reads.
        var whole = Array.LastIndexOf(bytes, (byte)'\n') + 1;
        var (newErrors, newWarnings) = AgentLogReader.Count(bytes.AsSpan(0, whole));
        var counted = (from + whole, errors + newErrors, warnings + newWarnings);

        _counts[file.FullName] = counted;
        return (counted.Item2, counted.Item3);
    }

    /// <summary><c>agent-20260927.log</c> to <c>2026-09-27</c>.</summary>
    private static string DateOf(string file) => $"{file[6..10]}-{file[10..12]}-{file[12..14]}";

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
                    _counts.TryRemove(file.FullName, out _);
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
