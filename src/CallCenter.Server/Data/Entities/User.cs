using System.ComponentModel.DataAnnotations.Schema;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// An agent or supervisor. Table <c>users</c>.
/// </summary>
/// <remarks>
/// Each agent has one PBX extension (SRS 2.3), used for every call they handle.
/// Internal calls are told apart by the other party's number against the list in
/// S-48, not by a second extension. The SIP secret is encrypted at rest with an
/// application-level key and is never returned to the supervisor UI or shown to
/// agents (N-05).
/// </remarks>
public class User
{
    public Guid Id { get; set; }

    public string Login { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    /// <summary>One of <see cref="Shared.UserRoles"/>: Agent or Supervisor.</summary>
    public string Role { get; set; } = null!;

    /// <summary>The agent's extension. Null until the supervisor assigns one.</summary>
    public string? Extension { get; set; }

    /// <summary>Encrypted at rest.</summary>
    public string? SipSecret { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<AgentSession> Sessions { get; } = new List<AgentSession>();
}
