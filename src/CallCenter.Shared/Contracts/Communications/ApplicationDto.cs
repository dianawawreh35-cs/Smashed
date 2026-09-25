using System.ComponentModel.DataAnnotations;
using CallCenter.Shared.Contracts.Classifications;

namespace CallCenter.Shared.Contracts.Communications;

/// <summary>
/// Records a message: a conversation that reached the restaurant on WhatsApp,
/// Facebook, Instagram, Wheels or another app rather than by phone (A-70).
/// </summary>
/// <remarks>
/// A message is a <c>communications</c> row with <c>kind = App</c>: the same
/// table as calls, so a contact's history shows both (A-72) and every report
/// is one query. Nothing here is a second schema.
///
/// The customer is found the way the pop-up finds one — the number through
/// <c>PhoneNormalizer</c>, then the last nine digits (A-13) — unless the agent
/// has already picked or created the contact, in which case
/// <paramref name="ContactId"/> says who.
/// </remarks>
/// <param name="ChannelId">Which app. Never Phone: a phone conversation is a call.</param>
/// <param name="RemoteNumber">
/// The customer's number as typed. Optional when <paramref name="ContactId"/>
/// is given, since some apps never show one.
/// </param>
/// <param name="ContactId">
/// The contact the agent chose, or the one they just saved (A-11). Null lets
/// the server match the number.
/// </param>
/// <param name="StartedAt">
/// When the customer wrote. Defaults to now. An agent may set it back within
/// the same day, so a message from 11:00 recorded at 11:20 keeps its hour in
/// the reports; never into the future, and other days are the supervisor's.
/// </param>
/// <param name="Classification">
/// What the message was about, saved in the same request so a recorded message
/// is classified at once. <b>Required</b> (Dia, 25 Sep): a message is typed by
/// an agent who knows what it was, so unlike a call it is never recorded
/// unclassified. The server refuses one without it (<c>classification_required</c>).
/// Nullable only so that leaving it out gets that answer rather than a bare 400.
/// </param>
public record RecordApplicationRequest(
    [Required] Guid? ChannelId,
    [MaxLength(40)] string? RemoteNumber,
    Guid? ContactId,
    DateTimeOffset? StartedAt,
    SaveClassificationRequest? Classification,
    [MaxLength(100)] string? LaptopId = null);

/// <summary>
/// Changes a message's channel, customer or time (A-71). The classification is
/// changed through the classification endpoints, as a call's is.
/// </summary>
public record EditApplicationRequest(
    [Required] Guid? ChannelId,
    [MaxLength(40)] string? RemoteNumber,
    Guid? ContactId,
    DateTimeOffset? StartedAt);

/// <summary>
/// One of the ways a customer reaches the restaurant (S-41): Phone, and the
/// apps the supervisor lists.
/// </summary>
/// <param name="IsSystem">
/// Phone. Every call is filed under it, so it can be neither renamed nor hidden.
/// </param>
/// <param name="InUse">
/// Some communication is filed under it. It can then be hidden but not removed,
/// and renaming is safe either way: rows hold the id, never the name.
/// </param>
public record ChannelDto(
    Guid Id,
    string Name,
    bool IsSystem,
    int SortOrder,
    bool IsActive,
    bool InUse);

/// <summary>Adds a channel, or renames, reorders or hides one (S-41).</summary>
public record UpsertChannelRequest(
    [Required, MaxLength(100)] string Name,
    int SortOrder = 0,
    bool IsActive = true);
