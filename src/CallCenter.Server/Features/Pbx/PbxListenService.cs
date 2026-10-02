using System.Collections.Concurrent;
using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Pbx;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// The listen-in calls in progress, so the Stop button can end one even if the
/// browser's connection is slow to close, and the supervisor's voice can reach
/// a <c>*223</c> one (S-62).
/// </summary>
public sealed class ListenSessions
{
    private readonly ConcurrentDictionary<Guid, Session> _open = new();

    /// <param name="Speak">Dialled with <c>*223</c>: the supervisor's voice goes to the agent.</param>
    public sealed record Session(
        Guid Id, Guid SupervisorId, Guid AgentId, bool Speak, IPbxListenCall Call, CancellationTokenSource Stop);

    public Session Open(Guid supervisorId, Guid agentId, bool speak, IPbxListenCall call)
    {
        var session = new Session(Guid.NewGuid(), supervisorId, agentId, speak, call, new CancellationTokenSource());
        _open[session.Id] = session;
        return session;
    }

    /// <summary>A session, if it is this supervisor's.</summary>
    public Session? Find(Guid id, Guid supervisorId) =>
        _open.TryGetValue(id, out var session) && session.SupervisorId == supervisorId ? session : null;

    /// <summary>Ends a session, if it is this supervisor's. False when there is no such session.</summary>
    public bool Stop(Guid id, Guid supervisorId)
    {
        if (!_open.TryGetValue(id, out var session) || session.SupervisorId != supervisorId)
        {
            return false;
        }

        session.Stop.Cancel();
        return true;
    }

    public void Close(Session session)
    {
        _open.TryRemove(session.Id, out _);
        session.Stop.Dispose();
    }
}

