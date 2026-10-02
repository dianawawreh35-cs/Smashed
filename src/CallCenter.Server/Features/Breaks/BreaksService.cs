using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Server.Features.Reports;
using CallCenter.Server.Features.Settings;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Breaks;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Breaks;

/// <summary>
/// Agents' breaks (A-86): what the Agent App sends, the agent's own total for
/// today, the supervisor's monitor (S-66) and the break report (R-22).
/// </summary>
/// <remarks>
/// <para>
/// <b>A break is sent whole</b> (<see cref="SaveBreakRequest"/>) and saved by
/// its laptop-made id, so a resend from the offline queue (A-04) changes
/// nothing, and an end can arrive before its start. A break's start never
/// changes once saved; its end is set once by the app, and may replace an end
/// the server had worked out for it (<see cref="BreakClock"/>), because the
/// app knows and the server only guessed.
/// </para>
/// <para>
/// <b>The day is the restaurant's</b> (<see cref="ReportScope.Today"/>), and a
/// break over midnight counts on each day for its part of it. The daily limit
/// does not stop anything: the app warns the agent past it, and the monitor and
/// the report show by how much (Dia, 1 Oct 2026).
/// </para>
/// <para>
/// <b>Read whole for a period, then counted here.</b> Five agents taking a few
/// breaks a day is a few thousand rows a year, and a break's end may have to be
/// worked out from its sign-in, which SQL cannot page by. The browser is still
/// given one page (20 Sep, "filtering a page lies").
/// </para>
/// </remarks>
public class BreaksService(
    CallCenterDbContext db,
    SettingsService settings,
    TimeProvider clock,
    ILogger<BreaksService> logger)
{
    /// <summary>The minutes of break an agent has a day (S-47).</summary>
    public const string DailyLimitKey = "breaks.daily_limit_minutes";

    public const int DefaultDailyLimitMinutes = 60;

    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 200;

    /// <summary>
    /// The oldest break taken. The offline queue keeps a week and more, but a
    /// break dated last year is a laptop clock that was wrong, not a break.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(31);

    public enum Failure
    {
        NotYours,
        EndsBeforeStart,
        BadEnding,
        TooOld,
    }

    public Task<int> DailyLimitMinutesAsync(CancellationToken ct) =>
        settings.GetIntAsync(DailyLimitKey, DefaultDailyLimitMinutes, ct);

    // ---- the Agent App ------------------------------------------------------

    /// <summary>Saves a break the app sent, new or ended (A-86).</summary>
    /// <param name="tokenSessionId">The sign-in the request came with.</param>
    public async Task<(BreakDto? Break, Failure? Failure)> SaveAsync(
        Guid id, Guid userId, Guid? tokenSessionId, SaveBreakRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // A laptop clock a minute fast must not put a break in the future; one
        // a month slow is not a break at all.
        var start = request.StartedAt > now ? now : request.StartedAt;

        if (now - start > MaxAge)
        {
            return (null, Failure.TooOld);
        }

        DateTimeOffset? end = request.EndedAt is { } e && e > now ? now : request.EndedAt;
        var endedBy = end is null ? null : request.EndedBy ?? BreakEndings.BreakOut;

        if (endedBy is not null && !BreakEndings.FromApp.Contains(endedBy))
        {
            return (null, Failure.BadEnding);
        }

        var existing = await db.AgentBreaks.FirstOrDefaultAsync(b => b.Id == id, ct);

        if (existing is not null && existing.UserId != userId)
        {
            return (null, Failure.NotYours);
        }

        if (end is { } until && until < (existing?.StartedAt ?? start))
        {
            return (null, Failure.EndsBeforeStart);
        }

        if (existing is null)
        {
            var sessionId = await SessionForAsync(userId, request.SessionId, tokenSessionId, ct);

            if (end is null)
            {
                // One break at a time. An open one left from before is over:
                // the agent could not have pressed Break in otherwise.
                await CloseOpenBreaksAsync(userId, start, now, ct);
            }

            db.AgentBreaks.Add(new AgentBreak
            {
                Id = id,
                UserId = userId,
                SessionId = sessionId,
                StartedAt = start,
                EndedAt = end,
                EndedBy = endedBy,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else if (end is not null && (existing.EndedAt is null || !BreakEndings.FromApp.Contains(existing.EndedBy!)))
        {
            existing.EndedAt = end;
            existing.EndedBy = endedBy;
            existing.UpdatedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (existing is null
            && ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName == "pk_agent_breaks")
        {
            // The same break twice at once: the direct send and the queue's
            // retry crossing. The other one's row is the break, so this one is
            // applied to it, as a call reported twice is (A-14).
            logger.LogInformation("Break {BreakId} arrived twice at once; the second is applied as an update", id);
            db.ChangeTracker.Clear();
            return await SaveAsync(id, userId, tokenSessionId, request, ct);
        }

        logger.LogInformation(
            "Break {BreakId} of {UserId}: {State}", id, userId, end is null ? "started" : $"ended ({endedBy})");

        var saved = (await LoadAsync(b => b.Id == id, now, ct)).Single();
        return (ToDto(saved, now), null);
    }

    /// <summary>
    /// The sign-in a new break belongs to: the one the app says, if it is the
    /// agent's, otherwise the one the request came with.
    /// </summary>
    private async Task<Guid?> SessionForAsync(Guid userId, Guid? claimed, Guid? fromToken, CancellationToken ct)
    {
        if (claimed is { } id && id != fromToken
            && !await db.AgentSessions.AnyAsync(s => s.Id == id && s.UserId == userId, ct))
        {
            logger.LogWarning("A break named session {SessionId}, which is not {UserId}'s; the request's is used", id, userId);
            return fromToken;
        }

        return claimed ?? fromToken;
    }

    /// <summary>Ends every open break of the agent's, at the end its sign-in gives it or at <paramref name="by"/>.</summary>
    private async Task CloseOpenBreaksAsync(Guid userId, DateTimeOffset by, DateTimeOffset now, CancellationToken ct)
    {
        var open = await db.AgentBreaks
            .Include(b => b.Session)
            .Where(b => b.UserId == userId && b.EndedAt == null)
            .ToListAsync(ct);

        foreach (var row in open)
        {
            var ending = BreakClock.EndOf(FactsOf(row), now);
            var at = ending.At ?? by;

            row.EndedAt = at < row.StartedAt ? row.StartedAt : at;
            row.EndedBy = ending.By ?? BreakEndings.BreakOut;
            row.UpdatedAt = now;

            logger.LogInformation("Open break {BreakId} of {UserId} ended at {At} ({By}) as a new one began",
                row.Id, userId, row.EndedAt, row.EndedBy);
        }
    }

    /// <summary>The agent's break time today, for the app's timer at sign-in (A-86).</summary>
    public async Task<MyBreaksTodayDto> MineTodayAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var (start, end) = ReportScope.Today(now);

        var breaks = await LoadAsync(b => b.UserId == userId && b.StartedAt < end && (b.EndedAt == null || b.EndedAt > start), now, ct);

        return new MyBreaksTodayDto(
            breaks.Sum(b => b.SecondsWithin(start, end, now)),
            await DailyLimitMinutesAsync(ct));
    }

    // ---- the monitor (S-66) -----------------------------------------------

    /// <summary>Every active agent: where they stand now, and their breaks today.</summary>
    public async Task<BreakMonitorDto> MonitorAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var (start, end) = ReportScope.Today(now);
        var onlineSince = SessionPresence.OnlineSince(now);

        var agents = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRoles.Agent && u.IsActive)
            .OrderBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                SignedIn = db.AgentSessions.Any(s => s.UserId == u.Id && s.LoggedOutAt == null),
                Heard = db.AgentSessions.Any(s => s.UserId == u.Id && s.LoggedOutAt == null && s.LastSeenAt >= onlineSince),
            })
            .ToListAsync(ct);

        var today = await LoadAsync(b => b.StartedAt < end && (b.EndedAt == null || b.EndedAt > start), now, ct);
        var byAgent = today.ToLookup(b => b.UserId);

        var rows = agents.Select(a =>
        {
            var mine = byAgent[a.Id].ToList();
            var going = mine.FirstOrDefault(b => b.Ending.At is null);

            var state = going is not null ? BreakStates.OnBreak
                : a.Heard ? BreakStates.Working
                : a.SignedIn ? BreakStates.NotHeard
                : BreakStates.SignedOut;

            return new BreakMonitorRowDto(
                a.Id,
                a.DisplayName,
                state,
                going?.StartedAt,
                mine.Sum(b => b.SecondsWithin(start, end, now)),
                mine.Count(b => b.Touches(start, end, now)));
        }).ToList();

        return new BreakMonitorDto(now, await DailyLimitMinutesAsync(ct), rows);
    }

    // ---- the report and the list (R-22) -------------------------------------

    /// <summary>The restaurant's days <paramref name="from"/> to <paramref name="to"/>, inclusive, as instants.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Period(DateOnly from, DateOnly to) =>
        (ReportScope.StartOfDay(from.ToDateTime(TimeOnly.MinValue)),
         ReportScope.StartOfDay(to.AddDays(1).ToDateTime(TimeOnly.MinValue)));

    /// <summary>Per agent per day, and per agent over the period.</summary>
    public async Task<BreakReportDto> ReportAsync(DateOnly from, DateOnly to, IReadOnlyList<Guid>? agentIds, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var limit = await DailyLimitMinutesAsync(ct);
        var limitSeconds = limit * 60;
        var breaks = await InPeriodAsync(from, to, agentIds, now, ct);

        var days = new List<BreakDayDto>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var (start, end) = Period(day, day);

            foreach (var agent in breaks.GroupBy(b => (b.UserId, b.AgentDisplayName)))
            {
                var parts = agent
                    .Where(b => b.Touches(start, end, now))
                    .Select(b => b.SecondsWithin(start, end, now))
                    .ToList();

                if (parts.Count == 0)
                {
                    continue;
                }

                var seconds = parts.Sum();
                days.Add(new BreakDayDto(
                    day, agent.Key.UserId, agent.Key.AgentDisplayName, parts.Count, seconds,
                    Math.Max(0, seconds - limitSeconds)));
            }
        }

        var totals = days
            .GroupBy(d => (d.AgentId, d.AgentDisplayName))
            .Select(g => new BreakAgentTotalDto(
                g.Key.AgentId,
                g.Key.AgentDisplayName,
                breaks.Count(b => b.UserId == g.Key.AgentId),
                g.Sum(d => d.Seconds),
                g.Count(),
                g.Count(d => d.OverSeconds > 0),
                g.Sum(d => d.OverSeconds)))
            .OrderByDescending(t => t.Seconds)
            .ToList();

        return new BreakReportDto(
            limit,
            totals,
            days.OrderByDescending(d => d.Day).ThenBy(d => d.AgentDisplayName).ToList());
    }

    /// <summary>The single breaks of the period, newest first, a page at a time.</summary>
    public async Task<BreakPageDto> ListAsync(
        DateOnly from, DateOnly to, IReadOnlyList<Guid>? agentIds, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var now = clock.GetUtcNow();
        var breaks = await InPeriodAsync(from, to, agentIds, now, ct);

        var rows = breaks
            .OrderByDescending(b => b.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => ToDto(b, now))
            .ToList();

        return new BreakPageDto(rows, breaks.Count, page, pageSize);
    }

    /// <summary>Every break of the period, newest first, for the export (S-05).</summary>
    public async Task<IReadOnlyList<BreakDto>> ExportAsync(DateOnly from, DateOnly to, IReadOnlyList<Guid>? agentIds, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return (await InPeriodAsync(from, to, agentIds, now, ct))
            .OrderByDescending(b => b.StartedAt)
            .Select(b => ToDto(b, now))
            .ToList();
    }

    /// <summary>Breaks with any part inside the period, once their end is worked out.</summary>
    private async Task<List<Loaded>> InPeriodAsync(
        DateOnly from, DateOnly to, IReadOnlyList<Guid>? agentIds, DateTimeOffset now, CancellationToken ct)
    {
        var (start, end) = Period(from, to);
        var agents = agentIds?.ToArray() ?? [];

        var breaks = await LoadAsync(
            b => b.StartedAt < end && (b.EndedAt == null || b.EndedAt > start)
                 && (agents.Length == 0 || agents.Contains(b.UserId)),
            now, ct);

        // An open row is matched whatever its end; the end worked out from its
        // sign-in may be before the period.
        return breaks.Where(b => b.Touches(start, end, now)).ToList();
    }

    // ---- shared ---------------------------------------------------------------

    /// <summary>A break with its end worked out (<see cref="BreakClock"/>).</summary>
    private sealed record Loaded(
        Guid Id, Guid UserId, string AgentDisplayName, DateTimeOffset StartedAt, BreakClock.Ending Ending)
    {
        public DateTimeOffset EndOrNow(DateTimeOffset now) => Ending.At ?? now;

        /// <summary>
        /// Whether any of it falls between <paramref name="from"/> and <paramref name="to"/>.
        /// A break ended the moment it began counts on the day it began.
        /// </summary>
        public bool Touches(DateTimeOffset from, DateTimeOffset to, DateTimeOffset now) =>
            BreakClock.SecondsWithin(StartedAt, EndOrNow(now), from, to) > 0
            || (StartedAt >= from && StartedAt < to);

        public int SecondsWithin(DateTimeOffset from, DateTimeOffset to, DateTimeOffset now) =>
            BreakClock.SecondsWithin(StartedAt, EndOrNow(now), from, to);
    }

    private async Task<List<Loaded>> LoadAsync(
        System.Linq.Expressions.Expression<Func<AgentBreak, bool>> where, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.AgentBreaks.AsNoTracking()
            .Where(where)
            .Select(b => new
            {
                b.Id,
                b.UserId,
                b.User.DisplayName,
                Facts = new BreakClock.Facts(
                    b.StartedAt,
                    b.EndedAt,
                    b.EndedBy,
                    b.SessionId != null,
                    b.Session != null ? b.Session.LoggedInAt : null,
                    b.Session != null ? b.Session.LoggedOutAt : null,
                    b.Session != null ? b.Session.LastSeenAt : null),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new Loaded(r.Id, r.UserId, r.DisplayName, r.Facts.StartedAt, BreakClock.EndOf(r.Facts, now)))
            .ToList();
    }

    private static BreakClock.Facts FactsOf(AgentBreak b) => new(
        b.StartedAt, b.EndedAt, b.EndedBy, b.Session is not null,
        b.Session?.LoggedInAt, b.Session?.LoggedOutAt, b.Session?.LastSeenAt);

    private static BreakDto ToDto(Loaded b, DateTimeOffset now) => new(
        b.Id,
        b.UserId,
        b.AgentDisplayName,
        b.StartedAt,
        b.Ending.At,
        b.Ending.By,
        (int)Math.Max(0, (b.EndOrNow(now) - b.StartedAt).TotalSeconds));
}
