using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Contacts;

/// <summary>
/// Creates or replaces a contact (A-63).
/// </summary>
/// <remarks>
/// The numbers sent are the contact's numbers afterwards: one missing from the
/// list is removed. The VIP and Blocked flags are deliberately absent — only a
/// supervisor changes those, through their own endpoint (S-45), so an agent
/// editing an address cannot clear a block by accident.
/// </remarks>
/// <param name="Phones">
/// At least one. Entered in any format; the server normalises them (A-13).
/// </param>
public record UpsertContactRequest(
    [MaxLength(200)] string? Name,
    [MaxLength(500)] string? Address,
    string? Notes,
    string? DeliveryNotes,
    [Required, MinLength(1)] IReadOnlyList<string> Phones);

/// <summary>
/// Why a save was refused because the number is already on file (A-63).
/// </summary>
/// <param name="Number">The number, normalised, that is already taken.</param>
/// <param name="ExistingContactId">
/// Who has it. The screen offers to open that contact, or to merge — which is
/// why the id is returned rather than just a message.
/// </param>
public record DuplicateNumberDto(
    string Number,
    Guid ExistingContactId,
    string? ExistingContactName);