/// <summary>
/// The agents' phones as the PBX reports them (S-61), and listening in on a
/// call through the PBX's <c>*222</c>, or listening and speaking to the agent
/// through <c>*223</c> (S-62).
/// </summary>
/// <remarks>
/// <para>
/// <b>Listening is a call like the blacklist's.</b> The server dials
/// <c>*222</c> and the extension from its own extension; the PBX joins it to
/// that extension's call, listen-only, and sends the call's sound, which the
/// controller passes to the supervisor's browser as it arrives. Neither the
/// agent nor the customer hears anything.
/// </para>
/// <para>
/// <b>Speaking is the same call with <c>*223</c></b> (Dia, 2 Oct 2026). The
/// PBX joins it as a whisper: what the server sends goes to the agent, and
/// the customer does not hear it. The browser posts the supervisor's
/// microphone to the session, and the server sends it in place of the
/// silence a <c>*222</c> call carries.
/// </para>
/// <para>
/// <b>It always ends.</b> Stop, the page closing, the call ending (the PBX
/// hangs up) or <see cref="MaxListen"/>, whichever is first, and the server
/// hangs up its call. Each start and end goes in the audit log (N-06): who
/// listened to whom, when, and for how long.
/// </para>
/// </remarks>
public class PbxListenService(
    CallCenterDbContext db,
    PbxFeatureLine line,
    IPbxCallListener listener,
    ExtensionWatch watch,
    ListenSessions sessions,
    TimeProvider clock,
    ILogger<PbxListenService> logger)
{
    /// <summary>The PBX's listen-in code; the extension follows it.</summary>
    public const string ListenCode = "*222";

    /// <summary>The PBX's whisper code: listen, and speak to the agent only. The extension follows it.</summary>
    public const string SpeakCode = "*223";

    /// <summary>No listen-in outlasts this, whatever the browser does.</summary>
    public static readonly TimeSpan MaxListen = TimeSpan.FromHours(1);

    public enum Failure
    {
        NotFound,
        NoExtension,
        NotConfigured,
        PbxFailed,
    }

    /// <summary>A listen-in that has started: the call, and the session that can stop it.</summary>
    public sealed record Started(ListenSessions.Session Session, IPbxListenCall Call, string Extension, DateTimeOffset At);

    /// <summary>Every active agent with an extension, and what their phone is doing.</summary>
    public async Task<AgentPhonesDto> PhonesAsync(CancellationToken ct = default)
    {
        var agents = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRoles.Agent && u.IsActive && u.Extension != null && u.Extension != "")
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName, Extension = u.Extension! })
            .ToListAsync(ct);

        var live = watch.IsLive;
        return new AgentPhonesDto(
            live,
            live ? null : watch.Problem,
            agents.Select(a =>
            {
                var (state, since) = watch.Get(a.Extension);
                return new AgentPhoneDto(a.Id, a.DisplayName, a.Extension, state, since);
            }).ToList());
    }

    /// <summary>
    /// Dials <c>*222</c>, or <c>*223</c> when <paramref name="speak"/>, and the
    /// agent's extension, and returns once the PBX has answered.
    /// </summary>
    public async Task<(Started? Started, Failure? Failure, string? Error)> StartAsync(
        Guid agentId, Guid supervisorId, bool speak = false, CancellationToken ct = default)
    {
        var code = speak ? SpeakCode : ListenCode;

        var agent = await db.Users.AsNoTracking()
            .Where(u => u.Id == agentId && u.Role == UserRoles.Agent)
            .Select(u => new { u.Id, u.DisplayName, u.Extension })
            .FirstOrDefaultAsync(ct);

        if (agent is null)
        {
            return (null, Failure.NotFound, null);
        }

        if (string.IsNullOrWhiteSpace(agent.Extension))
        {
            return (null, Failure.NoExtension, null);
        }

        var from = await line.GetAsync(ct);
        if (from is null)
        {
            return (null, Failure.NotConfigured, null);
        }

        IPbxListenCall call;
        try
        {
            call = await listener.ListenAsync(from, code + agent.Extension, ct);
        }
        catch (PbxFeatureException ex)
        {
            logger.LogWarning("PBX listen: {Code}{Extension} failed: {Error}", code, agent.Extension, ex.Message);
            return (null, Failure.PbxFailed, ex.Message);
        }

        var at = clock.GetUtcNow();
        var session = sessions.Open(supervisorId, agentId, speak, call);

        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = supervisorId,
            Entity = "listen",
            EntityId = agentId.ToString(),
            Action = "start",
            After = JsonSerializer.SerializeToDocument(new { extension = agent.Extension, agent = agent.DisplayName, speak }),
        });
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation("PBX listen: {Supervisor} is {Doing} {Agent} on {Extension}",
            supervisorId, speak ? "listening and speaking to" : "listening to", agent.DisplayName, agent.Extension);

        return (new Started(session, call, agent.Extension, at), null, null);
    }

    /// <summary>
    /// Whether the call being listened to is over, as the PBX watch says (S-61).
    /// Needed because <c>*222</c> does not hang up when that call does: the
    /// PBX's spy stays on the line waiting for the extension's next call
    /// (found on the server, 26 Sep). Free or offline is over; not known is
    /// not, since the watch may simply be down.
    /// </summary>
    public bool CallIsOver(string extension) =>
        watch.IsLive && watch.Get(extension).State is PhoneStates.Free or PhoneStates.Offline;

    /// <summary>Hangs up and records how long it lasted. Safe to call whatever ended it.</summary>
    public async Task EndAsync(Started started, string why)
    {
        await started.Call.DisposeAsync();
        sessions.Close(started.Session);

        var seconds = (int)Math.Round((clock.GetUtcNow() - started.At).TotalSeconds);
        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = started.Session.SupervisorId,
            Entity = "listen",
            EntityId = started.Session.AgentId.ToString(),
            Action = "stop",
            After = JsonSerializer.SerializeToDocument(new { extension = started.Extension, speak = started.Session.Speak, seconds, why }),
        });
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation("PBX listen: ended after {Seconds}s ({Why})", seconds, why);
    }
}
