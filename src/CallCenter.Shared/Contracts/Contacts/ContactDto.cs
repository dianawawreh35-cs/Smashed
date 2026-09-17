namespace CallCenter.Shared.Contracts.Contacts;

/// <summary>One of a contact's phone numbers (A-60).</summary>
/// <param name="Raw">As it was typed, or as the PBX delivered it.</param>
/// <param name="Normalised">
/// Digits only, E.164 without the '+'. What matching compares, so that 05…,
/// +9705… and 9705… are one number (A-13).
/// </param>
public record ContactPhoneDto(
    Guid Id,
    string Raw,
    string Normalised,
    bool IsPrimary);

/// <summary>
/// A contact in full (A-60). Shared by every agent (A-61).
/// </summary>
/// <param name="IsVip">Shown as a badge on the incoming-call pop-up (A-16).</param>
/// <param name="IsBlocked">Calls from this contact are rejected by the app (A-17).</param>
public record ContactDto(
    Guid Id,
    string? Name,
    string? Address,
    string? Notes,
    string? DeliveryNotes,
    bool IsVip,
    bool IsBlocked,
    string? FlagReason,
    IReadOnlyList<ContactPhoneDto> Phones,
    string? CreatedByDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>What to show when a contact was saved as a bare number (S-45).</summary>
    public bool HasName => !string.IsNullOrWhiteSpace(Name);
}

/// <summary>A contact as it appears in a list of search results (A-61).</summary>
public record ContactSummaryDto(
    Guid Id,
    string? Name,
    string? Address,
    bool IsVip,
    bool IsBlocked,
    IReadOnlyList<string> Phones);
