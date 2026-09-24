namespace CallCenter.Server.Data.Entities;

/// <summary>
/// A phone call or an app-based interaction. Table <c>communications</c>.
/// </summary>
/// <remarks>
/// Calls and app entries share one table so every report is a single query
/// (<see cref="Kind"/> and <see cref="ChannelId"/> separate them).
///
/// Records arrive from three places and are reconciled: the Agent App reports
/// what it handled, AMI reports everything the PBX did - including calls that
/// never reached an agent (Abandoned, Overflowed, S-50) - and the CDR importer
/// fills gaps afterwards. An AMI/CDR row and an Agent App row for the same call
/// are joined on <see cref="PbxUniqueId"/> where available, otherwise on
/// <see cref="RemoteNormalised"/> plus a time window.
/// </remarks>
public class Communication
{
    public Guid Id { get; set; }

    /// <summary><see cref="Shared.CommunicationKinds"/>: Call or App.</summary>
    public string Kind { get; set; } = null!;

    public Guid ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;

    /// <summary><see cref="Shared.Directions"/>: In, Out, or None for app entries.</summary>
    public string Direction { get; set; } = null!;

    /// <summary><see cref="Shared.CommunicationStatuses"/>. Logged = app entry or manual.</summary>
    public string Status { get; set; } = null!;

    /// <summary>Null for Abandoned and Overflowed - no agent was involved.</summary>
    public Guid? AgentId { get; set; }
    public User? Agent { get; set; }

    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string? RemoteNumberRaw { get; set; }

    /// <summary>Normalised form of <see cref="RemoteNumberRaw"/>, for matching.</summary>
    public string? RemoteNormalised { get; set; }

    /// <summary>Caller-ID name, when the trunk supplies one.</summary>
    public string? RemoteName { get; set; }

    /// <summary>Ring start, or the time an app entry describes.</summary>
    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? AnsweredAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Talk time in seconds: answered to ended.</summary>
    public int? DurationSec { get; set; }

    /// <summary>Queue wait in seconds, from AMI or the CDR. Feeds R-20 and R-21.</summary>
    public int? WaitSec { get; set; }

    public string? QueueName { get; set; }

    /// <summary>Which extension handled it.</summary>
    public string? Extension { get; set; }

    /// <summary>Call-ID from the Agent App INVITE.</summary>
    public string? SipCallId { get; set; }

    /// <summary>Asterisk uniqueid from AMI or the CDR, used to join the two views of one call.</summary>
    public string? PbxUniqueId { get; set; }

    /// <summary><see cref="Shared.CommunicationSources"/>: AgentApp, AMI, CDR or Manual.</summary>
    public string Source { get; set; } = null!;

    /// <summary>Machine name of the laptop that logged it.</summary>
    public string? LaptopId { get; set; }

    /// <summary>
    /// Why a missed, rejected or unanswered outbound call went the way it did,
    /// in the agent's words (A-41). Only those: an answered call is classified
    /// instead, and its notes live on the <see cref="Classification"/>.
    /// </summary>
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Classification? Classification { get; set; }
    public Recording? Recording { get; set; }
}
