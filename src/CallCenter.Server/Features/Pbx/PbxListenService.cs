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
/// browser's connection is slow to close (S-62).
/// </summary>
public sealed class ListenSessions
{
    private readonly ConcurrentDictionary<Guid, Session> _open = new();

    public sealed record Session(Guid Id, Guid SupervisorId, Guid AgentId, CancellationTokenSource Stop);

    public Session Open(Guid supervisorId, Guid agentId)
    {
        var session = new Session(Guid.NewGuid(), supervisorId, agentId, new CancellationTokenSource());
        _open[session.Id] = session;
        return session;
    }

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
/// call through the PBX's <c>*222</c> (S-62).
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

    /// <summary>Dials <c>*222</c> and the agent's extension, and returns once the PBX has answered.</summary>
    public async Task<(Started? Started, Failure? Failure, string? Error)> StartAsync(
        Guid agentId, Guid supervisorId, CancellationToken ct = default)
    {
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
            call = await listener.ListenAsync(from, ListenCode + agent.Extension, ct);
        }
        catch (PbxFeatureException ex)
        {
            logger.LogWarning("PBX listen: {Code}{Extension} failed: {Error}", ListenCode, agent.Extension, ex.Message);
            return (null, Failure.PbxFailed, ex.Message);
        }

        var at = clock.GetUtcNow();
        var session = sessions.Open(supervisorId, agentId);

        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = supervisorId,
            Entity = "listen",
            EntityId = agentId.ToString(),
            Action = "start",
            After = JsonSerializer.SerializeToDocument(new { extension = agent.Extension, agent = agent.DisplayName }),
        });
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation("PBX listen: {Supervisor} is listening to {Agent} on {Extension}",
            supervisorId, agent.DisplayName, agent.Extension);

        return (new Started(session, call, agent.Extension, at), null, null);
    }

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
            After = JsonSerializer.SerializeToDocument(new { extension = started.Extension, seconds, why }),
        });
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation("PBX listen: ended after {Seconds}s ({Why})", seconds, why);
    }
}
