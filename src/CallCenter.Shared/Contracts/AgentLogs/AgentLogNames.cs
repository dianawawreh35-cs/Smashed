using System.Text.RegularExpressions;

namespace CallCenter.Shared.Contracts.AgentLogs;

/// <summary>
/// The names an Agent App's log travels under (N-12): the laptop's folder and
/// the day's file, the same on the laptop and on the server.
/// </summary>
/// <remarks>
/// Both become parts of a path on the server, so both are checked against a
/// pattern that cannot climb out of the logs folder: no slashes, no "..", no
/// drive letters. The app cleans its machine name to fit rather than being
/// refused.
/// </remarks>
public static partial class AgentLogNames
{
    /// <summary>The route the server takes the logs on.</summary>
    public const string Route = "api/agent-logs";

    /// <summary>The largest piece of a file sent in one request.</summary>
    public const int MaxChunkBytes = 512 * 1024;

    /// <summary>A laptop's folder: letters, digits, dot, dash and underscore, starting with a letter or digit.</summary>
    public static bool IsLaptop(string? name) => name is not null && LaptopPattern().IsMatch(name);

    /// <summary>A day's file, as Serilog's daily file names it: <c>agent-20260927.log</c>.</summary>
    public static bool IsFile(string? name) => name is not null && FilePattern().IsMatch(name);

    /// <summary>A machine name made to fit <see cref="IsLaptop"/>.</summary>
    public static string Laptop(string machineName)
    {
        var cleaned = InvalidLaptopChars().Replace(machineName.Trim(), "_").TrimStart('.', '-', '_');

        if (cleaned.Length > 64)
        {
            cleaned = cleaned[..64];
        }

        return cleaned.Length == 0 ? "unknown" : cleaned;
    }

    // \z, not $: in .NET, $ also matches before a final newline.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z")]
    private static partial Regex LaptopPattern();

    [GeneratedRegex(@"^agent-[0-9]{8}(_[0-9]{3})?\.log\z")]
    private static partial Regex FilePattern();

    [GeneratedRegex(@"[^A-Za-z0-9._-]")]
    private static partial Regex InvalidLaptopChars();
}

/// <summary>How much of a file the server has, after an append or instead of one.</summary>
/// <param name="Length">The server's copy, in bytes. The next piece starts here.</param>
public record AgentLogLengthDto(long Length);
