using System.ComponentModel.DataAnnotations.Schema;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// An agent or supervisor. Table <c>users</c>.
/// </summary>
/// <remarks>
/// Each agent has two PBX extensions (SRS 2.3): one the inbound queue rings, one
/// used for outbound calls. Both SIP secrets are encrypted at rest with an
/// application-level key and are never returned to the supervisor UI or shown to
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

    public string? InboundExtension { get; set; }

    /// <summary>Encrypted at rest.</summary>
    public string? InboundSipSecret { get; set; }

    public string? OutboundExtension { get; set; }

    /// <summary>Encrypted at rest.</summary>
    public string? OutboundSipSecret { get; set; }

    public Guid? DefaultBranchId { get; set; }
    public Branch? DefaultBranch { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<AgentSession> Sessions { get; } = new List<AgentSession>();
}
