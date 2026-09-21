using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CallCenter.Shared.Contracts.Classifications;

/// <summary>One of the things a call can be about (A-40, S-40).</summary>
/// <param name="Name">
/// The stable key — <c>Order</c>, <c>Complaint</c> and so on. Reports and the
/// form's <c>showWhenType</c> rules are written against this, never against the
/// labels, so renaming what agents see never breaks either.
/// </param>
/// <param name="IsSystem">
/// One of the six the system was built around. These can be renamed and hidden
/// but not deleted: the reports name them.
/// </param>
/// <param name="InUse">
/// Whether any classification already uses it. A type in use can be hidden but
/// not deleted, or the calls that carry it would lose what they were about.
/// </param>
public record ClassificationTypeDto(
    Guid Id,
    string Name,
    string LabelAr,
    string LabelEn,
    string? Colour,
    bool IsSystem,
    int SortOrder,
    bool IsActive,
    bool InUse);

/// <summary>Creates or renames a type (S-40).</summary>
public record UpsertClassificationTypeRequest(
    [Required, MaxLength(100)] string LabelAr,
    [Required, MaxLength(100)] string LabelEn,
    [MaxLength(20)] string? Colour = null,
    int SortOrder = 0,
    bool IsActive = true)
{
    /// <summary>
    /// Only on create. The stable key reports are written against, so it is set
    /// once and never changed — renaming is what the labels are for.
    /// </summary>
    [MaxLength(100)]
    public string? Name { get; init; }
}

/// <summary>
/// Everything needed to draw the classification form (A-40).
/// </summary>
/// <remarks>
/// One call rather than three. The Agent App asks for this when it signs in and
/// when the form version changes, and it must not be making three requests while
/// an agent is staring at a form after a call.
/// </remarks>
/// <param name="Version">
/// The version the fields came from. It is sent back when classifying, so a form
/// filled in just as the supervisor published a change is still stored against
/// the questions the agent actually answered.
/// </param>
public record ClassificationFormDto(
    int Version,
    JsonDocument Definition,
    IReadOnlyList<ClassificationTypeDto> Types,
    IReadOnlyList<FormBranchDto> Branches);

/// <summary>A branch, as the form's branch field offers it.</summary>
public record FormBranchDto(Guid Id, string Name);

/// <summary>Replaces the form, creating a new version (S-40).</summary>
public record PublishFormRequest([Required] JsonDocument Definition);

/// <summary>What a call was about, as it is read back.</summary>
public record ClassificationDto(
    Guid CommunicationId,
    Guid TypeId,
    string TypeName,
    string TypeLabelAr,
    string TypeLabelEn,
    Guid? BranchId,
    string? BranchName,
    decimal? OrderValue,
    string? Notes,
    bool FollowUp,
    bool? Resolved,
    int FormVersion,
    JsonDocument CustomValues,
    string ClassifiedByName,
    DateTimeOffset ClassifiedAt,
    string? UpdatedByName,
    DateTimeOffset? UpdatedAt,
    /// <summary>Whether the signed-in user may still change it (A-42).</summary>
    bool CanEdit);

/// <summary>
/// Classifies a call, or edits an existing classification (A-40, A-42).
/// </summary>
/// <param name="FormVersion">
/// The version of the form the agent actually filled in. Sent by the client
/// rather than assumed by the server, so a classification saved seconds after
/// the supervisor published a change is stored against the questions that were
/// on screen.
/// </param>
/// <param name="CustomValues">
/// Values for fields the supervisor added, keyed by field key. Built-in fields
/// have their own columns because the reports group by them.
/// </param>
public record SaveClassificationRequest(
    // Nullable so that leaving it out is actually refused. A non-nullable Guid
    // arrives as all-zeros when the client omits it, and [Required] sees a
    // value rather than a gap - the call would be "classified" as nothing.
    [Required] Guid? TypeId,
    Guid? BranchId,
    [Range(0, 1000000)] decimal? OrderValue,
    [MaxLength(4000)] string? Notes,
    bool FollowUp = false,
    bool? Resolved = null,
    int? FormVersion = null,
    JsonDocument? CustomValues = null);

/// <summary>
/// Classifies a call the server has not seen yet (A-04, A-40).
/// </summary>
/// <remarks>
/// A call taken while the server was unreachable is queued on the agent's laptop
/// and has no server id, but the agent still classifies it at hang-up like any
/// other. This keys on what the Agent App does have: the SIP Call-ID the phone
/// system gave the call, and the extension it came in on. Both are already
/// stored on the communication, so the classification lands on the right call
/// whenever the queued call itself arrives.
/// </remarks>
public record SaveClassificationByCallRequest(
    [Required, MaxLength(200)] string SipCallId,
    [Required, MaxLength(20)] string Extension,
    [Required] SaveClassificationRequest Classification);

/// <summary>One entry in the audit trail (A-43).</summary>
public record ClassificationHistoryDto(
    DateTimeOffset ChangedAt,
    string ChangedByName,
    JsonDocument? Before,
    JsonDocument After);
