using System.Text;
using System.Text.RegularExpressions;
using CallCenter.Shared.Contracts.AgentLogs;

namespace CallCenter.Server.Features.AgentLogs;

/// <summary>
/// Reads an Agent App's log as entries, for the supervisor's Logs page (N-12).
/// </summary>
/// <remarks>
/// The app writes Serilog's default file format: a line that starts with the
/// time and the level in brackets, <c>2026-09-27 14:03:11.482 +03:00 [ERR]</c>,
/// and then whatever the message and its exception take, over as many lines as
/// they need. So an entry is a line that starts that way and every line after
/// it that does not. A line before the first entry (a file cut short) is an
/// entry of its own with no level, rather than being dropped.
/// </remarks>
public static partial class AgentLogReader
{
    /// <summary>The newest entries a page carries. A normal day is a few thousand in all.</summary>
    public const int MaxEntries = 2000;

    public static bool IsError(string level) => level is "ERR" or "FTL";

    public static bool IsWarning(string level) => level == "WRN";

    /// <summary>Every entry in the text, oldest first.</summary>
    public static List<AgentLogEntryDto> Entries(string text)
    {
        var entries = new List<AgentLogEntryDto>();
        var lines = text.Split('\n');

        var start = 0;
        string time = "", level = "";
        var body = new StringBuilder();

        void Close(int line)
        {
            if (body.Length > 0)
            {
                entries.Add(new AgentLogEntryDto(line, time, level, body.ToString().TrimEnd('\r', '\n')));
                body.Clear();
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var head = EntryStart().Match(line);

            if (head.Success)
            {
                Close(start + 1);
                start = i;
                time = head.Groups["time"].Value;
                level = head.Groups["level"].Value;
                body.Append(line[head.Length..]);
            }
            else if (line.Length > 0 || body.Length > 0)
            {
                if (body.Length > 0)
                {
                    body.Append('\n');
                }
                else
                {
                    start = i;
                }

                body.Append(line);
            }
        }

        Close(start + 1);
        return entries;
    }

    /// <summary>
    /// The day's entries, newest first, that match <paramref name="levels"/>
    /// (<see cref="AgentLogLevels"/>) and contain <paramref name="search"/>.
    /// </summary>
    public static AgentLogPageDto Page(string text, string? levels, string? search)
    {
        var all = Entries(text);

        var matching = all
            .Where(e => levels switch
            {
                AgentLogLevels.Errors => IsError(e.Level),
                AgentLogLevels.Warnings => IsError(e.Level) || IsWarning(e.Level),
                _ => true,
            })
            .Where(e => string.IsNullOrWhiteSpace(search)
                        || e.Text.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new AgentLogPageDto(
            Entries: matching.AsEnumerable().Reverse().Take(MaxEntries).ToList(),
            Matched: matching.Count,
            Total: all.Count,
            Errors: all.Count(e => IsError(e.Level)),
            Warnings: all.Count(e => IsWarning(e.Level)));
    }

    /// <summary>
    /// Errors and warnings in a piece of a file. The laptop sends whole lines,
    /// so a piece that starts where the last one ended starts at a line, and
    /// counting pieces adds up to counting the file.
    /// </summary>
    public static (int Errors, int Warnings) Count(ReadOnlySpan<byte> bytes)
    {
        int errors = 0, warnings = 0;

        foreach (var range in bytes.Split((byte)'\n'))
        {
            var line = bytes[range];

            // "2026-09-27 14:03:11.482 +03:00 [ERR] ": the level sits at a fixed place.
            if (line.Length >= LevelAt + 5 && line[LevelAt] == (byte)'[' && line[LevelAt + 4] == (byte)']'
                && line[4] == (byte)'-' && line[10] == (byte)' ')
            {
                var level = line.Slice(LevelAt + 1, 3);

                if (level.SequenceEqual("ERR"u8) || level.SequenceEqual("FTL"u8))
                {
                    errors++;
                }
                else if (level.SequenceEqual("WRN"u8))
                {
                    warnings++;
                }
            }
        }

        return (errors, warnings);
    }

    /// <summary>Where the level's bracket is: after <c>yyyy-MM-dd HH:mm:ss.fff +zz:zz </c>.</summary>
    private const int LevelAt = 31;

    [GeneratedRegex(@"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<level>VRB|DBG|INF|WRN|ERR|FTL)\] ")]
    private static partial Regex EntryStart();
}
