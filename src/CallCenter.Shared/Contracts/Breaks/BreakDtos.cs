using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Breaks;

/// <summary>
/// A break as the Agent App knows it, sent whole each time it changes (A-86):
/// once at Break in with no end, and again at Break out with both.
/// </summary>
/// <remarks>
/// The whole break rather than "started" and "ended" events, so the two can
/// arrive in either order, or the first not at all, and the server still ends
/// up with the break. The id is made on the laptop, which is what makes a
/// resend from the offline queue (A-04) harmless.
/// </remarks>
/// <param name="SessionId">
/// The sign-in the break was taken under. Sent because a break queued while the
/// server was unreachable may arrive under a later sign-in, and it must end with
/// the one it belongs to.
/// </param>
/// <param name="EndedBy">One of <see cref="BreakEndings.FromApp"/> when <paramref name="EndedAt"/> is set.</param>
public record SaveBreakRequest(
    Guid? SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    [MaxLength(20)] string? EndedBy);

/// <summary>One break as the server has it.</summary>
/// <param name="EndedAt">
/// When it ended, or null while it is still going. For a break the app never
/// ended, the end the server worked out (<see cref="BreakEndings.SessionEnded"/>,
/// <see cref="BreakEndings.NotHeard"/>).
/// </param>
/// <param name="EndedBy">One of <see cref="BreakEndings"/>; null while it is still going.</param>
/// <param name="Seconds">How long, to now for one still going.</param>
public record BreakDto(
    Guid Id,
    Guid AgentId,
    string AgentDisplayName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? EndedBy,
    int Seconds);

/// <summary>
/// The signed-in agent's break time today, for the Agent App's timer at sign-in
/// (A-86).
/// </summary>
/// <param name="Seconds">
/// Every break of theirs today, to now: the part of each that falls on the
/// restaurant's today.
/// </param>
/// <param name="DailyLimitMinutes"><c>breaks.daily_limit_minutes</c>.</param>
public record MyBreaksTodayDto(int Seconds, int DailyLimitMinutes);

/// <summary>
/// The Agent App's do-not-disturb switch (A-18), sent when it changes and at
/// sign-in, so the break monitor can show it (S-66).
/// </summary>
/// <param name="On">Whether do not disturb is on now.</param>
/// <param name="At">
/// When it was switched, by the laptop's clock. A resend carries the same time,
/// so the server keeps the first "since", and news older than what it has is
/// ignored.
/// </param>
public record SaveDoNotDisturbRequest(bool On, DateTimeOffset At);

/// <summary>One agent on the break monitor (S-66).</summary>
/// <param name="State">One of <see cref="BreakStates"/>.</param>
/// <param name="BreakStartedAt">When the break under way began; null unless on break.</param>
/// <param name="TodaySeconds">Every break today, the one under way included, to <see cref="BreakMonitorDto.AsOf"/>.</param>
/// <param name="TodayBreaks">How many breaks today, the one under way included.</param>
/// <param name="DoNotDisturb">
/// Whether the agent's app has do not disturb on (A-18). Null when the app is
/// not heard from, or is too old to say.
/// </param>
/// <param name="DoNotDisturbSince">When it was last switched; null when <paramref name="DoNotDisturb"/> is.</param>
public record BreakMonitorRowDto(
    Guid AgentId,
    string AgentDisplayName,
    string State,
    DateTimeOffset? BreakStartedAt,
    int TodaySeconds,
    int TodayBreaks,
    bool? DoNotDisturb = null,
    DateTimeOffset? DoNotDisturbSince = null);

/// <summary>
/// Every active agent's break today, as of <paramref name="AsOf"/> (S-66).
/// </summary>
/// <param name="AsOf">
/// The server's clock when this was worked out. The page counts the breaks
/// under way on from here, so a browser whose clock is wrong still shows the
/// right minutes.
/// </param>
public record BreakMonitorDto(DateTimeOffset AsOf, int DailyLimitMinutes, IReadOnlyList<BreakMonitorRowDto> Agents);

/// <summary>One agent's breaks on one of the restaurant's days (R-22).</summary>
/// <param name="Seconds">The part of every break that falls on that day.</param>
/// <param name="OverSeconds">How far past the day's limit; 0 within it.</param>
public record BreakDayDto(
    DateOnly Day,
    Guid AgentId,
    string AgentDisplayName,
    int Breaks,
    int Seconds,
    int OverSeconds);

/// <summary>One agent's breaks over the whole period (R-22).</summary>
/// <param name="Days">Days with at least one break.</param>
/// <param name="DaysOver">Days the agent went past the limit.</param>
public record BreakAgentTotalDto(
    Guid AgentId,
    string AgentDisplayName,
    int Breaks,
    int Seconds,
    int Days,
    int DaysOver,
    int OverSeconds);

/// <summary>
/// The break report for a period (R-22): per agent, and per agent per day.
/// </summary>
/// <param name="DailyLimitMinutes">The limit the "over" figures were worked out against: today's setting.</param>
public record BreakReportDto(
    int DailyLimitMinutes,
    IReadOnlyList<BreakAgentTotalDto> Agents,
    IReadOnlyList<BreakDayDto> Days);

/// <summary>A page of single breaks (R-22), newest first, with how many match in all.</summary>
public record BreakPageDto(IReadOnlyList<BreakDto> Rows, int Total, int Page, int PageSize);
