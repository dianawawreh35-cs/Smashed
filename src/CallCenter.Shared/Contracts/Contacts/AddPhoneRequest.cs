using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Contacts;

/// <summary>
/// Adds one number to a contact that already exists (A-63).
/// </summary>
/// <remarks>
/// Separate from <see cref="UpsertContactRequest"/> on purpose. An agent
/// answering "yes, that is the same person" has typed a number and nothing
/// else; sending a whole contact back would risk blanking an address or notes
/// they never saw.
/// </remarks>
public record AddPhoneRequest(
    [Required, MaxLength(40)] string Number);
