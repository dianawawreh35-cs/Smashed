using System.ComponentModel.DataAnnotations.Schema;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// An agent or supervisor. Table <c>users</c>.
/// </summary>
/// <remarks>
/// Each agent has two PBX extensions (SRS 2.3). Both make and receive calls;
/// they differ by who is on the other end. The customer extension is the one the
/// queue rings and the one used to call customers back; the internal extension
/// is for other agents and the four branches. Both SIP secrets are encrypted at
/// rest with an application-level key and are never returned to the supervisor UI
/// or shown to agents (N-05).
/// </remarks>
public class User
{
    public Guid Id { get; set; }

    public string Login { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    /// <summary>One of <see cref="Shared.UserRoles"/>: Agent or Supervisor.</summary>
    public string Role { get; set; } = null!;

    /// <summary>Customer-facing extension: the queue rings it, and it calls customers.</summary>
    public string? CustomerExtension { get; set; }

    /// <summary>Encrypted at rest.</summary>
    public string? CustomerSipSecret { get; set; }

    /// <summary>Internal extension: other agents and the four branches.</summary>
    public string? InternalExtension { get; set; }

    /// <summary>Encrypted at rest.</summary>
    public string? InternalSipSecret { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<AgentSession> Sessions { get; } = new List<AgentSession>();
}
