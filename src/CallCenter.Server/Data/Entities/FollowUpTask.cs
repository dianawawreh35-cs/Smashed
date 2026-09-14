namespace CallCenter.Server.Data.Entities;

/// <summary>
/// A call-back or complaint follow-up. Table <c>follow_up_tasks</c>.
/// </summary>
/// <remarks>
/// Created automatically from a Complaint classification or an abandoned call
/// (S-51), or by hand. Closes automatically when an outbound call is made to the
/// number, recorded in <see cref="ClosedByCommunicationId"/>.
/// </remarks>
public class FollowUpTask
{
    public Guid Id { get; set; }

    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public Guid? CommunicationId { get; set; }
    public Communication? Communication { get; set; }

    public string Title { get; set; } = null!;

    public Guid? AssignedTo { get; set; }
    public User? AssignedToUser { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    /// <summary><see cref="Shared.TaskStatuses"/>: Open, Done or Cancelled.</summary>
    public string Status { get; set; } = null!;

    /// <summary><see cref="Shared.TaskOrigins"/>: Complaint, Abandoned, Missed or Manual.</summary>
    public string CreatedFrom { get; set; } = null!;

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public Guid? ClosedBy { get; set; }

    /// <summary>The outbound call that closed this task, if it closed itself.</summary>
    public Guid? ClosedByCommunicationId { get; set; }
    public Communication? ClosedByCommunication { get; set; }
}
