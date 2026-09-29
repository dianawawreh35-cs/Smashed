namespace CallCenter.Shared.Contracts.AgentLogs;

/// <summary>A laptop that has sent its log, for the supervisor's Logs page (N-12).</summary>
/// <param name="Laptop">Its folder name: the machine name, cleaned (<see cref="AgentLogNames.Laptop"/>).</param>
/// <param name="LastWriteAt">When the server last took a piece of its log.</param>
/// <param name="Days">Its daily files, newest first.</param>
/// <param name="Nickname">What the supervisors call it, when they have named it.</param>
public record AgentLogLaptopDto(
    string Laptop, DateTimeOffset LastWriteAt, IReadOnlyList<AgentLogDayDto> Days, string? Nickname = null);

/// <summary>The supervisors have seen today's errors: each laptop's count when they did.</summary>
/// <param name="Date">The day acknowledged, <c>yyyy-MM-dd</c>.</param>
/// <param name="Errors">Each laptop's errors that day, as the page counted them.</param>
/// <param name="By">The supervisor who acknowledged, by display name.</param>
public record AgentLogAcknowledgementDto(
    string Date, IReadOnlyDictionary<string, int> Errors, string? By, DateTimeOffset At);

/// <summary>Acknowledges the errors counted on <paramref name="Date"/>, laptop by laptop.</summary>
public record AcknowledgeAgentLogErrorsRequest(string Date, IReadOnlyDictionary<string, int> Errors);

/// <summary>A laptop's new nickname. Blank or null removes it.</summary>
public record SetAgentLogNicknameRequest(string? Nickname);

/// <summary>One day's file on the server.</summary>
/// <param name="File">As the laptop names it, <c>agent-20260927.log</c>.</param>
/// <param name="Date">The day it covers, <c>yyyy-MM-dd</c>, read from the name.</param>
/// <param name="Errors">Entries at Error or Fatal.</param>
/// <param name="Warnings">Entries at Warning.</param>
public record AgentLogDayDto(string File, string Date, long Bytes, int Errors, int Warnings, DateTimeOffset LastWriteAt);

/// <summary>One entry: its first line's time and level, and every line under it (a stack trace, a SQL command).</summary>
/// <param name="Time">As written, <c>2026-09-27 14:03:11.482 +03:00</c>: the laptop's clock and zone.</param>
/// <param name="Level">Serilog's three letters: VRB, DBG, INF, WRN, ERR or FTL.</param>
public record AgentLogEntryDto(int Line, string Time, string Level, string Text);

/// <summary>A day's entries, newest first, after the filter.</summary>
/// <param name="Matched">How many entries the filter matched, of which <see cref="Entries"/> are the newest.</param>
/// <param name="Total">How many entries the day has.</param>
public record AgentLogPageDto(
    IReadOnlyList<AgentLogEntryDto> Entries, int Matched, int Total, int Errors, int Warnings);

/// <summary>Which entries to show.</summary>
public static class AgentLogLevels
{
    /// <summary>Everything.</summary>
    public const string All = "all";

    /// <summary>Warnings, errors and fatal ones.</summary>
    public const string Warnings = "warnings";

    /// <summary>Errors and fatal ones.</summary>
    public const string Errors = "errors";
}
